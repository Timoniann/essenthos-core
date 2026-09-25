using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A compound one edition prints as two words is read as the one word another edition prints.</summary>
public class EnglishCompoundsTests
{
    private static readonly EvidentiaAddress Luke1333 = new(42, 13, 33);

    [Fact]
    public void TheSecondHalfOfACompoundPrintedAsTwoWordsIsReadAsTheCompound()
    {
        var tokens = EnglishCompounds.Join(
        [
            new EvidentiaToken(1, Luke1333, 1, "to", "eng", Trailer: " "),
            new EvidentiaToken(2, Luke1333, 2, "day", "eng", Trailer: ", "),
            new EvidentiaToken(3, Luke1333, 3, "and", "eng", Trailer: " "),
            new EvidentiaToken(4, Luke1333, 4, "to", "eng", Trailer: ", "),
            new EvidentiaToken(5, Luke1333, 5, "morrow", "eng", Trailer: " "),
        ]);

        tokens.Select(token => token.Surface).Should().Equal("to", "today", "and", "to", "morrow");
    }
}
