using System.Collections.Concurrent;
using Essenthos.Core.Build;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>Producers run by what they read: each after what it needs, the independent ones at once, nothing more after a failure.</summary>
public sealed class ProducerScheduleTests
{
    private static readonly Dictionary<string, IReadOnlySet<string>> Graph = new()
    {
        ["convert"] = new HashSet<string>(),
        ["frame"] = new HashSet<string> { "convert" },
        ["tables"] = new HashSet<string> { "convert" },
        ["merge"] = new HashSet<string> { "frame", "tables" },
    };

    [Fact]
    public async Task A_producer_starts_after_everything_it_needs_and_the_independent_ones_run_at_once()
    {
        var ended = new ConcurrentBag<string>();
        var frameStarted = new TaskCompletionSource();
        var tablesStarted = new TaskCompletionSource();
        var seen = new ConcurrentDictionary<string, string[]>();

        var passed = await ProducerSchedule.Run(Graph, 4, async step =>
        {
            seen[step] = [.. ended];
            switch (step)
            {
                case "frame":
                    frameStarted.SetResult();
                    await tablesStarted.Task;
                    break;
                case "tables":
                    tablesStarted.SetResult();
                    await frameStarted.Task;
                    break;
            }

            ended.Add(step);
            return true;
        });

        passed.Should().BeTrue();
        seen["frame"].Should().Equal("convert");
        seen["tables"].Should().Equal("convert");
        seen["merge"].Should().BeEquivalentTo(["convert", "frame", "tables"], "frame and tables each waited for the other to start, so they ran at once");
    }

    [Fact]
    public async Task No_more_run_at_once_than_asked()
    {
        var running = 0;
        var most = 0;

        await ProducerSchedule.Run(Graph, 1, step =>
        {
            most = Math.Max(most, Interlocked.Increment(ref running));
            Interlocked.Decrement(ref running);
            return Task.FromResult(true);
        });

        most.Should().Be(1);
    }

    [Fact]
    public async Task A_failure_starts_nothing_that_needs_it_and_what_runs_beside_it_ends()
    {
        var ran = new ConcurrentBag<string>();
        var frameEnded = new TaskCompletionSource();

        var passed = await ProducerSchedule.Run(Graph, 4, async step =>
        {
            ran.Add(step);
            if (step == "tables")
            {
                await frameEnded.Task;
                return false;
            }

            if (step == "frame")
            {
                frameEnded.SetResult();
            }

            return true;
        });

        passed.Should().BeFalse();
        ran.Should().BeEquivalentTo(["convert", "frame", "tables"]);
    }

    [Fact]
    public async Task A_thrown_failure_is_thrown_again_once_nothing_runs()
    {
        var act = () => ProducerSchedule.Run(Graph, 4, step => step == "frame" ? throw new InvalidDataException("frame broke") : Task.FromResult(true));

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("frame broke");
    }
}
