using Essenthos.Core.Corpus;
﻿using Essenthos.Core.Endpoints;
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
    /// A group that begins with a case letter and is not a case group. <c>NUI</c> is an
    /// indeclinable numeral and <c>PRI</c> an indeclinable proper noun; an infinitive has no case
    /// group at all, and its tense group must not be read as one — <c>APN</c> is aorist passive, not
    /// accusative plural neuter.
    /// </summary>
    [Theory]
    [InlineData("A-NUI")]
    [InlineData("N-PRI")]
    [InlineData("V-AAN")]
    [InlineData("V-APN")]
    [InlineData("V-2AAN")]
    [InlineData("V-RAI-3S-ATT")]
    [InlineData("V-PEM-2P@@V-PNM-2P")]
    public void FindsNoCaseWhereTheCodeHasNone(string form) =>
        NestleCase.Of(form, null).Should().BeNull();

    /// <summary>
    /// A suffix after the case group is a note about the form. The case is the group's, whatever
    /// the suffix begins with.
    /// </summary>
    [Theory]
    [InlineData("R-GSN-ATT", "genitive")]
    [InlineData("V-RAP-GSM-ATT", "genitive")]
    [InlineData("A-NSM-ATT", "nominative")]
    [InlineData("A-NSM-N", "nominative")]
    [InlineData("A-ASN-C", "accusative")]
    [InlineData("A-DSF-S", "dative")]
    [InlineData("P-1NS-K", "nominative")]
    [InlineData("D-APN-K", "accusative")]
    public void ReadsTheCaseGroupPastASuffix(string form, string expected) =>
        NestleCase.Of(form, null).Should().Be(expected);

    /// <summary>The reflexive writes a person before its case group, the possessive the owner's person and number.</summary>
    [Theory]
    [InlineData("F-3ASM", "accusative")]
    [InlineData("F-2GPM", "genitive")]
    [InlineData("S-1SASF", "accusative")]
    [InlineData("S-2PDSM", "dative")]
    public void ReadsTheReflexiveAndPossessiveCase(string form, string expected) =>
        NestleCase.Of(form, null).Should().Be(expected);

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
