using Essenthos.Core.Database.Entities;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>The sources a text credits beside the one it was loaded from, as its row keeps them.</summary>
public sealed class TextPartSourcesTests
{
    private static readonly TextPartSource Opening = new(
        "Біблія (Огієнко)", "The contributors to Ukrainian Wikisource", "CC BY-SA 4.0",
        "https://creativecommons.org/licenses/by-sa/4.0/", "https://uk.wikisource.org/", "The first line of Psalm 7.");

    private static readonly TextPartSource Endings = Opening with { Covers = "The ends of seven verses." };

    [Fact]
    public void EachSourceIsCreditedOnceAndInTheOrderItWasWritten()
    {
        var text = new Text { Slug = "UBIO", Name = "Ohienko", Language = "ukr" };
        TextPartSources.Of(text).Should().BeEmpty();

        TextPartSources.Add(text, Opening).Should().BeTrue();
        TextPartSources.Add(text, Endings).Should().BeTrue();
        TextPartSources.Add(text, Opening).Should().BeFalse();

        TextPartSources.Of(text).Should().Equal(Opening, Endings);
        text.PartSources.Should().Contain("\"licenceUrl\":\"https://creativecommons.org/licenses/by-sa/4.0/\"");
    }
}
