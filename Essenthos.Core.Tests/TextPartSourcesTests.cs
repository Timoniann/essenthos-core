using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The sources a text credits beside the one it was loaded from, as its row keeps them.</summary>
public sealed class TextPartSourcesTests
{
    [Fact]
    public void EachSourceIsCreditedOnceAndInTheOrderItWasWritten()
    {
        var text = new Text { Slug = "UBIO", Name = "Ohienko", Language = "ukr" };
        TextPartSources.Of(text).Should().BeEmpty();

        TextPartSources.Add(text, LostPsalmOpenings.OhienkoSource).Should().BeTrue();
        TextPartSources.Add(text, LostVerseEndings.OhienkoPart).Should().BeTrue();
        TextPartSources.Add(text, LostPsalmOpenings.OhienkoSource).Should().BeFalse();

        TextPartSources.Of(text).Should().Equal(LostPsalmOpenings.OhienkoSource, LostVerseEndings.OhienkoPart);
        text.PartSources.Should().Contain("\"licenceUrl\":\"https://creativecommons.org/licenses/by-sa/4.0/\"");
    }
}
