using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Verbs;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
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

        _hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo", (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["In", "beginning", "verily"]));
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
    public async Task VerifyRecordsWhatItFindsSoTheHealthReportIsTheLatestMeasure()
    {
        var before = await _db.VerificationRuns.CountAsync();
        var services = new ServiceCollection().AddSingleton(_db).AddSingleton(_check).BuildServiceProvider();

        await ForgeVerbs.Find("verify")!.Run(new ForgeRun(services, NullLogger.Instance, "", ""), ["verify"]);

        (await _db.VerificationRuns.CountAsync()).Should().Be(before + 1);
    }

    /// <summary>
    /// The compiler counts each Hebrew number and BHSA can count its own; verify names every number
    /// the two part on, the largest first, and serves neither as the other.
    /// </summary>
    [Fact]
    public async Task ACompilersCountIsComparedWithBhsasOwnForEveryNumber()
    {
        _db.StrongProfiles.AddRange(
            Profile("H7225", 1),
            Profile("H1254", 3),
            Profile("H8", 2));
        await _db.SaveChangesAsync();

        var lexicon = (await _check.Measure()).Lexicon!;

        (lexicon.Numbers, lexicon.Agreeing, lexicon.Disagreeing, lexicon.Absent).Should().Be((3, 1, 2, 1));
        lexicon.Largest.Should().Equal(new CountDisagreement("H1254", 3, 1), new CountDisagreement("H8", 2, 0));
    }

    private static StrongProfile Profile(string number, int occurrences) => new()
    {
        StrongNumber = number, Language = "hbo", Occurrences = occurrences, Source = "a compiler",
    };

    [Fact]
    public async Task AWordALinkNamesIsRenderedAndAWordNothingNamesIsSilent()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Renders, english: 2, hebrew: 2);

        var coverage = (await _check.Measure()).Coverage.Single(c => c.Text == "KJV");

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

        var coverage = (await _check.Measure()).Coverage.Single(c => c.Text == "KJV");

        coverage.Rendered.Should().Be(1);
        coverage.StatedAbsent.Should().Be(1);
        coverage.Silent.Should().Be(1);

        // The word shown to have no counterpart is answered for; only the silent one is not.
        coverage.Share.Should().BeApproximately(2d / 3, 1e-12);
    }

    /// <summary>
    /// Nothing is missing in a text nobody has aligned yet, and calling those words silent would
    /// report unfinished work as a defect.
    /// </summary>
    [Fact]
    public async Task ATextNothingHasBeenAlignedToIsUnpairedRatherThanSilent()
    {
        var coverage = (await _check.Measure()).Coverage.Single(c => c.Text == "KJV");

        coverage.Unpaired.Should().Be(3);
        coverage.Silent.Should().Be(0);
    }

    /// <summary>
    /// A book of a linked text that no link has reached yet is work to do, not a loss: it is named,
    /// and the floor's share is taken without it, while the published share still counts it.
    /// </summary>
    [Fact]
    public async Task ABookNotAlignedYetIsNamedAndLeftOutOfTheFloorsShare()
    {
        _db.AddBook(_hebrew, 2, "Exodus", (2, 1, ["וְ", "אֵלֶּה"]));
        _db.AddBook(_english, 2, "Exodus", (2, 1, ["Now", "these"]));
        _db.SaveChanges();
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Renders, english: 2, hebrew: 2);

        var measures = await _check.Measure();

        measures.Unaligned.Should().ContainSingle()
            .Which.Should().Be(new Unaligned("KJV", 2, "Exodus", 2));
        measures.Rendered.Should().BeApproximately(2d / 5, 1e-12);
        measures.Aligned.Should().BeApproximately(2d / 3, 1e-12);
        measures.Describe().Should().Contain("KJV: Exodus 2");
    }

    [Fact]
    public async Task ABookWithOneLinkedWordIsAlignedWorkAndCountsInTheFloor()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);

        var measures = await _check.Measure();

        measures.Unaligned.Should().BeEmpty();
        measures.Aligned.Should().Be(measures.Rendered);
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

        reach.Witness.Should().Be("BHSA");
        reach.From.Should().Be("KJV");
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
    /// A second method arriving at a link already written is recorded as a second claim on it, which
    /// is two sources agreeing. Counting it as a dispute moved one pair from 209 to 200,807 on the
    /// day a strong-number load confirmed the aligner's links.
    /// </summary>
    [Fact]
    public async Task TwoSourcesClaimingOneLinkCorroborateRatherThanDispute()
    {
        var link = Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Claim(link, "a second source");

        var contention = (await _check.Measure()).Contention.Single();

        contention.Disputed.Should().Be(0);
        contention.Corroborated.Should().Be(1);
        contention.Contended.Should().Be(0);
    }

    /// <summary>
    /// Agreement is about the words named, not the rows naming them: two loaders that each wrote
    /// their own link to the same Hebrew word agree.
    /// </summary>
    [Fact]
    public async Task TwoSourcesNamingTheSameWordsInSeparateLinksCorroborate()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Renders, english: 1, hebrew: 1, source: "a second source");

        var contention = (await _check.Measure()).Contention.Single();

        contention.Disputed.Should().Be(0);
        contention.Corroborated.Should().Be(1);
    }

    [Fact]
    public async Task TwoSourcesNamingDifferentWordsDispute()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Renders, english: 1, hebrew: 2, source: "a second source");

        var contention = (await _check.Measure()).Contention.Single();

        contention.Disputed.Should().Be(1);
        contention.Corroborated.Should().Be(0);
        contention.Contended.Should().Be(0);
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

    /// <summary>
    /// The measure a variant witness is loaded for. Every other measure counts correspondence, so a
    /// corpus that had silently stopped recording absences would score perfectly on all of them and
    /// nothing published would say the disagreements had gone.
    /// </summary>
    [Fact]
    public async Task AnAbsenceIsCountedPerBookOnTheSideItsRelationNames()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);
        Link(LinkRelation.Expands, english: 3, hebrew: null);
        Link(LinkRelation.Omits, english: null, hebrew: 3);

        var absence = (await _check.Measure()).Absence.Single();

        absence.Text.Should().Be("KJV");
        absence.Against.Should().Be("BHSA");
        absence.Expands.Should().Be(1);
        absence.Omits.Should().Be(1);
        absence.Books.Should().BeEquivalentTo([new BookAbsence(1, "Genesis", 1, 1)]);
    }

    /// <summary>
    /// Only the side the relation names is read, so a link that names both is counted once. The
    /// same variant between two Greek editions has been written as <c>omits</c> from both ends, and
    /// a measure that counted whichever side held words would report one disagreement as two.
    /// </summary>
    [Fact]
    public async Task AnAbsenceNamingBothSidesIsStillOneAbsence()
    {
        Link(LinkRelation.Omits, english: 1, hebrew: 2);

        var absence = (await _check.Measure()).Absence.Single();

        absence.Omits.Should().Be(1);
        absence.Expands.Should().Be(0);
        absence.Words.Should().Be(1);
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
    public async Task AWordShownAsAbsentAndAsRenderedIsFound()
    {
        Link(LinkRelation.Expands, english: 2, hebrew: null);
        Link(LinkRelation.Renders, english: 2, hebrew: 1);
        Link(LinkRelation.Expands, english: 3, hebrew: null);

        (await Integrity("words shown as having no counterpart and as rendered at once")).Should().Be(1);
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
            Provenance = new() { Source = "a test" },
        });
        _db.SaveChanges();

        (await Integrity("links naming no word on either side")).Should().Be(1);
    }

    /// <summary>
    /// A lemma written with its accent as a separate combining mark is a different string from the
    /// same lemma written composed, and Postgres compares them byte for byte — so one Greek text
    /// lemmatised the decomposed way agrees with every other Greek text on nothing at all, and the
    /// empty result looks exactly like having no lemmas. Both spellings of καί are written here
    /// deliberately: the assertion is worthless if the two literals are the same bytes.
    /// </summary>
    [Fact]
    public async Task AGreekLemmaWrittenDecomposedIsFound()
    {
        var greek = Corpus.Add(
            _db, "NESTLE1904", TextKind.CriticalEdition, "grc", (1, 1, ["καί", "λόγος"]));
        _db.SaveChanges();

        // Composed, which is what every other Greek text in the corpus carries.
        _db.WordAt(greek, 1, 1, 1).Lemma = "καί";
        // Decomposed: kappa, alpha, iota, then the acute as its own character. Same three letters
        // on the screen, four characters in the column.
        _db.WordAt(greek, 1, 1, 2).Lemma = "καί";
        _db.SaveChanges();

        // The guard on the guard. An editor, a formatter or a git filter that normalises this file
        // would make the two literals identical, and the test would then pass while checking
        // nothing at all — the same silent-success failure the check itself is about.
        _db.WordAt(greek, 1, 1, 1).Lemma.Should().NotBe(_db.WordAt(greek, 1, 1, 2).Lemma,
            "the two spellings of this lemma must differ in bytes or this test proves nothing");

        (await Integrity("Greek lemmas not in canonical form, which nothing can join"))
            .Should().Be(1);
    }

    /// <summary>
    /// A Greek word carries G####, so a check that looked for the name only among the Hebrew numbers
    /// could never find one, however many men the Greek name belongs to. It is asked of both columns.
    /// </summary>
    [Fact]
    public async Task AGreekNameSeveralPeopleBearAnnotatedAsIfItWereOnesIsFound()
    {
        var greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc", (1, 1, ["Ζαχαρίαν"]));
        _db.SaveChanges();
        var word = _db.WordAt(greek, 1, 1, 1);
        word.StrongNumber = "G2197";

        Entity Zechariah(string slug) => new()
        {
            Kind = EntityKind.Person, Slug = slug, Name = "Zechariah", SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = "Zechariah", GreekStrongNumber = "G2197" }],
        };
        var first = Zechariah("zechariah-1");
        _db.Entities.AddRange(first, Zechariah("zechariah-2"));
        _db.WordEntities.Add(new WordEntity
        {
            Word = word, Entity = first, Method = LinkMethod.StrongNumber, Confidence = 0.85, Source = "a test",
        });
        _db.SaveChanges();

        (await Integrity("words a name-resolution annotated although the name is several people's"))
            .Should().Be(1);
    }

    /// <summary>
    /// The Hebrew texts are deliberately not counted. They order their points and accents
    /// differently from canonical order as well, but there the column holds the witness's own text
    /// and whether it may be rewritten is a decision nobody has taken. A check that
    /// reported an open question as a broken corpus would be worse than no check.
    /// </summary>
    [Fact]
    public async Task AHebrewLemmaWrittenDecomposedIsNotCounted()
    {
        _db.WordAt(_hebrew, 1, 1, 1).Lemma = "שָׁלוֹם";
        _db.SaveChanges();

        (await Integrity("Greek lemmas not in canonical form, which nothing can join"))
            .Should().Be(0);
    }

    /// <summary>
    /// Every relationship is read from the text by this project. A row a dataset states is one a
    /// loader brought in from somebody's edge list, and a corpus holding one is not sound.
    /// </summary>
    [Fact]
    public async Task ARelationshipADatasetStatesIsFound()
    {
        Entity Person(string slug) => new()
        {
            Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = "a test",
        };
        var (lot, haran) = (Person("lot"), Person("haran"));
        _db.Entities.AddRange(lot, haran);
        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = lot, To = haran, Type = "son-of", Category = RelationshipCategories.Read,
            CanonicalBook = 1, CanonicalChapter = 11, CanonicalVerse = 27,
            Method = LinkMethod.ModelReading, Confidence = 0.95, Source = "read from Scripture by a test",
        });
        _db.SaveChanges();

        const string check = "relationships a dataset states, where every one is read from the text by this project";
        (await Integrity(check)).Should().Be(0);

        _db.EntityRelationships.Add(new EntityRelationship
        {
            From = lot, To = haran, Type = "son", Category = "explicit",
            Method = LinkMethod.StatedBySource, Source = "BibleData by Brady Stephenson, a test",
        });
        _db.SaveChanges();

        (await Integrity(check)).Should().Be(1);
    }

    /// <summary>
    /// The same verse link written twice is counted once as a repeat; a link of the same verses that
    /// a different source states is a statement of its own and is not.
    /// </summary>
    [Fact]
    public async Task AVerseLinkWrittenTwiceIsARepeatAndOneAnotherSourceStatesIsNot()
    {
        VerseLink("the canonical frame");
        VerseLink("the canonical frame");
        VerseLink("a verse map");
        _db.SaveChanges();

        (await Integrity("verse links saying again what another link of the same pair says")).Should().Be(1);
    }

    /// <summary>
    /// A relationship read from Scripture stands on a clause of the same two records, relation and
    /// verse. One whose clause is gone, and one saying again what another says, are each counted.
    /// </summary>
    [Fact]
    public async Task ARelationshipNoClauseStatesAndOneWrittenTwiceAreCounted()
    {
        const string check = "relationships read from Scripture that no clause states, or that say again what another says";
        const string source = Sources.DescriptorReadingPrefix + " a test, asked 2026-09-10";
        Entity Person(string slug) => new() { Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        var amorites = Person("amorites");
        var canaan = Person("canaan");
        var deborah = Person("deborah");
        var rebekah = Person("rebekah");
        _db.Entities.AddRange(amorites, canaan, deborah, rebekah);
        _db.SaveChanges();
        EntityRelationship Row(Entity from, Entity to, string type) => new()
        {
            FromEntityId = from.Id, ToEntityId = to.Id, Type = type, Category = RelationshipCategories.Read,
            CanonicalBook = 1, CanonicalChapter = 10, CanonicalVerse = 16,
            Method = LinkMethod.ModelReading, Confidence = 0.9, Source = source,
        };
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = deborah.Id, Ordinal = 1, Relation = DescriptorRelations.ServantOf, TargetEntityId = rebekah.Id,
            CanonicalBook = 1, CanonicalChapter = 10, CanonicalVerse = 16,
            Method = LinkMethod.ModelReading, Confidence = 0.9, Source = source,
        });
        _db.EntityRelationships.Add(Row(deborah, rebekah, DescriptorRelations.ServantOf));
        _db.SaveChanges();

        (await Integrity(check)).Should().Be(0);

        _db.EntityRelationships.Add(Row(amorites, canaan, DescriptorRelations.SonOf));
        _db.EntityRelationships.Add(Row(deborah, rebekah, DescriptorRelations.ServantOf));
        _db.SaveChanges();

        (await Integrity(check)).Should().Be(2);
    }

    private void VerseLink(string source) =>
        _db.VerseLinks.Add(new VerseLink
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Equals,
            Method = LinkMethod.StatedBySource,
            Source = source,
            Verses =
            [
                new VerseLinkVerse { VerseId = _db.VerseAt(_english, 1, 1).Id, Side = LinkSide.From },
                new VerseLinkVerse { VerseId = _db.VerseAt(_hebrew, 1, 1).Id, Side = LinkSide.To },
            ],
        });

    private async Task<int> Integrity(string breaks) =>
        (await _check.Measure()).Integrity.Single(check => check.Breaks == breaks).Found;

    private Link Link(
        LinkRelation relation, int? english, int? hebrew, LinkMethod method = LinkMethod.Manual,
        string source = "a test")
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = relation,
            Method = method,
            Provenance = new() { Source = source },

            // Confidence is null exactly when a source stated it, and set exactly when something
            // inferred it. The schema holds that as a check constraint, so a fixture that ignores
            // it is refused rather than quietly testing a shape no loader can produce.
            Confidence = method is LinkMethod.StatedBySource or LinkMethod.Manual ? null : 0.9,
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        // Every link carries the claim of whatever asserted it. A link with none is invisible to
        // the measures that read `link_claim`, and a fixture without one would test a shape no
        // loader produces — which is exactly how a claimless link stayed hidden for a day.
        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id,
            Method = link.Method,
            Confidence = link.Confidence,
            Provenance = link.Provenance,
        });

        // An `omits` link names no word of the text it is written from — that is the whole claim —
        // so the from side has to be omissible here as well.
        if (english is { } englishPosition)
        {
            _db.LinkWords.Add(new LinkWord
            {
                LinkId = link.Id,
                WordId = _db.WordAt(_english, 1, 1, englishPosition).Id,
                Side = LinkSide.From,
            });
        }

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

    private void Claim(Link link, string source)
    {
        _db.LinkClaims.Add(new LinkClaim
        {
            LinkId = link.Id,
            Method = link.Method,
            Confidence = link.Confidence,
            Provenance = new() { Source = source },
        });
        _db.SaveChanges();
    }

    /// <summary>
    /// The share is published with the two numbers it is the ratio of, because a ratio alone cannot
    /// be checked. Two measurements of this corpus a day apart differed by four points and neither
    /// could be reproduced from the other — "which words did you count" has several defensible
    /// answers here, and a share does not say which was asked.
    /// </summary>
    [Fact]
    public async Task TheCoverageShareIsPublishedWithItsCounts()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 1);

        var measures = await _check.Measure();

        measures.Words.Should().Be(measures.Coverage.Sum(c => c.Promised));
        measures.UnpairedWords.Should().Be(measures.Coverage.Sum(c => c.Unpaired));
        measures.RenderedWords.Should().Be(measures.Coverage.Sum(c => c.Rendered));
        measures.RenderedWords.Should().BeLessThanOrEqualTo(measures.Words);
        (measures.Words + measures.UnpairedWords).Should().Be(measures.Coverage.Sum(c => c.Words));
        measures.AbsentWords.Should().Be(measures.Coverage.Sum(c => c.StatedAbsent));
        measures.Rendered.Should().BeApproximately((double)(measures.RenderedWords + measures.AbsentWords) / measures.Words, 1e-12);
    }
    /// <summary>
    /// Reach says how much of what it counted rests on testimony, because without that the table
    /// ranks translations by quality and what it ranks them by is how much testimony each has. The
    /// Berean's 88.9% into the Greek is its publisher's word tables; the Ukrainian's 77.3% is a
    /// model; the same aligner scored against each text's own stated pairs is better on the
    /// Ukrainian. One column invited that reading and it was taken.
    /// </summary>
    [Fact]
    public async Task ReachSaysHowMuchOfItselfASourceStated()
    {
        // The second and third Hebrew words, because the first is a prefix and reach counts only
        // lexical content — linking it would test nothing.
        Link(LinkRelation.Renders, english: 1, hebrew: 2, LinkMethod.StatedBySource);
        Link(LinkRelation.Renders, english: 2, hebrew: 3, LinkMethod.Aligner);

        var reach = (await _check.Measure()).Reach.Single();

        reach.Reached.Should().Be(2);
        reach.Stated.Should().Be(1);
        reach.ByMethod.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["stated-by-source"] = 1,
            ["aligner"] = 1,
        });
        reach.Testimony.Should().BeApproximately(0.5, 1e-12);
        reach.Attested.Should().Be(0);
        reach.Inferred.Should().BeApproximately(0.5, 1e-12);
    }

    /// <summary>
    /// A Strong number two publishers printed is not a source saying these words correspond, and it
    /// is not a guess either. Reported as one of the other two it says something false in one
    /// direction or the other: the King James against Nestle scored 0.0000 testimony on 129,626
    /// links made this way, which put the corpus's headline claim in the column reserved for a model
    /// that has never seen a Strong number.
    /// </summary>
    [Fact]
    public void APublishersTagIsNeitherTestimonyNorInference()
    {
        var reach = Reach.Gather(
        [
            ("BHSA", "KJV", 10, 8, null),
            ("BHSA", "KJV", 10, 6, "strong-number"),
            ("BHSA", "KJV", 10, 2, "aligner"),
        ]).Single();

        reach.Testimony.Should().Be(0);
        reach.Attested.Should().BeApproximately(0.75, 1e-12);
        reach.Inferred.Should().BeApproximately(0.25, 1e-12);
    }

    /// <summary>
    /// The three shares are read together and do not have to sum to one: the per-method counts
    /// overlap, so a word two methods reach is in both. Inference is what is left after the two
    /// that rest on somebody's statement, floored at zero rather than allowed to go negative when
    /// the overlap is large.
    /// </summary>
    [Fact]
    public void OverlappingMethodsDoNotDriveInferenceBelowZero()
    {
        var reach = Reach.Gather(
        [
            ("BHSA", "KJV", 10, 8, null),
            ("BHSA", "KJV", 10, 8, "stated-by-source"),
            ("BHSA", "KJV", 10, 8, "strong-number"),
        ]).Single();

        reach.Testimony.Should().Be(1);
        reach.Attested.Should().Be(1);
        reach.Inferred.Should().Be(0);
    }

    /// <summary>
    /// The total counts the pair's own words, it does not add the methods up. A word two methods
    /// both reach belongs to both, so summing them counts it twice — which put the reported reach
    /// above 100% the first time this was written.
    /// </summary>
    [Fact]
    public async Task AWordTwoMethodsReachIsOneWord()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 2, LinkMethod.StatedBySource);
        Link(LinkRelation.Renders, english: 2, hebrew: 2, LinkMethod.Aligner);

        var reach = (await _check.Measure()).Reach.Single();

        reach.Reached.Should().Be(1);
        reach.ByMethod.Values.Sum().Should().Be(2);
        reach.Share.Should().BeLessThanOrEqualTo(1);
    }

    /// <summary>
    /// A text linked for the first time below the corpus mean pulls the mean down and loses
    /// nothing. Only a section that reaches less of itself than it did last time has lost something.
    /// </summary>
    [Fact]
    public void ANewTextBelowTheMeanIsNotALoss()
    {
        var kingJames = new Coverage("KJV", "old testament", 100, 92, 0, 8, 0);
        var kulish = new Coverage("UKR1871", "old testament", 100, 82, 0, 18, 0);

        CorpusCheck.Fallen([kingJames], [kingJames, kulish]).Should().BeEmpty();

        var fewer = kingJames with { Rendered = 90, Silent = 10 };
        CorpusCheck.Fallen([kingJames], [fewer, kulish]).Should().ContainSingle()
            .Which.Now.Should().Be(fewer);
    }

    /// <summary>
    /// Two verses of one text at one canonical address is counted, and kept apart from the
    /// lettered kind the Septuagint prints on purpose.
    /// </summary>
    [Fact]
    public async Task VersesSharingAnAddressAreCountedWithAndWithoutALetter()
    {
        var first = _db.VerseAt(_english, 1, 1);
        var extra = new Verse
        {
            TextId = _english.Id, BookId = first.BookId, ChapterId = first.ChapterId,
            ChapterNumber = 1, Number = 2,
        };
        var lettered = new Verse
        {
            TextId = _hebrew.Id, BookId = _db.VerseAt(_hebrew, 1, 1).BookId,
            ChapterId = _db.VerseAt(_hebrew, 1, 1).ChapterId, ChapterNumber = 1, Number = 1, Label = "a",
        };
        foreach (var verse in (Verse[])[extra, lettered])
        {
            _db.Verses.Add(verse);
            _db.VerseReferences.Add(new VerseReference
            {
                Verse = verse, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, IsPrimary = true,
            });
        }
        await _db.SaveChangesAsync();

        var shared = (await _check.Measure()).Shared;

        shared.Should().BeEquivalentTo([
            new SharedAddresses("BHSA", Labelled: true, Addresses: 1, Verses: 2),
            new SharedAddresses("KJV", Labelled: false, Addresses: 1, Verses: 2),
        ]);
    }

    [Fact]
    public void OnlyMoreUnletteredSharingIsReported()
    {
        List<SharedAddresses> before =
        [
            new("BHSA", false, 8, 16),
            new("SWETE", true, 112, 326),
        ];
        List<SharedAddresses> now =
        [
            new("BHSA", false, 9, 18),
            new("SWETE", true, 140, 400),
            new("NEW", false, 3, 6),
        ];

        CorpusCheck.Crowded(before, now).Select(s => s.Text).Should().Equal("BHSA");
        CorpusCheck.Crowded(null, now).Should().BeEmpty();
    }

    /// <summary>
    /// A word whose counterpart stands in the verse next door, which a pairing inside one canonical
    /// address can never reach. Once a link crosses the boundary it is no longer stranded.
    /// </summary>
    [Fact]
    public async Task AWordWhoseNumberTheWitnessPrintsOnlyInTheNextVerseIsStranded()
    {
        var first = _db.VerseAt(_hebrew, 1, 1);
        var next = new Verse
        {
            TextId = _hebrew.Id, BookId = first.BookId, ChapterId = first.ChapterId, ChapterNumber = 1, Number = 2,
        };
        _db.Verses.Add(next);
        _db.VerseReferences.Add(new VerseReference
        {
            Verse = next, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 2, IsPrimary = true,
        });
        var god = new Word
        {
            Text = _hebrew, Verse = next, Position = 1, Surface = "אֱלֹהִים", Trailer = string.Empty,
            StrongNumber = "H430",
        };
        _db.Words.Add(god);
        _db.WordAt(_english, 1, 1, 2).StrongNumber = "H7225";
        _db.WordAt(_english, 1, 1, 3).StrongNumber = "H430";
        await _db.SaveChangesAsync();
        Link(LinkRelation.Renders, english: 1, hebrew: 1);

        var stranded = (await _check.Measure()).Stranded;

        // "beginning" is silent too, but the Hebrew prints its number in its own verse.
        stranded.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new Stranded("KJV", "BHSA", 1, ["Genesis 1"]));

        var across = Link(LinkRelation.Renders, english: 3, hebrew: null);
        _db.LinkWords.Add(new LinkWord { LinkId = across.Id, WordId = god.Id, Side = LinkSide.To });
        await _db.SaveChangesAsync();

        (await _check.Measure()).Stranded.Should().BeEmpty();
    }

    /// <summary>
    /// Two Greek editions joined by their letters, and the places they read differently counted the
    /// way an edition's preface counts them: a word one has in place of nothing, a word only the
    /// other has, and one missing at the end of a verse, each once — and a verse they print alike
    /// not at all.
    /// </summary>
    [Fact]
    public async Task ThePlacesTwoGreekEditionsReadDifferentlyAreCountedOnceEach()
    {
        var holmes = Corpus.Add(_db, "SBLGNT", TextKind.CriticalEdition, "grc");
        _db.AddBook(holmes, 40, "Matthew",
            (1, 1, ["Βίβλος", "γενέσεως", "Ἰησοῦ", "χριστοῦ"]),
            (1, 2, ["ὁ", "δὲ", "Ἰησοῦς", "εἶπεν", "αὐτοῖς"]),
            (1, 3, ["καὶ", "ἦλθεν", "εἰς", "τὴν", "πόλιν"]),
            (1, 4, ["ἐγώ", "εἰμι"]));
        var nestle = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc");
        _db.AddBook(nestle, 40, "Matthew",
            (1, 1, ["Βίβλος", "γενέσεως", "Ἰησοῦ", "Χριστοῦ"]),
            (1, 2, ["ὁ", "δὲ", "εἶπεν", "αὐτοῖς"]),
            (1, 3, ["καὶ", "ἦλθεν", "πάλιν", "εἰς", "τὴν", "πόλιν"]),
            (1, 4, ["ἐγώ", "εἰμι", "ὁ", "ποιμήν"]));
        await _db.SaveChangesAsync();

        await new Essenthos.Core.Loading.Links.SeptuagintLinkLoader(
                _db, NullLogger<Essenthos.Core.Loading.Links.SeptuagintLinkLoader>.Instance)
            .Load("SBLGNT", "NESTLE1904");

        (await _check.Measure()).Variation.Should().ContainSingle()
            .Which.Should().Be(new Variation("SBLGNT", "NESTLE1904", Units: 3, Addresses: 3, Shared: 4));
    }

    /// <summary>The coverage a run stored is the coverage the next run compares against.</summary>
    [Fact]
    public async Task TheStoredCoverageReadsBackAsItWasMeasured()
    {
        Link(LinkRelation.Renders, english: 1, hebrew: 2);
        var measures = await _check.Measure();

        var run = await _check.Record(measures);

        CorpusCheck.CoverageOf(run).Should().BeEquivalentTo(measures.Coverage);
    }
}
