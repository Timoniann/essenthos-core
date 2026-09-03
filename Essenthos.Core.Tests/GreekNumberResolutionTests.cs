using Essenthos.Core.Loading.Links;
using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The corroboration step, on the two cases that decide whether it is worth having: a redirect the
/// dictionary states correctly and one it states wrongly, both perfectly well formed.
/// </summary>
public class GreekNumberResolutionTests
{
    private static readonly GreekEntry[] Dictionary =
    [
        new("G2076", "ἐστί", "third person singular present indicative of G1510;"),
        new("G3450", "μοῦ", "the simpler form of G1700;"),
        new("G1700", "ἐμοῦ", "a prolonged form of G3449;"),
    ];

    private static readonly HashSet<string> Attested = ["G1510", "G3449", "G1473"];

    [Fact]
    public void AdmitsTheRedirectTheVersesBearOut()
    {
        var admitted = GreekNumberResolution.Admit(Dictionary, Attested, Verses("G2076", "G1510", 19, 1));

        admitted["G2076"].Numbers.Should().Equal("G1510");
        admitted["G2076"].Corroborated.Should().BeApproximately(0.95, 0.001);
    }

    /// <summary>
    /// G1700's printed derivation names μόχθος, a noun meaning toil, and the whole first person
    /// singular paradigm chains through it. Nothing about the chain looks wrong; the verses are what
    /// say it is.
    /// </summary>
    [Fact]
    public void RefusesTheRedirectNoVerseBearsOut()
    {
        var admitted = GreekNumberResolution.Admit(Dictionary, Attested, Verses("G3450", "G3449", 0, 20));

        admitted.Should().NotContainKey("G3450");
    }

    /// <summary>
    /// A word whose own number is in its verse is not a failure, so it is no evidence either way.
    /// Counting it would let a number that almost always matches carry a redirect that never does.
    /// </summary>
    [Fact]
    public void IgnoresTheOccurrencesThatDidNotFail()
    {
        IReadOnlySet<string> withItsOwn = new HashSet<string> { "G2076" };
        var occurrences = Enumerable.Repeat(new NumberOccurrence("G2076", withItsOwn), 100)
            .Concat(Verses("G2076", "G1510", 3, 0));

        GreekNumberResolution.Admit(Dictionary, Attested, occurrences)["G2076"]
            .Corroborated.Should().Be(1);
    }

    private static IEnumerable<NumberOccurrence> Verses(string tag, string head, int explained, int not)
    {
        IReadOnlySet<string> withHead = new HashSet<string> { head };
        IReadOnlySet<string> without = new HashSet<string> { "G3056" };

        return Enumerable.Repeat(new NumberOccurrence(tag, withHead), explained)
            .Concat(Enumerable.Repeat(new NumberOccurrence(tag, without), not));
    }
}
