using System.Text.Json;
using Essenthos.Core;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a reader can find out about where an answer came from, asked of the wire rather than of the
/// database.
///
/// The encyclopedia's bar is that a reading stored with its model and its date is what the corpus is
/// for and a reading stored as a fact is not — and that only holds if the difference survives the
/// serialiser. A card that says <em>Moses</em> in the same voice whether a lexicon resolved it or a
/// model read it fails the bar no matter how carefully the row was written.
/// </summary>
public sealed class AnnotationProvenanceTests
{
    /// <summary>
    /// Every response record has to be registered in the source-generated context. Forgetting one
    /// is a runtime failure on first request rather than a compile error, so the registration is
    /// asserted rather than trusted.
    /// </summary>
    [Theory]
    [InlineData(typeof(EntityRefResponse))]
    [InlineData(typeof(EntityResponse))]
    [InlineData(typeof(EntityClaimResponse))]
    [InlineData(typeof(EntityAlternativeResponse))]
    [InlineData(typeof(IList<EntityClaimResponse>))]
    [InlineData(typeof(IList<EntityAlternativeResponse>))]
    public void EveryResponseTheEntityPageReturnsIsRegistered(Type response) =>
        AppJsonSerializerContext.Default.GetTypeInfo(response).Should().NotBeNull();

    /// <summary>
    /// The hover card answers who said this and how sure they are, in the same shape whichever
    /// method produced it.
    /// </summary>
    [Fact]
    public void AWordsCardSaysWhoSaidItAndHowSure()
    {
        var card = new EntityRefResponse("person", "zechariah-2", "Zechariah")
        {
            Method = EnumSpelling.Of(LinkMethod.ModelReading),
            Confidence = 0.99,
            Source = "a reading of the verse by a-model, prompt sense-1, run to 2026-09-05",
            Note = "H2148, read as zechariah-2 with high confidence: the son of Berechiah",
        };

        var wire = JsonSerializer.Serialize(
            card, AppJsonSerializerContext.Default.GetTypeInfo(typeof(EntityRefResponse))!);

        wire.Should().Contain("model-reading")
            .And.Contain("0.99")
            .And.Contain("a-model")
            .And.Contain("sense-1");
    }

    /// <summary>
    /// A record this corpus wrote reaches the reader saying so, with the method and whoever decided
    /// on it, and naming who else it might be. Nothing of that is inferable from a source string.
    /// </summary>
    [Fact]
    public void ARecordWeWroteReachesTheReaderSayingSo()
    {
        var page = new EntityResponse(
            "zimri-in-jezebels-cry", "person", "Zimri", "whom Jezebel names", null, null, null, null,
            "The rhetoric is recorded rather than resolved.", null, null,
            "Essenthos, on the project owner's ruling", "essenthos", 1, 1, 0, [], [], [], [],
            [new EntityClaimResponse("manual", null, "Essenthos, on the project owner's ruling", "essenthos", "why")],
            [new EntityAlternativeResponse("jehu-2", "Jehu", null, null, "she may be addressing him", "Essenthos")],
            true);

        var wire = JsonSerializer.Serialize(
            page, AppJsonSerializerContext.Default.GetTypeInfo(typeof(EntityResponse))!);

        wire.Should().Contain("\"Unsettled\":true")
            .And.Contain("jehu-2")
            .And.Contain("\"Method\":\"manual\"");
    }
}
