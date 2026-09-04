using Essenthos.Core.Endpoints;
using Essenthos.Core.Nestle;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A Greek word's case, read from the form code because the attribute that claims to hold it does
/// not. The file writes `case="neuter"` — a gender — where the word is nominative, 20,629 times,
/// and never writes `nominative` at all.
/// </summary>
public class NestleCaseTests
{
    /// <summary>The case the corpus never had: `nominative` appears nowhere in the source file.</summary>
    [Theory]
    [InlineData("N-NSF", "nominative")]
    [InlineData("N-GSM", "genitive")]
    [InlineData("N-DPN", "dative")]
    [InlineData("N-ASF", "accusative")]
    [InlineData("N-VSM", "vocative")]
    public void ReadsTheCaseOffTheFormCode(string form, string expected) =>
        NestleCase.Of(form, "neuter").Should().Be(expected);

    /// <summary>A participle carries its case in the last group, after the tense and voice.</summary>
    [Theory]
    [InlineData("V-PAP-NSM", "nominative")]
    [InlineData("V-2AAP-GSM", "genitive")]
    public void ReadsAParticiplesCase(string form, string expected) =>
        NestleCase.Of(form, null).Should().Be(expected);

    /// <summary>A pronoun writes its person first, so the case is the second character.</summary>
    [Theory]
    [InlineData("P-1AS", "accusative")]
    [InlineData("P-2GS", "genitive")]
    [InlineData("P-1NP", "nominative")]
    public void ReadsAPronounsCasePastThePerson(string form, string expected) =>
        NestleCase.Of(form, null).Should().Be(expected);

    /// <summary>
    /// A finite verb is in no case, and that is an answer rather than a gap. Its last group is
    /// person and number.
    /// </summary>
    [Theory]
    [InlineData("V-PAI-3S")]
    [InlineData("V-2AAI-1P")]
    [InlineData("V-PAN")]
    [InlineData("CONJ")]
    [InlineData("PREP")]
    [InlineData("ADV")]
    public void SaysNothingWhereThereIsNoCase(string form) =>
        NestleCase.Of(form, null).Should().BeNull();

    /// <summary>
    /// The attribute is read only where the code is silent, and only if it names a case. It says
    /// `neuter` 138 times in that position, and a gender is not an answer to this question — the
    /// gender attribute already carries it, correctly, everywhere.
    /// </summary>
    [Fact]
    public void RefusesAGenderWhereACaseWasAsked()
    {
        NestleCase.Of("CONJ", "neuter").Should().BeNull();
        NestleCase.Of("CONJ", "masculine").Should().BeNull();
    }

    [Fact]
    public void FallsBackToTheAttributeWhereItNamesARealCase() =>
        NestleCase.Of("PRT-N", "accusative").Should().Be("accusative");

    [Fact]
    public void HasNothingToSayAboutAMissingForm() =>
        NestleCase.Of(null, null).Should().BeNull();
}

/// <summary>
/// The case reaches the reader, which for two months it did not.
///
/// The parser was fixed, the value was written, and the response record had no field for it — so a
/// reader of the Greek was shown gender, number and person and never the one annotation a Greek word
/// most needs to state. A parser test alone would have gone on passing throughout.
/// </summary>
public sealed class GreekCaseIsPublishedTests
{
    private static readonly string[] Cases =
        ["nominative", "genitive", "dative", "accusative", "vocative"];

    [Fact]
    public void TheResponseHasSomewhereToPutACase() =>
        typeof(MorphologyResponse).GetProperty("Case").Should().NotBeNull(
            "a Greek word's case is stored and has to be readable; the field set came from a Hebrew "
            + "text, which has no cases, and the gap was invisible from the Hebrew side");

    [Theory]
    [InlineData("N-NSF", "nominative")]
    [InlineData("N-GSM", "genitive")]
    [InlineData("V-PAP-NSM", "nominative")]
    [InlineData("P-1AS", "accusative")]
    public void EveryCaseTheParserProducesIsOneTheApiCanName(string form, string expected)
    {
        var parsed = NestleCase.Of(form, null);
        parsed.Should().Be(expected);
        Cases.Should().Contain(parsed!);
    }
}
