using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The annotations the links carried, carried again after the links moved.
///
/// Genesis 10:13 as the Synodal stood on the day the numbered links arrived: the aligner had put
/// <em>От</em> opposite לוּדִים at 0.69 and the annotation of the Ludim went with it, and the numbered
/// link to <em>Лудим</em> itself came afterwards and reached a word no pass would ever carry to again.
/// Every case is one thing the new links can do to a word that already has, or has not, a name.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class AnnotationCarrierTests : IDisposable
{
    private const string People = "the gentilic Strong's Dictionary derives, resolving to exactly one people";

    private const string VerseList = "the encyclopedia's own list of the verses each entity is named in";

    private readonly AppDbContext _db;
    private readonly AnnotationCarrier _carrier;
    private readonly Text _hebrew;
    private readonly Text _russian;
    private readonly Entity _ludim;
    private readonly WordEntity _seed;

    public AnnotationCarrierTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _carrier = new AnnotationCarrier(
            _db,
            new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(_db, NullLogger<ForeignNames>.Instance),
            new EqualTwinNames(_db, NullLogger<EqualTwinNames>.Instance), new PronounReferents(_db, NullLogger<PronounReferents>.Instance), NullLogger<AnnotationCarrier>.Instance);

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (10, 13, ["מצרים", "לודים"]));
        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (10, 13, ["От", "Мицраима", "Лудим"]),
            (10, 14, ["от", "Лудима"]));

        _ludim = new Entity
        {
            Kind = EntityKind.People, Slug = "lydians", Name = "Lydians", SourceId = "lydians",
            Source = "a test",
        };
        _db.Entities.Add(_ludim);
        _db.SaveChanges();

        _seed = new WordEntity
        {
            WordId = Hebrew(2).Id,
            EntityId = _ludim.Id,
            Method = LinkMethod.Lexical,
            Confidence = 0.9,
            Source = People,
            Note = "H3866, which Strong's Dictionary derives from H3865",
            Claims =
            [
                new WordEntityClaim
                {
                    Method = LinkMethod.Lexical, Confidence = 0.9, Source = People,
                    Note = "H3866, which Strong's Dictionary derives from H3865",
                },
                new WordEntityClaim
                {
                    Method = LinkMethod.StatedBySource, Confidence = null, Source = VerseList,
                    Note = "the encyclopedia states that this entity is named in this verse",
                },
            ],
        };
        _db.WordEntities.Add(_seed);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private Word Hebrew(int position) => _db.WordAt(_hebrew, 10, 13, position);

    private Word Russian(int position) => _db.WordAt(_russian, 10, 13, position);

    /// <summary>A link saying one word of a translation renders one witness word.</summary>
    private Link Link(Word witness, Word rendering, LinkMethod method, double? confidence)
    {
        var link = new Link
        {
            FromTextId = rendering.TextId,
            ToTextId = witness.TextId,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = witness, Side = LinkSide.To });
        _db.SaveChanges();
        return link;
    }

    private async Task<Dictionary<long, WordEntity>> Named() =>
        await _db.WordEntities
            .AsNoTracking()
            .Include(a => a.Claims)
            .Where(a => a.Word!.TextId == _russian.Id)
            .ToDictionaryAsync(a => a.WordId);

    /// <summary>
    /// The case the pass exists for: a word a link arriving later reaches, which no pass that had
    /// already run would ever carry a name to.
    /// </summary>
    [Fact]
    public async Task AWordALaterLinkReachesIsNamed()
    {
        Link(Hebrew(2), Russian(1), LinkMethod.Aligner, 0.69);
        await _carrier.Carry();

        Link(Hebrew(2), Russian(3), LinkMethod.StrongNumber, 0.9);
        var outcome = await _carrier.Carry();

        var named = await Named();
        named.Should().ContainKey(Russian(3).Id);
        named[Russian(3).Id].EntityId.Should().Be(_ludim.Id);
        named[Russian(3).Id].Confidence.Should().BeApproximately(0.81, 1e-9);
        outcome.Written.Should().Be(1);
    }

    /// <summary>
    /// And the word the aligner had nowhere else to put. It keeps its link — the link is not this
    /// pass's to remove — but it no longer names the Ludim, because the verse now renders them firmly
    /// two words later.
    /// </summary>
    [Fact]
    public async Task AFaintLinkThatNamedAWordLosesItOnceTheNameIsRenderedFirmly()
    {
        Russian(1).Surface = "Сыны";
        await _db.SaveChangesAsync();
        Link(Hebrew(2), Russian(1), LinkMethod.Aligner, 0.69);
        await _carrier.Carry();
        (await Named()).Should().ContainKey(Russian(1).Id);

        Link(Hebrew(2), Russian(3), LinkMethod.StrongNumber, 0.9);
        var outcome = await _carrier.Carry();

        (await Named()).Should().NotContainKey(Russian(1).Id);
        outcome.Withdrawn.Should().Be(1);
    }

    /// <summary>
    /// The searchable forms the page is read by: the text writes <em>от</em> without a capital at
    /// 10:14, so the <em>От</em> opening 10:13 is that preposition, and it writes the name only ever
    /// with one.
    /// </summary>
    private void Spelled()
    {
        Russian(1).NormalisedText = "от";
        Russian(2).NormalisedText = "мицраима";
        Russian(3).NormalisedText = "лудим";
        _db.WordAt(_russian, 10, 14, 1).NormalisedText = "от";
        _db.WordAt(_russian, 10, 14, 2).NormalisedText = "лудима";
        _db.SaveChanges();
    }

    /// <summary>
    /// Numbers 26:20 in Ukrainian: <em>від</em> reached from the Shelanites at 0.93, beside
    /// <em>Шелин</em> at 0.98. Both links are above anything a confidence line could draw, and the
    /// page is what tells the preposition from the name.
    /// </summary>
    [Fact]
    public async Task AWordTheTextWritesWithoutACapitalLosesTheNameBesideOneWrittenAsAName()
    {
        Spelled();
        Link(Hebrew(2), Russian(1), LinkMethod.Aligner, 0.93);
        Link(Hebrew(2), Russian(3), LinkMethod.Aligner, 0.98);

        await _carrier.Carry();

        var named = await Named();
        named.Should().NotContainKey(Russian(1).Id);
        named.Should().ContainKey(Russian(3).Id);
    }

    /// <summary>
    /// Where the seed reached nothing else in the verse, the word is the only account there is, and
    /// a text that writes its gentilics in lower case is still naming the people.
    /// </summary>
    [Fact]
    public async Task AWordWrittenWithoutACapitalKeepsTheNameWhereNothingBesideItIsWrittenAsAName()
    {
        Spelled();
        Russian(1).Surface = "лудимляне";
        Russian(1).NormalisedText = "лудимляне";
        await _db.SaveChangesAsync();
        Link(Hebrew(2), Russian(1), LinkMethod.Aligner, 0.93);

        await _carrier.Carry();

        (await Named()).Should().ContainKey(Russian(1).Id);
    }

    /// <summary>
    /// A preposition is never the name, however strong the link that reached it.
    /// </summary>
    [Fact]
    public async Task APrepositionIsNeverNamed()
    {
        Link(Hebrew(2), Russian(1), LinkMethod.StrongNumber, 0.95);

        await _carrier.Carry();

        (await Named()).Should().NotContainKey(Russian(1).Id);
    }

    /// <summary>
    /// A word the text capitalises in the middle of a sentence is written as a name here, whatever
    /// the text does with it elsewhere: <em>Господа Бога</em> renders the divine name in two words.
    /// </summary>
    [Fact]
    public async Task AWordCapitalisedMidSentenceKeepsTheNameBesideOneWrittenAsAName()
    {
        Spelled();
        Russian(2).Surface = "Бога";
        Russian(2).NormalisedText = "от";
        await _db.SaveChangesAsync();
        Link(Hebrew(2), Russian(2), LinkMethod.Aligner, 0.93);
        Link(Hebrew(2), Russian(3), LinkMethod.Aligner, 0.98);

        await _carrier.Carry();

        (await Named()).Should().ContainKey(Russian(2).Id);
    }

    /// <summary>A link removed as a guess the numbers contradicted takes its annotation with it.</summary>
    [Fact]
    public async Task AnAnnotationWhoseLinkIsGoneIsWithdrawn()
    {
        var guess = Link(Hebrew(2), Russian(2), LinkMethod.Aligner, 0.8);
        await _carrier.Carry();
        (await Named()).Should().ContainKey(Russian(2).Id);

        _db.Links.Remove(guess);
        await _db.SaveChangesAsync();
        await _carrier.Carry();

        (await Named()).Should().BeEmpty();
    }

    /// <summary>
    /// The number an annotation carries is the number of the link standing under it now, not of the
    /// guess it was first carried across.
    /// </summary>
    [Fact]
    public async Task AnAnnotationFollowsTheConfidenceOfTheLinkNowStandingUnderIt()
    {
        var link = Link(Hebrew(2), Russian(3), LinkMethod.Aligner, 0.67);
        await _carrier.Carry();

        link.Method = LinkMethod.StrongNumber;
        link.Confidence = 0.9;
        await _db.SaveChangesAsync();
        await _carrier.Carry();

        var named = await Named();
        named[Russian(3).Id].Confidence.Should().BeApproximately(0.81, 1e-9);
        named[Russian(3).Id].Claims.Should().Contain(c =>
            c.Method == LinkMethod.Lexical && c.Confidence != null && Math.Abs(c.Confidence.Value - 0.81) < 1e-9);
    }

    /// <summary>
    /// A numbered link pairing a repeated name in order is worth 0.70, and where the aligner had
    /// paired the same two words at 0.97 the name crosses at 0.97: two methods agreeing are not less
    /// sure than the better of them.
    /// </summary>
    [Fact]
    public async Task AOneToOneLinkIsWorthTheBestClaimStandingOnIt()
    {
        var link = Link(Hebrew(2), Russian(3), LinkMethod.StrongNumber, 0.7);
        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id, Method = LinkMethod.Aligner, Confidence = 0.97, Provenance = new() { Source = "an aligner" },
        });
        await _db.SaveChangesAsync();

        await _carrier.Carry();

        (await Named())[Russian(3).Id].Confidence.Should().BeApproximately(0.9 * 0.97, 1e-9);
    }

    /// <summary>
    /// And not where the link names a set. A claim folded into it may be about any pair within the
    /// set, so it cannot vouch for the one word the name lands on.
    /// </summary>
    [Fact]
    public async Task ALinkNamingSeveralWordsIsWorthOnlyItsOwnNumber()
    {
        var link = new Link
        {
            FromTextId = _russian.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber, Confidence = 0.3, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Russian(1), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Russian(3), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Hebrew(2), Side = LinkSide.To });
        _db.LinkClaims.Add(new LinkClaim
        {
            Link = link, Method = LinkMethod.Aligner, Confidence = 0.97, Provenance = new() { Source = "an aligner" },
        });
        await _db.SaveChangesAsync();

        await _carrier.Carry();

        (await Named())[Russian(3).Id].Confidence.Should().BeApproximately(0.9 * 0.3, 1e-9);
    }

    /// <summary>
    /// The seed's claims arrive crossed with the link, and the verse list's testimony arrives as
    /// testimony: the same words and no number, because the list said nothing about a link.
    /// </summary>
    [Fact]
    public async Task ACarriedAnnotationCarriesItsSeedsClaimsCrossedWithTheLink()
    {
        Link(Hebrew(2), Russian(3), LinkMethod.StrongNumber, 0.9);
        await _carrier.Carry();

        var carried = (await Named())[Russian(3).Id];
        carried.Method.Should().Be(LinkMethod.Lexical);
        carried.Source.Should().Be(People);
        carried.Note.Should().StartWith($"through {EntityCandidates.Witness} word {Hebrew(2).Id}");
        carried.Claims.Should().HaveCount(2);
        carried.Claims.Should().ContainSingle(c => c.Method == LinkMethod.StatedBySource)
            .Which.Should().Match<WordEntityClaim>(c => c.Confidence == null && c.Source == VerseList);
    }

    /// <summary>
    /// A text whose links did not move comes out as it went in, row for row and id for id. Run on a
    /// corpus of millions of rows, anything else would be churn nobody could review.
    /// </summary>
    [Fact]
    public async Task CarryingAgainOverLinksThatDidNotMoveChangesNothing()
    {
        Link(Hebrew(2), Russian(3), LinkMethod.StrongNumber, 0.9);
        await _carrier.Carry();
        var before = await _db.WordEntities.AsNoTracking().Select(a => new { a.Id, a.Confidence }).ToListAsync();

        var outcome = await _carrier.Carry();

        outcome.Written.Should().Be(0);
        outcome.Withdrawn.Should().Be(0);
        (await _db.WordEntities.AsNoTracking().Select(a => new { a.Id, a.Confidence }).ToListAsync())
            .Should().BeEquivalentTo(before);
    }

    /// <summary>
    /// A source whose answer stands only as a claim, on a row another source's link already put
    /// there, still carries from it. The Scrivener occurrence of a place the Nestle occurrence had
    /// been carried to is held that way, and the Stephanus words were reached from it.
    /// </summary>
    [Fact]
    public async Task AnAnswerHeldOnlyAsAClaimOnACarriedRowStillCarries()
    {
        const string Second = "a second source, whose answer arrived after the first had carried";

        var middle = _db.WordAt(_russian, 10, 13, 2);
        Link(Hebrew(2), middle, LinkMethod.StatedBySource, null);
        await _carrier.Carry();

        var row = await _db.WordEntities.SingleAsync(a => a.WordId == middle.Id);
        _db.WordEntityClaims.Add(new WordEntityClaim
        {
            WordEntityId = row.Id, Method = LinkMethod.Lexical, Confidence = 0.96, Source = Second,
            Note = "an answer of its own",
        });
        var farther = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (10, 13, ["Лудим"]));
        await _db.SaveChangesAsync();
        Link(middle, _db.WordAt(farther, 10, 13, 1), LinkMethod.Aligner, 0.5);

        await _carrier.Carry();

        var carried = await _db.WordEntities.AsNoTracking()
            .SingleAsync(a => a.WordId == _db.WordAt(farther, 10, 13, 1).Id);
        carried.Source.Should().Be(Second);
        carried.Confidence.Should().BeApproximately(0.48, 1e-9);
    }

    /// <summary>
    /// The resolution's own question, asked again of the word the link lands on: a word carrying a
    /// number several records bear was named by choosing between them, and <c>strong-number</c>,
    /// which says nothing was chosen, would be the wrong claim however right the answer.
    /// </summary>
    [Fact]
    public async Task AResolutionLandingOnAWordWhoseNumberSeveralRecordsBearIsTheFormOfTheWord()
    {
        var egypt = new Entity
        {
            Kind = EntityKind.Place, Slug = "egypt", Name = "Egypt", SourceId = "egypt", Source = "a test",
            Names = [new EntityName { Label = "Egypt", HebrewStrongNumber = "H4714", Kind = "name" }],
        };
        var mizraim = new Entity
        {
            Kind = EntityKind.Person, Slug = "mizraim", Name = "Mizraim", SourceId = "mizraim", Source = "a test",
            Names = [new EntityName { Label = "Mizraim", HebrewStrongNumber = "H4714", Kind = "name" }],
        };
        _db.Entities.AddRange(egypt, mizraim);
        _db.SaveChanges();

        var resolution = EntityAnnotationLoader.Written[0];
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Hebrew(1).Id, EntityId = egypt.Id, Method = LinkMethod.StrongNumber, Confidence = 0.9,
            Source = resolution, Note = "H4714, which BHSA marks topo",
            Claims = [new WordEntityClaim { Method = LinkMethod.StrongNumber, Confidence = 0.9, Source = resolution }],
        });

        var english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (10, 13, ["Mizraim"]));
        _db.SaveChanges();
        var word = _db.WordAt(english, 10, 13, 1);
        word.StrongNumber = "H4714";
        _db.SaveChanges();

        Link(Hebrew(1), word, LinkMethod.StatedBySource, null);
        await _carrier.Carry();

        var carried = await _db.WordEntities.AsNoTracking()
            .Include(a => a.Claims)
            .SingleAsync(a => a.WordId == word.Id);
        carried.Method.Should().Be(LinkMethod.Lexical);
        carried.Claims.Should().OnlyContain(c => c.Method == LinkMethod.Lexical);
    }
}
