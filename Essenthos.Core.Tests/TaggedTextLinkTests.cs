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
/// A translation that arrived carrying its own Strong numbers, linked to a witness that carries them
/// too — the route Luther 1912 reaches Hebrew and Greek by, and the only one it has that is not this
/// project's own model.
///
/// The shapes here are the ones that must survive being rows: a link that says <c>strong-number</c>
/// and not <c>stated-by-source</c>, because eBible states the number and the correspondence is
/// ours; and a second run that adds nothing, because the startup pipeline re-runs.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TaggedTextLinkTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TaggedTextLinkLoader _loader;
    private readonly Text _german;
    private readonly Text _hebrew;

    public TaggedTextLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new TaggedTextLinkLoader(_db, NullLogger<TaggedTextLinkLoader>.Instance);

        _german = Corpus.Add(_db, "LUTH1912", TextKind.Translation, "deu",
            (1, 1, ["Am", "Anfang", "schuf", "Gott", "Himmel", "und", "Erde"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא", "אֱלֹהִים", "אֵת", "הַ", "שָּׁמַיִם"]));

        _db.SaveChanges();

        Tag(_german, [null, "H7225", "H1254", "H430", "H8064", null, "H776"]);
        Tag(_hebrew, ["H9003", "H7225", "H1254", "H430", "H853", "H9009", "H8064"]);
    }

    private void Tag(Text text, string?[] numbers)
    {
        var words = _db.Words.Where(w => w.TextId == text.Id).OrderBy(w => w.Position).ToList();
        for (var at = 0; at < numbers.Length; at++)
        {
            words[at].StrongNumber = numbers[at];
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private async Task<List<Link>> Links() =>
        await _db.Links
            .Include(l => l.Words)
            .Where(l => l.FromTextId == _german.Id && l.ToTextId == _hebrew.Id)
            .ToListAsync();

    /// <summary>
    /// The number is the source's and the correspondence is not, so nothing here may say
    /// <c>stated-by-source</c>. The confidence column is where the difference between a settled
    /// match and a guess is kept, and a stated link is forbidden to carry one.
    /// </summary>
    [Fact]
    public async Task EveryLinkSaysItWasMatchedByNumberAndCarriesAConfidence()
    {
        await _loader.Load(_german.Slug, _hebrew.Slug);

        var links = await Links();
        links.Should().NotBeEmpty();
        links.Should().OnlyContain(link => link.Method == LinkMethod.StrongNumber);
        links.Should().OnlyContain(link => link.Confidence != null);
        links.Should().OnlyContain(link => link.Relation == LinkRelation.Renders);
    }

    /// <summary>
    /// The German word whose number the Hebrew verse writes once reaches that word and no other.
    /// </summary>
    [Fact]
    public async Task ATaggedWordReachesTheWitnessWordCarryingItsNumber()
    {
        await _loader.Load(_german.Slug, _hebrew.Slug);

        var gott = _db.WordAt(_german, 1, 1, 4);
        var link = (await Links()).Single(l => l.Words!.Any(w => w.WordId == gott.Id));

        _db.Side(link.Id, LinkSide.To).Select(word => word.Surface).Should().Equal("אֱלֹהִים");
        link.Confidence.Should().Be(0.9);
    }

    /// <summary>
    /// A word the file does not tag is reached by nothing. Luther tags 52% of its words and the
    /// untagged half is the function words: no source says anything about them, and this loader is
    /// not the place to start guessing.
    /// </summary>
    [Fact]
    public async Task AnUntaggedWordIsLeftAlone()
    {
        await _loader.Load(_german.Slug, _hebrew.Slug);

        var und = _db.WordAt(_german, 1, 1, 6);
        (await Links()).Should().NotContain(link => link.Words!.Any(w => w.WordId == und.Id));
    }

    /// <summary>
    /// A number the witness does not write reaches nothing, and is counted rather than silenced.
    /// H776 is Luther's <em>Erde</em>; this cut of the Hebrew verse stops before it.
    /// </summary>
    [Fact]
    public async Task ANumberTheWitnessDoesNotWriteIsCounted()
    {
        var outcome = await _loader.Load(_german.Slug, _hebrew.Slug);

        outcome.Unmatched.Should().Be(1);
        outcome.Tagged.Should().Be(5);
        outcome.Matched.Should().Be(4);
    }

    /// <summary>The startup pipeline re-runs on every boot, so a second run must add nothing.</summary>
    [Fact]
    public async Task ASecondRunAddsNothing()
    {
        await _loader.Load(_german.Slug, _hebrew.Slug);
        var first = (await Links()).Count;

        var again = await _loader.Load(_german.Slug, _hebrew.Slug);

        again.AlreadyLoaded.Should().BeTrue();
        (await Links()).Should().HaveCount(first);
    }

    /// <summary>
    /// A Greek number in a German word never reaches a Hebrew witness, whatever digits it shares
    /// with one. The series is read from the witness's language rather than from the tag, because a
    /// number read out of the wrong half is a valid Strong number for the wrong word.
    /// </summary>
    [Fact]
    public async Task AGreekNumberIsNotOfferedToAHebrewWitness()
    {
        Tag(_german, [null, "G7225", null, null, null, null, null]);

        var outcome = await _loader.Load(_german.Slug, _hebrew.Slug);

        outcome.Tagged.Should().Be(0);
        (await Links()).Should().BeEmpty();
    }
}
