using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A reader's language asked for as an interface locale spells it. <c>?language=uk</c> answered every
/// name in English, because the corpus spells Ukrainian <c>ukr</c> and nothing said the two were one.
/// </summary>
public sealed class LanguageCodeTests
{
    [Theory]
    [InlineData("uk", "ukr")]
    [InlineData("UK", "ukr")]
    [InlineData("de", "deu")]
    [InlineData("es", "spa")]
    [InlineData("en", "eng")]
    [InlineData("ukr", "ukr")]
    [InlineData("xx", "xx")]
    public void BothSpellingsOfALanguageAreOne(string asked, string corpus) =>
        LanguageCodes.Normalise(asked).Should().Be(corpus);

    [Fact]
    public void ARequestAskingForUkIsAnsweredAsUkr()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?q=Moses&language=uk");

        LanguageCodes.Rewrite(context.Request);

        context.Request.Query["language"].ToString().Should().Be("ukr");
        context.Request.Query["q"].ToString().Should().Be("Moses");
    }
}
