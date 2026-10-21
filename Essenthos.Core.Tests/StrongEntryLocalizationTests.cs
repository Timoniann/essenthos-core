using System.Data.Common;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongEntryLocalizationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly TranslationCommands _commands = new();

    public StrongEntryLocalizationTests(WitnessDatabase database)
    {
        using var connection = database.NewConnection();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection.ConnectionString).AddInterceptors(_commands).Options);
        _transaction = _db.Database.BeginTransaction();
        _db.StrongEntryTranslations.AddRange(
            new StrongEntryTranslation
            {
                StrongNumber = "G26", Language = "deu", Definition = "Liebe",
                Derivation = "von G25", KjvDefinition = "Liebe; Liebesmahl",
                DetailedDefinition = "1) Liebe", Method = LinkMethod.ModelReading,
                Source = "translator; prompt-version; 2026-10-18", Note = "a stated uncertainty",
            },
            new StrongEntryTranslation
            {
                StrongNumber = "G26", Language = "spa", Definition = "amor",
                Method = LinkMethod.ModelReading, Source = "Spanish translator",
            });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Theory]
    [InlineData("de")]
    [InlineData("deu")]
    [InlineData(" DE ")]
    public async Task AReaderGetsOnlyTheRequestedLanguageWithItsProvenance(string language)
    {
        var translation = await StrongEndpoints.Translation(_db, "G26", language, default);
        translation.Should().Be(new StrongTranslationResponse(
            "deu", "Liebe", "von G25", "Liebe; Liebesmahl", "1) Liebe",
            LinkMethod.ModelReading, null, "translator; prompt-version; 2026-10-18", "a stated uncertainty"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("en")]
    [InlineData("eng")]
    [InlineData("fr")]
    public async Task AbsentAndEnglishLanguagesLeaveTheOriginalAlone(string? language) =>
        (await StrongEndpoints.Translation(_db, "G26", language, default)).Should().BeNull();

    [Fact]
    public async Task AnotherNumberDoesNotBorrowATranslation() =>
        (await StrongEndpoints.Translation(_db, "G27", "de", default)).Should().BeNull();

    [Fact]
    public async Task ACardPageReadsOnlyItsNumbersAndLanguageInOneQuery()
    {
        _db.StrongEntryTranslations.AddRange(
            new StrongEntryTranslation
            {
                StrongNumber = "G27", Language = "deu", Definition = "geliebt",
                Method = LinkMethod.ModelReading, Source = "translator",
            },
            new StrongEntryTranslation
            {
                StrongNumber = "H1", Language = "deu", Definition = "Vater",
                Method = LinkMethod.ModelReading, Source = "translator",
            });
        await _db.SaveChangesAsync();
        _commands.Reads = 0;

        var translations = await StrongEndpoints.Translations(_db, ["G26", "G27", "G28"], "de", default);

        translations.Keys.Should().BeEquivalentTo("G26", "G27");
        translations["G26"].Definition.Should().Be("Liebe");
        translations["G27"].Definition.Should().Be("geliebt");
        _commands.Reads.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("en")]
    public async Task EnglishCardsDoNotQueryTheTranslationTable(string? language)
    {
        _commands.Reads = 0;
        (await StrongEndpoints.Translations(_db, ["G26"], language, default)).Should().BeEmpty();
        _commands.Reads.Should().Be(0);
    }

    [Fact]
    public async Task TheWireKeepsTheEnglishAndEveryTranslationFieldSeparate()
    {
        var response = new StrongEntryResponse(
            "G26", "ἀγάπη", "agápē", null, "love", "from G25", "charity", null,
            "1) love", null, "grc", null, false, null)
        {
            Translation = await StrongEndpoints.Translation(_db, "G26", "de", default),
        };
        AppJsonSerializerContext.Default.GetTypeInfo(typeof(StrongTranslationResponse)).Should().NotBeNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            response, AppJsonSerializerContext.Default.StrongEntryResponse));
        json.RootElement.GetProperty("definition").GetString().Should().Be("love");
        var translated = json.RootElement.GetProperty("translation");
        translated.GetProperty("definition").GetString().Should().Be("Liebe");
        translated.GetProperty("source").GetString().Should().Contain("prompt-version");
        translated.GetProperty("note").GetString().Should().Be("a stated uncertainty");
        translated.GetProperty("confidence").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private sealed class TranslationCommands : DbCommandInterceptor
    {
        public int Reads { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM strong_entry_translation", StringComparison.Ordinal))
            {
                Reads++;
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
