using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Strong's dictionary in a reader's language, from the file a translation run publishes to the row
/// that stands beside the English.
///
/// The fixtures are written here rather than taken from the corpus: the real file is the output of
/// a five-hour model run and is on almost no disk, and a loader that cannot be exercised without it
/// is a loader nobody exercises. What matters in every case below is the same thing — the prose may
/// change language, and the addresses inside it may not.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongTranslationLoadTests : IDisposable
{
    private const string Model = "claude-sonnet-5";

    private const string Prompt = "lexicon-3";

    private readonly AppDbContext _db;

    private readonly IDbContextTransaction _transaction;

    private readonly string _directory;

    public StrongTranslationLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _directory = Directory.CreateTempSubdirectory("essenthos-lexicon").FullName;
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task TheTranslationStandsBesideTheEnglishAndNamesWhatMadeIt()
    {
        English("H430", definition: "gods in the ordinary sense", derivation: "plural of אֱלוֹהַּ (H433);");
        Published("H430", definition: "боги у звичайному значенні", derivation: "множина від אֱלוֹהַּ (H433);");

        var outcome = await Load();

        outcome.Written.Should().Be(1);
        outcome.Refused.Total.Should().Be(0);

        var translated = await _db.StrongEntryTranslations.SingleAsync(t => t.StrongNumber == "H430");
        translated.Language.Should().Be("ukr");
        translated.Definition.Should().Be("боги у звичайному значенні");
        translated.Method.Should().Be(LinkMethod.ModelReading);
        translated.Source.Should().Contain(Model).And.Contain(Prompt).And.Contain("2026-09-06");

        var english = await _db.StrongEntries.SingleAsync(e => e.StrongNumber == "H430");
        english.Definition.Should().Be("gods in the ordinary sense");
    }

    /// <summary>
    /// The failure the whole exercise is measured on. A dropped <c>H433</c> reads perfectly and
    /// points nowhere, and nothing downstream could tell.
    /// </summary>
    [Fact]
    public async Task ATranslationThatDroppedAStrongReferenceIsRefused()
    {
        English("H430", derivation: "plural of אֱלוֹהַּ (H433);");
        Published("H430", derivation: "множина від елоах;");

        var outcome = await Load();

        outcome.Written.Should().Be(0);
        outcome.Refused.LostReference.Should().Be(1);
        (await _db.StrongEntryTranslations.AnyAsync(t => t.StrongNumber == "H430"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ATranslationThatMergedTwoSensesIsRefused()
    {
        English("H1", detailed: "1) father\n1a) of an individual\n1b) of God");
        Published("H1", detailed: "1) батько\n1a) окремої особи, Бога");

        var outcome = await Load();

        outcome.Written.Should().Be(0);
        outcome.Refused.LostSense.Should().Be(1);
    }

    [Fact]
    public async Task ANumberTheDictionaryDoesNotHoldIsRefusedAndCounted()
    {
        English("H430", definition: "gods in the ordinary sense");
        Published("H9999", definition: "щось");

        var outcome = await Load();

        outcome.Written.Should().Be(0);
        outcome.Refused.UnknownNumber.Should().Be(1);
    }

    /// <summary>
    /// The guard is per number and per language, so a second language loads beside the first and a
    /// row already there is left exactly as it is. Guarding on whether the table holds anything is
    /// what left another table empty for a whole load (PRB-0343).
    /// </summary>
    [Fact]
    public async Task ASecondLanguageLoadsBesideTheFirstAndTheFirstIsLeftAlone()
    {
        English("H430", definition: "gods in the ordinary sense");
        Published("H430", definition: "боги у звичайному значенні");
        (await Load()).Written.Should().Be(1);

        Published("H430", definition: "Götter im gewöhnlichen Sinne", language: "deu", file: "deu.jsonl");
        Published("H430", definition: "щось інше", file: "uk-again.jsonl");

        var outcome = await Load();

        outcome.Written.Should().Be(1);
        outcome.Skipped.Should().Be(1);

        var rows = await _db.StrongEntryTranslations
            .Where(t => t.StrongNumber == "H430")
            .OrderBy(t => t.Language)
            .ToListAsync();
        rows.Select(r => r.Language).Should().Equal("deu", "ukr");
        rows.Single(r => r.Language == "ukr").Definition.Should().Be("боги у звичайному значенні");
    }

    /// <summary>
    /// The only doubt the run produces per row is the translator's own, and it is kept as it was
    /// given rather than turned into a number nobody measured.
    /// </summary>
    [Fact]
    public async Task TheTranslatorsOwnDoubtIsKeptAndNoConfidenceIsInvented()
    {
        English("G810", definition: "unsavedness");
        Published("G810", definition: "неспасенність", uncertain: ["unsavedness"]);

        await Load();

        var translated = await _db.StrongEntryTranslations.SingleAsync(t => t.StrongNumber == "G810");
        translated.Confidence.Should().BeNull();
        translated.Note.Should().Contain("unsavedness");
    }

    private async Task<TranslationOutcome> Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [StrongTranslationFiles.ConfigurationKey] = _directory,
            })
            .Build();

        var loader = new StrongTranslationLoader(
            _db, configuration, NullLogger<StrongTranslationLoader>.Instance);

        return await loader.Load("unused-because-the-key-is-set");
    }

    private void English(
        string number,
        string? definition = null,
        string? derivation = null,
        string? detailed = null)
    {
        _db.StrongEntries.Add(new StrongEntry
        {
            StrongNumber = number,
            Definition = definition,
            Derivation = derivation,
            DetailedDefinition = detailed,
        });
        _db.SaveChanges();
    }

    private void Published(
        string number,
        string? definition = null,
        string? derivation = null,
        string? detailed = null,
        string language = "ukr",
        string file = "ukr.jsonl",
        string[]? uncertain = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["strong_number"] = number,
            ["language"] = language,
            ["method"] = StrongTranslationFiles.ModelTranslation,
            ["model"] = Model,
            ["prompt_version"] = Prompt,
            ["translated_at"] = "2026-09-06T17:41:02+00:00",
        };

        Say(row, "definition", definition);
        Say(row, "derivation", derivation);
        Say(row, "detailed_definition", detailed);
        if (uncertain is not null)
        {
            row["uncertain"] = uncertain;
        }

        File.AppendAllText(
            Path.Combine(_directory, file),
            System.Text.Json.JsonSerializer.Serialize(row) + Environment.NewLine,
            Encoding.UTF8);
    }

    private static void Say(Dictionary<string, object?> row, string field, string? value)
    {
        if (value is not null)
        {
            row[field] = value;
        }
    }
}
