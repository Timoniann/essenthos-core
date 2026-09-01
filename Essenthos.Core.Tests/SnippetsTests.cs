using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class SnippetsTests
{
    private static readonly (string Text, string Trailer)[] Verse =
    [
        ("For", " "), ("God", " "), ("so", " "), ("loved", " "), ("the", " "), ("world", "."),
    ];

    [Fact]
    public void WrapsMatchedWordsAndRebuildsTheRest()
    {
        Snippets.Build(Verse, text => text == "loved")
            .Should().Be("For God so <em>loved</em> the world.");
    }

    [Fact]
    public void EscapesTextSoTheSnippetCanBeRenderedAsIs()
    {
        Snippets.Build([("<script>", " "), ("a&b", "")], _ => false)
            .Should().Be("&lt;script&gt; a&amp;b");
    }

    [Fact]
    public void EscapesInsideAMatchToo()
    {
        Snippets.Build([("a&b", "")], _ => true).Should().Be("<em>a&amp;b</em>");
    }

    [Fact]
    public void AnEmptyVerseIsAnEmptySnippet()
    {
        Snippets.Build([], _ => true).Should().BeEmpty();
    }

    /// <summary>
    /// the apostrophe is legal in HTML text content, and escaping it left every
    /// possessive in the corpus reading "Joseph&amp;#39;s".
    /// </summary>
    [Fact]
    public void AnApostropheIsNotEscaped()
    {
        Snippets.Build([("Joseph's", " "), ("name", "")], _ => false).Should().Be("Joseph's name");
    }

    [Fact]
    public void TheCallerDecidesWhichWordsAreMarked()
    {
        Snippets.Build([("the", " ", false), ("city", "", true)]).Should().Be("the <em>city</em>");
    }

    [Fact]
    public void TwoWordsSpelledTheSameAreMarkedIndependently()
    {
        Snippets.Build([("Jacob", " ", true), ("and", " ", false), ("Jacob", "", false)])
            .Should().Be("<em>Jacob</em> and Jacob");
    }
}
