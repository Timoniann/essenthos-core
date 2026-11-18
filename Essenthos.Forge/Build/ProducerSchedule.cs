using System.Runtime.ExceptionServices;

namespace Essenthos.Core.Build;

/// <summary>
/// Runs steps by what each needs: a step starts once every step it needs has ended, as many at once as asked, the ready ones in
/// the order given. The first failure starts nothing more; what runs then is waited for, and an exception is thrown again once
/// nothing runs.
/// </summary>
internal static class ProducerSchedule
{
    /// <param name="needs">Every step, in the order ready steps start, with the steps that must end before it.</param>
    /// <param name="atOnce">How many steps may run at the same time.</param>
    /// <param name="run">Runs one step; false when it failed.</param>
    /// <returns>Whether every step ran and none failed.</returns>
    public static async Task<bool> Run(IReadOnlyDictionary<string, IReadOnlySet<string>> needs, int atOnce, Func<string, Task<bool>> run)
    {
        var unknown = needs.SelectMany(step => step.Value).FirstOrDefault(need => !needs.ContainsKey(need));
        if (unknown is not null)
        {
            throw new InvalidOperationException($"A step needs {unknown}, which is not among the steps to run.");
        }

        var order = needs.Keys.ToList();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var started = new HashSet<string>(StringComparer.Ordinal);
        var running = new Dictionary<Task<bool>, string>();
        var failed = false;
        ExceptionDispatchInfo? thrown = null;
        while (true)
        {
            if (!failed)
            {
                foreach (var step in order.Where(step => !started.Contains(step) && needs[step].All(done.Contains)).Take(Math.Max(1, atOnce) - running.Count))
                {
                    started.Add(step);
                    running[Task.Run(() => run(step))] = step;
                }
            }

            if (running.Count == 0)
            {
                break;
            }

            var ended = await Task.WhenAny(running.Keys).ConfigureAwait(false);
            var name = running[ended];
            running.Remove(ended);
            try
            {
                if (await ended.ConfigureAwait(false))
                {
                    done.Add(name);
                }
                else
                {
                    failed = true;
                }
            }
            catch (Exception exception)
            {
                failed = true;
                thrown ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        thrown?.Throw();
        if (!failed && done.Count < order.Count)
        {
            throw new InvalidDataException($"The steps {string.Join(", ", order.Where(step => !done.Contains(step)))} need each other in a cycle.");
        }

        return !failed;
    }
}
