using Essenthos.Core.Bhsa;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Utils;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Samaritan Pentateuch against BHSA, measured over the whole Pentateuch rather than sampled.
///
/// The point of holding this witness is the count at the bottom: the places where one tradition has
/// a word the other has not. That count is only worth anything if the alignment refuses to pair
/// words it has no reason to pair — an aligner that closes every gap reports no differences and
/// looks excellent — so what is pinned here is both what agrees and what does not.
/// </summary>
public sealed class SamaritanLinkTests(ITestOutputHelper output)
{
    private static readonly Lazy<Measurement> Measured = new(Measure);

    /// <summary>Verses both witnesses number, which is every verse the Samaritan has.</summary>
    private const int SharedVerses = 5_841;

    /// <summary>
    /// Verses the Masoretic numbers and the Samaritan does not: Exodus 30:1-10, which this
    /// tradition sets after Exodus 26:35, and Deuteronomy 34:2-3, which it writes into 34:1. No
    /// link is written for either, because the words are elsewhere rather than absent.
    /// </summary>
    private const int MasoreticOnlyVerses = 12;

    /// <summary>
    /// What the alignment produced over the whole Pentateuch, measured. Pinned rather than bounded
    /// because the Samaritan release is fetched at a named commit and BHSA does not move either: a
    /// change in any of these is a change in the corpus's answer and should have to be looked at.
    /// </summary>
    private const int SamaritanWords = 114_889;

    private const int MasoreticWords = 112_711;

    /// <summary>
    /// The Masoretic morphemes that print no letters — the article assimilated into the preposition
    /// before it. They stand inside another correspondence rather than in one of their own, because
    /// the Samaritan writes the same letters and simply does not analyse an article there.
    /// </summary>
    private const int ElidedMasoreticWords = 1_677;

    private const int Identical = 103_905;
    private const int WrittenDifferently = 5_838;
    private const int SamaritanHasAndMasoreticHasNot = 5_146;
    private const int MasoreticHasAndSamaritanHasNot = 1_291;

    /// <summary>
    /// Verses where one tradition has a word the other has not: the count this text was loaded for.
    /// The survey that proposed it measured 1,533 against a different edition of the same tradition,
    /// whole words rather than morphemes, so this is the neighbourhood it predicted rather than the
    /// same number.
    /// </summary>
    private static readonly (string Book, int Verses)[] DifferingByBook =
    [
        ("Genesis", 434), ("Exodus", 443), ("Leviticus", 217), ("Numbers", 313), ("Deuteronomy", 325),
    ];

    [Fact]
    public void TheTwoWitnessesAgreeOnMostOfTheirWords()
    {
        var measured = Measured.Value;
        output.WriteLine(measured.ToString());

        measured.Verses.Should().Be(SharedVerses);
        measured.MasoreticOnly.Should().Be(MasoreticOnlyVerses);
        measured.SamaritanOnly.Should().Be(0);
        measured.SamaritanWords.Should().Be(SamaritanWords);
        measured.MasoreticWords.Should().Be(MasoreticWords);

        measured.Relations[LinkRelation.Equals].Should().Be(Identical);
        measured.Relations[LinkRelation.Renders].Should().Be(WrittenDifferently);
        measured.Relations[LinkRelation.Expands].Should().Be(SamaritanHasAndMasoreticHasNot);
        measured.Relations[LinkRelation.Omits].Should().Be(MasoreticHasAndSamaritanHasNot);
    }

    /// <summary>
    /// Every Samaritan word and every Masoretic word of the shared verses stands in exactly one
    /// correspondence. A word in none of them is a word the reader will never be shown beside the
    /// other tradition, and a word in two is a claim made twice; nothing else here would notice
    /// either.
    /// </summary>
    [Fact]
    public void EveryWordOfEveryPairedVerseStandsInExactlyOneCorrespondence()
    {
        var measured = Measured.Value;

        measured.SamaritanCovered.Should().Be(SamaritanWords);
        measured.MasoreticCovered.Should().Be(MasoreticWords);
        measured.CoveredTwice.Should().Be(0);
    }

    /// <summary>
    /// The Masoretic morphemes that print no letters are not absences. BHSA records the article
    /// that assimilated into the preposition before it as a word of its own, the Samaritan dataset
    /// records no such thing, and letting them through the alignment made the corpus say the
    /// Samaritan omits an article 1,677 times where the two write the identical letters — more
    /// times than it really does lack a Masoretic word.
    /// </summary>
    [Fact]
    public void TheMasoreticArticleThatPrintsNothingIsNotCountedAsAnAbsence()
    {
        var measured = Measured.Value;

        measured.Elided.Should().Be(ElidedMasoreticWords);
        measured.ElidedStandingAlone.Should().Be(0);
        ElidedMasoreticWords.Should().BeGreaterThan(MasoreticHasAndSamaritanHasNot);
    }

    /// <summary>
    /// The count this text was loaded for: verses where one tradition has a word the other has not,
    /// book by book.
    /// </summary>
    [Fact]
    public void EveryBookHasVersesWhereOneTraditionHasAWordTheOtherHasNot()
    {
        var measured = Measured.Value;

        measured.DifferingVerses.OrderBy(entry => entry.Key)
            .Select(entry => (BibleBookAbbreviation.GetByOrdinal(entry.Key)!.FullName.Full, entry.Value))
            .Should().Equal(DifferingByBook);
    }

    /// <summary>
    /// Genesis 1:11, where the Samaritan reads <em>and a tree bearing fruit</em> against the
    /// Masoretic <em>a tree bearing fruit</em>. The first plus in the first chapter of the first
    /// book, and the whole shape of what this witness is for: a link with words on one side and
    /// nothing on the other, saying which of the two lacks them.
    /// </summary>
    [Fact]
    public void TheFirstSamaritanPlusIsRecordedAsAnExpansion()
    {
        var samaritan = Verses(SamaritanTextSource.Read(TestResources.Samaritan));
        var masoretic = Verses(BhsaTextSource.Build(Bhsa()));

        var pairings = HebrewWitnessAlignment.Pair(samaritan[(1, 1, 11)], masoretic[(1, 1, 11)]);

        var expansions = pairings.Where(p => p.Relation == LinkRelation.Expands).ToList();
        var only = expansions.Should().ContainSingle().Which;

        only.To.Should().BeEmpty();
        samaritan[(1, 1, 11)][only.From.Should().ContainSingle().Which].Consonants.Should().Be("ו");
        only.Confidence.Should().Be(HebrewWitnessAlignment.AbsenceWhereTheyAgree);
        pairings.Should().NotContain(p => p.Relation == LinkRelation.Omits);
    }

    /// <summary>
    /// Two words the alignment has no reason to connect are left unpaired rather than joined. The
    /// score for pairing them is set below the cost of two absences on purpose, and if that ever
    /// stops holding the plus and minus counts silently become a measurement of the aligner.
    /// </summary>
    [Fact]
    public void UnrelatedWordsAreNeverPaired()
    {
        var pairings = HebrewWitnessAlignment.Pair(
            [new HebrewForm("אלהים", "אלהים")],
            [new HebrewForm("ארץ", "ארץ")]);

        pairings.Select(p => p.Relation).Should()
            .BeEquivalentTo([LinkRelation.Expands, LinkRelation.Omits]);
    }

    /// <summary>
    /// One tradition writing the vowel letter the other leaves out is the same word written two
    /// ways, and the corpus says so rather than reporting a plus and a minus in the same place.
    /// </summary>
    [Fact]
    public void PleneAndDefectiveSpellingsOfOneWordAreOnePair()
    {
        var pairings = HebrewWitnessAlignment.Pair(
            [new HebrewForm("מאורות", "מאור")],
            [new HebrewForm("מאורת", "מאור")]);

        var only = pairings.Should().ContainSingle().Which;
        only.Relation.Should().Be(LinkRelation.Renders);
        only.Confidence.Should().Be(HebrewWitnessAlignment.LexemeAgrees);
    }

    /// <summary>
    /// An absence in a verse the two witnesses otherwise write the same way is a stronger claim
    /// than one in a verse they do not, and the confidence has to say which it was. Both come out
    /// of the same alignment, so nothing else distinguishes them.
    /// </summary>
    [Fact]
    public void AnAbsenceCarriesTheConfidenceOfTheVerseItStandsIn()
    {
        var together = HebrewWitnessAlignment.Pair(
            [new HebrewForm("ו", "ו"), new HebrewForm("עץ", "עץ"), new HebrewForm("פרי", "פרי")],
            [new HebrewForm("עץ", "עץ"), new HebrewForm("פרי", "פרי")]);

        var apart = HebrewWitnessAlignment.Pair(
            [new HebrewForm("ו", "ו"), new HebrewForm("עץ", "עץ"), new HebrewForm("פרי", "פרי")],
            [new HebrewForm("אלהים", "אלהים"), new HebrewForm("שמים", "שמים")]);

        together.Single(p => p.Relation == LinkRelation.Expands).Confidence
            .Should().Be(HebrewWitnessAlignment.AbsenceWhereTheyAgree);
        apart.Where(p => p.Relation == LinkRelation.Expands).Should()
            .OnlyContain(p => p.Confidence == HebrewWitnessAlignment.AbsenceWhereTheyDoNot);
    }

    private static BhsaProject Bhsa() => BhsaProject.Load(TestResources.Etcbc);

    private static Dictionary<(int Book, int Chapter, int Verse), List<HebrewForm>> Verses(TextSource source) =>
        source.Books
            .SelectMany(book => book.Chapters
                .SelectMany(chapter => chapter.Verses
                    .Select(verse => ((book.CanonicalOrdinal, chapter.Number, verse.Number),
                        verse.Words
                            .Select(w => new HebrewForm(
                                HebrewLetters.Of(w.Surface), HebrewLetters.Of(w.Lemma ?? string.Empty)))
                            .ToList()))))
            .ToDictionary(entry => entry.Item1, entry => entry.Item2);

    private static Measurement Measure()
    {
        var samaritan = Verses(SamaritanTextSource.Read(TestResources.Samaritan));
        var masoretic = Verses(BhsaTextSource.Build(Bhsa()));

        var relations = new Dictionary<LinkRelation, int>
        {
            [LinkRelation.Equals] = 0,
            [LinkRelation.Renders] = 0,
            [LinkRelation.Expands] = 0,
            [LinkRelation.Omits] = 0,
        };

        var differing = new Dictionary<int, int>();
        var verses = 0;
        var samaritanWords = 0;
        var masoreticWords = 0;
        var samaritanCovered = 0;
        var masoreticCovered = 0;
        var twice = 0;
        var elided = 0;
        var elidedAlone = 0;

        foreach (var (address, left) in samaritan)
        {
            if (!masoretic.TryGetValue(address, out var right))
            {
                continue;
            }

            verses++;
            samaritanWords += left.Count;
            masoreticWords += right.Count;
            elided += right.Count(form => form.Consonants.Length == 0);

            var pairings = HebrewWitnessAlignment.Pair(left, right);
            var here = new HashSet<int>();
            var there = new HashSet<int>();

            foreach (var pairing in pairings)
            {
                relations[pairing.Relation]++;
                samaritanCovered += pairing.From.Count;
                masoreticCovered += pairing.To.Count;
                twice += pairing.From.Count(at => !here.Add(at)) + pairing.To.Count(at => !there.Add(at));

                if (pairing.To.Count > 0 && pairing.To.All(at => right[at].Consonants.Length == 0))
                {
                    elidedAlone++;
                }
            }

            if (pairings.Any(p => p.Relation is LinkRelation.Expands or LinkRelation.Omits))
            {
                differing[address.Book] = differing.GetValueOrDefault(address.Book) + 1;
            }
        }

        return new Measurement(
            verses,
            samaritan.Keys.Count(a => !masoretic.ContainsKey(a)),
            masoretic.Keys.Count(a => a.Book <= 5 && !samaritan.ContainsKey(a)),
            samaritanWords,
            masoreticWords,
            samaritanCovered,
            masoreticCovered,
            twice,
            elided,
            elidedAlone,
            relations,
            differing);
    }

    private sealed record Measurement(
        int Verses,
        int SamaritanOnly,
        int MasoreticOnly,
        int SamaritanWords,
        int MasoreticWords,
        int SamaritanCovered,
        int MasoreticCovered,
        int CoveredTwice,
        int Elided,
        int ElidedStandingAlone,
        Dictionary<LinkRelation, int> Relations,
        Dictionary<int, int> DifferingVerses)
    {
        public int Links => Relations.Values.Sum();

        public override string ToString() =>
            $"{Verses} shared verses, {SamaritanWords} Samaritan words against {MasoreticWords} Masoretic " +
            $"of which {Elided} print no letters, {Links} links: "
            + string.Join(", ", Relations.Select(r => $"{r.Value} {r.Key}"));
    }
}
