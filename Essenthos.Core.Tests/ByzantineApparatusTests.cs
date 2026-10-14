using Essenthos.Core.Byzantine;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// Robinson's apparatus: read out of the CCAT beta code, each note placed on the words it is about,
/// and linked to a loaded witness only where the witness prints the note's reading at the place.
/// </summary>
public sealed class ByzantineApparatusTests
{
    /// <summary>John 1:18 and 3:16 as the CCAT file writes them.</summary>
    internal const string John =
        """
        01:18 *QEO\N OU)DEI\S E(W/RAKEN PW/POTE: O( {N O( MONOGENH\S UI(O/S > MONOGENH\S QEO/S } MONOGENH\S UI(O/S, O( W)\N EI)S TO\N KO/LPON TOU= PATRO/S, E)KEI=NOS E)CHGH/SATO.
        03:16 ? *OU(/TWS GA\R H)GA/PHSEN O( QEO\S TO\N KO/SMON, W(/STE TO\N UI(O\N AU)TOU= {N AU)TOU= > - } TO\N MONOGENH= E)/DWKEN, I(/NA PA=S O( PISTEU/WN EI)S AU)TO\N MH\ A)PO/LHTAI, A)LL' E)/XH| ZWH\N AI)W/NION.
        """;

    [Fact]
    public void TheBetaCodeIsReadBare()
    {
        CcatReader.Bare("*)IWA/NNHS").Should().Be("ιωαννης");
        CcatReader.Bare("A)LL'").Should().Be("αλλ");
        CcatReader.Bare("E)/XH|").Should().Be("εχη");
        CcatReader.Bare("E)CHGH/SATO.").Should().Be("εξηγησατο");
        CcatReader.Bare("?").Should().BeEmpty();
    }

    [Fact]
    public void ANoteIsPlacedOnTheWordsItRepeatsAndTheVerseReadsWithoutIt()
    {
        var verses = CcatReader.Read(John);

        verses.Should().HaveCount(2);
        var john118 = verses[0];
        john118.Words.Should().HaveCount(16);
        john118.Words.Skip(4).Take(3).Should().Equal("ο", "μονογενης", "υιος");
        john118.Notes.Should().ContainSingle();
        john118.Notes[0].Should().BeEquivalentTo(new
        {
            Kind = 'N', From = 4, Lemma = new[] { "ο", "μονογενης", "υιος" }, Reading = new[] { "μονογενης", "θεος" },
        });

        verses[1].Notes.Single().Reading.Should().BeEmpty("Nestle-Aland has no αὐτοῦ there");
        verses[1].Words[10].Should().Be("αυτου");
    }

    /// <summary>A note set before its word, as Matthew 5:39's is, is placed on the word after it.</summary>
    [Fact]
    public void ANoteSetBeforeItsWordsIsPlacedOnThem()
    {
        var verse = CcatReader.Read(@"05:39 E)PI\ TH\N DECIA\N {B SIAGO/NA > SOU SIAGO/NA } SIAGO/NA, STRE/YON")[0];

        verse.Notes.Single().From.Should().Be(3);
        verse.Notes.Single().Reading.Should().Equal("σου", "σιαγονα");
    }

    [Fact]
    public void ANoteWhoseWordsTheVerseDoesNotHoldIsCounted()
    {
        var verse = CcatReader.Read(@"08:02 PA/LIN {B BAQE/OS > BAQE/WS } H)=LQEN")[0];

        verse.Notes.Should().BeEmpty();
        verse.Unplaced.Should().Be(1);
    }

    /// <summary>
    /// Nestle 1904 prints μονογενὴς θεός at John 1:18 between the words Robinson has around ὁ μονογενὴς
    /// υἱός, and leaves out αὐτοῦ at 3:16: both notes link, the second as words Robinson has and the
    /// witness does not.
    /// </summary>
    [Fact]
    public void ANoteLinksWhereTheWitnessPrintsItsReadingAtThePlace()
    {
        var (laid, robinson, nestle) = Lay(
            (1, 18, "θεον ουδεις εωρακεν πωποτε μονογενης θεος ο ων εις τον κολπον του πατρος εκεινος εξηγησατο"),
            (3, 16, "ουτως γαρ ηγαπησεν ο θεος τον κοσμον ωστε τον υιον τον μονογενη εδωκεν ινα πας ο πιστευων εις αυτον μη αποληται αλλ εχη ζωην αιωνιον"));

        laid.Verses.Should().Be(2);
        laid.Held.Should().Be(2);
        var first = laid.Links[0];
        first.Relation.Should().Be(LinkRelation.Renders);
        first.From.Select(id => robinson[id]).Should().Equal("ο", "μονογενης", "υιος");
        first.To.Select(id => nestle[id]).Should().Equal("μονογενης", "θεος");
        laid.Links[1].Relation.Should().Be(LinkRelation.Expands);
        laid.Links[1].From.Select(id => robinson[id]).Should().Equal("αυτου");
        laid.Links[1].To.Should().BeEmpty();
    }

    /// <summary>Where the witness reads with Robinson, the note is about another edition and links nothing.</summary>
    [Fact]
    public void ANoteTheWitnessDoesNotBearOutLinksNothing()
    {
        var (laid, _, _) = Lay(
            (1, 18, "θεον ουδεις εωρακεν πωποτε ο μονογενης υιος ο ων εις τον κολπον του πατρος εκεινος εξηγησατο"),
            (3, 16, "ουτως γαρ ηγαπησεν ο θεος τον κοσμον ωστε τον υιον αυτου τον μονογενη εδωκεν ινα πας ο πιστευων εις αυτον μη αποληται αλλ εχη ζωην αιωνιον"));

        laid.Held.Should().Be(0);
        laid.NotHeld.Should().Be(2);
        laid.Links.Should().BeEmpty();
    }

    /// <summary>
    /// Nestle-Aland prints τῆς at Romans 3:25 in brackets, as doubtful, and Nestle 1904 does not print
    /// it: the reading is found without its bracketed word.
    /// </summary>
    [Fact]
    public void AReadingIsFoundWithoutTheWordsItsEditionDoubts()
    {
        const string romans = @"03:25 O(\N PROE/QETO O( QEO\S I(LASTH/RION DIA\ TH=S {N TH=S PI/STEWS > [TH=S] PI/STEWS } PI/STEWS, E)N TW=| AU)TOU= AI(/MATI";

        var (laid, robinson, nestle) = Lay(romans, ['N'],
            (3, 25, "ον προεθετο ο θεος ιλαστηριον δια πιστεως εν τω αυτου αιματι"));

        laid.Held.Should().Be(1);
        laid.Links.Single().From.Select(id => robinson[id]).Should().Equal("της", "πιστεως");
        laid.Links.Single().To.Select(id => nestle[id]).Should().Equal("πιστεως");
        laid.Links.Single().Relation.Should().Be(LinkRelation.Renders);
    }

    private static (LaidApparatus Laid, Dictionary<long, string> Robinson, Dictionary<long, string> Witness) Lay(
        params (int Chapter, int Verse, string Words)[] witness) =>
        Lay(John, ['N'], witness);

    private static (LaidApparatus Laid, Dictionary<long, string> Robinson, Dictionary<long, string> Witness) Lay(
        string ccat, char[] kinds, params (int Chapter, int Verse, string Words)[] witness)
    {
        var verses = CcatReader.Read(ccat).ToDictionary(verse => (43, verse.Chapter, verse.Number));
        long id = 0;
        var robinson = verses.ToDictionary(
            verse => verse.Key,
            verse => (IReadOnlyList<CorpusWord>)[.. verse.Value.Words.Select(word => new CorpusWord(++id, word))]);
        var other = witness.ToDictionary(
            verse => (43, verse.Chapter, verse.Verse),
            verse => (IReadOnlyList<CorpusWord>)[.. verse.Words.Split(' ').Select(word => new CorpusWord(1_000 + ++id, word))]);

        var laid = ByzantineApparatus.Lay(verses, robinson, other, kinds.ToHashSet());
        return (
            laid,
            robinson.Values.SelectMany(words => words).ToDictionary(word => word.Id, word => word.Text),
            other.Values.SelectMany(words => words).ToDictionary(word => word.Id, word => word.Text));
    }
}

/// <summary>
/// The whole apparatus against the files the corpus loads, without a database: how many verses of
/// the CCAT text are the parsed text word for word, how many notes find their words, and how many
/// readings Nestle 1904 and the Received Text print where the notes put them.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class ByzantineApparatusCorpusTests(ITestOutputHelper output)
{
    [Fact]
    public void TheApparatusLaysOntoTheLoadedText()
    {
        long id = 0;
        Dictionary<(int, int, int), IReadOnlyList<CorpusWord>> Words(TextSource text) =>
            text.Books.SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse => (
                    Address: (book.CanonicalOrdinal, chapter.Number, verse.Number),
                    Words: (IReadOnlyList<CorpusWord>)[.. verse.Words.Select(word => new CorpusWord(++id, word.Surface))]))))
                .ToDictionary(verse => verse.Address, verse => verse.Words);

        var folder = TestResources.Folder("Byzantine");
        var apparatus = ByzantineApparatusLinkLoader.Read(folder);
        var robinson = Words(ByzantineTextSource.Read(folder));
        var nestle = Words(NestleTextSource.Read(TestResources.Nestle1904));
        var scrivener = Words(TextusReceptusTextSource.Read(TestResources.Folder("TextusReceptus"), Edition.Scrivener1894));

        var notes = apparatus.Values.SelectMany(verse => verse.Notes).ToList();
        var unplaced = apparatus.Values.Sum(verse => verse.Unplaced);
        var critical = ByzantineApparatus.Lay(apparatus, robinson, nestle, ByzantineApparatusLinkLoader.Critical);
        var byzantine = ByzantineApparatus.Lay(apparatus, robinson, scrivener, ByzantineApparatusLinkLoader.Divided);

        var report =
            $"{apparatus.Count} verses, {notes.Count} notes placed ({string.Join(", ", notes.GroupBy(n => n.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"))}), {unplaced} unplaced; "
            + $"against Nestle 1904: {critical}; against Scrivener: {byzantine}";
        output.WriteLine(report);

        critical.Refused.Should().Be(0, report);
        unplaced.Should().BeLessThan(notes.Count / 100, report);
        ((double)critical.Held / critical.Notes).Should().BeGreaterThan(0.9, report);
    }
}

/// <summary>
/// The apparatus written: a note over words a link of the pair already names becomes a claim on it,
/// a note the pair already contradicts is left out, and a second run finds the pair linked.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ByzantineApparatusLinkTests : IDisposable
{
    private const string Divided =
        """
        01:18 *QEO\N OU)DEI\S E(W/RAKEN PW/POTE: O( {B O( MONOGENH\S UI(O/S > MONOGENH\S QEO/S } MONOGENH\S UI(O/S, O( W)\N EI)S TO\N KO/LPON TOU= PATRO/S, E)KEI=NOS E)CHGH/SATO.
        03:16 *OU(/TWS GA\R H)GA/PHSEN O( QEO\S TO\N KO/SMON, W(/STE TO\N UI(O\N AU)TOU= {B AU)TOU= > - } TO\N MONOGENH= E)/DWKEN
        """;

    private readonly AppDbContext _db;
    private readonly ByzantineApparatusLinkLoader _loader;
    private readonly Text _robinson;
    private readonly Text _scrivener;
    private readonly Dictionary<(int, int, int), CcatVerse> _apparatus;

    public ByzantineApparatusLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _apparatus = CcatReader.Read(Divided).ToDictionary(verse => (1, verse.Chapter, verse.Number));
        _robinson = Corpus.Add(_db, ByzantineTextSource.Slug, TextKind.CriticalEdition, "grc",
            [.. _apparatus.Values.Select(verse => (verse.Chapter, verse.Number, verse.Words.ToArray()))]);
        _scrivener = Corpus.Add(_db, Sources.ScrivenerSlug, TextKind.PrintedEdition, "grc",
            (1, 18, "θεον ουδεις εωρακεν πωποτε μονογενης θεος ο ων εις τον κολπον του πατρος εκεινος εξηγησατο".Split(' ')),
            (3, 16, "ουτως γαρ ηγαπησεν ο θεος τον κοσμον ωστε τον υιον τον μονογενη εδωκεν".Split(' ')));
        _db.SaveChanges();
        _loader = new ByzantineApparatusLinkLoader(_db, NullLogger<ByzantineApparatusLinkLoader>.Instance);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    private void Numbered(LinkRelation relation, IEnumerable<Word> from, IEnumerable<Word> to)
    {
        var (fromIds, toIds) = (from.Select(w => w.Id).ToList(), to.Select(w => w.Id).ToList());
        var link = new Link
        {
            FromTextId = _robinson.Id,
            ToTextId = _scrivener.Id,
            Relation = relation,
            Method = LinkMethod.StrongNumber,
            Confidence = StrongNumberMatch.Unambiguous,
            Provenance = new() { Source = "a Strong pairing under test" },
            Fingerprint = LinkShape.Of(fromIds, toIds),
        };
        fromIds.ForEach(id => link.Words.Add(new LinkWord { WordId = id, Side = LinkSide.From }));
        toIds.ForEach(id => link.Words.Add(new LinkWord { WordId = id, Side = LinkSide.To }));
        _db.Links.Add(link);
        _db.SaveChanges();
        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id, Method = link.Method, Confidence = link.Confidence, ProvenanceId = link.ProvenanceId,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task ANoteJoinsTheLinkOverItsWordsAndOneThePairContradictsIsLeftOut()
    {
        Numbered(LinkRelation.Expands, [_db.WordAt(_robinson, 3, 16, 11)], []);
        Numbered(LinkRelation.Omits, [], [_db.WordAt(_scrivener, 1, 18, 6)]);

        (await _loader.Linked(Sources.ScrivenerSlug)).Should().BeFalse();
        var outcome = await _loader.Write(_apparatus, Sources.ScrivenerSlug, ByzantineApparatusLinkLoader.Divided);

        outcome.Laid!.Held.Should().Be(2);
        outcome.Corroborated.Should().Be(1);
        outcome.Contrary.Should().Be(1);
        outcome.Written.Should().Be(0);

        _db.ChangeTracker.Clear();
        var absence = _db.Links.Include(l => l.Claims).ThenInclude(c => c.Provenance)
            .Single(l => l.Relation == LinkRelation.Expands);
        absence.Method.Should().Be(LinkMethod.StatedBySource);
        absence.Claims.Select(c => c.Provenance!.Source).Should().Contain(ByzantineApparatusLinkLoader.Credit);
        (await _loader.Linked(Sources.ScrivenerSlug)).Should().BeTrue();
    }
}
