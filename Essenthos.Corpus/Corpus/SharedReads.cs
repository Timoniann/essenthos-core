using System.Collections.Concurrent;

namespace Essenthos.Core.Corpus;

/// <summary>
/// Reads the build makes once rather than once per page, from a corpus that cannot change under it. <see cref="Current"/>
/// holds what the pages of one record or one Strong number read alike in every site language, while the build composes them
/// one after another (<see cref="Once{T}"/>); <see cref="Corpus"/> holds what every page of the run reads alike, a text's own
/// facts, whichever worker asks first (<see cref="Kept{T}"/>). A request sets neither, and every read then goes to the database
/// as it always did. A read held here names in its key everything it reads by, and none names a language: a key that left out
/// an argument would hand one record's, text's or language's answer to another. What is held is shared and must never be
/// changed by whoever takes it.
/// </summary>
internal sealed class SharedReads
{
    /// <summary>Where set, the reads made through <see cref="Once{T}"/> in this flow are made once and taken again from here.</summary>
    public static readonly AsyncLocal<SharedReads?> Current = new();

    /// <summary>Where set, the reads made through <see cref="Kept{T}"/> by every worker of the run are made once.</summary>
    public static readonly AsyncLocal<CorpusReads?> Corpus = new();

    private readonly Dictionary<string, object?> _held = new(StringComparer.Ordinal);

    /// <summary>The answer of <paramref name="read"/>, made once per <paramref name="key"/> where <see cref="Current"/> is set.</summary>
    public static async Task<T> Once<T>(string key, Func<Task<T>> read)
    {
        if (Current.Value is not { } shared)
        {
            return await read();
        }

        if (shared._held.TryGetValue(key, out var held))
        {
            return (T)held!;
        }

        var value = await read();
        shared._held[key] = value;
        return value;
    }

    /// <summary>
    /// The answer of <paramref name="read"/>, made once per <paramref name="key"/> for the whole run where <see cref="Corpus"/>
    /// is set, by the first worker to ask; the others wait for it.
    /// </summary>
    public static async Task<T> Kept<T>(string key, Func<Task<T>> read) =>
        Corpus.Value is { } corpus
            ? (T)(await corpus.Held.GetOrAdd(key, _ => new Lazy<Task<object?>>(async () => await read())).Value)!
            : await read();

    /// <summary>What every page of one run reads alike.</summary>
    internal sealed class CorpusReads
    {
        public ConcurrentDictionary<string, Lazy<Task<object?>>> Held { get; } = new(StringComparer.Ordinal);

        /// <summary>Each verse's plain text as a page quotes it, by the verse's id; null where it has none.</summary>
        public ConcurrentDictionary<int, string?> Quoted { get; } = new();
    }
}
