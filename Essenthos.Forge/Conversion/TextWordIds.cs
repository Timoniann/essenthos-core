using Essenthos.Core.Corpus;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Conversion;

/// <summary>A word whose id the conversion gave out or lost, for <c>build/ids/&lt;SLUG&gt;.tsv</c>.</summary>
/// <param name="How"><c>new</c>: a word no earlier corpus held, given an id above the ceiling; <c>gone</c>: an id no word of the conversion keeps.</param>
/// <param name="From">Where a gone id was held: <c>ids</c> (the corpus's export) or <c>previous</c> (an id an earlier conversion gave out).</param>
internal sealed record WordIdChange(string How, string Own, long? Id, string Surface, string? From)
{
    public static readonly ProductSchema<WordIdChange> Schema = new ProductSchema<WordIdChange>()
        .Text("how", c => c.How)
        .Text("own", c => c.Own)
        .OptionalInteger("id", c => c.Id)
        .Text("surface", c => c.Surface)
        .OptionalText("from", c => c.From)
        .Reads(row => new WordIdChange(row.Text(0), row.Text(1), row.IntegerOrNull(2), row.Text(3), row.TextOrNull(4)));
}

/// <summary>How a conversion's words came by their ids.</summary>
/// <param name="Given">Words whose id no corpus export holds: given out by this conversion or an earlier one.</param>
internal sealed record WordIdOutcome(long ByKey, long Shifted, long New, long Gone, bool Allocated, long Given)
{
    /// <summary>The words product's header note that counts <see cref="Given"/>, so a conversion knows whether the previous product holds any.</summary>
    public const string GivenNote = "ids-given";

    public override string ToString() =>
        $"{ByKey} by key, {Shifted} matched across a shift, {New} new{(Allocated || New == 0 ? string.Empty : " without an id (no ceiling: run forge texts ids)")}, {Gone} gone";
}

/// <summary>
/// The highest word id any corpus or conversion has given out, in <c>Essenthos/build/word-id-ceiling</c>: new words take
/// ids above it, so an id is never given twice, whichever text asks and however many convert at once.
/// <c>forge texts ids</c> raises it to the database's highest id; it is never lowered.
/// </summary>
internal static class WordIdCeiling
{
    private static readonly object Gate = new();

    public static string PathOf(string resources) => Path.Combine(resources, ProductStore.EssenthosFolder, "build", "word-id-ceiling");

    public static long? Read(string resources) =>
        File.Exists(PathOf(resources)) ? long.Parse(File.ReadAllText(PathOf(resources)).Trim(), System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>Raises the ceiling to at least <paramref name="value"/>.</summary>
    public static void Raise(string resources, long value) => Update(resources, ceiling => Math.Max(ceiling ?? 0, value));

    /// <summary>The first of <paramref name="count"/> ids nobody holds, now held; null when there is no ceiling to count from.</summary>
    public static long? Take(string resources, int count)
    {
        long? first = null;
        Update(resources, ceiling =>
        {
            if (ceiling is null)
            {
                return null;
            }

            first = ceiling + 1;
            return ceiling + count;
        });
        return first;
    }

    private static void Update(string resources, Func<long?, long?> change)
    {
        var path = PathOf(resources);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        lock (Gate)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    using var reader = new StreamReader(file, leaveOpen: true);
                    var text = reader.ReadToEnd().Trim();
                    var next = change(text.Length == 0 ? null : long.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
                    if (next is { } value)
                    {
                        file.SetLength(0);
                        using var writer = new StreamWriter(file, leaveOpen: true);
                        writer.Write(value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
                    }

                    return;
                }
                catch (IOException) when (attempt < 200)
                {
                    Thread.Sleep(25);
                }
            }
        }
    }
}

/// <summary>
/// Gives each converted word the id it held, so that an id names the same word from one release to the next.
/// A verse whose words read as they did takes its ids by position. A verse a correction or a new source changed
/// is matched word by word against what the corpus held there (<c>texts/&lt;SLUG&gt;/ids</c>): the longest common
/// run of folded forms keeps its ids, and between two kept words an equal number of words on each side pairs in
/// order (a word whose letters were put right is still the same word). A word left over is held against the ids an
/// earlier conversion gave out (the previous words product), and only then takes a new id above the ceiling.
/// An id no word keeps is gone, and listed.
/// </summary>
internal static class TextWordIds
{
    private sealed record Held(int Position, long Id, string Surface);

    public static string ReportOf(string resources, string slug) =>
        Path.Combine(resources, ProductStore.EssenthosFolder, "build", "ids", $"{slug}.tsv");

    /// <summary>Each word's id, by position within its verse, in the text's verse order; null where no id could be given.</summary>
    public static (Dictionary<VerseKey, long?[]> Ids, WordIdOutcome Outcome) Assign(ProductStore store, ConvertedText text)
    {
        var slug = text.Slug;
        var language = text.Definition.Language;
        var corpus = ReadIds(store, slug);
        var corpusIds = corpus.Values.SelectMany(verse => verse).Select(word => word.Id).ToHashSet();
        var previous = ReadPrevious(store, slug, corpusIds);

        var ids = new Dictionary<VerseKey, long?[]>(text.Verses.Count);
        var changes = new List<WordIdChange>();
        var pending = new List<(VerseKey Verse, int At)>();
        long byKey = 0, shifted = 0, reusedEarlier = 0;

        foreach (var verse in text.Verses)
        {
            var surfaces = verse.Words.Select(word => word.Surface).ToList();
            var given = new long?[surfaces.Count];
            ids[verse.Key] = given;
            var held = corpus.GetValueOrDefault(verse.Key) ?? [];
            var earlier = previous.GetValueOrDefault(verse.Key) ?? [];

            if (held.Count == surfaces.Count && held.Select(h => h.Surface).SequenceEqual(surfaces, StringComparer.Ordinal))
            {
                for (var at = 0; at < given.Length; at++)
                {
                    given[at] = held[at].Id;
                }

                byKey += given.Length;
                changes.AddRange(earlier.Select(e => Gone(verse.Key, e, "previous")));
                continue;
            }

            var folded = surfaces.Select(surface => WordFolding.Fold(surface, language)).ToList();
            var pairs = Match([.. held.Select(h => WordFolding.Fold(h.Surface, language))], folded);
            var kept = new bool[held.Count];
            for (var at = 0; at < pairs.Length; at++)
            {
                if (pairs[at] >= 0)
                {
                    given[at] = held[pairs[at]].Id;
                    kept[pairs[at]] = true;
                    if (pairs[at] == at)
                    {
                        byKey++;
                    }
                    else
                    {
                        shifted++;
                    }
                }
            }

            for (var at = 0; at < held.Count; at++)
            {
                if (!kept[at])
                {
                    changes.Add(Gone(verse.Key, held[at], "ids"));
                }
            }

            var open = Enumerable.Range(0, given.Length).Where(at => given[at] is null).ToList();
            var reused = new bool[earlier.Count];
            if (open.Count > 0 && earlier.Count > 0)
            {
                var again = Match([.. earlier.Select(e => WordFolding.Fold(e.Surface, language))], [.. open.Select(at => folded[at])]);
                for (var k = 0; k < again.Length; k++)
                {
                    if (again[k] >= 0)
                    {
                        given[open[k]] = earlier[again[k]].Id;
                        reused[again[k]] = true;
                        shifted++;
                        reusedEarlier++;
                    }
                }
            }

            for (var at = 0; at < earlier.Count; at++)
            {
                if (!reused[at])
                {
                    changes.Add(Gone(verse.Key, earlier[at], "previous"));
                }
            }

            pending.AddRange(Enumerable.Range(0, given.Length).Where(at => given[at] is null).Select(at => (verse.Key, at)));
        }

        foreach (var (verse, earlier) in previous.Where(entry => !ids.ContainsKey(entry.Key)))
        {
            changes.AddRange(earlier.Select(e => Gone(verse, e, "previous")));
        }

        foreach (var (verse, held) in corpus.Where(entry => !ids.ContainsKey(entry.Key)))
        {
            changes.AddRange(held.Select(h => Gone(verse, h, "ids")));
        }

        var allocated = false;
        if (pending.Count > 0 && WordIdCeiling.Take(store.ResourcesPath, pending.Count) is { } first)
        {
            allocated = true;
            foreach (var (verse, at) in pending)
            {
                ids[verse][at] = first++;
            }
        }

        var surfacesOf = text.Verses.ToDictionary(v => v.Key);
        var listed = corpus.Count > 0 || previous.Count > 0;
        changes.AddRange(pending.Where(_ => listed).Select(p => new WordIdChange("new", Own(p.Verse, p.At), ids[p.Verse][p.At], surfacesOf[p.Verse].Words[p.At].Surface, null)));
        Report(store.ResourcesPath, slug, changes);
        return (ids, new WordIdOutcome(byKey, shifted, pending.Count, changes.Count(c => c.How == "gone"), allocated,
            reusedEarlier + (allocated ? pending.Count : 0)));
    }

    private static string Own(VerseKey verse, int at) => $"{verse}.{at + 1}";

    private static WordIdChange Gone(VerseKey verse, Held held, string from) => new("gone", $"{verse}.{held.Position}", held.Id, held.Surface, from);

    /// <summary>
    /// Pairs the words of a verse as it was held (<paramref name="before"/>) with the words as converted (<paramref name="after"/>),
    /// both folded: the longest common subsequence pairs equal words; between two pairs, or before the first and after the last,
    /// runs of equal length on both sides pair in order. Returns, for each word after, the index of its word before or -1.
    /// </summary>
    public static int[] Match(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var n = before.Count;
        var m = after.Count;
        var pairs = new int[m];
        Array.Fill(pairs, -1);
        var longest = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                longest[i, j] = string.Equals(before[i], after[j], StringComparison.Ordinal)
                    ? longest[i + 1, j + 1] + 1
                    : Math.Max(longest[i + 1, j], longest[i, j + 1]);
            }
        }

        int x = 0, y = 0, fromX = 0, fromY = 0;
        while (x < n && y < m)
        {
            if (string.Equals(before[x], after[y], StringComparison.Ordinal))
            {
                PairInOrder(pairs, fromX, x, fromY, y);
                pairs[y] = x;
                fromX = ++x;
                fromY = ++y;
            }
            else if (longest[x + 1, y] >= longest[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        PairInOrder(pairs, fromX, n, fromY, m);
        return pairs;
    }

    private static void PairInOrder(int[] pairs, int fromX, int toX, int fromY, int toY)
    {
        if (toX - fromX != toY - fromY)
        {
            return;
        }

        for (var k = 0; k < toY - fromY; k++)
        {
            pairs[fromY + k] = fromX + k;
        }
    }

    private static Dictionary<VerseKey, List<Held>> ReadIds(ProductStore store, string slug)
    {
        var verses = new Dictionary<VerseKey, List<Held>>();
        if (!store.Exists(TextWordId.ProductOf(slug)))
        {
            return verses;
        }

        using var reader = store.Open(TextWordId.ProductOf(slug), TextWordId.Schema(slug));
        foreach (var row in reader.Rows())
        {
            Add(verses, row.Key, new Held(row.Key.Position, row.Id, row.Surface));
        }

        return verses;
    }

    /// <summary>
    /// The ids an earlier conversion gave out that the corpus's export does not hold, by verse, from the words product it wrote;
    /// its rows are read only when its header says it holds any.
    /// </summary>
    private static Dictionary<VerseKey, List<Held>> ReadPrevious(ProductStore store, string slug, HashSet<long> corpusIds)
    {
        var verses = new Dictionary<VerseKey, List<Held>>();
        if (!store.Exists(TextWord.ProductOf(slug)))
        {
            return verses;
        }

        try
        {
            using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
            if (reader.Header?.Notes.LastOrDefault(note => note.Key == WordIdOutcome.GivenNote).Value is not { } given || given == "0")
            {
                return verses;
            }

            foreach (var word in reader.Rows())
            {
                if (word.Id is { } id && !corpusIds.Contains(id))
                {
                    Add(verses, word.Key, new Held(word.Key.Position, id, word.Surface));
                }
            }
        }
        catch (InvalidDataException)
        {
            // A words product of another shape gave out no id this one can read; it is rewritten below.
            verses.Clear();
        }

        return verses;
    }

    private static void Add(Dictionary<VerseKey, List<Held>> verses, WordKey key, Held held)
    {
        if (!verses.TryGetValue(key.Verse, out var list))
        {
            verses[key.Verse] = list = [];
        }

        list.Add(held);
    }

    private static void Report(string resources, string slug, List<WordIdChange> changes)
    {
        var path = ReportOf(resources, slug);
        if (changes.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = TsvFormat.Instance.Create(path, new ProductHeader($"Essenthos/build/ids/{slug}.tsv", [], ProductHeader.DescribeProducer($"forge convert {slug}")),
            WordIdChange.Schema);
        writer.WriteAll(changes.OrderBy(c => c.How).ThenBy(c => c.Own, StringComparer.Ordinal));
    }
}
