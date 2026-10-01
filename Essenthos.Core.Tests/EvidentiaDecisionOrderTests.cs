using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A run is stored in the order its words are decided in, and its ids follow that order, so two
/// verses a text places at one canonical address are decided in one order however the database
/// happened to return their words.
/// </summary>
public sealed class EvidentiaDecisionOrderTests
{
    private static readonly EvidentiaAddress Shared = new(4, 26, 1);

    [Fact]
    public void WordsOfTwoVersesAtOneAddressAreDecidedInOneOrderWhicheverArrivesFirst()
    {
        var first = new EvidentiaToken(13441502, Shared, 1, "nach", "deu");
        var second = new EvidentiaToken(13441520, Shared, 1, "und", "deu");

        Decided([first, second]).Should().Equal(Decided([second, first]));
        Decided([second, first]).Should().Equal(first.Id, second.Id);
    }

    private static List<long?> Decided(IReadOnlyList<EvidentiaToken> source) =>
    [
        .. EvidentiaDecisionRecorder.Decide(
                1,
                new EvidentiaChapterDecisions(4, 26, source, source.Select(token => token.Id).ToHashSet(), [], [], []))
            .Select(decision => decision.SourceWordId),
    ];
}
