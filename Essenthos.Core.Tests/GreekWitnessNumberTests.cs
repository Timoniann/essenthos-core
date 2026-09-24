using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Joda of Luke 3:26, whom the critical editions tag with the number of the land of Judah.</summary>
public class GreekWitnessNumberReadingTests
{
    private const int Luke = 42;

    private static TextSource Luke3(params (int Verse, string Surface, string Number)[] words) =>
        new(NestleTextSource.Definition,
        [
            new BookDraft(Luke, 1, "Luke", "luk",
            [
                new ChapterDraft(3,
                [
                    .. words.Select(w => new VerseDraft(w.Verse, [new WordDraft(w.Surface, " ", StrongNumber: w.Number)])),
                ]),
            ]),
        ]);

    private static string? Number(TextSource source, int verse) =>
        source.Books[0].Chapters[0].Verses.Single(v => v.Number == verse).Words[0].StrongNumber;

    [Theory]
    [InlineData("Ἰωδὰ")]
    [InlineData("ιωδα")]
    public void JodaIsReadUnderTheNumberOfTheMan(string printed) =>
        Number(GreekWitnessNumbers.Apply(Luke3((26, printed, "G2448"))), 26).Should().Be("G2455");

    /// <summary>
    /// The three editions as they are read: Joda under the man's number, and the land of Luke 1:39
    /// still under its own.
    /// </summary>
    [Fact]
    public void EveryCriticalEditionReadsJodaAsTheMan()
    {
        foreach (var edition in new[]
                 {
                     NestleTextSource.Read(TestResources.Nestle1904),
                     TischendorfTextSource.Read(TestResources.TischendorfFolder),
                     WestcottHortTextSource.Read(TestResources.WestcottHortFolder),
                 })
        {
            var luke = edition.Books.Single(book => book.CanonicalOrdinal == Luke);
            Numbers(luke, 3, 26).Should().Contain("G2455", edition.Definition.Slug).And.NotContain("G2448");
            Numbers(luke, 1, 39).Should().Contain("G2448", edition.Definition.Slug);
        }
    }

    private static IEnumerable<string?> Numbers(BookDraft book, int chapter, int verse) =>
        book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse).Words
            .Select(word => word.StrongNumber);

    /// <summary>The region keeps its number wherever it is the region, and so does any other word.</summary>
    [Fact]
    public void EveryOtherWordKeepsTheNumberItCarries()
    {
        var read = GreekWitnessNumbers.Apply(Luke3((30, "Ἰούδα", "G2455"), (26, "Ἰωσὴχ", "G2501")));

        Number(read, 30).Should().Be("G2455");
        Number(read, 26).Should().Be("G2501");
    }
}

/// <summary>The same reading made on a corpus that loaded the editions before it.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GreekWitnessNumberMigrationTests : IDisposable
{
    private const int Luke = 42;

    private const string BibleData = "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public GreekWitnessNumberMigrationTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry");
    }

    /// <summary>
    /// Nestle's word named as the land, and the Textus Receptus word beside it carrying that name
    /// across the link as well as Joda's own. After the migration the land is off both, and the
    /// namesake pass names Nestle's word Joda because his is the one name of G2455 the verse lists.
    /// </summary>
    [Fact]
    public async Task JodaIsTheManInEveryTextAndTheLandIsNamedNowhereInHisVerse()
    {
        var nestle = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (3, 26, ["Ἰωδὰ"]));
        var scrivener = Corpus.Add(_db, "TR1894", TextKind.CriticalEdition, "grc", (3, 26, ["ιουδα"]));
        _db.SaveChanges();
        _db.In(nestle, Luke);
        _db.In(scrivener, Luke);

        var joda = Word(nestle, "G2448", "ιωδα");
        var judas = Word(scrivener, "G2455", "ιουδα");

        _db.StrongEntries.Add(new StrongEntry { StrongNumber = "G2448", Lemma = "Ἰουδά" });
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = "G2455", Lemma = "Ἰούδας" });
        var land = Named("judah-6", EntityKind.Place, "G2448", "Ἰουδά");
        var man = Named("joda", EntityKind.Person, "G2455", "Ἰωδά");
        var apostle = Named("judas", EntityKind.Person, "G2455", "Ἰούδας");
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = man, CanonicalBook = Luke, CanonicalChapter = 3, CanonicalVerse = 26, Source = BibleData,
        });
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = apostle, CanonicalBook = Luke, CanonicalChapter = 6, CanonicalVerse = 16, Source = BibleData,
        });
        Annotate(joda, land);
        Annotate(judas, land);
        Annotate(judas, man);
        await _db.SaveChangesAsync();

        await Migrate();
        await Migrate();

        (await _db.Words.AsNoTracking().SingleAsync(w => w.Id == joda.Id)).StrongNumber.Should().Be("G2455");
        (await _db.WordEntities.AnyAsync(a => a.EntityId == land.Id)).Should().BeFalse();
        (await _db.WordEntities.AnyAsync(a => a.WordId == judas.Id && a.EntityId == man.Id)).Should().BeTrue();

        await new GreekNamesakeLoader(_db, NullLogger<GreekNamesakeLoader>.Instance).Load();

        var named = await _db.WordEntities.AsNoTracking().SingleAsync(a => a.WordId == joda.Id);
        named.EntityId.Should().Be(man.Id);
    }

    private Word Word(Text text, string number, string folded)
    {
        var word = _db.WordAt(text, 3, 26, 1);
        word.StrongNumber = number;
        word.NormalisedText = folded;
        word.Morphology = JsonDocument.Parse("""{"pos": "noun"}""");
        _db.SaveChanges();
        return word;
    }

    private Entity Named(string slug, EntityKind kind, string number, string greek)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = slug, Greek = greek, GreekStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Annotate(Word word, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.Lexical, Confidence = 0.99,
            Source = "a test",
        });

    /// <summary>The migration's statements, run in one transaction as the migrator runs them.</summary>
    private async Task Migrate()
    {
        var builder = new MigrationBuilder(_db.Database.ProviderName);
        typeof(AWitnessNumberIsReadAsTheWord)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AWitnessNumberIsReadAsTheWord(), [builder]);

        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var operation in builder.Operations.OfType<SqlOperation>())
        {
            await using var command = new NpgsqlCommand(operation.Sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        _db.ChangeTracker.Clear();
    }
}
