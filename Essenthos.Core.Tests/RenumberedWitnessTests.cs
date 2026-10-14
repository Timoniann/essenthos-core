using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The title of Psalm 52 as the corpus held it after BHSA's numbers there were put right: BHSA writes
/// the title as two verses at one address, and every numbering had matched its <em>Doeg</em> against
/// the preposition that then carried Doeg's number, and its <em>David</em> against Doeg's word. The
/// numbered links there are matched again against the numbers the Hebrew carries now, in place, and
/// the name the Hebrew gives Doeg reaches the translation over them.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RenumberedWitnessTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TaggedTextLinkLoader _tagged;
    private readonly RenumberedWitnessRepair _repair;
    private readonly Text _hebrew;
    private readonly Text _luther;
    private readonly Text _synodal;
    private readonly Entity _doeg;

    public RenumberedWitnessTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _tagged = new TaggedTextLinkLoader(_db, NullLogger<TaggedTextLinkLoader>.Instance);
        _repair = new RenumberedWitnessRepair(
            _db,
            _tagged,
            new SynodalStrongLinkLoader(_db, _tagged, NullLogger<SynodalStrongLinkLoader>.Instance),
            new UnionStrongLinkLoader(_db, _tagged, NullLogger<UnionStrongLinkLoader>.Instance),
            new ReinaValeraStrongLinkLoader(_db, _tagged, NullLogger<ReinaValeraStrongLinkLoader>.Instance),
            NullLogger<RenumberedWitnessRepair>.Instance);

        _hebrew = Corpus.Add(_db, BhsaTextSource.Slug, TextKind.CriticalEdition, "hbo",
            (52, 1, ["לַ", "מְנַצֵּחַ", "מַשְׂכִּיל", "לְ", "דָוִד"]),
            (52, 2, ["בְּ", "בוֹא", "דּוֹאֵג", "הָ", "אֲדֹמִי"]),
            (52, 3, ["מַה", "תִּתְהַלֵּל"]));
        _luther = Corpus.Add(_db, "LUTH1912", TextKind.Translation, "deu",
            (52, 0, ["Eine", "Unterweisung", "Davids", "da", "Doeg", "der", "Edomiter", "kam"]),
            (52, 3, ["Was", "trotzest"]));
        _synodal = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (52, 0, ["Учение", "Давида", "после", "того", "как", "приходил", "Доик", "Идумеянин"]));
        _db.SaveChanges();

        foreach (var reference in _db.VerseReferences.Where(r => r.Verse!.TextId == _hebrew.Id && r.Verse.Number < 3))
        {
            reference.CanonicalVerse = 0;
        }

        string[][] numbers =
        [
            ["H9005", "H5329", "H4905", "H9005", "H1732"],
            ["H9003", "H935", "H1673", "H9009", "H130"],
            ["H4100", "H1984"],
        ];
        for (var verse = 0; verse < numbers.Length; verse++)
        {
            for (var position = 0; position < numbers[verse].Length; position++)
            {
                Hebrew(verse + 1, position + 1).StrongNumber = numbers[verse][position];
            }
        }

        Luther(0, 3).StrongNumber = "H1732";
        Luther(0, 5).StrongNumber = "H1673";
        Luther(0, 7).StrongNumber = "H130";
        _db.WordAt(_luther, 52, 3, 2).StrongNumber = "H1984";

        _doeg = new Entity { Kind = EntityKind.Person, Slug = "doeg", Name = "Doeg", SourceId = "doeg", Source = "a test" };
        _db.Entities.Add(_doeg);
        _db.SaveChanges();
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Hebrew(2, 3).Id, EntityId = _doeg.Id, Method = LinkMethod.StrongNumber, Confidence = 0.9,
            Source = EntityAnnotationLoader.Written[0], Note = "H1673, which BHSA marks pers",
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM carry_mark");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private Word Hebrew(int verse, int position) => _db.WordAt(_hebrew, 52, verse, position);

    private Word Luther(int verse, int position) => _db.WordAt(_luther, 52, verse, position);

    private Word Synodal(int position) => _db.WordAt(_synodal, 52, 0, position);

    private const string SynodalCredit = "a numbering of the Synodal under test";

    /// <summary>The Synodal's numbering, laid onto its words as an edition is: never on the words themselves.</summary>
    private EditionNumbers SynodalNumbers() =>
        new(new Dictionary<long, WordTag>
        {
            [Synodal(2).Id] = new(["H1732"], 2),
            [Synodal(7).Id] = new(["H1673"], 7),
            [Synodal(8).Id] = new(["H130"], 8),
        }, SynodalCredit);

    /// <summary>A link as a numbering drew it, with its own claim beside it.</summary>
    private Link Numbered(Text from, string source, Word translated, Word witness)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber,
            Confidence = StrongNumberMatch.Unambiguous,
            Provenance = new() { Source = source },
        };
        link.Words.Add(new LinkWord { WordId = translated.Id, Side = LinkSide.From });
        link.Words.Add(new LinkWord { WordId = witness.Id, Side = LinkSide.To });
        _db.Links.Add(link);
        _db.SaveChanges();
        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id, Method = link.Method, Confidence = link.Confidence, Provenance = link.Provenance,
        });
        _db.SaveChanges();
        return link;
    }

    /// <summary>Luther's title as the numbers read a word out of step drew it, and its first verse as they drew it right.</summary>
    private (Link Doeg, Link David, Link Edomite, Link Verse) LutherAsTheOldNumbersDrewIt()
    {
        var source = TaggedTextLinkLoader.Source(_luther.Slug, _hebrew.Slug);
        return (
            Numbered(_luther, source, Luther(0, 5), Hebrew(1, 4)),
            Numbered(_luther, source, Luther(0, 3), Hebrew(2, 3)),
            Numbered(_luther, source, Luther(0, 7), Hebrew(2, 5)),
            Numbered(_luther, source, _db.WordAt(_luther, 52, 3, 2), Hebrew(3, 2)));
    }

    private async Task<Dictionary<long, (List<long> From, List<long> To)>> Links(Text from) =>
        (await _db.Links.AsNoTracking().Include(l => l.Words)
            .Where(l => l.FromTextId == from.Id && l.ToTextId == _hebrew.Id)
            .ToListAsync())
        .ToDictionary(
            l => l.Id,
            l => (l.Words.Where(w => w.Side == LinkSide.From).Select(w => w.WordId).Order().ToList(),
                  l.Words.Where(w => w.Side == LinkSide.To).Select(w => w.WordId).Order().ToList()));

    /// <summary>
    /// Doeg's and David's links keep their ids and their claims and move to the words that carry their
    /// numbers now; the Edomite and the psalm's first verse, which the numbers still bear out, are not
    /// touched. A second run finds nothing stale.
    /// </summary>
    [Fact]
    public async Task ATitleMatchedAgainstShiftedNumbersIsMatchedAgainInPlace()
    {
        var (doeg, david, edomite, verse) = LutherAsTheOldNumbersDrewIt();

        var outcome = await _repair.Run(Path.GetTempPath());

        outcome.Stale.Should().Be(2);
        outcome.Changed.Should().Be(2);
        outcome.Removed.Should().Be(0);
        outcome.Added.Should().Be(0);
        var links = await Links(_luther);
        links.Keys.Should().BeEquivalentTo(new[] { doeg.Id, david.Id, edomite.Id, verse.Id });
        links[doeg.Id].To.Should().Equal(Hebrew(2, 3).Id);
        links[david.Id].To.Should().Equal(Hebrew(1, 5).Id);
        links[edomite.Id].To.Should().Equal(Hebrew(2, 5).Id);
        links[verse.Id].To.Should().Equal(Hebrew(3, 2).Id);
        (await _db.LinkClaims.CountAsync(c => c.LinkId == doeg.Id)).Should().Be(1);
        outcome.Verses.Should().BeEquivalentTo(new[] { Luther(0, 1).VerseId, Hebrew(1, 1).VerseId, Hebrew(2, 1).VerseId });

        var again = await _repair.Run(Path.GetTempPath());

        again.Stale.Should().Be(0);
        again.Verses.Should().BeEmpty();
        (await Links(_luther)).Should().BeEquivalentTo(links);
    }

    /// <summary>A link another method drew, with its own claim beside it.</summary>
    private Link Drawn(Text from, LinkMethod method, string source, double confidence, Word translated, Word witness)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new() { Source = source },
        };
        link.Words.Add(new LinkWord { WordId = translated.Id, Side = LinkSide.From });
        link.Words.Add(new LinkWord { WordId = witness.Id, Side = LinkSide.To });
        _db.Links.Add(link);
        _db.SaveChanges();
        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id, Method = link.Method, Confidence = link.Confidence, Provenance = link.Provenance,
        });
        _db.SaveChanges();
        return link;
    }

    private async Task<List<(LinkMethod Method, string Source)>> Claims(long link) =>
        (await _db.LinkClaims.AsNoTracking().Where(c => c.LinkId == link)
            .Select(c => new { c.Method, c.Provenance!.Source }).ToListAsync())
        .Select(c => (c.Method, c.Source)).ToList();

    /// <summary>
    /// The aligner had already joined Luther's <em>Doeg</em> to Doeg's word, so the word the numbers
    /// now give it is taken: Doeg's numbered link joins the aligner's as a claim and goes, and the
    /// number, the stronger claim, becomes that link's answer. Two links over one set of words would
    /// be refused when the repair commits.
    /// </summary>
    [Fact]
    public async Task ALinkMatchedOntoWordsAnotherMethodAlreadyLinksJoinsThatLink()
    {
        var (doeg, david, _, _) = LutherAsTheOldNumbersDrewIt();
        var source = TaggedTextLinkLoader.Source(_luther.Slug, _hebrew.Slug);
        var aligned = Drawn(_luther, LinkMethod.Aligner, "an aligner under test", 0.6, Luther(0, 5), Hebrew(2, 3));

        var outcome = await _repair.Run(Path.GetTempPath());

        outcome.Stale.Should().Be(2);
        outcome.Changed.Should().Be(1, "David's link moves in place");
        outcome.Joined.Should().Be(1, "Doeg's link joins the aligner's");
        outcome.Removed.Should().Be(0);
        var links = await Links(_luther);
        links.Should().NotContainKey(doeg.Id);
        links[aligned.Id].Should().BeEquivalentTo((new List<long> { Luther(0, 5).Id }, new List<long> { Hebrew(2, 3).Id }));
        links[david.Id].To.Should().Equal(Hebrew(1, 5).Id);
        var standing = await _db.Links.AsNoTracking().Include(l => l.Provenance).SingleAsync(l => l.Id == aligned.Id);
        standing.Method.Should().Be(LinkMethod.StrongNumber);
        standing.Provenance!.Source.Should().Be(source);
        standing.Confidence.Should().Be(StrongNumberMatch.Unambiguous);
        (await Claims(aligned.Id)).Should().BeEquivalentTo(new[]
        {
            (LinkMethod.Aligner, "an aligner under test"), (LinkMethod.StrongNumber, source),
        });
        outcome.Verses.Should().Contain([Luther(0, 1).VerseId, Hebrew(1, 1).VerseId, Hebrew(2, 1).VerseId]);

        var again = await _repair.Run(Path.GetTempPath());

        again.Stale.Should().Be(0);
        (await Links(_luther)).Should().BeEquivalentTo(links);
    }

    /// <summary>
    /// A numbering read from an edition and never written to the words — the Synodal's — is matched
    /// again by the numbers it was drawn from, and a translated word no link named before is matched
    /// now that a word carries its number.
    /// </summary>
    [Fact]
    public async Task AnEditionsNumbersAreReadAsTheirLoadReadThem()
    {
        var source = $"{SynodalCredit}, matched within the verse against {_hebrew.Slug}";
        var doeg = Numbered(_synodal, source, Synodal(7), Hebrew(1, 4));

        var (outcome, stale, touched) = await _tagged.RematchAt(
            _synodal.Slug, _hebrew.Slug, SynodalNumbers(), new HashSet<(int, int, int)> { (1, 52, 0) }, apply: true);

        stale.Should().Be(1);
        outcome.Changed.Should().Be(1);
        outcome.Added.Should().Be(2, "David and the Edomite had no link, and their numbers stand on words now");
        var links = await Links(_synodal);
        links[doeg.Id].To.Should().Equal(Hebrew(2, 3).Id);
        links.Values.Select(link => (link.From.Single(), link.To.Single())).Should().BeEquivalentTo(new[]
        {
            (Synodal(7).Id, Hebrew(2, 3).Id), (Synodal(2).Id, Hebrew(1, 5).Id), (Synodal(8).Id, Hebrew(2, 5).Id),
        });
        touched.Should().Contain([Synodal(7).Id, Hebrew(1, 4).Id, Hebrew(2, 3).Id]);

        (await _tagged.RematchAt(
                _synodal.Slug, _hebrew.Slug, SynodalNumbers(), new HashSet<(int, int, int)> { (1, 52, 0) }, apply: true))
            .Stale.Should().Be(0);
    }

    /// <summary>
    /// What the load does with it: the links move in place, so no newer link marks the verse, and the
    /// repair names the verses for the carry. Doeg, named on the Hebrew, reaches Luther's Doeg.
    /// </summary>
    [Fact]
    public async Task TheNameTheHebrewGivesDoegReachesTheWordMatchedToItAgain()
    {
        LutherAsTheOldNumbersDrewIt();
        var carrier = new AnnotationCarrier(
            _db,
            new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(_db, NullLogger<ForeignNames>.Instance),
            new EqualTwinNames(_db, NullLogger<EqualTwinNames>.Instance),
            new PronounReferents(_db, NullLogger<PronounReferents>.Instance),
            NullLogger<AnnotationCarrier>.Instance);
        await carrier.CarryNewLinks();
        (await _db.WordEntities.AnyAsync(a => a.WordId == Luther(0, 5).Id)).Should().BeFalse();
        (await _db.WordEntities.AnyAsync(a => a.WordId == Luther(0, 3).Id)).Should().BeTrue("Davids was matched to Doeg's word");

        var outcome = await _repair.Run(Path.GetTempPath());
        var carried = await carrier.CarryNewLinks(default, outcome.Verses);

        carried.Since.Should().Be(carried.Through, "no link was drawn; the links moved in place");
        (await _db.WordEntities.Where(a => a.WordId == Luther(0, 5).Id).Select(a => a.EntityId).ToListAsync())
            .Should().Equal(_doeg.Id);
        (await _db.WordEntities.AnyAsync(a => a.WordId == Luther(0, 3).Id)).Should().BeFalse();
    }
}
