using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Sword;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Received Text positions on CrossWire's RLT: read beside the Greek forms they name, carried
/// through the reader onto the element's words, and believed only where the witness holds the word
/// the module names at the place.
/// </summary>
public sealed class ReceivedTextPositionTests
{
    /// <summary>John 3:16 as the module writes its first words, positions and forms out of order.</summary>
    internal const string John316 =
        """<w lemma="strong:G1063 lemma.TR:γαρ" src="2">For</w> <w lemma="strong:G3588 strong:G2316 lemma.TR:ο lemma.TR:θεος" src="4 5">God</w> <w lemma="strong:G3779 lemma.TR:ουτως" src="1">so</w> <w lemma="strong:G25 lemma.TR:ηγαπησεν" src="3">loved</w> <w lemma="strong:G3588 strong:G2889 lemma.TR:τον lemma.TR:κοσμον" src="6 7">the world</w>, <w lemma="strong:G5620 lemma.TR:ωστε" src="8">that</w> <w lemma="strong:G1325 lemma.TR:εδωκεν" src="14">he gave</w> <w lemma="strong:G846 lemma.TR:αυτου" src="11">his</w> <w lemma="strong:G3439 lemma.TR:μονογενη" src="13">only begotten</w> <w lemma="strong:G3588 strong:G5207 lemma.TR:τον lemma.TR:υιον" src="9 10">Son</w>,""";

    /// <summary>Scrivener's John 3:16 as far as the module's fragment reaches, unaccented as the corpus holds it.</summary>
    internal static readonly string[] Scrivener =
        ["ουτως", "γαρ", "ηγαπησεν", "ο", "θεος", "τον", "κοσμον", "ωστε", "τον", "υιον", "αυτου", "τον", "μονογενη", "εδωκεν"];

    [Fact]
    public void ThePositionsAreReadBesideTheFormsTheyName()
    {
        ReceivedTextPositions.Read("strong:G3588 strong:G2316 lemma.TR:ο lemma.TR:θεος", "4 5")
            .Should().Equal(new StatedPosition(4, false, "ο"), new StatedPosition(5, false, "θεος"));
    }

    /// <summary>Romans 1:3 ends in English with words the Greek has in 1:4, and says so.</summary>
    [Fact]
    public void APositionInTheNextVerseSaysSo()
    {
        ReceivedTextPositions.Read("strong:G2424 lemma.TR:ιησου", "13n")
            .Should().Equal(new StatedPosition(13, true, "ιησου"));
    }

    [Theory]
    [InlineData("strong:G3588 lemma.TR:τα", "5 6")]
    [InlineData("strong:G3588 lemma.TR:τα", "5x")]
    [InlineData("strong:G3588 lemma.TR:τα", "0")]
    [InlineData("strong:G3588", "5")]
    [InlineData("strong:G3588 lemma.TR:τα", null)]
    public void AnElementThatCannotBeReadOneForOneStatesNothing(string lemma, string? source)
    {
        ReceivedTextPositions.Read(lemma, source).Should().BeEmpty();
    }

    [Fact]
    public void TheReaderCarriesThePositionsOntoEveryWordOfTheElement()
    {
        var unit = 0;
        var words = SwordTextSource.Segment(
            OsisVerse.Parse(John316), SwordSegmentation.Spaced, ref unit, out _, fhlPrefixes: false);

        var gave = words.Where(word => word.Surface is "he" or "gave").ToList();
        gave.Should().HaveCount(2);
        gave.Should().AllSatisfy(word => word.Positions.Should().Equal(new StatedPosition(14, false, "εδωκεν")));
        words.Single(word => word.Surface == "God").Positions!.Select(p => p.Position).Should().Equal(4, 5);
    }

    /// <summary>
    /// John 3:16 laid onto Scrivener: each element reaches the occurrence its position names — his
    /// Son is τὸν υἱὸν at 9 and 10, not the τὸν at 6 or at 12, which a Strong number cannot tell apart.
    /// </summary>
    [Fact]
    public void EachElementReachesTheOccurrenceItsPositionNames()
    {
        var (laid, english, greek) = Lay(Scrivener);

        laid.Agreed.Should().Be(10);
        laid.Disagreed.Should().Be(0);
        Rendering(laid, english, greek, "Son").Should().Equal("τον@9", "υιον@10");
        Rendering(laid, english, greek, "the", "world").Should().Equal("τον@6", "κοσμον@7");
        Rendering(laid, english, greek, "he", "gave").Should().Equal("εδωκεν@14");
    }

    /// <summary>
    /// A witness that divides the verse otherwise holds other words at the places, and the elements
    /// past the difference say nothing rather than point one word off.
    /// </summary>
    [Fact]
    public void APositionHoldingAnotherWordSaysNothing()
    {
        string[] shifted = ["ουτως", "γαρ", "ο", "θεος", "ηγαπησεν", "τον", "κοσμον", "ωστε", "τον", "υιον", "αυτου", "τον", "μονογενη", "εδωκεν"];

        var (laid, english, greek) = Lay(shifted);

        laid.Disagreed.Should().Be(2);
        laid.Agreed.Should().Be(8);
        Rendering(laid, english, greek, "God").Should().BeEmpty();
        Rendering(laid, english, greek, "loved").Should().BeEmpty();
    }

    [Fact]
    public void AWordThePairSaysIsAbsentIsNotLinked()
    {
        var (laid, _, _) = Lay(Scrivener, absent: new HashSet<long> { 1_000 + 1 });

        laid.Absent.Should().Be(1);
        laid.Agreed.Should().Be(9);
    }

    private static (LaidPositions Laid, Dictionary<long, string> English, Dictionary<long, string> Greek) Lay(
        string[] witness, IReadOnlySet<long>? absent = null)
    {
        var unit = 0;
        var words = SwordTextSource.Segment(
            OsisVerse.Parse(John316), SwordSegmentation.Spaced, ref unit, out _, fhlPrefixes: false);
        var english = words.Select((word, at) => (Id: (long)at + 1, word)).ToList();
        var stated = words.Where(word => word.Positions is { Count: > 0 })
            .GroupBy(word => word.Unit)
            .ToDictionary(group => group.Key, group => new StatedPositions(43, 3, 16, group.First().Positions!));
        var greek = witness.Select((word, at) => new CorpusWord(1_000 + at + 1, word)).ToList();

        var laid = ReceivedTextPositions.Lay(
            stated,
            english.Where(entry => entry.word.Positions is { Count: > 0 }).ToDictionary(entry => entry.Id, entry => entry.word.Unit),
            new Dictionary<(int, int, int), IReadOnlyList<CorpusWord>> { [(43, 3, 16)] = greek },
            absent);
        return (
            laid,
            english.ToDictionary(entry => entry.Id, entry => entry.word.Surface),
            greek.ToDictionary(word => word.Id, word => $"{word.Text}@{word.Id - 1_000}"));
    }

    private static List<string> Rendering(
        LaidPositions laid, Dictionary<long, string> english, Dictionary<long, string> greek, params string[] surfaces) =>
        laid.Links
            .Where(link => link.From.Select(id => english[id]).SequenceEqual(surfaces))
            .SelectMany(link => link.To.Select(id => greek[id]))
            .ToList();
}

/// <summary>
/// Which Received Text the module's positions count, measured over the whole New Testament without a
/// database: the module laid onto itself, as the run lays it onto the text loaded from it, against
/// both editions Robinson's composite holds, read the way their loader reads them. Neither is the
/// KJV2003 project's text, and each holds the words at 99.4% of the elements.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class ReceivedTextPositionCorpusTests(ITestOutputHelper output)
{
    [Fact]
    public void ThePositionsHoldTheirWordsInBothPrintedEditions()
    {
        var folder = TestResources.Folder(SwordTextSource.Texts["RLT"].Folder);
        var loaded = SwordTextSource.Read(folder);
        long id = 0;
        var corpus = loaded.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse => new CorpusVerse(
                book.CanonicalOrdinal, chapter.Number, verse.Number, [],
                [.. verse.Words.Select(word => new CorpusWord(++id, word.Surface))]))))
            .ToList();
        var units = SynodalStrongLayer.Lay(SwordTextSource.Numbers(folder), corpus).Tags
            .ToDictionary(tag => tag.Key, tag => tag.Value.Unit);
        var stated = SwordTextSource.Positions(folder);

        var shares = new Dictionary<Edition, LaidPositions>();
        foreach (var edition in new[] { Edition.Scrivener1894, Edition.Stephanus1550 })
        {
            var witness = TextusReceptusTextSource.Read(TestResources.Folder("TextusReceptus"), edition).Books
                .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse => (
                    Address: (book.CanonicalOrdinal, chapter.Number, verse.Number),
                    Words: (IReadOnlyList<CorpusWord>)[.. verse.Words.Select(word => new CorpusWord(++id, word.Surface))]))))
                .ToDictionary(verse => verse.Address, verse => verse.Words);
            shares[edition] = ReceivedTextPositions.Lay(stated, units, witness);
            output.WriteLine($"{edition}: {shares[edition]}");
        }

        output.WriteLine($"{stated.Count} elements state positions; {units.Count(unit => stated.ContainsKey(unit.Value))} words carry them");
        shares.Values.Should().AllSatisfy(laid => laid.Share.Should().BeGreaterThan(0.993, laid.ToString()));
    }
}

/// <summary>
/// The positions written as links: the module's statement over words a number link already names
/// becomes a claim on it, a statement naming other words stands beside the number link and is
/// counted against it, and a second run finds the pair already linked.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CrossWirePositionLinkTests : IDisposable
{
    private const string NumberCredit = "a Strong numbering under test";

    private static readonly SwordPositions Module = CrossWirePositionLinkLoader.All[0];

    private readonly AppDbContext _db;
    private readonly CrossWirePositionLinkLoader _loader;
    private readonly Text _english;
    private readonly Text _greek;
    private readonly Dictionary<long, int> _units;
    private readonly Dictionary<int, StatedPositions> _stated;

    public CrossWirePositionLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        var unit = 0;
        var words = SwordTextSource.Segment(
            OsisVerse.Parse(ReceivedTextPositionTests.John316), SwordSegmentation.Spaced, ref unit, out _, fhlPrefixes: false);
        _english = Corpus.Add(_db, Module.Slug, TextKind.Translation, "eng", (3, 16, [.. words.Select(w => w.Surface)]));
        _greek = Corpus.Add(_db, Sources.ScrivenerSlug, TextKind.PrintedEdition, "grc", (3, 16, ReceivedTextPositionTests.Scrivener));
        _db.SaveChanges();
        _loader = new CrossWirePositionLinkLoader(
            _db,
            new UnionStrongLinkLoader(_db, new TaggedTextLinkLoader(_db, NullLogger<TaggedTextLinkLoader>.Instance),
                NullLogger<UnionStrongLinkLoader>.Instance),
            NullLogger<CrossWirePositionLinkLoader>.Instance);

        _units = words.Select((word, at) => (Id: English(at + 1).Id, word.Unit))
            .ToDictionary(entry => entry.Id, entry => entry.Unit);
        _stated = words.Where(word => word.Positions is { Count: > 0 })
            .GroupBy(word => word.Unit)
            .ToDictionary(group => group.Key, group => new StatedPositions(1, 3, 16, group.First().Positions!));
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    private Word English(int position) => _db.WordAt(_english, 3, 16, position);

    private Word Greek(int position) => _db.WordAt(_greek, 3, 16, position);

    private Word EnglishWord(string surface) =>
        _db.Words.Single(w => w.TextId == _english.Id && w.Surface == surface);

    private void Numbered(Word english, params Word[] greek)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _greek.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber,
            Confidence = StrongNumberMatch.Unambiguous,
            Provenance = new() { Source = NumberCredit },
            Fingerprint = LinkShape.Of([english.Id], [.. greek.Select(w => w.Id)]),
        };
        link.Words.Add(new LinkWord { WordId = english.Id, Side = LinkSide.From });
        foreach (var word in greek)
        {
            link.Words.Add(new LinkWord { WordId = word.Id, Side = LinkSide.To });
        }

        _db.Links.Add(link);
        _db.SaveChanges();
        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id, Method = link.Method, Confidence = link.Confidence, ProvenanceId = link.ProvenanceId,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task ThePositionsAreWrittenBesideTheNumbersAndOnce()
    {
        Numbered(EnglishWord("God"), Greek(4), Greek(5));
        Numbered(EnglishWord("Son"), Greek(12), Greek(10));

        (await _loader.Linked(Module, Sources.ScrivenerSlug)).Should().BeFalse();
        var outcome = await _loader.Write(Module, Sources.ScrivenerSlug, _units, _stated);

        outcome.Laid!.Agreed.Should().Be(10);
        outcome.Corroborated.Should().Be(1);
        outcome.Written.Should().Be(9);
        outcome.Contradicted.Should().Be(1);

        _db.ChangeTracker.Clear();
        var god = _db.Links.Include(l => l.Provenance).Include(l => l.Claims)
            .Single(l => l.Words.Any(w => w.WordId == EnglishWord("God").Id));
        god.Method.Should().Be(LinkMethod.StatedBySource);
        god.Confidence.Should().BeNull();
        god.Provenance!.Source.Should().Be(Module.Credit);
        god.Claims.Should().HaveCount(2);

        var son = _db.Links.Where(l => l.Method == LinkMethod.StatedBySource
                                       && l.Words.Any(w => w.WordId == EnglishWord("Son").Id))
            .SelectMany(l => l.Words.Where(w => w.Side == LinkSide.To).Select(w => w.Word!.Position))
            .ToList();
        son.Should().BeEquivalentTo([9, 10]);
        _db.Links.Count(l => l.Method == LinkMethod.StrongNumber).Should().Be(1, "the number link that picked another τόν stands");

        (await _loader.Linked(Module, Sources.ScrivenerSlug)).Should().BeTrue();
    }
}
