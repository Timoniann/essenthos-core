using Essenthos.Core.Corpus;
using Essenthos.Core.Pages;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// What the process keeps for the release it serves: made once however many ask at once, made again after a failure, and
/// never stopped by one caller giving up.
/// </summary>
public sealed class PerReleaseTests
{
    [Fact]
    public async Task CallersThatAskTogetherWaitForOneMaking()
    {
        var made = 0;
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var value = new PerRelease<int>(async _ =>
        {
            Interlocked.Increment(ref made);
            return await release.Task;
        });

        var asked = Enumerable.Range(0, 8).Select(_ => value.Get(CancellationToken.None)).ToList();
        release.SetResult(42);

        (await Task.WhenAll(asked)).Should().AllBeEquivalentTo(42);
        (await value.Get(CancellationToken.None)).Should().Be(42);
        made.Should().Be(1);
    }

    [Fact]
    public async Task AFailedMakingIsForgottenAndMadeAgain()
    {
        var made = 0;
        var value = new PerRelease<int>(_ => Interlocked.Increment(ref made) == 1
            ? throw new InvalidOperationException("the database went away")
            : Task.FromResult(7));

        await value.Invoking(v => v.Get(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        (await value.Get(CancellationToken.None)).Should().Be(7);
        made.Should().Be(2);
    }

    [Fact]
    public async Task ACallerThatStopsWaitingLeavesTheMakingToFinishForTheNext()
    {
        var made = 0;
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new PerRelease<string, string>(async (key, _) =>
        {
            Interlocked.Increment(ref made);
            return key + await release.Task;
        });

        using var leaving = new CancellationTokenSource();
        var first = values.Get("a", leaving.Token);
        await leaving.CancelAsync();
        await first.Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();

        var second = values.Get("a", CancellationToken.None);
        release.SetResult("!");
        (await second).Should().Be("a!");
        made.Should().Be(1);
    }

    [Fact]
    public async Task OneKeysMakingNeverHoldsUpAnothers()
    {
        var slow = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new PerRelease<int, string>((key, _) => key == 1 ? slow.Task : Task.FromResult($"text {key}"));

        var waiting = values.Get(1, CancellationToken.None);
        (await values.Get(2, CancellationToken.None)).Should().Be("text 2");
        waiting.IsCompleted.Should().BeFalse();
        slow.SetResult("text 1");
        (await waiting).Should().Be("text 1");
    }

    [Fact]
    public async Task APageAskedForManyTimesAtOnceIsComposedOnce()
    {
        using var cache = new PageCache();
        var composed = 0;
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<string> Ask() => cache.Remember("page:/people/moses", PageCache.Page, async () =>
        {
            Interlocked.Increment(ref composed);
            return await release.Task;
        });

        var asked = Enumerable.Range(0, 6).Select(_ => Ask()).ToList();
        release.SetResult("<html>");

        (await Task.WhenAll(asked)).Should().AllBe("<html>");
        (await Ask()).Should().Be("<html>");
        composed.Should().Be(1);
    }

    [Fact]
    public async Task APageWhoseCompositionFailedIsComposedAgainByTheNextReader()
    {
        using var cache = new PageCache();
        var composed = 0;
        Task<string> Ask() => cache.Remember("page:/strong/H1", PageCache.Page, () => Interlocked.Increment(ref composed) == 1
            ? throw new InvalidOperationException("a statement timed out")
            : Task.FromResult("<html>"));

        await FluentActions.Awaiting(Ask).Should().ThrowAsync<InvalidOperationException>();
        (await Ask()).Should().Be("<html>");
        composed.Should().Be(2);
    }
}
