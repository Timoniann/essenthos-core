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
/// Swete's Septuagint against Brenton's, once the alignment is rows.
///
/// The shapes under test are the ones this pair produces and no earlier loader did: two Greek
/// editions joined on the canonical frame rather than on their own numbering, an accent the two
/// print differently folded away before they are compared, and a word one edition has and the other
/// has not written positively rather than left as a hole.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SeptuagintLinkLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SeptuagintLinkLoader _loader;
    private readonly Text _swete;
    private readonly Text _brenton;

    public SeptuagintLinkLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new SeptuagintLinkLoader(_db, NullLogger<SeptuagintLinkLoader>.Instance);

        // The opening of Genesis, where the two editions differ in the three ways that matter: the
        // accent Swete prints and Brenton does not, the movable nu one keeps and the other drops,
        // and the article one has and the other has not.
        _swete = Corpus.Add(_db, "SWETE", TextKind.CriticalEdition, "grc",
            (1, 1, ["Ἐν", "ἀρχῇ", "ἐποίησεν", "ὁ", "θεὸς"]));
        _brenton = Corpus.Add(_db, "GRCBRENT", TextKind.PrintedEdition, "grc",
            (1, 1, ["Ἐν", "ἀρχῇ", "ἐποίησε", "θεός"]));

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private async Task<List<Link>> Load()
    {
        await _loader.Load(_swete.Slug, _brenton.Slug);
        return await _db.Links
            .Include(l => l.Words)
            .Include(l => l.Provenance)
            .Where(l => l.FromTextId == _swete.Id && l.ToTextId == _brenton.Id)
            .ToListAsync();
    }

    [Fact]
    public async Task RefreshRetainsManualAndUnrelatedBookEvidence()
    {
        var links = await Load();
        var deity = links.Single(l => _db.Side(l.Id, LinkSide.From).Any(w => w.Surface == "θεὸς"));
        var left = _db.Side(deity.Id, LinkSide.From).Select(w => w.Id).ToArray();
        var right = _db.Side(deity.Id, LinkSide.To).Select(w => w.Id).ToArray();
        await _db.Database.OpenConnectionAsync();
        await LinkWriter.Write((Npgsql.NpgsqlConnection)_db.Database.GetDbConnection(), null,
            [new NewLink(_swete.Id, _brenton.Id, deity.Relation, LinkMethod.Manual, null,
                "owner reviewed correspondence", null, left, right)], CancellationToken.None);
        var manual = await _db.LinkClaims.AsNoTracking().SingleAsync(c => c.LinkId == deity.Id && c.Method == LinkMethod.Manual);
        _db.AddBook(_swete, 10, "Second Samuel", (19, 42, ["καὶ"]));
        _db.AddBook(_brenton, 10, "Second Samuel", (19, 42, ["καὶ"]));
        await _db.SaveChangesAsync();
        await _loader.Load(_swete.Slug, _brenton.Slug);
        var other = await _db.Links.AsNoTracking().Where(l => l.Words.Any(w => w.Word!.Verse!.Book!.CanonicalOrdinal == 10))
            .Select(l => l.Id).ToListAsync();
        await _loader.Refresh(_swete.Slug, _brenton.Slug, new HashSet<int> { 1 });
        (await _db.LinkClaims.AsNoTracking().SingleAsync(c => c.Id == manual.Id)).Should()
            .BeEquivalentTo(manual, options => options.Excluding(c => c.Link).Excluding(c => c.Provenance));
        (await _db.Links.AsNoTracking().SingleAsync(l => l.Id == deity.Id)).Method.Should().Be(LinkMethod.Manual);
        (await _db.Links.AsNoTracking().Where(l => l.Words.Any(w => w.Word!.Verse!.Book!.CanonicalOrdinal == 10))
            .Select(l => l.Id).ToListAsync()).Should().Equal(other);
        (await _db.LinkClaims.CountAsync(c => c.LinkId == deity.Id)).Should().Be(2);
    }

    [Fact]
    public async Task AFailedRefreshPreservesTheEarlierDerivedRows()
    {
        await Load();
        var before = await _db.Links.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Fingerprint }).ToListAsync();
        await _db.Texts.Where(t => t.Id == _brenton.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Language, "en"));
        _db.ChangeTracker.Clear();
        var refreshing = () => _loader.Refresh(_swete.Slug, _brenton.Slug, new HashSet<int> { 1 });
        await refreshing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*alphabet*");
        (await _db.Links.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Fingerprint }).ToListAsync())
            .Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
    }

    /// <summary>
    /// Two editions print the same word with different accents, and a comparison that reads the
    /// bytes says they are different words. Folded, they are one word and the link says so.
    /// </summary>
    [Fact]
    public async Task AWordTheTwoEditionsAccentDifferentlyIsStillTheSameWord()
    {
        var links = await Load();

        var deity = links.Single(l => _db.Side(l.Id, LinkSide.From).Any(w => w.Surface == "θεὸς"));

        deity.Relation.Should().Be(LinkRelation.Equals);
        _db.Side(deity.Id, LinkSide.To).Select(w => w.Surface).Should().Equal("θεός");
    }

    /// <summary>
    /// The movable nu: one edition writes it and the other does not, which is a letter of
    /// difference rather than a different word. It pairs, and it pairs at the confidence that says
    /// the spelling was the whole of the evidence.
    /// </summary>
    [Fact]
    public async Task AWordDifferingByOneLetterPairsAsTheSameWordWrittenDifferently()
    {
        var links = await Load();

        var verb = links.Single(l => _db.Side(l.Id, LinkSide.From).Any(w => w.Surface == "ἐποίησεν"));

        verb.Relation.Should().Be(LinkRelation.Renders);
        verb.Confidence.Should().Be(WitnessAlignment.Spelling);
        _db.Side(verb.Id, LinkSide.To).Select(w => w.Surface).Should().Equal("ἐποίησε");
    }

    /// <summary>
    /// The article Swete prints and Brenton does not, stored positively: words on the <c>from</c>
    /// side, nothing on the <c>to</c> side, and <c>expands</c> to say which way round that is. This
    /// is what a second Septuagint was loaded for, so it has to be a statement rather than a gap.
    /// </summary>
    [Fact]
    public async Task AWordTheOtherEditionLacksIsWrittenAsAnExpansionWithAnEmptySide()
    {
        var links = await Load();

        var expansion = links.Should().ContainSingle(l => l.Relation == LinkRelation.Expands).Which;
        expansion.Words.Should().OnlyContain(w => w.Side == LinkSide.From);
        _db.Side(expansion.Id, LinkSide.From).Should().ContainSingle().Which.Surface.Should().Be("ὁ");
        _db.Side(expansion.Id, LinkSide.To).Should().BeEmpty();
    }

    /// <summary>
    /// Nobody states this correspondence — no Septuagint word alignment exists at any price — so
    /// every link is an inference and has to look like one: a method that is not
    /// <c>stated-by-source</c>, and a confidence. A check constraint holds the two apart.
    /// </summary>
    [Fact]
    public async Task EveryLinkSaysItWasInferredAndHowSure()
    {
        var links = await Load();

        links.Should().OnlyContain(l => l.Method == LinkMethod.Lexical);
        links.Should().OnlyContain(l => l.Confidence != null);
        links.Should().OnlyContain(l => l.Provenance!.Source.StartsWith("the letters both Greek editions print"));
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
        var again = await _loader.Load(_swete.Slug, _brenton.Slug);

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.Links.CountAsync(l => l.FromTextId == _swete.Id)).Should().Be(first.Count);
    }

    /// <summary>
    /// Two texts in two languages are refused. The whole evidence here is that both witnesses write
    /// the same alphabet; run over a translation it would pair Greek against Ukrainian by edit
    /// distance and store the result looking exactly like the sound links beside it.
    /// </summary>
    [Fact]
    public async Task TwoTextsInDifferentLanguagesAreRefused()
    {
        _brenton.Language = "ukr";
        await _db.SaveChangesAsync();

        var refused = async () => await _loader.Load(_swete.Slug, _brenton.Slug);

        await refused.Should().ThrowAsync<InvalidOperationException>().WithMessage("*alphabet*");
    }

    /// <summary>
    /// The join is the canonical frame and not either edition's own numbering, which is the whole
    /// difference between this loader and the Hebrew one: these two editions divide most of the
    /// books they share differently. Where the frame puts two of one edition's verses at one
    /// address, the words of both stand in one bag — so the alignment sees the passage the frame
    /// says it is, and no link crosses a verse pair the frame does not already join.
    /// </summary>
    [Fact]
    public async Task VersesTheFramePutsAtOneAddressAreAlignedTogether()
    {
        var first = _db.VerseAt(_brenton, 1, 1);

        // Brenton's last word moved into a verse of its own, numbered 2 in its own text and placed
        // by the frame at 1:1 beside the first — which is what a passage the two editions divide
        // differently looks like.
        var second = new Verse
        {
            TextId = _brenton.Id,
            BookId = first.BookId,
            ChapterId = first.ChapterId,
            ChapterNumber = 1,
            Number = 2,
        };
        _db.Verses.Add(second);
        _db.VerseReferences.Add(new VerseReference
        {
            Verse = second,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            IsPrimary = true,
        });
        _db.Words.Remove(_db.WordAt(_brenton, 1, 1, 4));
        _db.Words.Add(new Word
        {
            TextId = _brenton.Id,
            Verse = second,
            Position = 1,
            Surface = "θεός",
            Trailer = string.Empty,
        });
        await _db.SaveChangesAsync();

        var links = await Load();

        var deity = links.Single(l => _db.Side(l.Id, LinkSide.From).Any(w => w.Surface == "θεὸς"));
        deity.Relation.Should().Be(LinkRelation.Equals);
        _db.Side(deity.Id, LinkSide.To).Should().ContainSingle().Which.VerseId.Should().Be(second.Id);
    }
}
