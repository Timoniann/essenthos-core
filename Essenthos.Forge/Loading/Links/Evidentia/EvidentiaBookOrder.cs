using System.Runtime.ExceptionServices;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Computes the books of a run several at a time and hands each to one writer in the order the run
/// names them. The writer is the only code that stores anything, and it sees book <c>n</c> only after
/// every book before it, so the rows it writes and the ids the database gives them are those of a
/// run that computed one book after another.
/// </summary>
internal static class EvidentiaBookOrder
{
    /// <summary>
    /// How many books may be computed or waiting for the writer at once, per worker. A long book holds
    /// the writer while the shorter ones after it finish; this lets the workers run that far ahead of it
    /// and no further, so a slow Genesis cannot leave the whole Pentateuch's decisions in memory.
    /// </summary>
    private const int BooksAheadPerWorker = 2;

    /// <param name="compute">Computes one book on one worker: (worker, book index, token). A worker computes one book at a time.</param>
    /// <param name="write">Stores one computed book: (book index, result, token). Called once per book, in index order, never concurrently.</param>
    public static async Task Run<TResult>(
        int books,
        int workers,
        Func<int, int, CancellationToken, Task<TResult>> compute,
        Func<int, TResult, CancellationToken, Task> write,
        CancellationToken cancellationToken = default)
    {
        workers = Math.Min(workers, books);
        if (workers <= 1)
        {
            for (var book = 0; book < books; book++)
            {
                await write(book, await compute(0, book, cancellationToken), cancellationToken);
            }

            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var ahead = new SemaphoreSlim(workers * BooksAheadPerWorker);
        var results = Enumerable.Range(0, books)
            .Select(_ => new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var next = -1;

        async Task Work(int worker)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await ahead.WaitAsync(stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var book = Interlocked.Increment(ref next);
                if (book >= books)
                {
                    ahead.Release();
                    return;
                }

                try
                {
                    results[book].SetResult(await compute(worker, book, stop.Token));
                }
                catch (Exception exception)
                {
                    results[book].SetException(exception);
                    await stop.CancelAsync();
                    return;
                }
            }
        }

        var running = Enumerable.Range(0, workers).Select(worker => Task.Run(() => Work(worker), CancellationToken.None)).ToArray();
        try
        {
            for (var book = 0; book < books; book++)
            {
                var result = await results[book].Task.WaitAsync(stop.Token);
                await write(book, result, cancellationToken);
                ahead.Release();
            }
        }
        catch (Exception exception)
        {
            await stop.CancelAsync();
            await Task.WhenAll(running);
            // A book that failed stops the others, and they then fail by being cancelled; the failure
            // worth reporting is the first one that was not a cancellation.
            var cause = results
                .Where(result => result.Task.IsFaulted)
                .Select(result => result.Task.Exception!.InnerException!)
                .FirstOrDefault(inner => inner is not OperationCanceledException);
            ExceptionDispatchInfo.Capture(cause ?? exception).Throw();
        }

        await Task.WhenAll(running);
    }
}
