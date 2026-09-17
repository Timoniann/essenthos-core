using Essenthos.Core.Corpus;
using System.Text.Json;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The addresses <c>/v1/verses</c> takes, and the shape it answers in.
///
/// The address is the whole of the endpoint's input, and a client builds it from a reference that
/// arrived from somewhere else — an entity's verse, a Strong occurrence, an event. So the two
/// things worth pinning are that every spelling of a book a reference can arrive in resolves, and
/// that nothing else does: a malformed address is refused where it was typed rather than silently
/// answering the wrong verse.
/// </summary>
public class VerseAddressTests
{
    [Theory]
    [InlineData("genesis:11:27", 1, 11, 27)]
    [InlineData("Genesis:11:27", 1, 11, 27)]
    [InlineData("1-samuel:1:1", 9, 1, 1)]
    [InlineData("Gen:1:1", 1, 1, 1)]
    [InlineData("66:22:21", 66, 22, 21)]
    public void ReadsAnAddressInEverySpellingOfABook(string asked, int ordinal, int chapter, int verse)
    {
        var address = VerseEndpoints.Address.Parse(asked);

        address.Should().NotBeNull();
        address!.Value.Asked.Should().Be(asked);
        address.Value.Key.Should().Be(
            (ordinal * VerseEndpoints.Address.BookStride)
            + (chapter * VerseEndpoints.Address.ChapterStride)
            + verse);
    }

    [Theory]
    [InlineData("genesis:11")]
    [InlineData("genesis:11:27:1")]
    [InlineData("genesis")]
    [InlineData("nowhere:1:1")]
    [InlineData("genesis:0:1")]
    [InlineData("genesis:1:0")]
    [InlineData("genesis:eleven:27")]
    [InlineData("")]
    public void RefusesAnythingThatIsNotOne(string asked)
    {
        VerseEndpoints.Address.Parse(asked).Should().BeNull();
    }

    /// <summary>
    /// The key packs three numbers into one so the database can match a set of addresses in a
    /// single <c>IN</c>. That only works while it orders the way the three columns order, and the
    /// strides are what keep it true — a chapter never reaches a thousand verses.
    /// </summary>
    [Fact]
    public void OrdersTheWayTheThreeNumbersOrder()
    {
        var first = VerseEndpoints.Address.Parse("genesis:11:27")!.Value.Key;
        var later = VerseEndpoints.Address.Parse("genesis:11:28")!.Value.Key;
        var nextChapter = VerseEndpoints.Address.Parse("genesis:12:1")!.Value.Key;
        var nextBook = VerseEndpoints.Address.Parse("exodus:1:1")!.Value.Key;

        first.Should().BeLessThan(later);
        later.Should().BeLessThan(nextChapter);
        nextChapter.Should().BeLessThan(nextBook);
    }

    /// <summary>
    /// Every response record an endpoint returns is registered in the serializer context, and
    /// forgetting one fails at runtime on the first request rather than at compile time (RUL-0003).
    /// </summary>
    [Theory]
    [InlineData(typeof(VerseTextResponse))]
    [InlineData(typeof(VerseTextListResponse))]
    [InlineData(typeof(IList<VerseTextResponse>))]
    public void IsSerializableWithoutReflection(Type response)
    {
        AppJsonSerializerContext.Default.GetTypeInfo(response).Should().NotBeNull();
    }

    [Fact]
    public void SendsTheVerseAndSaysWhatItLeftOut()
    {
        var answer = new VerseTextListResponse(
            "KJV",
            [new VerseTextResponse(new BookRefResponse(1, "Genesis", "genesis"), 11, 27, "Now these are the generations of Terah.")],
            ["sirach:1:1"]);

        var json = JsonSerializer.Serialize(
            answer, AppJsonSerializerContext.Default.GetTypeInfo(typeof(VerseTextListResponse))!);

        json.Should().Contain("KJV");
        json.Should().Contain("generations of Terah");
        json.Should().Contain("sirach:1:1");
    }
}
