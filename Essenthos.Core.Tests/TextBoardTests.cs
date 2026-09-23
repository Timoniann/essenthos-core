using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Desk;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The console's texts: that the census counts what the corpus holds — each text's size, what its
/// words carry and how far they are linked, and by what — that the last load's check is handed over
/// for the text it is about, that a problem is matched to the texts it names, and that the translation
/// the reader opens in is written where the API reads it and logged.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextBoardTests : IDisposable
{
    private const string Translation = "TSTA";

    private const string Original = "TSTB";

    private const string BereanSource = "Berean Standard Bible translation tables, bereanbible.com, public domain";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"texts-{Guid.NewGuid():n}");

    public TextBoardTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task TheCensusCountsWhatEachTextsWordsCarry()
    {
        Seed();

        var census = await TextCensus.Count(_db.Database.GetDbConnection(), inTransaction: true, default);

        var translation = census.Texts.Single(t => t.Text == Translation);
        (translation.Books, translation.Chapters, translation.Verses, translation.Words).Should().Be((1, 1, 2, 5));
        Feature(translation, "strong").Should().Be(1);
        Feature(translation, "lemma").Should().Be(2);
        Feature(translation, "paragraphs").Should().Be(1);
        Feature(translation, "supplied").Should().Be(1);
        Feature(translation, "notes").Should().Be(1);
        Feature(translation, "morphology").Should().Be(0, "a feature the text does not carry is counted as nothing, not left out");
        translation.Features.Single(f => f.Key == "notes").Parts.Should().Equal(new KeyCount("footnote", 1));

        var original = census.Texts.Single(t => t.Text == Original);
        Feature(original, "morphology").Should().Be(2);
        original.Morphology.Should().Equal(new KeyCount("pos", 2), new KeyCount("gender", 1));
        census.BookNames.Should().ContainSingle(b => b.Ordinal == 1).Which.Name.Should().Be("Genesis");
    }

    [Fact]
    public async Task TheCensusSaysHowFarATextIsLinkedAndByWhat()
    {
        Seed();

        var census = await TextCensus.Count(_db.Database.GetDbConnection(), inTransaction: true, default);

        var translation = census.Texts.Single(t => t.Text == Translation);
        translation.Linked.Should().Be(3, "three of its five words are named by a link");
        translation.LinkedVerses.Should().Be(1);
        var link = translation.Links.Should().ContainSingle().Which;
        (link.Other, link.Words, link.Promised, link.Links, link.Corroborated, link.VerseLinks).Should().Be((Original, 3, 5, 2, 1, 1));
        link.PerBook.Should().Equal(new BookLinkCount(1, 5, 3));

        var stated = link.Methods.Single(m => m.Method == "stated-by-source");
        (stated.Links, stated.Confidence).Should().Be((1, null));
        stated.Credits.Should().Equal("Berean Standard Bible");
        var aligned = link.Methods.Single(m => m.Method == "aligner");
        (aligned.Links, aligned.Confidence).Should().Be((1, 0.5));
        aligned.Credits.Should().BeEmpty("an aligner's run is our own inference, not somebody's statement");

        var other = census.Texts.Single(t => t.Text == Original).Links.Should().ContainSingle().Which;
        (other.Other, other.Words, other.Promised).Should().Be((Translation, 2, 3));
    }

    [Fact]
    public async Task TheCensusCountsWithoutWritingAnything()
    {
        Seed();
        var before = await _db.Texts.CountAsync();

        await TextCensus.Count(_db.Database.GetDbConnection(), inTransaction: true, default);

        (await _db.Texts.CountAsync()).Should().Be(before);
        (await _db.Links.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task TheLastChecksMeasuresAreHandedOverForTheTextTheyAreAbout()
    {
        Seed();
        _db.VerificationRuns.Add(new VerificationRun
        {
            RanAt = new DateTimeOffset(2999, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Measures = JsonDocument.Parse(
                $$"""
                {
                  "coverage": [{ "text": "{{Translation}}", "share": 0.9 }, { "text": "OTHER", "share": 0.1 }],
                  "reach": [{ "from": "{{Translation}}", "witness": "{{Original}}", "share": 0.8 }],
                  "pairing": [{ "text": "OTHER", "against": "{{Original}}", "suspect": 3 }]
                }
                """),
        });
        await _db.SaveChangesAsync();

        var translation = (await TextBoard.Verification(_db, Translation, default))!;
        translation.RanAt.Should().Be("2999-01-01T00:00:00Z");
        translation.Measures["coverage"]!.AsArray().Should().ContainSingle();
        translation.Measures["reach"]!.AsArray().Should().ContainSingle();
        translation.Measures["pairing"]!.AsArray().Should().BeEmpty("the pairing measured is another text's");

        var witness = (await TextBoard.Verification(_db, Original, default))!;
        witness.Measures["reach"]!.AsArray().Should().BeEmpty();
        witness.Measures["reachedBy"]!.AsArray().Should().ContainSingle("a witness is measured by what reaches it");

        (await TextBoard.Verification(_db, "NOSUCH", default)).Should().BeNull();
    }

    [Fact]
    public void AProblemIsMatchedToTheTextsItNamesAndToNoOther()
    {
        var texts = new[]
        {
            new NamedText("KJV", "King James Version", []),
            new NamedText("WEB", "World English Bible", []),
            new NamedText("RUSV", "Russian Synodal Version", ["SYNO"]),
            new NamedText("NESTLE1904", "Nestle 1904 Greek New Testament", []),
        };
        JsonObject Problem(string id, string title, string? summary = null) =>
            new() { ["id"] = id, ["title"] = title, ["summary"] = summary, ["status"] = "open", ["severity"] = "medium" };

        var matched = TextProblems.Match(
        [
            Problem("PRB-1", "The King James lowercases the divine name"),
            Problem("PRB-2", "The web client drops a pane", "Nothing about any text in essenthos-web."),
            Problem("PRB-3", "Two words", "The Synodal and nestle1904 disagree."),
            Problem("PRB-4", "The WEB psalm titles sit inside verse 1"),
        ], texts);

        matched.Select(p => (p.Id, string.Join(",", p.Texts))).Should().BeEquivalentTo(
        [
            ("PRB-1", "KJV"),
            ("PRB-3", "RUSV,NESTLE1904"),
            ("PRB-4", "WEB"),
        ]);
    }

    [Fact]
    public async Task TheTranslationTheReaderOpensInIsWrittenWhereTheApiReadsItAndLogged()
    {
        var paths = new DeskPaths(_root, Path.Combine(_root, "Resources"), _root);
        var switches = new SiteSwitches(paths, new ChangeLog(paths));
        SiteSettings.ReadChoices(paths.SiteSettings)[SiteSettings.ReaderTranslation].Should().Be("BSB");

        await switches.Choose(SiteSettings.ReaderTranslation, "KJV", "texts", "King James Version", "для перевірки");

        SiteSettings.ReadChoices(paths.SiteSettings)[SiteSettings.ReaderTranslation].Should().Be("KJV");
        SiteSettings.Read(paths.SiteSettings).Should().BeEquivalentTo(SiteSettings.Defaults(), "a choice leaves the switches as they were");
        var served = new Essenthos.Core.Endpoints.SiteSettingsFile(
            paths.SiteSettings, Microsoft.Extensions.Logging.Abstractions.NullLogger<Essenthos.Core.Endpoints.SiteSettingsFile>.Instance);
        served.Choices[SiteSettings.ReaderTranslation].Should().Be("KJV");

        var logged = new ChangeLog(paths).Read().Entries.Should().ContainSingle().Which;
        (logged.Section, logged.Action, logged.Target, logged.Label, logged.Needs)
            .Should().Be(("texts", "choice", "setting/readerTranslation", "King James Version", null));
        (logged.Before!.GetValue<string>(), logged.After!.GetValue<string>(), logged.Note).Should().Be(("BSB", "KJV", "для перевірки"));

        await switches.Choose(SiteSettings.ReaderTranslation, "KJV", "texts", "King James Version", null);
        new ChangeLog(paths).Read().Entries.Should().ContainSingle("choosing what is already chosen changes nothing");
    }

    [Fact]
    public void AChoiceTheFileDoesNotStateAsTextIsAtItsDefault()
    {
        SiteSettings.ChoicesFrom(JsonNode.Parse("""{ "settings": { "readerTranslation": true } }"""))[SiteSettings.ReaderTranslation]
            .Should().Be("BSB");
        SiteSettings.ChoicesFrom(JsonNode.Parse("""{ "settings": { "readerTranslation": " UBIO " } }"""))[SiteSettings.ReaderTranslation]
            .Should().Be("UBIO");
        SiteSettings.ChoicesFrom(null).Should().BeEquivalentTo(SiteSettings.ChoiceDefaults());
    }

    private static int Feature(TextCount text, string key) => text.Features.Single(f => f.Key == key).Count;

    /// <summary>
    /// A translation of two verses and an original of two, joined by one stated link another method
    /// agrees with and one aligner's link, with a verse link, a note, a supplied word and a paragraph.
    /// </summary>
    private void Seed()
    {
        var translation = Corpus.Add(_db, Translation, TextKind.Translation, "eng", (1, 1, ["a", "b", "c"]), (1, 2, ["d", "e"]));
        var original = Corpus.Add(_db, Original, TextKind.CriticalEdition, "hbo", (1, 1, ["x", "y"]), (1, 2, ["z"]));
        _db.SaveChanges();

        var a = _db.WordAt(translation, 1, 1, 1);
        var b = _db.WordAt(translation, 1, 1, 2);
        var d = _db.WordAt(translation, 1, 2, 1);
        a.StrongNumber = "H7225";
        a.Lemma = "a";
        b.Lemma = "b";
        a.Break = TextBreak.Paragraph;

        var x = _db.WordAt(original, 1, 1, 1);
        var y = _db.WordAt(original, 1, 1, 2);
        var z = _db.WordAt(original, 1, 2, 1);
        x.Morphology = JsonDocument.Parse("""{ "pos": "noun", "gender": "m" }""");
        y.Morphology = JsonDocument.Parse("""{ "pos": "verb" }""");

        var stated = new Link
        {
            FromTextId = translation.Id, ToTextId = original.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Source = BereanSource,
        };
        stated.Words.Add(new LinkWord { WordId = a.Id, Side = LinkSide.From });
        stated.Words.Add(new LinkWord { WordId = b.Id, Side = LinkSide.From });
        stated.Words.Add(new LinkWord { WordId = x.Id, Side = LinkSide.To });
        stated.Claims.Add(new LinkClaim { Method = LinkMethod.StatedBySource, Source = BereanSource });
        stated.Claims.Add(new LinkClaim { Method = LinkMethod.Aligner, Confidence = 0.9, Source = "SIL.Machine, a test run" });

        var aligned = new Link
        {
            FromTextId = translation.Id, ToTextId = original.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner, Confidence = 0.5, Source = "SIL.Machine, a test run",
        };
        aligned.Words.Add(new LinkWord { WordId = d.Id, Side = LinkSide.From });
        aligned.Words.Add(new LinkWord { WordId = z.Id, Side = LinkSide.To });
        aligned.Claims.Add(new LinkClaim { Method = LinkMethod.Aligner, Confidence = 0.5, Source = "SIL.Machine, a test run" });
        _db.Links.AddRange(stated, aligned);

        var verses = new VerseLink
        {
            FromTextId = translation.Id, ToTextId = original.Id, Relation = LinkRelation.Equals,
            Method = LinkMethod.StatedBySource, Source = "a test frame",
        };
        verses.Verses.Add(new VerseLinkVerse { VerseId = _db.VerseAt(translation, 1, 1).Id, Side = LinkSide.From });
        verses.Verses.Add(new VerseLinkVerse { VerseId = _db.VerseAt(original, 1, 1).Id, Side = LinkSide.To });
        _db.VerseLinks.Add(verses);

        _db.VerseNotes.Add(new VerseNote
        {
            VerseId = _db.VerseAt(translation, 1, 1).Id, Position = 1, Kind = VerseNoteKind.Footnote, Content = "Or, a note",
        });
        var supplied = new WordGroup { TextId = translation.Id, Kind = WordGroupKind.Supplied, Position = 1 };
        supplied.Words.Add(new WordGroupWord { WordId = _db.WordAt(translation, 1, 2, 2).Id });
        _db.WordGroups.Add(supplied);
        _db.SaveChanges();
    }
}
