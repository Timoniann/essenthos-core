using Essenthos.Core.Products;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Conversion;

/// <summary>One difference between a corpus's text and its conversion, by key.</summary>
/// <param name="What">The column, or <c>word</c>, <c>verse</c>, <c>note</c>, <c>book</c> for a row only one side has.</param>
/// <param name="How"><c>differs</c>, <c>added</c> (only the conversion has it), <c>lost</c> (only the corpus has it).</param>
internal sealed record TextDifference(string Slug, string What, string How, string At, string? Corpus, string? Converted)
{
    public static readonly ProductSchema<TextDifference> Schema = new ProductSchema<TextDifference>()
        .Text("text", d => d.Slug)
        .Text("what", d => d.What)
        .Text("how", d => d.How)
        .Text("at", d => d.At)
        .OptionalText("corpus", d => d.Corpus)
        .OptionalText("converted", d => d.Converted)
        .Reads(row => new TextDifference(row.Text(0), row.Text(1), row.Text(2), row.Text(3), row.TextOrNull(4), row.TextOrNull(5)));
}

/// <summary>
/// A conversion held against the corpus it is to reproduce, by natural key: every word column, every
/// verse's place and length, every note and book. What differs is listed, never accepted silently.
/// </summary>
internal static class TextComparison
{
    private static readonly (string Name, Func<TextWord, string?> Get)[] Columns =
    [
        ("surface", w => w.Surface),
        ("trailer", w => w.Trailer),
        ("lemma", w => w.Lemma),
        ("strong", w => w.Strong),
        ("gloss", w => w.Gloss),
        ("morphology", w => w.Morphology),
        ("normalised", w => w.Normalised),
        ("graphical", w => w.Graphical),
        ("elided", w => w.Elided ? "t" : "f"),
        ("break", w => w.Break),
        ("supplied", w => w.Supplied?.ToString()),
        ("restored", w => w.Restored?.ToString()),
        ("supplied_mark", w => w.SuppliedMark),
        ("doubtful", w => w.Doubtful?.ToString()),
        ("brackets", w => w.Brackets?.ToString()),
        ("subscription", w => w.Subscription?.ToString()),
        ("id", w => w.Id?.ToString()),
    ];

    public static List<TextDifference> Compare(TextSnapshot corpus, IReadOnlyList<TextWord> words, IReadOnlyList<VerseManifestRow> verses,
        IReadOnlyList<TextNote> notes, IReadOnlyList<TextBook> books)
    {
        var slug = corpus.Slug;
        var differences = new List<TextDifference>();
        var converted = words.ToDictionary(w => w.Key);
        foreach (var held in corpus.Words)
        {
            if (!converted.Remove(held.Key, out var made))
            {
                differences.Add(new(slug, "word", "lost", held.Key.Own, held.Surface, null));
                continue;
            }

            foreach (var (name, get) in Columns)
            {
                var a = get(held);
                var b = get(made);
                if (a != b)
                {
                    differences.Add(new(slug, name, a is null ? "added" : b is null ? "lost" : "differs", held.Key.Own, a, b));
                }
            }
        }

        differences.AddRange(converted.Values.OrderBy(w => w.Key).Select(w => new TextDifference(slug, "word", "added", w.Key.Own, null, w.Surface)));

        var manifest = verses.ToDictionary(v => v.Own);
        foreach (var held in corpus.Verses.OrderBy(v => v.Own))
        {
            if (!manifest.Remove(held.Own, out var made))
            {
                differences.Add(new(slug, "verse", "lost", held.Own.ToString(), $"{held.Sequence}/{held.Words}", null));
            }
            else if (held.Sequence != made.Sequence || held.Words != made.Words)
            {
                differences.Add(new(slug, "verse", "differs", held.Own.ToString(), $"{held.Sequence}/{held.Words}", $"{made.Sequence}/{made.Words}"));
            }
        }

        differences.AddRange(manifest.Values.OrderBy(v => v.Own)
            .Select(v => new TextDifference(slug, "verse", "added", v.Own.ToString(), null, $"{v.Sequence}/{v.Words}")));

        Rows(slug, "note", corpus.Notes, notes, n => $"{n.Scope} {n.At} #{n.Position}", differences);
        Rows(slug, "book", corpus.Books, books, b => b.Book.ToString(), differences);
        return differences;
    }

    private static void Rows<T>(string slug, string what, IReadOnlyList<T> held, IReadOnlyList<T> made, Func<T, string> at, List<TextDifference> differences)
        where T : notnull
    {
        var remaining = made.GroupBy(at).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var row in held)
        {
            var key = at(row);
            if (remaining.TryGetValue(key, out var candidates) && candidates.Count > 0)
            {
                var same = candidates.FindIndex(c => c.Equals(row));
                var other = same >= 0 ? same : 0;
                if (same < 0)
                {
                    differences.Add(new(slug, what, "differs", key, row.ToString(), candidates[other].ToString()));
                }

                candidates.RemoveAt(other);
            }
            else
            {
                differences.Add(new(slug, what, "lost", key, row.ToString(), null));
            }
        }

        differences.AddRange(remaining.Values.SelectMany(rows => rows).Select(row => new TextDifference(slug, what, "added", at(row), null, row.ToString())));
    }

    public static void Write(string path, IReadOnlyList<TextDifference> differences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = TsvFormat.Instance.Create(path, new ProductHeader("comparison", [], ProductHeader.DescribeProducer("forge texts compare")), TextDifference.Schema);
        writer.WriteAll(differences);
    }

    public static string Summary(string slug, IReadOnlyList<TextDifference> differences) =>
        differences.Count == 0
            ? $"{slug}: 0 differences"
            : $"{slug}: {differences.Count} differences: " + string.Join(", ",
                differences.GroupBy(d => (d.What, d.How)).OrderByDescending(g => g.Count()).Select(g => $"{g.Key.What} {g.Key.How} {g.Count()}"));
}
