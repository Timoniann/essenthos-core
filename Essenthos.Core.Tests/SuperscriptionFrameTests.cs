using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// One verse of a translation standing at two addresses of the frame, which is how a word landing
/// in another verse is made sayable.
///
/// The corpus here is Psalm 3 in miniature: a Hebrew text whose first verse is the title and whose
/// second is the body, and a translation printing both inside one verse. Everything the change has
/// to be true of is visible in it — the second address, the verse link that then joins the boundary,
/// and the integrity check that must keep reading zero *because* the frame joins the verses being
/// crossed rather than because nothing crosses.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SuperscriptionFrameTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly SuperscriptionFrameLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _slavic;
    private readonly VerseLink _verseLink;

    public SuperscriptionFrameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _loader = new SuperscriptionFrameLoader(_db, NullLogger<SuperscriptionFrameLoader>.Instance);

        _hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (3, 1, ["מִזְמֹור", "לְדָוִד"]),
            (3, 2, ["יְהוָה", "מָה"]));
        _slavic = Corpus.Add(_db, "rusv", TextKind.Translation, "rus",
            (3, 1, ["Псалом", "Давида", "Господи", "как"]));
        _db.SaveChanges();

        // The Hebrew numbers the superscription as its own verse, so it stands at the title address
        // and everything after it is one lower than the number it prints.
        Reference(_hebrew, verse: 1).CanonicalVerse = 0;
        Reference(_hebrew, verse: 2).CanonicalVerse = 1;
        _db.SaveChanges();

        _verseLink = Join(_db.VerseAt(_slavic, 3, 1), _db.VerseAt(_hebrew, 3, 2));
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AVerseHoldingATitleStandsAtTheTitleAddressAsWell()
    {
        var outcome = await _loader.Load(Marked());

        outcome.Verses.Should().Be(1);
        outcome.Placed.Should().Be(1);

        Addresses(_slavic, 3, 1).Should().BeEquivalentTo([(0, false), (1, true)]);
    }

    /// <summary>
    /// The Hebrew's title verse belonged to no verse link at all: nothing of the translation stood
    /// at its address, so the loader that derives verse links from the frame counted it as a verse
    /// with no counterpart. Saying the translation's verse covers that address is what gives it one.
    /// </summary>
    [Fact]
    public async Task TheTitleVerseJoinsTheVerseLinkTheTranslationIsAlreadyIn()
    {
        _db.VerseLinkVerses.Count(v => v.VerseId == _db.VerseAt(_hebrew, 3, 1).Id).Should().Be(0);

        var outcome = await _loader.Load(Marked());

        outcome.Joined.Should().Be(1);
        Members(_verseLink.Id, LinkSide.To).Should().BeEquivalentTo(
            [_db.VerseAt(_hebrew, 3, 1).Id, _db.VerseAt(_hebrew, 3, 2).Id]);
    }

    /// <summary>
    /// The check that separates a word link crossing a verse boundary on purpose from one crossing
    /// it by mistake. Both are here — the title word reaching the Hebrew's title verse, which the
    /// frame now joins, and a word reaching a verse nothing joins — and only the second is a fault.
    /// A run where this reads zero because the frame was never told about the first would be the
    /// check passing for the wrong reason.
    /// </summary>
    [Fact]
    public async Task ALinkCrossingAJoinedBoundaryIsNotAFaultAndOneCrossingAnUnjoinedBoundaryIs()
    {
        await _loader.Load(Marked());
        Link(_db.WordAt(_slavic, 3, 1, 1), _db.WordAt(_hebrew, 3, 1, 1));

        (await Crossings()).Should().Be(0);

        var apart = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (3, 1, ["A", "Psalm"]));
        _db.SaveChanges();
        Link(_db.WordAt(_slavic, 3, 1, 3), _db.WordAt(apart, 3, 1, 1));

        (await Crossings()).Should().Be(0, "the two stand at the same address, so nothing is crossed");

        Reference(apart, verse: 1).CanonicalVerse = 9;
        _db.SaveChanges();

        (await Crossings()).Should().Be(1);
    }

    /// <summary>
    /// It runs on every start, against a database it has already written to, so a second run has to
    /// cost nothing rather than write the address twice or the membership again.
    /// </summary>
    [Fact]
    public async Task ASecondRunWritesNothing()
    {
        await _loader.Load(Marked());
        var again = await _loader.Load(Marked());

        again.Verses.Should().Be(1);
        again.Placed.Should().Be(0);
        again.Joined.Should().Be(0);
    }

    /// <summary>
    /// The condition that keeps this to psalms without naming them. A verse the publisher merged in
    /// the middle of a chapter opens before the address it states in exactly the same way, and
    /// there is no title verse in front of it for it to cover.
    /// </summary>
    [Fact]
    public async Task AMarkedVerseTheFrameHasNoTitleVerseForIsLeftAlone()
    {
        Reference(_hebrew, verse: 1).CanonicalVerse = 1;
        Reference(_hebrew, verse: 2).CanonicalVerse = 2;
        _db.SaveChanges();

        var outcome = await _loader.Load(Marked());

        outcome.Verses.Should().Be(0);
        outcome.Placed.Should().Be(0);
        Addresses(_slavic, 3, 1).Should().BeEquivalentTo([(1, true)]);
    }

    /// <summary>
    /// A text whose file marks nothing says nothing, and silence is not a claim that no verse of it
    /// holds a title.
    /// </summary>
    [Fact]
    public async Task ATextThatMarksNothingIsUntouched()
    {
        var outcome = await _loader.Load(Marked(marks: false));

        outcome.Verses.Should().Be(0);
        Addresses(_slavic, 3, 1).Should().BeEquivalentTo([(1, true)]);
    }

    /// <summary>
    /// The translation as its reader produces it: one verse of one chapter, carrying whichever of
    /// the two statements the file makes about it.
    /// </summary>
    private TextSource Marked(bool marks = true) => new(
        Bible4uTextSource.Definitions["RUSV"],
        [
            new BookDraft(
                CanonicalOrdinal: 1,
                Position: 1,
                Name: "Genesis",
                Slug: "gen",
                Chapters:
                [
                    new ChapterDraft(3,
                    [
                        new VerseDraft(1, [new WordDraft("Псалом", " ")])
                        {
                            MarksASuperscription = marks,
                        },
                    ]),
                ]),
        ]);

    private VerseReference Reference(Text text, int verse) =>
        _db.VerseReferences.Single(r => r.Verse!.TextId == text.Id && r.Verse.Number == verse && r.IsPrimary);

    private List<(int Verse, bool Primary)> Addresses(Text text, int chapter, int verse) =>
    [
        .. _db.VerseReferences
            .Where(r => r.Verse!.TextId == text.Id && r.Verse.ChapterNumber == chapter && r.Verse.Number == verse)
            .Select(r => new { r.CanonicalVerse, r.IsPrimary })
            .AsEnumerable()
            .Select(r => (r.CanonicalVerse, r.IsPrimary)),
    ];

    private List<int> Members(int verseLinkId, LinkSide side) =>
    [
        .. _db.VerseLinkVerses
            .Where(v => v.VerseLinkId == verseLinkId && v.Side == side)
            .Select(v => v.VerseId),
    ];

    private async Task<int> Crossings()
    {
        var check = new CorpusCheck(_db, NullLogger<CorpusCheck>.Instance);
        var measures = await check.Measure();
        return measures.Integrity.Single(i => i.Breaks.Contains("crossing")).Found;
    }

    private VerseLink Join(Verse from, Verse to)
    {
        var link = new VerseLink
        {
            FromTextId = from.TextId,
            ToTextId = to.TextId,
            Relation = LinkRelation.Equals,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
        };
        _db.VerseLinks.Add(link);
        _db.SaveChanges();

        _db.VerseLinkVerses.Add(new VerseLinkVerse
        {
            VerseLinkId = link.Id,
            VerseId = from.Id,
            Side = LinkSide.From,
        });
        _db.VerseLinkVerses.Add(new VerseLinkVerse
        {
            VerseLinkId = link.Id,
            VerseId = to.Id,
            Side = LinkSide.To,
        });
        _db.SaveChanges();
        return link;
    }

    private void Link(Word from, Word to)
    {
        var link = new Link
        {
            FromTextId = from.TextId,
            ToTextId = to.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Manual,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id,
            Method = link.Method,
            Confidence = link.Confidence,
            Source = link.Source,
        });
        _db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = from.Id, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = to.Id, Side = LinkSide.To });
        _db.SaveChanges();
    }
}
