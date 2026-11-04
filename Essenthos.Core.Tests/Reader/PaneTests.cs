using System.Text.Json;
using Essenthos.Core.Build;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>A pane holds what the parallel view reads of one text's chapter, in the compact shape the reader decodes.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PaneTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public PaneTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task APaneHoldsTheWordsAsTheParallelViewReadsThem()
    {
        var (english, hebrew) = Pair();
        Link(english, 2, hebrew, 2, 0.83);

        var pane = await Pane(english);
        var read = (await ChapterReading.ReadByCanonicalVerse(_db, english.Id, 1, 1, default))[1];

        pane.GetProperty("text").GetString().Should().Be("KJV");
        pane.GetProperty("chapters").GetInt32().Should().Be(1);
        var verse = pane.GetProperty("verses").EnumerateArray().Single();
        verse.GetProperty("n").GetInt32().Should().Be(1);
        verse.GetProperty("own").EnumerateArray().Select(v => v.GetString()).Should().Equal("1:1");
        verse.GetProperty("ref").EnumerateArray().Select(v => v.GetInt32()).Should().Equal(1, 1, 1);
        var words = verse.GetProperty("words").EnumerateArray().ToList();
        words.Select(w => w.GetProperty("s").GetString()).Should().Equal(read.Select(w => w.Text));
        words.Select(w => w.GetProperty("id").GetInt64()).Should().Equal(read.Select(w => w.Id));
        words[1].GetProperty("w").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(read[1].OriginalWordIds);
        pane.GetProperty("provenance")[words[1].GetProperty("p").GetInt32()].GetString().Should().Be("aligner:0.83");
        words[0].TryGetProperty("w", out _).Should().BeFalse("a word linked to nothing carries no witnesses at all");
    }

    [Fact]
    public async Task AWordThatNamesARecordCarriesItsRoleAndOriginAndTheBaseTextsReadingAsASecondCard()
    {
        var (english, hebrew) = Pair();
        var joshua = new Entity { Kind = EntityKind.Person, Slug = "joshua", Name = "Joshua", SourceId = "test:joshua", Source = "test" };
        var hoshea = new Entity { Kind = EntityKind.Person, Slug = "hoshea", Name = "Hoshea", SourceId = "test:hoshea", Source = "test" };
        var word = _db.WordAt(english, 1, 1, 3);
        _db.WordEntities.AddRange(
            new WordEntity
            {
                Word = word, Entity = joshua, Method = LinkMethod.Manual, Source = "an override", Role = WordEntityRoles.Name,
                Origin = WordEntityOrigins.Differs,
            },
            new WordEntity
            {
                Word = word, Entity = hoshea, Method = LinkMethod.StrongNumber, Confidence = 0.99, Source = "the base text",
                Note = "through BHSA word 1", Role = WordEntityRoles.Name, Origin = WordEntityOrigins.BaseReading,
            });
        _db.SaveChanges();

        var pane = await Pane(english);

        var records = pane.GetProperty("records").EnumerateArray().Select(r => r[0].GetString()).ToList();
        var named = pane.GetProperty("verses")[0].GetProperty("words")[2];
        var card = named.GetProperty("e").EnumerateArray().Single();
        records[card.GetProperty("r").GetInt32()].Should().Be("joshua");
        card.GetProperty("role").GetString().Should().Be("name");
        card.GetProperty("origin").GetString().Should().Be("differs");
        var second = named.GetProperty("eb").EnumerateArray().Single();
        records[second.GetProperty("r").GetInt32()].Should().Be("hoshea");
        second.GetProperty("origin").GetString().Should().Be("base-reading");
        pane.GetProperty("sources")[second.GetProperty("src").GetInt32()].GetString().Should().Be("the base text");
        second.GetProperty("via").GetString().Should().Be("through BHSA word 1");
        records.Should().HaveCount(2, "a record is listed once per pane");
    }

    [Fact]
    public async Task AChapterTheTextHasNothingInHasNoPane()
    {
        var (english, _) = Pair();

        (await Panes.Read(_db, english.Id, english.Slug, 1, 2, 1, default)).Should().BeNull();
    }

    [Fact]
    public void ATextHasAPaneForEveryChapterItHoldsAndForEachChapterHoldingOneOfItsPassagesUnderAnotherName()
    {
        Panes.Chapters([(LetterOfJeremiah.Baruch, LetterOfJeremiah.InBaruch), (1, 1)])
            .Should().Equal((1, 1), (LetterOfJeremiah.Baruch, LetterOfJeremiah.InBaruch), (LetterOfJeremiah.Book, LetterOfJeremiah.Chapter));
    }

    [Fact]
    public void ABodyIsStoredCompressedAndHashedByWhatItSays()
    {
        var json = "{\"format\":1}"u8.ToArray();

        var (gzip, hash) = Panes.Pack(json);

        Panes.Unpack(gzip).Should().Equal(json);
        hash.Should().HaveLength(16).And.Be(Panes.Pack(json).Hash);
    }

    private async Task<JsonElement> Pane(Text text)
    {
        var body = await Panes.Read(_db, text.Id, text.Slug, 1, 1, 1, default);
        body.Should().NotBeNull();
        return JsonDocument.Parse(Panes.Unpack(body!.Gzip)).RootElement;
    }

    private (Text English, Text Hebrew) Pair()
    {
        var english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["And", "Joshua", "Joshua"]));
        var hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo", (1, 1, ["וַ", "יְהוֹשֻׁעַ"]));
        _db.SaveChanges();
        return (english, hebrew);
    }

    private void Link(Text from, int fromPosition, Text to, int toPosition, double confidence)
    {
        var link = new Link
        {
            FromTextId = from.Id, ToTextId = to.Id, Relation = LinkRelation.Renders, Method = LinkMethod.Aligner,
            Provenance = new() { Source = "a test" }, Confidence = confidence,
        };
        _db.Links.Add(link);
        _db.SaveChanges();
        _db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = _db.WordAt(from, 1, 1, fromPosition).Id, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = _db.WordAt(to, 1, 1, toPosition).Id, Side = LinkSide.To });
        _db.SaveChanges();
    }
}

/// <summary>
/// The pane stage of the build: every text's panes the first time, none when nothing moved, and only the panes of a text
/// one of whose inputs moved; each text's panes replaced whole.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PaneBuildTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public PaneBuildTests(WitnessDatabase database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed record Row(string Value);

    private static readonly ProductSchema<Row> Schema = new ProductSchema<Row>().Text("value", r => r.Value).Reads(r => new Row(r.Text(0)));

    [Fact]
    public async Task OnlyTheTextsWhoseInputsMovedAreMadeAgain()
    {
        await using (var db = _database.NewContext())
        {
            Corpus.Add(db, "KJV", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning"]), (2, 1, ["Thus"]));
            Corpus.Add(db, "BSB", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning"]));
            db.SaveChanges();
        }

        var store = new ProductStore(_root);
        Make(store, "texts/KJV/words", "one");
        Make(store, "texts/BSB/words", "one");

        var first = await Run(store);
        first.Where(o => o.Made).Select(o => o.Text).Should().BeEquivalentTo("BSB", "KJV");
        first.Single(o => o.Text == "KJV").Panes.Should().Be(2);
        (await Held()).Should().Be(3);

        (await Run(store)).Should().OnlyContain(o => !o.Made, "nothing a pane reads has moved");

        Make(store, "texts/BSB/words", "two");
        var third = await Run(store);
        third.Where(o => o.Made).Select(o => o.Text).Should().Equal("BSB");
        (await Held()).Should().Be(3, "a text's panes are replaced, not added to");

        await using (var connection = _database.NewConnection())
        {
            await connection.OpenAsync();
            await using var delete = new Npgsql.NpgsqlCommand("DELETE FROM pane WHERE canonical_chapter = 2", connection);
            await delete.ExecuteNonQueryAsync();
        }

        (await Run(store)).Where(o => o.Made).Select(o => o.Text).Should().Equal(["KJV"], "the table no longer holds what KJV's product lists");
    }

    private void Make(ProductStore store, string product, string value)
    {
        using var writer = store.Create(product, ProductKind.Machine, Schema, "test", []);
        writer.Write(new Row(value));
        writer.Complete();
    }

    private async Task<IReadOnlyList<PaneOutcome>> Run(ProductStore store)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.NewContext());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var connection = _database.NewConnection();
        return await new PaneBuild(connection.ConnectionString, store, provider, NullLogger.Instance).Run(null, false, 4, default);
    }

    private async Task<int> Held()
    {
        await using var db = _database.NewContext();
        return await db.Panes.CountAsync();
    }
}
