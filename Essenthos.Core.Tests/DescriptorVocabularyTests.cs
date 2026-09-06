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
    /// A language nothing has phrasings for renders nothing rather than falling back to English.
    /// Showing a German reader an English sentence is the failure this whole layer replaces.
    /// </summary>
    [Fact]
    public void ALanguageWithNoPhrasingsRendersNothing() =>
        DescriptorPhrasings.For("deu").Should().BeNull();

    /// <summary>Asking for no language is not asking for one, so it is answered in English.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoLanguageAskedForIsEnglish(string? asked) =>
        DescriptorPhrasings.Spoken(asked).Should().Be(DescriptorPhrasings.English);

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
                    new DescriptorPartResponse("Мойсея")
                    {
                        Entity = new EntityRefResponse("person", "moses-1", "Moses"),
                    },
                ],
                [
                    new DescriptorClaimResponse(
                        1,
                        DescriptorRelations.FatherInLawOf,
                        new EntityRefResponse("person", "moses-1", "Moses"),
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
    }
}
