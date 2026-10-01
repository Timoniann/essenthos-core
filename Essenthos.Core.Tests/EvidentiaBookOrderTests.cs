using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A stored run computes its books several at a time and must still store exactly what a run of one
/// book after another stores, ids included. That holds only if the writer sees the books in order
/// however they finish, one at a time, and these make them finish out of order on purpose.
/// </summary>
public sealed class EvidentiaBookOrderTests
{
    [Fact]
    public async Task BooksThatFinishOutOfOrderAreWrittenInOrderOneAtATime()
    {
        const int books = 24;
        var random = new Random(915);
        var delays = Enumerable.Range(0, books).Select(_ => random.Next(0, 40)).ToArray();
        delays[0] = 150;
        var written = new List<(int Book, string Result)>();
        int computing = 0, mostComputing = 0, writing = 0;

        await EvidentiaBookOrder.Run(books, 4,
            async (_, book, token) =>
            {
                var now = Interlocked.Increment(ref computing);
                InterlockedMax(ref mostComputing, now);
                await Task.Delay(delays[book], token);
                Interlocked.Decrement(ref computing);
                return $"book {book}";
            },
            async (book, result, _) =>
            {
                Interlocked.Increment(ref writing).Should().Be(1, "the writer is one writer");
                await Task.Yield();
                written.Add((book, result));
                Interlocked.Decrement(ref writing);
            });

        written.Should().Equal(Enumerable.Range(0, books).Select(book => (book, $"book {book}")));
        mostComputing.Should().BeLessThanOrEqualTo(4);
        mostComputing.Should().BeGreaterThan(1, "the books were computed side by side");
    }

    [Fact]
    public async Task OneWorkerComputesAndWritesEachBookInTurn()
    {
        var steps = new List<string>();

        await EvidentiaBookOrder.Run(3, 1,
            (_, book, _) =>
            {
                steps.Add($"compute {book}");
                return Task.FromResult(book);
            },
            (book, _, _) =>
            {
                steps.Add($"write {book}");
                return Task.CompletedTask;
            });

        steps.Should().Equal("compute 0", "write 0", "compute 1", "write 1", "compute 2", "write 2");
    }

    [Fact]
    public async Task ASlowFirstBookHoldsTheWorkersAFixedDistanceAhead()
    {
        var first = new TaskCompletionSource();
        var started = 0;
        var running = EvidentiaBookOrder.Run(40, 4,
            async (_, book, _) =>
            {
                Interlocked.Increment(ref started);
                if (book == 0)
                {
                    await first.Task;
                }

                return book;
            },
            (_, _, _) => Task.CompletedTask);

        await Task.Delay(300);
        var whileHeld = Volatile.Read(ref started);
        first.SetResult();
        await running;

        whileHeld.Should().Be(8, "four workers run two books each ahead of the writer and no further");
        started.Should().Be(40);
    }

    [Fact]
    public async Task AFailedBookIsReportedAsItselfAndNothingAfterItIsWritten()
    {
        var written = new List<int>();

        var run = () => EvidentiaBookOrder.Run(12, 4,
            async (_, book, token) =>
            {
                await Task.Delay(book == 5 ? 10 : 30, token);
                return book == 5 ? throw new InvalidDataException("book 5 is unreadable") : book;
            },
            (book, _, _) =>
            {
                written.Add(book);
                return Task.CompletedTask;
            });

        await run.Should().ThrowAsync<InvalidDataException>().WithMessage("book 5 is unreadable");
        written.Should().OnlyContain(book => book < 5);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
