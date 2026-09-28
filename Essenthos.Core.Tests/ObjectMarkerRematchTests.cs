using System.Text.Json;
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
/// A pair a numbering linked before a bare object marker stopped being a target, matched again in
/// place.
///
/// The links here are the ones the old rule wrote for Genesis 1:1 in the Synodal — сотворил against
/// בָּרָא and both אֵת, и against both אֵת — and for a verse where the marker is once bare and once a
/// pronoun. What must hold: a link whose translated words are still matched together keeps its id
/// and becomes what a load would draw now, confidence and claim included; one the numbers no longer
/// draw is gone; nothing is written without being asked; and a second run finds nothing to do.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ObjectMarkerRematchTests : IDisposable
{
    private const string Credit = "a numbering under test";

    private readonly AppDbContext _db;
    private readonly TaggedTextLinkLoader _loader;
    private readonly Text _russian;
    private readonly Text _hebrew;

    public ObjectMarkerRematchTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new TaggedTextLinkLoader(_db, NullLogger<TaggedTextLinkLoader>.Instance);

        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (1, 1, ["В", "начале", "сотворил", "Бог", "небо", "и", "землю"]),
            (1, 2, ["и", "его"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא", "אֱלֹהִים", "אֵת", "הַ", "שָּׁמַיִם", "וְ", "אֵת", "הָ", "אָרֶץ"]),
            (1, 2, ["אֵת", "אֹתוֹ"]));
        _db.SaveChanges();

        string[] genesis = ["H9003", "H7225", "H1254", "H430", "H853", "H9009", "H8064", "H9000", "H853", "H9009", "H776"];
        for (var at = 0; at < genesis.Length; at++)
        {
            Hebrew(1, at + 1).StrongNumber = genesis[at];
        }

        Hebrew(2, 1).StrongNumber = ObjectMarker.Number;
        Hebrew(2, 2).StrongNumber = ObjectMarker.Number;
        Hebrew(2, 2).Morphology = JsonDocument.Parse("""{"pos": "prep", "suffixPerson": "p3"}""");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private Word Russian(int verse, int position) => _db.WordAt(_russian, 1, verse, position);

    private Word Hebrew(int verse, int position) => _db.WordAt(_hebrew, 1, verse, position);

    /// <summary>The Synodal's numbering of both verses, as its edition tags them.</summary>
    private EditionNumbers Numbers()
    {
        (int Verse, int Position, string[] Numbers)[] tags =
        [
            (1, 2, ["H7225"]), (1, 3, ["H1254", "H853"]), (1, 4, ["H430"]), (1, 5, ["H8064"]),
            (1, 6, ["H853"]), (1, 7, ["H776"]),
            (2, 1, ["H853"]), (2, 2, ["H853"]),
        ];
        return new EditionNumbers(
            tags.ToDictionary(
                tag => Russian(tag.Verse, tag.Position).Id,
                tag => new WordTag(tag.Numbers, tag.Verse * 100 + tag.Position)),
            Credit);
    }

    /// <summary>A link as the old rule wrote it, with the claim a load writes beside it.</summary>
    private Link Old(double confidence, int verse, int[] russian, int[] hebrew)
    {
        var link = new Link
        {
            FromTextId = _russian.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber,
            Source = $"{Credit}, matched within the verse against {_hebrew.Slug}",
            Confidence = confidence,
        };
        foreach (var position in russian)
        {
            link.Words.Add(new LinkWord { WordId = Russian(verse, position).Id, Side = LinkSide.From });
        }

        foreach (var position in hebrew)
        {
            link.Words.Add(new LinkWord { WordId = Hebrew(verse, position).Id, Side = LinkSide.To });
        }

        _db.Links.Add(link);
        _db.SaveChanges();
        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id, Method = link.Method, Confidence = link.Confidence, Source = link.Source,
        });
        _db.SaveChanges();
        return link;
    }

    private (Link Created, Link And) GenesisAsTheOldRuleDrewIt()
    {
        Old(0.9, 1, [2], [2]);
        var created = Old(0.5, 1, [3], [3, 5, 9]);
        Old(0.9, 1, [4], [4]);
        Old(0.9, 1, [5], [7]);
        var and = Old(0.5, 1, [6], [5, 9]);
        Old(0.9, 1, [7], [11]);
        return (created, and);
    }

    private async Task<List<Link>> Links() =>
        await _db.Links
            .AsNoTracking()
            .Include(l => l.Words)
            .Include(l => l.Claims)
            .Where(l => l.FromTextId == _russian.Id && l.ToTextId == _hebrew.Id)
            .ToListAsync();

    private List<long> To(Link link) =>
        [.. link.Words.Where(w => w.Side == LinkSide.To).Select(w => w.WordId).Order()];

    /// <summary>
    /// сотворил keeps its link and loses both markers, and is then exactly what an unambiguous match
    /// names, so it reads 0.9 like one — its claim too. и has nothing left to reach, and its link goes.
    /// </summary>
    [Fact]
    public async Task GenesisOneOneKeepsTheVerbOnItsVerbAndLetsTheConjunctionGo()
    {
        var (created, and) = GenesisAsTheOldRuleDrewIt();

        var outcome = await _loader.Rematch(_russian.Slug, _hebrew.Slug, Numbers(), apply: true);

        outcome.Named.Should().Be(2);
        outcome.Changed.Should().Be(1);
        outcome.Removed.Should().Be(1);
        outcome.Added.Should().Be(0);
        outcome.Dropped.Should().Be(2);

        var links = await Links();
        links.Should().HaveCount(5);
        links.Should().NotContain(link => link.Id == and.Id);
        var verb = links.Single(link => link.Id == created.Id);
        To(verb).Should().Equal(Hebrew(1, 3).Id);
        verb.Confidence.Should().Be(StrongNumberMatch.Unambiguous);
        verb.Claims.Should().ContainSingle().Which.Confidence.Should().Be(StrongNumberMatch.Unambiguous);
        links.Should().OnlyContain(link => link.Confidence == StrongNumberMatch.Unambiguous);
    }

    [Fact]
    public async Task NothingIsWrittenWithoutBeingAsked()
    {
        GenesisAsTheOldRuleDrewIt();
        var before = (await Links()).Select(link => (link.Id, link.Confidence, To(link).Count)).ToList();

        var outcome = await _loader.Rematch(_russian.Slug, _hebrew.Slug, Numbers(), apply: false);

        outcome.Changed.Should().Be(1);
        outcome.Removed.Should().Be(1);
        (await Links()).Select(link => (link.Id, link.Confidence, To(link).Count)).Should().Equal(before);
    }

    [Fact]
    public async Task ASecondRunFindsNothingToDo()
    {
        GenesisAsTheOldRuleDrewIt();
        await _loader.Rematch(_russian.Slug, _hebrew.Slug, Numbers(), apply: true);
        var once = (await Links()).Select(link => (link.Id, link.Confidence, To(link).Count)).ToList();

        var again = await _loader.Rematch(_russian.Slug, _hebrew.Slug, Numbers(), apply: true);

        again.Named.Should().Be(0);
        (await Links()).Select(link => (link.Id, link.Confidence, To(link).Count)).Should().Equal(once);
    }

    /// <summary>
    /// The old rule paired и with the bare אֵת and его with אֹתוֹ, in order. The bare one is no target
    /// now, so both tags reach the pronoun alone and the pair can no longer be split: the two links
    /// give way to one naming both words against it, contended, with its own claim.
    /// </summary>
    [Fact]
    public async Task LinksTheRematchDrawsOtherwiseGiveWayToWhatItDraws()
    {
        var and = Old(0.7, 2, [1], [1]);
        var him = Old(0.7, 2, [2], [2]);

        var outcome = await _loader.Rematch(_russian.Slug, _hebrew.Slug, Numbers(), apply: true);

        outcome.Named.Should().Be(2);
        outcome.Removed.Should().Be(2);
        outcome.Added.Should().Be(1);
        var link = (await Links()).Should().ContainSingle().Subject;
        link.Id.Should().NotBe(and.Id).And.NotBe(him.Id);
        link.Words.Where(w => w.Side == LinkSide.From).Select(w => w.WordId)
            .Should().BeEquivalentTo([Russian(2, 1).Id, Russian(2, 2).Id]);
        To(link).Should().Equal(Hebrew(2, 2).Id);
        link.Confidence.Should().Be(StrongNumberMatch.OneSideContended);
        link.Claims.Should().ContainSingle().Which.Method.Should().Be(LinkMethod.StrongNumber);
    }
}
