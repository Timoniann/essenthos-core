using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A verse standing at an address of the frame that is not its own, and the verse link that has to
/// join it to whatever stands at that address in the other text.
///
/// <para>
/// The psalm superscriptions are the case this was written for and they are not the only one. BHSA
/// writes Isaiah 63:19 as one verse where the English begins chapter 64 inside it, Nehemiah 7:68
/// where the English has both 7:68 and 7:69, and Psalm 13:6 where the English has 13:5 and 13:6.
/// The frame recorded all three long before anything read a second address, and nothing joined
/// them — so the aligner proposed forty true links across those boundaries and the verification
/// read every one of them as a fault.
/// </para>
///
/// <para>
/// The second test is the other half of the rule. The versification data parks the addresses of
/// material a text does not contain on the nearest verse it does — the King James' Esther 1:1
/// carries eighteen of them for the Greek additions — and nothing stands at those addresses in any
/// text here. Joining two such coverings would state a correspondence between two absences.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CoveredVerseTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly VerseLinkLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;
    private readonly VerseLink _verseLink;

    public CoveredVerseTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _loader = new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance);

        // One Hebrew verse against two English ones, which is the shape of all three splits.
        _hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (1, 5, ["הֲלֹוא", "קָרָאתָ"]));
        _english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng",
            (1, 5, ["Oh", "that"]),
            (1, 6, ["thou", "wouldest"]));
        _db.SaveChanges();

        _verseLink = Join(_db.VerseAt(_english, 1, 5), _db.VerseAt(_hebrew, 1, 5));
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AVerseCoveringASecondAddressIsJoinedToWhatStandsThere()
    {
        Covers(_hebrew, chapter: 1, verse: 5, alsoAt: 6);

        (await _loader.Cover()).Should().Be(1);

        Members(_verseLink.Id, LinkSide.From).Should().BeEquivalentTo(
            [_db.VerseAt(_english, 1, 5).Id, _db.VerseAt(_english, 1, 6).Id]);
    }

    [Fact]
    public async Task AnAddressNoVerseOfTheOtherTextStandsAtJoinsNothing()
    {
        Covers(_hebrew, chapter: 1, verse: 5, alsoAt: 9);

        (await _loader.Cover()).Should().Be(0);

        Members(_verseLink.Id, LinkSide.From).Should().BeEquivalentTo([_db.VerseAt(_english, 1, 5).Id]);
    }

    /// <summary>
    /// It runs on every start against a database it has already written to, so the second run has
    /// to add nothing rather than fail on the membership it wrote the first time.
    /// </summary>
    [Fact]
    public async Task ASecondRunAddsNothing()
    {
        Covers(_hebrew, chapter: 1, verse: 5, alsoAt: 6);

        await _loader.Cover();

        (await _loader.Cover()).Should().Be(0);
    }

    /// <summary>Records that a verse stands at a second canonical address as well as its own.</summary>
    private void Covers(Text text, int chapter, int verse, int alsoAt)
    {
        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = _db.VerseAt(text, chapter, verse).Id,
            CanonicalBook = 1,
            CanonicalChapter = chapter,
            CanonicalVerse = alsoAt,
            IsPrimary = false,
        });
        _db.SaveChanges();
    }

    private List<int> Members(int verseLinkId, LinkSide side) =>
    [
        .. _db.VerseLinkVerses
            .Where(v => v.VerseLinkId == verseLinkId && v.Side == side)
            .Select(v => v.VerseId),
    ];

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
}
