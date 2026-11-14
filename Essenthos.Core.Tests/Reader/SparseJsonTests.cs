using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>The parallel view's and a chapter's words are written without their empty fields.</summary>
public class SparseJsonTests
{
    private static async Task<JsonElement> Written<T>(T value)
    {
        var json = new JsonOptions();
        json.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        await new SparseJson(Options.Create(json)).Ok(value).ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (await JsonDocument.ParseAsync(context.Response.Body)).RootElement.Clone();
    }

    [Fact]
    public async Task A_plain_word_carries_its_letters_and_links_and_nothing_else()
    {
        var word = new TextWordResponse(7, "the", " ", null, null, null, [3, 4], "stated-by-source", null, null, null, null, null, null);

        var written = await Written<IList<TextWordResponse>>([word]);

        written[0].EnumerateObject().Select(p => p.Name).Should()
            .Equal("id", "text", "trailer", "originalWordIds", "mappingProvenance");
        written[0].GetProperty("originalWordIds").EnumerateArray().Select(id => id.GetInt64()).Should().Equal(3, 4);
    }

    [Fact]
    public async Task What_a_word_does_carry_is_written_as_before()
    {
        var word = new TextWordResponse(8, "God", "", "god", "H430", "H430", [], null, "expands", null, null, null, null, "hbo")
        {
            Supplied = true,
            Mark = "supplied",
            Entities = [new EntityRefResponse("person", "god", "God") { Confidence = 0.5 }],
        };

        var written = (await Written<IList<TextWordResponse>>([word]))[0];

        written.GetProperty("supplied").GetBoolean().Should().BeTrue();
        written.GetProperty("absence").GetString().Should().Be("expands");
        written.GetProperty("language").GetString().Should().Be("hbo");
        written.TryGetProperty("trailer", out _).Should().BeFalse("an empty trailer is left out");
        written.TryGetProperty("originalWordIds", out _).Should().BeFalse("a word linked to nothing has no list");
        written.TryGetProperty("elided", out _).Should().BeFalse();
        var entity = written.GetProperty("entities")[0];
        entity.GetProperty("confidence").GetDouble().Should().Be(0.5);
        entity.TryGetProperty("method", out _).Should().BeFalse();
    }
}
