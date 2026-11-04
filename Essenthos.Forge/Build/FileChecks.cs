using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Essenthos.Core.Conversion;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Build;

/// <summary>A check with a right answer, run on the products before a database is touched, and what it found.</summary>
internal sealed record FileCheck(string Name, long Found, IReadOnlyList<string> Examples);

/// <summary>
/// The integrity checks that have a right answer and can be read off the files (DOC-0224 §14): Strong numbers and Greek
/// lemmas on the words, and on every pair's final the keys that name no word, links naming no word, absences with words on
/// the wrong side, two links over the same words, and links that do not begin with exactly one settled claim; relationships
/// of a record with itself on the record files. Each is zero on a sound build. The checks that need the database (verse
/// links, records a reader cannot tell apart, claims on annotations) stay in <c>verify</c>; checks the keys make
/// impossible (a link word of another text, a verse with no canonical reference) are not run.
/// </summary>
internal static partial class FileChecks
{
    private const int Shown = 5;

    public static List<FileCheck> Run(ProductStore store, int parallel)
    {
        var slugs = TextLoad.Slugs(store);
        var strong = new Tally("Strong numbers that are not a letter and digits");
        var lemmas = new Tally("Greek lemmas not in canonical form, which nothing can join");
        var manifests = new ConcurrentDictionary<string, Dictionary<VerseKey, int>>(StringComparer.Ordinal);
        Parallel.ForEach(slugs, new ParallelOptions { MaxDegreeOfParallelism = parallel }, slug =>
        {
            var greek = Language(store, slug) == "grc";
            using (var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug)))
            {
                foreach (var word in reader.Rows())
                {
                    if (word.Strong is { } number && !StrongNumber().IsMatch(number))
                    {
                        strong.Add($"{word.Key} {number}");
                    }

                    if (greek && word.Lemma is { } lemma && !UnicodeNormalization.IsComposed(lemma))
                    {
                        lemmas.Add($"{word.Key} {lemma}");
                    }
                }
            }

            using var verses = store.Open(VerseManifestRow.ProductOf(slug), VerseManifestRow.Schema);
            manifests[slug] = verses.Rows().ToDictionary(v => v.Own, v => v.Words);
        });

        var unknown = new Tally("link keys naming no word of their text");
        var empty = new Tally("links naming no word on either side");
        var sides = new Tally("absences whose relation contradicts the side the words are on");
        var twice = new Tally("links naming the same words as another link, instead of one link with two claims");
        var settled = new Tally("links that do not begin with exactly one settled claim");
        var pairs = MappingProducts.Pairs(store).Where(p => store.Exists(MappingProducts.Of(p, MappingProducts.Final))).ToList();
        Parallel.ForEach(pairs, new ParallelOptions { MaxDegreeOfParallelism = parallel }, pair =>
        {
            var (fromText, toText) = MappingProducts.Split(pair);
            var shapes = new HashSet<(string, string)>();
            long current = long.MinValue;
            using var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
            foreach (var row in reader.Rows())
            {
                if (row.Id == current)
                {
                    if (row.Settled)
                    {
                        settled.Add($"{pair} {row.Id}");
                    }

                    continue;
                }

                current = row.Id;
                if (!row.Settled)
                {
                    settled.Add($"{pair} {row.Id}");
                }

                foreach (var (keys, text) in new[] { (row.From, fromText), (row.To, toText) })
                {
                    foreach (var key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var word = WordKey.Parse(key, text);
                        if (!manifests.TryGetValue(text, out var verses) || !verses.TryGetValue(word.Verse, out var count) || word.Position < 1 || word.Position > count)
                        {
                            unknown.Add($"{pair} {row.Id} {text} {key}");
                        }
                    }
                }

                if (row.From.Length == 0 && row.To.Length == 0)
                {
                    empty.Add($"{pair} {row.Id}");
                }

                if ((row.Relation == "omits" && row.From.Length > 0) || (row.Relation == "expands" && row.To.Length > 0))
                {
                    sides.Add($"{pair} {row.Id} {row.Relation}");
                }

                if (!shapes.Add((row.From, row.To)))
                {
                    twice.Add($"{pair} {row.Id} {row.From} | {row.To}");
                }
            }
        });

        var self = new Tally("relationships of a record with itself");
        if (store.Exists("records/facts/relationships"))
        {
            using var reader = store.Open("records/facts/relationships");
            var from = reader.Columns.ToList().FindIndex(c => c.Name == "from_slug");
            var to = reader.Columns.ToList().FindIndex(c => c.Name == "to_slug");
            var relation = reader.Columns.ToList().FindIndex(c => c.Name == "relation");
            foreach (var row in reader.Rows())
            {
                if (row[from] == row[to])
                {
                    self.Add($"{row[from]} {row[relation]} {row[to]}");
                }
            }
        }

        return [.. new[] { strong, lemmas, unknown, empty, sides, twice, settled, self }.Select(t => t.Result())];
    }

    private static string? Language(ProductStore store, string slug)
    {
        using var reader = store.Open(TextField.ProductOf(slug), TextField.Schema);
        return reader.Rows().FirstOrDefault(f => f.Field == "language")?.Value;
    }

    private sealed class Tally(string name)
    {
        private long _found;
        private readonly ConcurrentQueue<string> _examples = new();

        public void Add(string example)
        {
            if (Interlocked.Increment(ref _found) <= Shown)
            {
                _examples.Enqueue(example);
            }
        }

        public FileCheck Result() => new(name, _found, [.. _examples]);
    }

    [GeneratedRegex("^[GH][0-9]+$")]
    private static partial Regex StrongNumber();
}
