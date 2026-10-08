using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The aligner pairs the verses standing at one canonical address, so a verse the frame moves
/// afterwards leaves its links naming a verse that is no longer the counterpart. Brenton's
/// Nehemiah 3:6 'up' was linked to the Greek 3:6a while 3:6a stood at 3:6, and 3:6a now stands at
/// 3:7, where no verse link joins it to Brenton's 3:6.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StatisticalCrossingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly VerseLinkLoader _loader;
    private readonly Text _english;
    private readonly Text _greek;
    private readonly Text _other;

    public StatisticalCrossingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance);

        _english = Corpus.Add(_db, "ENG", TextKind.Translation, "eng", (3, 6, ["the", "wall", "up"]));
        _greek = Corpus.Add(_db, "GRK", TextKind.CriticalEdition, "grc", (3, 6, ["α", "β"]), (3, 7, ["γ", "δ"]));
        _other = Corpus.Add(_db, "OTH", TextKind.Translation, "deu", (3, 6, ["die", "Mauer"]), (3, 7, ["hoch"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private long Link(Text from, int fromVerse, Text to, int toVerse, LinkMethod method, params LinkMethod[] claims) =>
        Link(from, fromVerse, 1, to, toVerse, method, claims);

    private long Link(Text from, int fromVerse, int position, Text to, int toVerse, LinkMethod method, params LinkMethod[] claims)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = to.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = method == LinkMethod.StatedBySource ? null : 0.6,
            Provenance = new() { Source = $"a test {Guid.NewGuid()}" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, WordId = _db.WordAt(from, 3, fromVerse, position).Id, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, WordId = _db.WordAt(to, 3, toVerse, position).Id, Side = LinkSide.To });
        foreach (var claim in claims)
        {
            _db.LinkClaims.Add(new LinkClaim
            {
                Link = link,
                Method = claim,
                Confidence = claim == LinkMethod.StatedBySource ? null : 0.6,
                Provenance = new() { Source = $"a claim {Guid.NewGuid()}" },
            });
        }

        _db.SaveChanges();
        return link.Id;
    }

    private Task<bool> Stands(long link) => _db.Links.AnyAsync(l => l.Id == link);

    [Fact]
    public async Task AnAlignerLinkAcrossVersesNothingJoinsIsWithdrawnAndOneInsideAJoinedPairStays()
    {
        var crossing = Link(_english, 6, _greek, 7, LinkMethod.Aligner, LinkMethod.Aligner);
        var inside = Link(_english, 6, _greek, 6, LinkMethod.Aligner, LinkMethod.Aligner);

        var outcome = await _loader.Load();

        outcome.Withdrawn.Should().Be(1);
        (await Stands(crossing)).Should().BeFalse();
        (await Stands(inside)).Should().BeTrue();
        (await _db.LinkWords.CountAsync(w => w.LinkId == crossing)).Should().Be(0);
    }

    [Fact]
    public async Task ALinkAnotherMethodAlsoStandsOnIsLeftForTheIntegrityCheck()
    {
        var crossing = Link(_english, 6, _greek, 7, LinkMethod.Aligner, LinkMethod.Aligner, LinkMethod.Manual);

        (await _loader.Load()).Withdrawn.Should().Be(0);

        (await Stands(crossing)).Should().BeTrue();
    }

    [Fact]
    public async Task OnlyTheAlignersLinksAreWithdrawn()
    {
        var stated = Link(_english, 6, _greek, 7, LinkMethod.StatedBySource, LinkMethod.StatedBySource);
        var lexical = Link(_english, 6, 2, _greek, 7, LinkMethod.Lexical, LinkMethod.Lexical);

        await _loader.Load();

        (await Stands(stated)).Should().BeTrue();
        (await Stands(lexical)).Should().BeTrue();
    }

    [Fact]
    public async Task ASecondLoadWithdrawsNothing()
    {
        Link(_english, 6, _greek, 7, LinkMethod.Aligner, LinkMethod.Aligner);
        await _loader.Load();

        var second = await _loader.Load();

        second.Withdrawn.Should().Be(0);
        second.ToString().Should().Be("the verse links are already loaded");
    }

    [Fact]
    public async Task ARefreshOfOneTextLeavesTheCrossingsOfOtherTextsAlone()
    {
        var elsewhere = Link(_other, 6, _greek, 7, LinkMethod.Aligner, LinkMethod.Aligner);
        Link(_english, 6, _greek, 6, LinkMethod.Aligner, LinkMethod.Aligner);
        Link(_other, 6, _greek, 6, LinkMethod.Aligner, LinkMethod.Aligner);
        await _loader.Load();
        var crossing = Link(_english, 6, _greek, 7, LinkMethod.Aligner, LinkMethod.Aligner);
        var otherCrossing = Link(_other, 7, _greek, 6, LinkMethod.Aligner, LinkMethod.Aligner);

        var outcome = await _loader.Refresh(new HashSet<string> { "ENG" });

        outcome.Withdrawn.Should().Be(1);
        (await Stands(crossing)).Should().BeFalse();
        (await Stands(otherCrossing)).Should().BeTrue();
        (await Stands(elsewhere)).Should().BeFalse("the first load took the crossing that was there");
    }

    [Fact]
    public async Task ARefreshOfATextTheCorpusDoesNotHoldWithdrawsNothing()
    {
        var crossing = Link(_english, 6, _greek, 7, LinkMethod.Aligner, LinkMethod.Aligner);
        Link(_english, 6, _greek, 6, LinkMethod.Aligner, LinkMethod.Aligner);

        var outcome = await _loader.Refresh(new HashSet<string> { "NOPE" });

        outcome.Withdrawn.Should().Be(0);
        (await Stands(crossing)).Should().BeTrue();
    }
}
