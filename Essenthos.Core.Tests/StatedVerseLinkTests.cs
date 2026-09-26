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
/// A source that says which word renders which has said, in the same breath, which verse answers
/// which — and where the canonical frame disagrees, the source is the better witness.
///
/// This exists because of the Reina-Valera. eBible mapped its two German texts into the English
/// numbering and did not map the Spanish, so in ten chapters the frame places a Spanish
/// verse one row from the Hebrew verse it renders. Clear Bible's hand-made alignment is keyed to the
/// Spanish file's own numbering and joins the two correctly, and without this step the corpus holds
/// that hand-made claim and its own verification reports it as a fault.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StatedVerseLinkTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly VerseLinkLoader _loader;
    private readonly Text _spanish;
    private readonly Text _hebrew;

    public StatedVerseLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance);

        // The seam: the Spanish prints as 30:1 what the frame places at 29:40, so the two verses
        // stand at different canonical addresses and nothing derived from the frame joins them.
        _spanish = Corpus.Add(_db, "RV1909", TextKind.Translation, "spa", (30, 1, ["Y", "habló"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo", (30, 1, ["וַ", "יְדַבֵּר"]));

        foreach (var reference in _db.VerseReferences.Where(r => r.Verse!.TextId == _spanish.Id))
        {
            reference.CanonicalChapter = 29;
            reference.CanonicalVerse = 40;
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private void Link(LinkMethod method, double? confidence, LinkRelation relation = LinkRelation.Renders)
    {
        var link = new Link
        {
            FromTextId = _spanish.Id,
            ToTextId = _hebrew.Id,
            Relation = relation,
            Method = method,
            Confidence = confidence,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord
        {
            Link = link,
            WordId = _db.WordAt(_spanish, 30, 1, 1).Id,
            Side = LinkSide.From,
        });
        _db.LinkWords.Add(new LinkWord
        {
            Link = link,
            WordId = _db.WordAt(_hebrew, 30, 1, 1).Id,
            Side = LinkSide.To,
        });
        _db.SaveChanges();
    }

    private Task<List<VerseLink>> Stated() =>
        _db.VerseLinks.Include(v => v.Verses).Where(v => v.Note != null).ToListAsync();

    [Fact]
    public async Task AStatedWordLinkAcrossAVerseTheFrameDoesNotJoinBecomesAVerseLink()
    {
        Link(LinkMethod.StatedBySource, null);

        var written = await _loader.Stated();

        written.Should().Be(1);
        var link = (await Stated()).Should().ContainSingle().Subject;
        link.Method.Should().Be(LinkMethod.StatedBySource);
        link.Confidence.Should().BeNull();
        link.Source.Should().Be("a test");
        link.Verses.Should().HaveCount(2);
    }

    /// <summary>
    /// A model proposing a link across a verse boundary is not testifying to anything, and taking
    /// its word here would turn every stray alignment into a statement about versification.
    /// </summary>
    [Fact]
    public async Task AnAlignersWordLinkAcrossTheSameBoundaryStatesNothing()
    {
        Link(LinkMethod.Aligner, 0.4);

        (await _loader.Stated()).Should().Be(0);
        (await Stated()).Should().BeEmpty();
    }

    /// <summary>
    /// A transposition is the claim that a passage stands elsewhere, whoever drew it, so the verse
    /// pair it names is written as a transposition with the link's own method and confidence.
    /// </summary>
    [Fact]
    public async Task ATranspositionAcrossTheBoundaryStatesItsVersePair()
    {
        Link(LinkMethod.Lexical, 0.8, LinkRelation.Transposes);

        (await _loader.Stated()).Should().Be(1);
        var link = (await Stated()).Should().ContainSingle().Subject;
        link.Relation.Should().Be(LinkRelation.Transposes);
        link.Method.Should().Be(LinkMethod.Lexical);
        link.Confidence.Should().Be(0.8);
    }

    [Fact]
    public async Task ASecondRunAddsNothing()
    {
        Link(LinkMethod.StatedBySource, null);

        await _loader.Stated();

        (await _loader.Stated()).Should().Be(0);
        (await Stated()).Should().ContainSingle();
    }

    /// <summary>
    /// Where the frame already joins the two verses there is nothing to add, which is the case
    /// almost everywhere: the whole step writes a few hundred rows against a corpus of six million
    /// links.
    /// </summary>
    [Fact]
    public async Task NothingIsWrittenWhereTheFrameAlreadyJoinsTheVerses()
    {
        foreach (var reference in _db.VerseReferences.Where(r => r.Verse!.TextId == _spanish.Id))
        {
            reference.CanonicalChapter = 30;
            reference.CanonicalVerse = 1;
        }

        _db.SaveChanges();
        Link(LinkMethod.StatedBySource, null);
        await _loader.Load();

        (await Stated()).Should().BeEmpty();
    }
}
