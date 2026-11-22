using System.Collections.Concurrent;

namespace Essenthos.Core.Corpus;

/// <summary>
/// A value of the corpus release being served, made once per process: a new corpus is always a restart. The first caller starts
/// it and every caller meanwhile waits for that one making, so a burst of first requests reads the database once. It is made
/// on nobody's cancellation, since it is everybody's; a caller who stops waiting leaves it to finish for the next. A making
/// that failed is forgotten, and the next caller makes it again.
/// </summary>
internal sealed class PerRelease<T>(Func<CancellationToken, Task<T>> make)
{
    private readonly Lock _lock = new();

    private Task<T>? _value;

    public Task<T> Get(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_value is null || _value.IsFaulted || _value.IsCanceled)
            {
                _value = Task.Run(() => make(CancellationToken.None), CancellationToken.None);
            }

            return _value.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Drops the value, so the next caller makes it again.</summary>
    public void Forget()
    {
        lock (_lock)
        {
            _value = null;
        }
    }
}

/// <summary>
/// Values of the release being served, one per key, each made once per process as <see cref="PerRelease{T}"/> makes its one:
/// concurrent first callers for a key wait for the same making, a making for one key never holds up another's, and a failed
/// making is forgotten.
/// </summary>
internal sealed class PerRelease<TKey, T>(Func<TKey, CancellationToken, Task<T>> make, IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<Task<T>>> _values = new(comparer);

    public async Task<T> Get(TKey key, CancellationToken cancellationToken)
    {
        var value = _values.GetOrAdd(key, k => new Lazy<Task<T>>(() => Task.Run(() => make(k, CancellationToken.None), CancellationToken.None)));
        try
        {
            return await value.Value.WaitAsync(cancellationToken);
        }
        catch when (value.Value.IsFaulted || value.Value.IsCanceled)
        {
            _values.TryRemove(new KeyValuePair<TKey, Lazy<Task<T>>>(key, value));
            throw;
        }
    }
}
