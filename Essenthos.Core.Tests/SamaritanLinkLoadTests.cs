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
/// What the Samaritan links look like once they are rows.
///
/// The shapes under test are the ones Postgres has to accept and no in-memory provider would
/// refuse: a link naming words on one side and none on the other, and a link naming two words on
/// one side against one on the other. Both are what this witness produces and neither had ever been
/// written for a Hebrew pair before.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SamaritanLinkLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SamaritanLinkLoader _loader;
    private readonly Text _samaritan;
    private readonly Text _masoretic;

    public SamaritanLinkLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new SamaritanLinkLoader(_db, NullLogger<SamaritanLinkLoader>.Instance);

        // Genesis 1:11, where the Samaritan has a word the Masoretic has not; and the shape of
        // Genesis 1:5, where BHSA records the article that assimilated into the preposition as a
        // word of its own and prints nothing for it.
        _samaritan = Corpus.Add(_db, "sp", TextKind.ManuscriptTradition, "hbo",
            (1, 11, ["ו", "עץ", "פרי"]),
            (1, 5, ["ל", "אור"]));
        _masoretic = Corpus.Add(_db, "bhsa", TextKind.CriticalEdition, "hbo",
            (1, 11, ["עץ", "פרי"]),
            (1, 5, ["ל", "", "אור"]));

        _samaritan.Versification = Versification.Original;
        _masoretic.Versification = Versification.Original;
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private async Task<List<Link>> Load()
    {
        await _loader.Load(_samaritan.Slug, _masoretic.Slug);
        return await _db.Links
            .Include(l => l.Words)
            .Where(l => l.FromTextId == _samaritan.Id && l.ToTextId == _masoretic.Id)
            .ToListAsync();
    }

    /// <summary>
    /// The word the Samaritan has and the Masoretic has not, stored positively: words on the
    /// <c>from</c> side, nothing on the <c>to</c> side, and <c>expands</c> to say which way round
    /// that is. A link with one empty side cannot say it for itself.
    /// </summary>
    [Fact]
    public async Task AWordTheMasoreticLacksIsWrittenAsAnExpansionWithAnEmptySide()
    {
        var links = await Load();

        var expansion = links.Should().ContainSingle(l => l.Relation == LinkRelation.Expands).Which;
        expansion.Words.Should().OnlyContain(w => w.Side == LinkSide.From);
        _db.Side(expansion.Id, LinkSide.From).Should().ContainSingle().Which.Surface.Should().Be("ו");
        _db.Side(expansion.Id, LinkSide.To).Should().BeEmpty();
    }

    /// <summary>
    /// The Masoretic article that prints no letters stands in the correspondence of the preposition
    /// it was pronounced with, so the link names two Masoretic words against one Samaritan one. Left
    /// on its own it would be a word the Samaritan omits, and the Samaritan omits nothing there.
    /// </summary>
    [Fact]
    public async Task TheArticleThatPrintsNothingJoinsThePrepositionItAssimilatedInto()
    {
        var links = await Load();

        var preposition = links.Single(l =>
            _db.Side(l.Id, LinkSide.From).Any(w => w.Surface == "ל"));

        _db.Side(preposition.Id, LinkSide.From).Select(w => w.Surface).Should().Equal("ל");
        _db.Side(preposition.Id, LinkSide.To).Select(w => w.Surface).Should().BeEquivalentTo(["ל", ""]);
        preposition.Relation.Should().Be(LinkRelation.Equals);
        links.Should().NotContain(l => l.Relation == LinkRelation.Omits);
    }

    /// <summary>
    /// Nobody states these correspondences, so every one of them is an inference and has to look
    /// like one: a method that is not <c>stated-by-source</c>, and a confidence. A check constraint
    /// holds the two apart, so an inference cannot be stored looking like scholarship.
    /// </summary>
    [Fact]
    public async Task EveryLinkSaysItWasInferredAndHowSure()
    {
        var links = await Load();

        links.Should().OnlyContain(l => l.Method == LinkMethod.Lexical);
        links.Should().OnlyContain(l => l.Confidence != null);
        links.Should().OnlyContain(l => l.Source.StartsWith("the consonants both Hebrew witnesses write"));
    }

    /// <summary>
    /// A claim for every link, written in the same transaction. A link with no claim is invisible
    /// to the agreement measure, which once spent a day reporting a migration instead of the corpus.
    /// </summary>
    [Fact]
    public async Task EveryLinkCarriesTheClaimThatMadeIt()
    {
        var links = await Load();

        var claims = await _db.LinkClaims
            .Where(c => links.Select(l => l.Id).Contains(c.LinkId))
            .ToListAsync();

        claims.Should().HaveCount(links.Count);
        claims.Should().OnlyContain(c => c.Method == LinkMethod.Lexical);
    }

    /// <summary>
    /// Running it twice writes the links once. The startup pipeline re-runs on every boot, and a
    /// loader that does not check duplicates the corpus.
    /// </summary>
    [Fact]
    public async Task LoadingTwiceLeavesTheLinksAsTheyWere()
    {
        var first = await Load();
        var again = await _loader.Load(_samaritan.Slug, _masoretic.Slug);

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.Links.CountAsync(l => l.FromTextId == _samaritan.Id)).Should().Be(first.Count);
    }

    /// <summary>
    /// Two texts that number their verses differently are refused rather than joined on the numbers
    /// they happen to share. This pairs verses by the numbering both texts use, which is only sound
    /// while it is the same numbering — and the failure it would otherwise produce is every link in
    /// a chapter naming the wrong verse, which nothing about a single link would reveal.
    /// </summary>
    [Fact]
    public async Task TwoTextsOnDifferentNumberingAreRefused()
    {
        _masoretic.Versification = Versification.English;
        await _db.SaveChangesAsync();

        var refused = async () => await _loader.Load(_samaritan.Slug, _masoretic.Slug);

        await refused.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*numbers its verses*");
    }
}
