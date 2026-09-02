using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The four measures, each asked of a corpus small enough that the right answer is countable by
/// hand. Three Hebrew words against three English ones, and every case the measures are meant to
/// separate is arranged deliberately: a word rendered, a word whose absence is stated, a word
/// nothing reaches, and a text nothing has been aligned to at all.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CorpusCheckTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly CorpusCheck _check;
    private readonly Text _hebrew;
    private readonly Text _english;

    public CorpusCheckTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _check = new CorpusCheck(_db, NullLogger<CorpusCheck>.Instance);

        _hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא"]));
        _english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["In", "beginning", "verily"]));
        _db.SaveChanges();

        // The Hebrew preposition is a prefix and carries no lexical content, so reach must not count
        // it as a word the English failed to render.
        _db.WordAt(_hebrew, 1, 1, 1).StrongNumber = "H9003";
        _db.WordAt(_hebrew, 1, 1, 2).StrongNumber = "H7225";
        _db.WordAt(_hebrew, 1, 1, 3).StrongNumber = "H1254";
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AWordALinkNamesIsRenderedAndAWordNothingNamesIsSilent()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Renders, english: 2, hebrew: 2);

        var coverage = (await _check.Measure()).Coverage.Single(c => c.Text == "kjv");

        coverage.Words.Should().Be(3);
        coverage.Rendered.Should().Be(2);
        coverage.Silent.Should().Be(1);
        coverage.Unpaired.Should().Be(0);
    }

    /// <summary>
    /// The distinction the whole schema is for. A word the corpus says has no counterpart is not
    /// the same as a word the corpus has nothing to say about, and a single "unlinked" number
    /// would report them identically.
    /// </summary>
    [Fact]
    public async Task AStatedAbsenceIsNotSilence()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Expands, english: 3, hebrew: null);

        var coverage = (await _check.Measure()).Coverage.Single(c => c.Text == "kjv");

        coverage.Rendered.Should().Be(1);
        coverage.StatedAbsent.Should().Be(1);
        coverage.Silent.Should().Be(1);
    }

    /// <summary>
    /// Nothing is missing in a text nobody has aligned yet, and calling those words silent would
    /// report unfinished work as a defect.
    /// </summary>
    [Fact]
    public async Task ATextNothingHasBeenAlignedToIsUnpairedRatherThanSilent()
    {
        var coverage = (await _check.Measure()).Coverage.Single(c => c.Text == "kjv");

        coverage.Unpaired.Should().Be(3);
        coverage.Silent.Should().Be(0);
    }

    /// <summary>
    /// The direction the forward count hides. Here the English is fully rendered and half the
    /// Hebrew is untouched, and only this measure says so.
    /// </summary>
    [Fact]
    public async Task ReachCountsTheWitnessSideAndIgnoresItsPrefixes()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 2);

        var reach = (await _check.Measure()).Reach.Single();

        reach.Witness.Should().Be("bhsa");
        reach.From.Should().Be("kjv");
        reach.Lexical.Should().Be(2);
        reach.Reached.Should().Be(1);
    }

    [Fact]
    public async Task AWordNamedByTwoLinksIsContended()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Renders, english: 1, hebrew: 2);
        Link(LinkRelation.Renders, english: 2, hebrew: 3);

        var contention = (await _check.Measure()).Contention.Single();

        contention.Contended.Should().Be(1);
        contention.Worst.Should().Be(2);
    }

    /// <summary>
    /// The measure a reader feels, and the one the forward count cannot see. Contention asks how
    /// many words a word claims; this asks how many claim it, which is how many light together
    /// when one is touched.
    /// </summary>
    [Fact]
    public async Task AWitnessWordManyWordsClaimIsCrowded()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Renders, english: 2, hebrew: 1);
        Link(LinkRelation.Renders, english: 3, hebrew: 1);

        var crowding = (await _check.Measure()).Crowding.Single();

        crowding.Worst.Should().Be(3);
        crowding.Crowded.Should().Be(1);
    }

    [Fact]
    public async Task ASoundCorpusBreaksNothing()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);

        var measures = await _check.Measure();

        measures.Sound.Should().BeTrue(because: string.Join(
            ", ", measures.Integrity.Where(i => i.Found > 0).Select(i => $"{i.Found} {i.Breaks}")));
    }

    /// <summary>A Strong number is a letter and digits; anything else reached the column by mistake.</summary>
    [Fact]
    public async Task AMalformedStrongNumberIsFound()
    {
        _db.WordAt(_hebrew, 1, 1, 3).StrongNumber = "H1254a";
        _db.SaveChanges();

        var integrity = await Integrity("Strong numbers that are not a letter and digits");

        integrity.Should().Be(1);
    }

    /// <summary>
    /// The denormalised text on a link and the text of the word it names have to agree. Nothing in
    /// the schema forces it, and a loader that gets it wrong writes links that no query can find.
    /// </summary>
    [Fact]
    public async Task ALinkWhoseTextDisagreesWithItsWordIsFound()
    {
        var link = Link(LinkRelation.Renders, english: 1, hebrew: 1);
        link.ToTextId = _english.Id;
        _db.SaveChanges();

        var integrity = await Integrity("link words whose text disagrees with the link's own");

        integrity.Should().Be(1);
    }

    /// <summary>
    /// An absence is one claim read from either end, and only the relation says which end. A link
    /// that says <c>omits</c> and names a word on the side that is supposed to be empty has thrown
    /// the direction away, and nothing downstream can recover it: 8,451 links between the two Greek
    /// witnesses said <c>omits</c> in both directions, so the relation named the fact of a variant
    /// and never which edition lacked the word.
    /// </summary>
    [Fact]
    public async Task AnAbsenceNamingAWordOnTheSideItSaysIsEmptyIsFound()
    {
        Link(LinkRelation.Omits, english: 1, hebrew: 1);
        Link(LinkRelation.Expands, english: 2, hebrew: null);

        var integrity = await Integrity("absences whose relation contradicts the side the words are on");

        integrity.Should().Be(1);
    }

    [Fact]
    public async Task ALinkNamingNoWordAtAllIsFound()
    {
        _db.Links.Add(new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Manual,
            Source = "a test",
        });
        _db.SaveChanges();

        (await Integrity("links naming no word on either side")).Should().Be(1);
    }

    private async Task<int> Integrity(string breaks) =>
        (await _check.Measure()).Integrity.Single(check => check.Breaks == breaks).Found;

    private Link Link(LinkRelation relation, int english, int? hebrew)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = relation,
            Method = LinkMethod.Manual,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id,
            WordId = _db.WordAt(_english, 1, 1, english).Id,
            Side = LinkSide.From,
        });

        if (hebrew is { } position)
        {
            _db.LinkWords.Add(new LinkWord
            {
                LinkId = link.Id,
                WordId = _db.WordAt(_hebrew, 1, 1, position).Id,
                Side = LinkSide.To,
            });
        }

        _db.SaveChanges();
        return link;
    }
}
