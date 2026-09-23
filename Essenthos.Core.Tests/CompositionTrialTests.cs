using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The dry run's arithmetic: what a composition would add to a text, and how often it names the word
/// a source states where one does.
/// </summary>
public class CompositionTrialTests
{
    private static readonly CompositionScope Scope = new(
        new Dictionary<long, (int Book, string Form)>
        {
            [1] = (1, "und"), [2] = (1, "gott"), [3] = (1, "sprach"), [4] = (1, "licht"), [5] = (2, "es"),
        },
        Linked: new HashSet<long> { 2, 3, 5 },
        Kept: new HashSet<long> { 2, 3 },
        Stated: new Dictionary<long, HashSet<long>> { [2] = [20], [3] = [30] },
        Frequent: new HashSet<string> { "und", "es" });

    [Fact]
    public void AWordNothingLinksIsAddedAndAStatedOneIsScored()
    {
        var trial = CompositionTrial.Score(Scope,
        [
            new RoutedLink(1, 10, 0.9, Route.Written | Route.Composed),
            new RoutedLink(2, 20, 0.9, Route.Written | Route.Composed),
            new RoutedLink(3, 31, 0.9, Route.Reduced),
        ], ["KJV"]);

        trial.Added.Should().Be(1);
        trial.Held.Should().Be((2, 1));
        trial.RivalWords.Should().Be(1);
    }

    /// <summary>
    /// A word only this pair's earlier aligner links reach counts as linked before a rerun and not
    /// after it, unless the rerun reaches it again: the rerun replaces those links.
    /// </summary>
    [Fact]
    public void ARerunReplacesTheLinksItWroteBefore()
    {
        var trial = CompositionTrial.Score(Scope, [new RoutedLink(4, 40, 0.9, Route.Written | Route.Composed)], ["KJV"]);

        trial.Words.Should().Be(5);
        trial.Before.Should().Be(3);
        trial.After.Should().Be(3);
    }
}
