using System.Text.Json;
using Essenthos.Core;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The two halves of the closed vocabulary, checked against each other.
///
/// A relation the loader accepts and no language can say would reach a reader as its own
/// identifier — <c>father-in-law-of</c> printed under a name. A phrasing for a relation nothing can
/// store is dead code that reads as coverage. Neither is visible from either file alone, which is
/// why this is asserted rather than trusted.
/// </summary>
public sealed class DescriptorVocabularyTests
{
    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var language in DescriptorPhrasings.Languages)
        {
            data.Add(language);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryRelationCanBeSaidInEveryLanguageTheEncyclopediaSpeaks(string language)
    {
        var phrasings = DescriptorPhrasings.For(language);

        phrasings.Should().NotBeNull();
        DescriptorRelations.All.Except(phrasings!.Keys).Should().BeEmpty(
            "a relation the loader accepts and {0} cannot say would be printed at a reader as "
            + "itself", language);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void NoLanguagePhrasesARelationNothingCanStore(string language) =>
        DescriptorPhrasings.For(language)!.Keys.Except(DescriptorRelations.All).Should().BeEmpty();

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryPhrasingAsksForACaseAFormCanBeHeldIn(string language) =>
        DescriptorPhrasings.For(language)!.Values
            .Select(p => p.Case)
            .Should().OnlyContain(c => GrammaticalCases.All.Contains(c));

    /// <summary>
    /// The four clauses a place page is made of, in the one language that inflects them: each asks
    /// for the locative, and none of them may quietly go back to asking for the genitive.
    ///
    /// It is asserted rather than left to the table because the genitive is what DOC-0191 asks a
    /// generation pass for, so a phrasing that asks for it renders today and a phrasing that asks
    /// for the locative renders the English name until TSK-0364's pass lands. Making the line look
    /// finished is exactly the wrong reason to change one back, and it is a one-word edit.
    /// </summary>
    [Theory]
    [InlineData("ukr")]
    public void EveryClauseThatPutsAPlaceSomewhereAsksForTheLocative(string language)
    {
        string[] somewhere =
        [
            DescriptorRelations.LivedIn,
            DescriptorRelations.BuriedIn,
            DescriptorRelations.CityIn,
            DescriptorRelations.MountainIn,
        ];

        var phrasings = DescriptorPhrasings.For(language)!;

        somewhere.Select(relation => phrasings[relation].Case)
            .Should().AllBe(GrammaticalCases.Locative);
    }

    /// <summary>
    /// Every widening of the vocabulary asks for a case a pass has already been asked to produce.
    ///
    /// The instrumental is the one that would otherwise have crept in: <em>убитий Зіхрі</em> wants
    /// it, no entity in the corpus has a form in it, and a clause asking for one would render the
    /// English name for every entity there is rather than for the few that are missing a form.
    /// </summary>
    [Theory]
    [InlineData("ukr")]
    public void NoRelationAsksForACaseNoPassHasBeenAskedToProduce(string language)
    {
        string[] widened =
        [
            DescriptorRelations.HalfBrotherOf, DescriptorRelations.HalfSisterOf,
            DescriptorRelations.GrandsonOf, DescriptorRelations.GranddaughterOf,
            DescriptorRelations.UncleOf, DescriptorRelations.AuntOf,
            DescriptorRelations.NephewOf, DescriptorRelations.NieceOf,
            DescriptorRelations.BrotherInLawOf, DescriptorRelations.SisterInLawOf,
            DescriptorRelations.ConcubineOf,
            DescriptorRelations.GovernorOf, DescriptorRelations.TetrarchOf,
            DescriptorRelations.MasterOf, DescriptorRelations.CompanionOf,
            DescriptorRelations.KilledBy, DescriptorRelations.KillerOf,
            DescriptorRelations.AngelOf, DescriptorRelations.GateOf,
            DescriptorRelations.TeacherOf, DescriptorRelations.AllyOf, DescriptorRelations.CousinOf,
            DescriptorRelations.RapedBy, DescriptorRelations.RaperOf,
            DescriptorRelations.SupporterOf, DescriptorRelations.SupportedBy,
            DescriptorRelations.CreatorOf, DescriptorRelations.CreatedBy,
            DescriptorRelations.HeirOf, DescriptorRelations.ExilerOf, DescriptorRelations.ExiledBy,
        ];

        var phrasings = DescriptorPhrasings.For(language)!;

        widened.Select(relation => phrasings[relation].Case)
            .Should().AllBe(GrammaticalCases.Genitive);
    }

    /// <summary>
    /// No language asks for a case its own generation pass was never asked for. DOC-0191 says what
    /// each pass produces — three forms for Ukrainian, two for German, the nominative alone for
    /// English and Spanish — and a phrasing that reaches past them renders the English name for
    /// every entity in the corpus rather than for the few missing a form.
    /// </summary>
    [Theory]
    [InlineData("eng", GrammaticalCases.Nominative)]
    [InlineData("spa", GrammaticalCases.Nominative)]
    [InlineData("deu", GrammaticalCases.Nominative, GrammaticalCases.Genitive)]
    [InlineData("ukr", GrammaticalCases.Nominative, GrammaticalCases.Genitive,
        GrammaticalCases.Locative)]
    public void NoLanguageAsksForACaseItsOwnPassDoesNotProduce(string language, params string[] produced) =>
        DescriptorPhrasings.For(language)!.Values
            .Select(p => p.Case)
            .Should().OnlyContain(c => produced.Contains(c));

    /// <summary>
    /// A language nothing has phrasings for has none, and is answered in English instead of being
    /// refused. The alternative on the page is the imported sentence, which is English too and is
    /// somebody else's prose with no link in it.
    /// </summary>
    [Fact]
    public void ALanguageWithNoPhrasingsHasNoneAndIsSpokenAsEnglish()
    {
        DescriptorPhrasings.For("rus").Should().BeNull();
        DescriptorPhrasings.Spoken("rus").Should().Be(DescriptorPhrasings.English);
    }

    /// <summary>
    /// The four the client is written in (FTR-0280), and Russian which it is not. Asserted because
    /// the two halves drifted apart once already: the renderer spoke Russian and not German while
    /// 1,159 German forms sat in the table with nothing to render them (PRB-0436).
    /// </summary>
    [Fact]
    public void TheEncyclopediaSpeaksTheLanguagesTheClientIsWrittenIn() =>
        DescriptorPhrasings.Languages.Should().BeEquivalentTo(["eng", "ukr", "deu", "spa"]);

    /// <summary>Asking for no language is not asking for one, so it is answered in English.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("rus")]
    [InlineData("not-a-language")]
    public void WhatIsNotSpokenIsAnsweredInEnglish(string? asked) =>
        DescriptorPhrasings.Spoken(asked).Should().Be(DescriptorPhrasings.English);

    [Theory]
    [InlineData("ukr")]
    [InlineData("  ukr  ")]
    public void ALanguageTheEncyclopediaSpeaksIsTheOneRendered(string asked) =>
        DescriptorPhrasings.Spoken(asked).Should().Be(DescriptorPhrasings.Ukrainian);


    [Theory]
    [InlineData("NUM 10:29", 4, 10, 29)]
    [InlineData("JDG 4:11", 7, 4, 11)]
    [InlineData("1SA 1:1", 9, 1, 1)]
    [InlineData("1 Samuel 1:1", 9, 1, 1)]
    [InlineData("Numbers 10:29", 4, 10, 29)]
    public void AReferenceResolvesInWhicheverSpellingThePassUsed(
        string reference, int book, int chapter, int verse) =>
        EntityDescriptorLoader.Reference(reference).Should().Be((book, chapter, verse));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NUM")]
    [InlineData("NUM 10")]
    [InlineData("Numbers of the Wanderings 10:29")]
    public void AReferenceNothingCanResolveIsNotGuessedAt(string? reference) =>
        EntityDescriptorLoader.Reference(reference).Should().BeNull();

    /// <summary>
    /// A response record missing from the source-generated context fails at runtime on the first
    /// request rather than at compile time, so the registration is asserted rather than trusted.
    /// </summary>
    [Theory]
    [InlineData(typeof(EntityDescriptorResponse))]
    [InlineData(typeof(DescriptorTargetResponse))]
    [InlineData(typeof(Dictionary<string, string>))]
    [InlineData(typeof(DescriptorPartResponse))]
    [InlineData(typeof(IList<DescriptorPartResponse>))]
    [InlineData(typeof(DescriptorClaimResponse))]
    [InlineData(typeof(IList<DescriptorClaimResponse>))]
    public void EveryRecordTheDescriptionTravelsInIsRegistered(Type response) =>
        AppJsonSerializerContext.Default.GetTypeInfo(response).Should().NotBeNull();

    /// <summary>
    /// The entity page carries the description and still carries the imported sentence. The second
    /// half is the point: the client is being changed separately, and a field that vanishes
    /// mid-flight breaks whoever is still reading it.
    /// </summary>
    [Fact]
    public void ThePageSendsTheDescriptionWithoutTakingTheImportedSentenceAway()
    {
        var page = new EntityResponse(
            "hobab-1", "person", "Hobab", "the son of Reuel, Moses' father-in-law (NUM 10:29)",
            null, null, null, null, null, null, null, "BibleData", "bibledata", 2, 2, 0,
            [], [], [], [], [], [], false)
        {
            Descriptor = new EntityDescriptorResponse(
                DescriptorPhrasings.Ukrainian,
                [
                    new DescriptorPartResponse("тесть "),
                    new DescriptorPartResponse("Мойсея") { Entity = Moses },
                ],
                [
                    new DescriptorClaimResponse(
                        1,
                        DescriptorRelations.FatherInLawOf,
                        Moses,
                        new VerseRefResponse(7, "Judges", "judges", 4, 11),
                        "model-reading",
                        0.93,
                        "read from Scripture by a-model, asked 2026-09-06",
                        false),
                ]),
        };

        var wire = JsonSerializer.Serialize(
            page, AppJsonSerializerContext.Default.GetTypeInfo(typeof(EntityResponse))!);

        // The Cyrillic arrives escaped, which is what the serializer does with everything outside
        // ASCII and is JSON a client decodes without noticing.
        wire.Should().Contain("\"Language\":\"ukr\"")
            .And.Contain("\\u0442\\u0435\\u0441\\u0442\\u044C ")
            .And.Contain("moses-1")
            .And.Contain("father-in-law-of")
            .And.Contain("father-in-law (NUM 10:29)", "the imported sentence stays on the wire for now");

        wire.Should().Contain("\"Kind\":\"person\"", "a slug alone cannot be routed to a page")
            .And.Contain("\"EnglishName\":\"Moses\"")
            .And.Contain("\"genitive\":\"\\u041C\\u043E\\u0439\\u0441\\u0435\\u044F\"")
            .And.Contain("\"Slug\":\"judges\"", "a client links a verse by book slug, not by JDG");
    }

    /// <summary>
    /// Moses as a client receives him: the kind that says which page to open, the name in the
    /// language being rendered, the cases that language's phrases put it in, and the English name a
    /// case nobody produced falls back to.
    /// </summary>
    private static DescriptorTargetResponse Moses { get; } =
        new("person", "moses-1", "Мойсей", "Moses")
        {
            Forms = new Dictionary<string, string>
            {
                [GrammaticalCases.Nominative] = "Мойсей",
                [GrammaticalCases.Genitive] = "Мойсея",
            },
        };

    /// <summary>
    /// §11 of the Ukrainian orthography, which is the one direction it decides: <em>у</em> before a
    /// word beginning with <em>в</em> or <em>ф</em>. The rule cannot sit in the table because the
    /// right word depends on the name that follows, which the phrase is written without.
    /// </summary>
    [Theory]
    [InlineData("місто в ", "Вавилоні", "місто у ")]
    [InlineData("гора в ", "Фаранській", "гора у ")]
    [InlineData("місто в ", "Юдеї", "місто в ")]
    [InlineData("місто в ", "Авані", "місто в ")]
    [InlineData("жив у ", "Авані", "жив у ")]
    [InlineData("жив у ", "Вавилоні", "жив у ")]
    [InlineData("брама ", "Вавилона", "брама ")]
    public void ThePrepositionAgreesWithTheWordAfterIt(string before, string next, string expected) =>
        DescriptorPhrasings.AgreeWithWhatFollows(before, next).Should().Be(expected);
}
