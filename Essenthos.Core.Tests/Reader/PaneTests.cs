using System.Text.Json;
using Essenthos.Core.Build;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Indexes;
using Essenthos.Core.Names;
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
        var joshua = new Entity { Kind = EntityKind.Person, Slug = "joshua", Name = "Joshua" };
        var hoshea = new Entity { Kind = EntityKind.Person, Slug = "hoshea", Name = "Hoshea" };
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
    public async Task AWordCarriesItsMarkAndTheLettersBracketedInsideIt()
    {
        var (english, _) = Pair();
        var group = new WordGroup
        {
            TextId = english.Id, Kind = WordGroupKind.Doubtful, Position = 1, Features = JsonDocument.Parse("""{"letters": "s"}"""),
        };
        _db.WordGroups.Add(group);
        _db.SaveChanges();
        _db.WordGroupWords.Add(new WordGroupWord { WordGroupId = group.Id, WordId = _db.WordAt(english, 1, 1, 3).Id });
        _db.SaveChanges();

        var words = (await Pane(english)).GetProperty("verses")[0].GetProperty("words");

        (words[2].GetProperty("m").GetString(), words[2].GetProperty("bk").GetString()).Should().Be(("doubtful", "s"));
        words[1].TryGetProperty("bk", out _).Should().BeFalse();
    }

    /// <summary>
    /// A word with no number of its own carries the number it renders in every text, an original's and a printed edition's
    /// as much as a translation's (NOT-0236): the Ge'ez of 1881 has no numbers of its own, so all its numbers come through its
    /// links. A word with a number of its own shows only that.
    /// </summary>
    [Fact]
    public async Task AWordWithNoNumberOfItsOwnCarriesTheNumberItRendersInEveryText()
    {
        var (english, hebrew) = Pair();
        var geez = Corpus.Add(_db, "GEEZ81", TextKind.PrintedEdition, "gez", (1, 1, ["ወ", "ኢያሱ"]));
        _db.SaveChanges();
        var and = _db.WordAt(english, 1, 1, 1);
        var joshua = _db.WordAt(english, 1, 1, 2);
        joshua.StrongNumber = "H3091";
        var conjunction = _db.WordAt(hebrew, 1, 1, 1);
        var yehoshua = _db.WordAt(hebrew, 1, 1, 2);
        yehoshua.StrongNumber = "H3091";
        var iyasu = _db.WordAt(geez, 1, 1, 2);
        _db.WordRenderedStrongs.AddRange(
            new WordRenderedStrong { VerseId = and.VerseId, WordId = and.Id, StrongNumber = "H9000" },
            new WordRenderedStrong { VerseId = joshua.VerseId, WordId = joshua.Id, StrongNumber = "H3091" },
            new WordRenderedStrong { VerseId = conjunction.VerseId, WordId = conjunction.Id, StrongNumber = "H9000" },
            new WordRenderedStrong { VerseId = yehoshua.VerseId, WordId = yehoshua.Id, StrongNumber = "H3092" },
            new WordRenderedStrong { VerseId = iyasu.VerseId, WordId = iyasu.Id, StrongNumber = "H3091" });
        _db.SaveChanges();

        var words = (await Pane(english)).GetProperty("verses")[0].GetProperty("words");
        var hebrewWords = (await Pane(hebrew)).GetProperty("verses")[0].GetProperty("words");
        var geezWords = (await Pane(geez)).GetProperty("verses")[0].GetProperty("words");

        words[0].GetProperty("rn").GetString().Should().Be("H9000");
        words[0].TryGetProperty("n", out _).Should().BeFalse();
        (words[1].GetProperty("n").GetString(), words[1].TryGetProperty("rn", out _)).Should().Be(("H3091", false), "its own number is written once");
        words[2].TryGetProperty("rn", out _).Should().BeFalse("no link gives it a number");
        hebrewWords[0].GetProperty("rn").GetString().Should().Be("H9000", "an original's word without a number of its own shows the one it renders");
        (hebrewWords[1].GetProperty("n").GetString(), hebrewWords[1].TryGetProperty("rn", out _)).Should().Be(("H3091", false), "a number of its own wins");
        geezWords[0].TryGetProperty("rn", out _).Should().BeFalse("no link gives it a number");
        geezWords[1].GetProperty("rn").GetString().Should().Be("H3091");
        (await ChapterReading.ReadByCanonicalVerse(_db, geez.Id, 1, 1, default))[1][1].RenderedStrong.Should().Be("H3091");
    }

    /// <summary>
    /// «twelve» renders שְׁנֵים and עָשָׂר as one word and carries both numbers, in the order the Hebrew words stand, though the
    /// table holds them in another. Every translation's words carry them, the Synodal's too, whose links are drawn from a
    /// numbering laid onto it (the owner, 2026-11-22), in the pane and in the parallel view alike.
    /// </summary>
    [Fact]
    public async Task AWordCarriesEveryNumberItRendersInItsOriginalsOrderInEveryTranslation()
    {
        var (english, hebrew) = Pair();
        _db.WordAt(hebrew, 1, 1, 1).StrongNumber = "H8147";
        _db.WordAt(hebrew, 1, 1, 2).StrongNumber = "H6240";
        Link(english, 1, hebrew, 1, 0.9);
        Link(english, 1, hebrew, 2, 0.9);
        var synodal = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus", (1, 1, ["двенадцать"]));
        _db.SaveChanges();
        var twelve = _db.WordAt(english, 1, 1, 1);
        var dvenadtsat = _db.WordAt(synodal, 1, 1, 1);
        _db.WordRenderedStrongs.AddRange(
            new WordRenderedStrong { VerseId = twelve.VerseId, WordId = twelve.Id, StrongNumber = "H8147" },
            new WordRenderedStrong { VerseId = twelve.VerseId, WordId = twelve.Id, StrongNumber = "H6240" },
            new WordRenderedStrong { VerseId = dvenadtsat.VerseId, WordId = dvenadtsat.Id, StrongNumber = "H8147" });
        _db.SaveChanges();

        (await Pane(english)).GetProperty("verses")[0].GetProperty("words")[0].GetProperty("rn").GetString().Should().Be("H8147 H6240");
        (await ChapterReading.ReadByCanonicalVerse(_db, english.Id, 1, 1, default))[1][0].RenderedStrong.Should().Be("H8147 H6240");
        (await Pane(synodal)).GetProperty("verses")[0].GetProperty("words")[0].GetProperty("rn").GetString().Should().Be("H8147");
        (await ChapterReading.ReadByCanonicalVerse(_db, synodal.Id, 1, 1, default))[1].Single().RenderedStrong.Should().Be("H8147");
        RenderedNumbers.Join(["H430", "H1004", "G26"]).Should().Be("G26 H430 H1004", "numbers no original word carries go in number order");
        RenderedNumbers.Join(["G3638", "G5144"], [("G5144", 5, 3), ("G2532", 5, 4), ("G03638", 5, 5)])
            .Should().Be("G5144 G3638", "«thirty-eight» over τριάκοντα καὶ ὀκτώ");
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

        var first = await Run(store, Pair);
        first.Where(o => o.Made).Select(o => o.Text).Should().BeEquivalentTo("BSB", "KJV");
        first.Single(o => o.Text == "KJV").Panes.Should().Be(2);
        (await Held(Pair)).Should().Be(3);

        (await Run(store, Pair)).Should().OnlyContain(o => !o.Made, "nothing a pane reads has moved");

        Make(store, "texts/BSB/words", "two");
        var third = await Run(store, Pair);
        third.Where(o => o.Made).Select(o => o.Text).Should().Equal("BSB");
        third.Single(o => o.Text == "BSB").Read.Should().Be(1);
        (await Held(Pair)).Should().Be(3, "a text's panes are replaced, not added to");

        await using (var connection = _database.NewConnection())
        {
            await connection.OpenAsync();
            await using var delete = new Npgsql.NpgsqlCommand("DELETE FROM pane WHERE canonical_chapter = 2", connection);
            await delete.ExecuteNonQueryAsync();
        }

        var fourth = await Run(store, Pair);
        fourth.Where(o => o.Made).Select(o => o.Text).Should().Equal(["KJV"], "the table no longer holds what KJV's product keeps");
        fourth.Single(o => o.Text == "KJV").Should().Match<PaneOutcome>(o => o.Read == 0 && o.Copied == 1, "the body is copied from the product, not read again");
        (await Held(Pair)).Should().Be(3);
    }

    [Fact]
    public async Task ANameOnOneChapterReadsThatChapterAgainAndCopiesTheRest()
    {
        await using (var db = _database.NewContext())
        {
            Corpus.Add(db, "ASV", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning"]), (2, 1, ["Thus"]));
            db.SaveChanges();
        }

        var store = new ProductStore(_root);
        Make(store, "texts/ASV/words", "one");
        (await Run(store, "ASV")).Single().Read.Should().Be(2);

        await using (var connection = _database.NewConnection())
        {
            await connection.OpenAsync();
            await using var delete = new Npgsql.NpgsqlCommand("DELETE FROM pane WHERE text_id = (SELECT id FROM text WHERE slug = 'ASV')", connection);
            await delete.ExecuteNonQueryAsync();
        }

        using (var names = store.Create(NameProducts.Projected("ASV"), ProductKind.Machine, ProjectedName.Schema("ASV"), "test", []))
        {
            names.Write(new ProjectedName(WordKey.Parse("1.1:1.3", "ASV"), "adam", "name", "settled", null, "manual", null, "test"));
            names.Complete();
        }

        var outcome = (await Run(store, "ASV")).Single();
        outcome.Should().Match<PaneOutcome>(o => o.Made && o.Read == 1 && o.Copied == 2, "Genesis 1's names moved and Genesis 2's inputs did not");
        (await Held("ASV")).Should().Be(2);
    }

    /// <summary>
    /// Every translation's panes read its numbers file, the Synodal's too (the owner, 2026-11-22): a new number moves the key
    /// of each and its pane is made again.
    /// </summary>
    [Fact]
    public async Task EveryTranslationsPanesReadItsNumbers()
    {
        await using (var db = _database.NewContext())
        {
            Corpus.Add(db, "YLT", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning"]));
            Corpus.Add(db, "RUSV", TextKind.Translation, "rus", (1, 1, ["В", "начале"]));
            db.SaveChanges();
        }

        var store = new ProductStore(_root);
        Make(store, "texts/YLT/words", "one");
        Make(store, "texts/RUSV/words", "one");
        (await Run(store, "YLT", "RUSV")).Should().OnlyContain(o => o.Made);

        foreach (var text in new[] { "YLT", "RUSV" })
        {
            using var numbers = store.Create(StrongNumberFiles.Product(text), ProductKind.Machine, StrongNumberRow.Schema(text), "test", []);
            numbers.Write(new StrongNumberRow(WordKey.Parse("1.1:1.3", text), "H7225", 1, "BHSA 1.1:1.1", null, null, null));
            numbers.Complete();
        }

        store.Save();
        (await Run(store, "YLT", "RUSV")).Where(o => o.Made).Select(o => o.Text).Should().BeEquivalentTo("YLT", "RUSV");
    }

    [Fact]
    public async Task ARaisedPaneVersionReadsEveryPaneOnceThoughNoInputMoved()
    {
        await using (var db = _database.NewContext())
        {
            Corpus.Add(db, "WEB", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning"]), (2, 1, ["Thus"]));
            db.SaveChanges();
        }

        var store = new ProductStore(_root);
        Make(store, "texts/WEB/words", "one");
        (await Run(store, "WEB")).Single().Read.Should().Be(2);

        MadeUnderAnEarlierVersion(store, PaneBuild.Product("WEB"));
        var raised = (await Run(store, "WEB")).Single();
        raised.Should().Match<PaneOutcome>(o => o.Made && o.Read == 2, "the product was made under an earlier version, so no chapter's key is current");

        (await Run(store, "WEB")).Single().Should().Match<PaneOutcome>(o => !o.Made && o.Read == 0, "the product is now made under this version");
        (await Held("WEB")).Should().Be(2);
    }

    /// <summary>
    /// The product rewritten as the version before this one left it: the same inputs, an earlier <c>pane-inputs</c> note and
    /// other chapter keys (a key holds the version).
    /// </summary>
    private static void MadeUnderAnEarlierVersion(ProductStore store, string product)
    {
        List<PaneRow> rows;
        ProductHeader header;
        using (var reader = store.Open(product, PaneRow.Schema))
        {
            header = reader.Header!;
            rows = [.. reader.Rows()];
        }

        var earlier = (PaneInputs.Version - 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        using (var writer = store.Create(product, ProductKind.Machine, PaneRow.Schema, header.Producer, header.Inputs,
                   annotate => annotate.Notes.AddRange(header.Notes.Select(n => n.Key == "pane-inputs" ? new KeyValuePair<string, string>(n.Key, earlier) : n))))
        {
            foreach (var row in rows)
            {
                writer.Write(row with { Inputs = "earlier " + row.Inputs });
            }

            writer.Complete();
        }

        store.Save();
    }

    private void Make(ProductStore store, string product, string value)
    {
        using var writer = store.Create(product, ProductKind.Machine, Schema, "test", []);
        writer.Write(new Row(value));
        writer.Complete();
    }

    private static readonly string[] Pair = ["KJV", "BSB"];

    private async Task<IReadOnlyList<PaneOutcome>> Run(ProductStore store, params string[] texts)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.NewContext());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var connection = _database.NewConnection();
        return await new PaneBuild(connection.ConnectionString, store, provider, NullLogger.Instance).Run(texts, false, 4, default);
    }

    private async Task<int> Held(params string[] texts)
    {
        await using var db = _database.NewContext();
        return await db.Panes.CountAsync(p => texts.Contains(db.Texts.Single(t => t.Id == p.TextId).Slug));
    }
}
