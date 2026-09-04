using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the published coverage counts, and where it draws its lines.
///
/// Both are claims the corpus makes about itself and both were wrong. It counted the translations
/// alone, which left the worst-covered text in the corpus out of its own headline number, and it
/// pooled each text into one share, which produced a figure for the King James describing neither
/// its Old Testament nor its New.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CoverageSectionTests : IDisposable
{
    private const int Matthew = 40;

    private readonly AppDbContext _db;
    private readonly CorpusCheck _check;
    private readonly Text _hebrew;
    private readonly Text _greek;
    private readonly Text _english;
    private readonly Text _russian;
    private readonly Text _septuagint;

    public CoverageSectionTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _check = new CorpusCheck(_db, NullLogger<CorpusCheck>.Instance);

        _hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא"]));
        _greek = Corpus.Add(_db, "nestle1904", TextKind.CriticalEdition, "grc", (1, 1, ["Βίβλος", "γενέσεως"]));
        _english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning"]));
        _russian = Corpus.Add(_db, "rusv", TextKind.Translation, "rus", (1, 1, ["Книга", "родства"]));

        // A printed edition holding a verse the Hebrew does not, which is the shape of the whole
        // deuterocanon and of the sixty-five verses Brenton's Daniel 3 has beyond the Masoretic.
        _septuagint = Corpus.Add(_db, "lxx-brenton", TextKind.PrintedEdition, "grc",
            (1, 1, ["ἐν", "ἀρχῇ"]), (1, 2, ["ἡ", "δὲ", "γῆ"]));
        _db.SaveChanges();

        _db.In(_greek, Matthew);
        _db.In(_russian, Matthew);

        Link(_english, _hebrew);
        Link(_russian, _greek);
        Link(_septuagint, _hebrew);
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    /// <summary>
    /// The Septuagint is a printed edition, so a measure that counted translations counted none of
    /// its words — and it is the text whose coverage is worst.
    /// </summary>
    [Fact]
    public async Task ATextThatIsNotATranslationIsCountedWhenItHasLinks()
    {
        var coverage = await Coverage();

        coverage.Should().Contain(c => c.Text == "lxx-brenton");
    }

    /// <summary>
    /// The two halves of the canon are reached through different sources and do not reach the same
    /// distance. One number over both describes neither.
    /// </summary>
    [Fact]
    public async Task EachHalfOfTheCanonIsCountedApart()
    {
        var coverage = await Coverage();

        coverage.Single(c => c.Text == "kjv").Section.Should().Be("old testament");
        coverage.Single(c => c.Text == "rusv").Section.Should().Be("new testament");
    }

    /// <summary>
    /// Whether a word was promised anything is a question about its verse. Genesis 1:2 of the Greek
    /// has no Hebrew verse at all, so its words are unpaired — calling them silent would report the
    /// shape of the canon as a failure of the alignment.
    /// </summary>
    [Fact]
    public async Task AWordInAVerseNoWitnessHoldsIsUnpairedRatherThanSilent()
    {
        var coverage = (await Coverage()).Single(c => c.Text == "lxx-brenton");

        coverage.Rendered.Should().Be(1);
        coverage.Silent.Should().Be(1);
        coverage.Unpaired.Should().Be(3);
    }

    /// <summary>
    /// A word with nothing to reach is outside the share, not a failure inside it. Genesis 1:2 of
    /// the Greek has no Hebrew verse, so the Septuagint here reaches one of the two words that had
    /// a counterpart rather than one of its five — and in the corpus itself this is 98,670 words of
    /// deuterocanon that no loaded text holds a single book of.
    /// </summary>
    [Fact]
    public async Task AWordWithNothingToReachIsOutsideTheShareRatherThanBelowIt()
    {
        var measures = await _check.Measure();
        var coverage = measures.Coverage.Single(c => c.Text == "lxx-brenton");

        coverage.Words.Should().Be(5);
        coverage.Promised.Should().Be(2);
        coverage.Share.Should().Be(0.5);

        measures.UnpairedWords.Should().Be(3);
        measures.Words.Should().Be(measures.Coverage.Sum(c => c.Words) - 3);
    }

    private async Task<IReadOnlyList<Coverage>> Coverage() => (await _check.Measure()).Coverage;

    /// <summary>The first word of each text's first verse, which is enough to pair the two.</summary>
    private void Link(Text from, Text to)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = to.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Manual,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id, WordId = _db.WordAt(from, 1, 1, 1).Id, Side = LinkSide.From,
        });
        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id, WordId = _db.WordAt(to, 1, 1, 1).Id, Side = LinkSide.To,
        });
        _db.SaveChanges();
    }
}
