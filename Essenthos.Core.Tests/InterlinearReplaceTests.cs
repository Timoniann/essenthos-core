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
/// A Door43 join written over the links an older join left. The corpus folds other methods into the
/// stated links as claims and lets the Strong matcher stay off stated words, so a reload that simply
/// deleted the statements and wrote them again would take the aligner's and the Strong matcher's
/// answers with it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class InterlinearReplaceTests : IDisposable
{
    private const string Door43 = "a Door43 interlinear";

    private readonly AppDbContext _db;
    private readonly InterlinearLinkLoader _loader;
    private readonly Text _ukrainian;
    private readonly Text _greek;

    public InterlinearReplaceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new InterlinearLinkLoader(_db, NullLogger<InterlinearLinkLoader>.Instance);

        _ukrainian = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (1, 1, ["Павло", "апостол", "Христа"]));
        _greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc", (1, 1, ["Παῦλος", "ἀπόστολος", "Χριστοῦ"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task AStatementOnTheWrongOriginalWordIsWithdrawnAndTheStatedOneWritten()
    {
        var wrong = Link(1, 1, LinkMethod.StatedBySource, null, Door43);

        var written = await Reconcile((1, 3));

        written.Should().Be(new InterlinearReconciliation(0, 0, 1, 1, 0, 0, 1));
        (await _db.Links.AnyAsync(link => link.Id == wrong.Id)).Should().BeFalse();
        var stated = (await Links()).Should().ContainSingle().Which;
        stated.Method.Should().Be(LinkMethod.StatedBySource);
        Words(stated).Should().Equal(Ukrainian(1).Id, Greek(3).Id);
        stated.Claims.Should().ContainSingle(claim => claim.Provenance!.Source == Door43);
    }

    [Fact]
    public async Task ALinkTheJoinStillStatesKeepsItsRowAndTheClaimsFoldedIntoIt()
    {
        var kept = Link(2, 2, LinkMethod.StatedBySource, null, Door43);
        Claim(kept, LinkMethod.Aligner, 0.9, "an aligner");

        var written = await Reconcile((2, 2));

        written.Should().Be(new InterlinearReconciliation(1, 0, 0, 0, 0, 0, 0));
        var link = (await Links()).Should().ContainSingle().Which;
        link.Id.Should().Be(kept.Id);
        link.Claims.Select(claim => claim.Method).Should()
            .BeEquivalentTo([LinkMethod.StatedBySource, LinkMethod.Aligner]);
    }

    [Fact]
    public async Task AStatementWithdrawnFromALinkTheAlignerAlsoDrewLeavesTheAlignersLink()
    {
        var folded = Link(1, 1, LinkMethod.StatedBySource, null, Door43);
        Claim(folded, LinkMethod.Aligner, 0.8, "an aligner");

        var written = await Reconcile((1, 3));

        written.Should().Be(new InterlinearReconciliation(0, 0, 1, 1, 0, 1, 0));
        var guess = (await Links()).Should().ContainSingle(link => link.Id == folded.Id).Which;
        guess.Method.Should().Be(LinkMethod.Aligner);
        guess.Confidence.Should().Be(0.8);
        guess.Provenance!.Source.Should().Be("an aligner");
        guess.Claims.Should().ContainSingle().Which.Method.Should().Be(LinkMethod.Aligner);
    }

    [Fact]
    public async Task AGuessOverExactlyTheStatedWordsTakesTheStatementRatherThanASecondLink()
    {
        var guess = Link(3, 3, LinkMethod.Aligner, 0.6, "an aligner");

        var written = await Reconcile((3, 3));

        written.Should().Be(new InterlinearReconciliation(0, 1, 0, 0, 0, 0, 0));
        var link = (await Links()).Should().ContainSingle().Which;
        link.Id.Should().Be(guess.Id);
        link.Method.Should().Be(LinkMethod.StatedBySource);
        link.Confidence.Should().BeNull();
        link.Provenance!.Source.Should().Be(Door43);
        link.Claims.Should().Contain(claim => claim.Method == LinkMethod.Aligner && claim.Confidence == 0.6);
    }

    [Fact]
    public async Task AStrongNumberMatchOnAStatedWordYieldsAndOneOnAnUnstatedWordStays()
    {
        var contradicted = Link(1, 2, LinkMethod.StrongNumber, 0.9, "a Strong edition");
        var elsewhere = Link(2, 2, LinkMethod.StrongNumber, 0.9, "a Strong edition");

        var written = await Reconcile((1, 1));

        written.Should().Be(new InterlinearReconciliation(0, 0, 1, 0, 1, 0, 1));
        var links = await Links();
        links.Should().NotContain(link => link.Id == contradicted.Id);
        links.Should().Contain(link => link.Id == elsewhere.Id && link.Method == LinkMethod.StrongNumber);
    }

    [Fact]
    public async Task AnotherSourcesStatementOnAWordTheJoinDoesNotReachIsLeftAlone()
    {
        var other = Link(2, 2, LinkMethod.StatedBySource, null, "another interlinear");

        await Reconcile((1, 1));

        (await Links()).Should().Contain(link => link.Id == other.Id && link.Provenance!.Source == "another interlinear");
    }

    [Fact]
    public async Task WritingTheSameJoinTwiceChangesNothingTheSecondTime()
    {
        Link(1, 1, LinkMethod.StatedBySource, null, Door43);
        Link(3, 3, LinkMethod.Aligner, 0.6, "an aligner");
        await Reconcile((1, 2), (3, 3), (2, 1));
        var before = (await Links()).Select(link => (link.Id, link.Method, link.Claims.Count)).ToList();

        var written = await Reconcile((1, 2), (3, 3), (2, 1));

        written.Should().Be(new InterlinearReconciliation(3, 0, 0, 0, 0, 0, 0));
        (await Links()).Select(link => (link.Id, link.Method, link.Claims.Count)).Should().Equal(before);
    }

    private Task<InterlinearReconciliation> Reconcile(params (int Ukrainian, int Greek)[] pairs) =>
        _loader.Reconcile(
            _ukrainian.Id,
            Door43,
            [.. pairs.Select(pair => new InterlinearDraft(
                _ukrainian.Id, _greek.Id, [Ukrainian(pair.Ukrainian).Id], [Greek(pair.Greek).Id]))],
            CancellationToken.None);

    private Word Ukrainian(int position) => _db.WordAt(_ukrainian, 1, 1, position);

    private Word Greek(int position) => _db.WordAt(_greek, 1, 1, position);

    /// <summary>A link with its own claim, the way every loader writes one.</summary>
    private Link Link(int ukrainian, int greek, LinkMethod method, double? confidence, string source)
    {
        var link = new Link
        {
            FromTextId = _ukrainian.Id,
            ToTextId = _greek.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new() { Source = source },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Ukrainian(ukrainian), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Greek(greek), Side = LinkSide.To });
        _db.LinkClaims.Add(new LinkClaim { Link = link, Method = method, Confidence = confidence, Provenance = new() { Source = source }});
        _db.SaveChanges();
        return link;
    }

    private void Claim(Link link, LinkMethod method, double confidence, string source)
    {
        _db.LinkClaims.Add(new LinkClaim { LinkId = link.Id, Method = method, Confidence = confidence, Provenance = new() { Source = source }});
        _db.SaveChanges();
    }

    private async Task<List<Link>> Links() =>
        await _db.Links
            .AsNoTracking()
            .Include(link => link.Provenance).Include(link => link.Claims).ThenInclude(claim => claim.Provenance)
            .Include(link => link.Words)
            .Where(link => link.FromTextId == _ukrainian.Id)
            .OrderBy(link => link.Id)
            .ToListAsync();

    private static List<long> Words(Link link) =>
        [.. link.Words.OrderBy(word => word.Side).Select(word => word.WordId)];
}
