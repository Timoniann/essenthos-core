using Essenthos.Core.Macula;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// What MACULA actually says, measured over all 137,779 of its rows rather than over a sample.
///
/// The figures are here so that a re-fetch says what it changed. They are also the answer to two
/// questions this corpus had already asked and answered badly: which Greek words are names, which
/// the naming pass had to infer from a capital letter in the lexicon, and what case an indeclinable
/// numeral has, which the Nestle form-code reader answers with <em>nominative</em> 476 times.
/// </summary>
public class MaculaClassTests(ITestOutputHelper output)
{
    private const int Books = 27;

    /// <summary>One row per word of Nestle 1904, which is the whole reason this needs no alignment.</summary>
    private const int Words = 137_779;

    /// <summary>Words MACULA states are names. The Greek naming pass has never had this figure stated.</summary>
    private const int Proper = 4_639;

    private const int Common = 23_644;

    /// <summary>
    /// Indeclinable numerals — δύο, δώδεκα, τεσσεράκοντα. A class of their own here, where Nestle's
    /// morphology calls them adjectives.
    /// </summary>
    private const int Numerals = 476;

    /// <summary>
    /// Words written in the Attic way, whose form code carries <c>-ATT</c> after the group that
    /// holds the case.
    /// </summary>
    private const int Attic = 117;

    private static bool Fetched => File.Exists(MaculaReader.Path(TestResources.MaculaFolder));

    private static IReadOnlyList<MaculaWord> Annotation() => MaculaReader.ReadFile(TestResources.MaculaFolder);

    private bool Absent()
    {
        if (Fetched)
        {
            return false;
        }

        output.WriteLine("MACULA is not on disk; run scripts/fetch-macula.ps1");
        return true;
    }

    [Fact]
    public void AnnotatesEveryWordOfTheNewTestamentInCanonicalOrder()
    {
        if (Absent())
        {
            return;
        }

        var words = Annotation();

        words.Should().HaveCount(Words);
        words.Select(word => word.Book).Distinct().Should().HaveCount(Books);
        words[0].Book.Should().Be(40);
        words[^1].Book.Should().Be(66);
        words
            .Select(word => (word.Book, word.Chapter, word.Verse, word.Position))
            .Should().BeInAscendingOrder();
    }

    /// <summary>
    /// The reason this dataset is in the corpus. Nestle's own morphology says <c>noun</c> 28,394
    /// times and stops; every noun here is one thing or the other, on 196 gaps.
    /// </summary>
    [Fact]
    public void StatesWhichWordsAreNames()
    {
        if (Absent())
        {
            return;
        }

        var words = Annotation();

        words.Count(word => word.Type == "proper").Should().Be(Proper);
        words.Count(word => word.Type == "common").Should().Be(Common);
        words.Where(word => word.Type == "proper").Select(word => word.Class).Distinct()
            .Should().BeEquivalentTo(["noun", "adj"],
                "a name is a noun, except for the seven adjectives formed straight from one");
    }

    /// <summary>
    /// The pronoun kinds come out of the same column, and they are finer than the corpus's own
    /// vocabulary: Nestle says <c>pron</c> where this separates the personal from the demonstrative,
    /// relative, interrogative, indefinite and possessive.
    /// </summary>
    [Fact]
    public void SeparatesThePronounKindsUnderTheSameColumn()
    {
        if (Absent())
        {
            return;
        }

        var types = Annotation()
            .Where(word => word.Class == "pron")
            .Select(word => word.Type)
            .Where(type => type is not null)
            .Distinct()
            .Order(StringComparer.Ordinal);

        types.Should().Equal("demonstrative", "indefinite", "interrogative", "personal", "relative");
    }

    /// <summary>
    /// A third reading of the same form code, and it agrees with the second: an indeclinable
    /// numeral has no case. This corpus reports all 476 of them nominative, because the reader takes
    /// the first letter of the last hyphen-group and <c>NUI</c> begins with N.
    /// </summary>
    [Fact]
    public void GivesTheIndeclinableNumeralsNoCaseAtAll()
    {
        if (Absent())
        {
            return;
        }

        var numerals = Annotation().Where(word => word.Class == "num").ToList();

        numerals.Should().HaveCount(Numerals);
        numerals.Should().OnlyContain(word => word.Morph == "A-NUI");
        numerals.Should().OnlyContain(word => word.Case == null);
    }

    /// <summary>
    /// The other half of the same reading fault. <c>-ATT</c> is a note about the form and stands
    /// after the group that holds the case, so a reader looking in the last position calls 110
    /// verbs accusative. Here the case comes from the group that holds it: the participles have one,
    /// the finite verbs have none, and the pronouns are genitive rather than accusative.
    /// </summary>
    [Fact]
    public void ReadsTheCaseOfAnAtticFormFromTheGroupThatHoldsIt()
    {
        if (Absent())
        {
            return;
        }

        var attic = Annotation().Where(word => word.Morph is { } morph && morph.EndsWith("-ATT")).ToList();

        attic.Should().HaveCount(Attic);
        attic.Count(word => word.Class == "verb" && word.Case is null).Should().Be(101);
        attic.Where(word => word.Class == "pron").Should().OnlyContain(word => word.Case == "genitive");
        attic.Where(word => word.Class == "adj").Should().OnlyContain(word => word.Case == "nominative");
    }

    /// <summary>
    /// A degree is a feature Nestle 1904 has no field for at all, so these 513 words gain something
    /// rather than being checked against anything.
    /// </summary>
    [Fact]
    public void CarriesADegreeTheCorpusHasNoFieldFor()
    {
        if (Absent())
        {
            return;
        }

        var degrees = Annotation().Where(word => word.Degree is not null).ToList();

        degrees.Count(word => word.Degree == "comparative").Should().Be(313);
        degrees.Count(word => word.Degree == "superlative").Should().Be(200);
    }
}
