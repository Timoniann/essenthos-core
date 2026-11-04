using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Essenthos.Core.Conversion;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Names;

/// <summary>
/// The projection against a database's names (<c>names/live</c>) by (word key, record), every difference put in a category
/// and a sample of each category written with its verse, for a person to decide which side is right (DOC-0221 §8.4,
/// DOC-0224 §15 step 4). Reads no database.
/// </summary>
internal static class NamesComparison
{
    public sealed record Outcome(long Equal, long OnlyLive, long OnlyProjection, IReadOnlyDictionary<string, long> Categories, TimeSpan Elapsed);

    private sealed record Difference(string Text, string Category, string Own, string Record, string Detail);

    /// <summary>The differences the sample of 2026-11-04 found the database right on, each with the override that keeps it.</summary>
    public const string TranslatorsOwn = "the base verse does not name it, written as a name";

    public const string NoLink = "only-live: no link to any reference text";

    /// <param name="writeOverrides">Write <c>names/overrides/&lt;SLUG&gt;</c> for the categories decided for the database, where a text has none yet.</param>
    public static Outcome Run(NamesProjection projection, IReadOnlyList<string> texts, string outFolder, int perCategory, int parallel, bool writeOverrides = false)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Directory.CreateDirectory(outFolder);
        var counts = new ConcurrentDictionary<(string Text, string Category), long>();
        var samples = new ConcurrentDictionary<string, ConcurrentBag<(ulong Hash, Difference Row, string Context)>>(StringComparer.Ordinal);
        long equal = 0, onlyLive = 0, onlyProjection = 0;
        var baseWords = new ConcurrentDictionary<string, Lazy<Dictionary<string, string>>>(StringComparer.Ordinal);

        Parallel.ForEach(texts, new ParallelOptions { MaxDegreeOfParallelism = parallel }, text =>
        {
            if (!projection.Store.Exists(NameProducts.Live(text)))
            {
                return;
            }

            var result = projection.Project(text, diagnose: true);
            var mine = result.Names.Where(n => NameOrigins.IsOwn(n.Origin)).ToDictionary(n => (n.Own.Own, n.Record), n => n);
            var live = new Dictionary<(string, string), LiveName>();
            var liveByWord = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            using (var reader = projection.Store.Open(NameProducts.Live(text), LiveName.Schema(text)))
            {
                foreach (var row in reader.Rows())
                {
                    live[(row.Own.Own, row.Record)] = row;
                    if (!liveByWord.TryGetValue(row.Own.Own, out var list))
                    {
                        liveByWord[row.Own.Own] = list = [];
                    }

                    list.Add(row.Record);
                }
            }

            var others = NameProducts.IsReference(text) ? [] : OtherReferences(projection, text);
            var words = NameProducts.IsReference(text) ? new Dictionary<string, NamesProjection.Word>() : projection.WordsOf(text);
            var differences = new List<Difference>();
            foreach (var (key, row) in live)
            {
                if (mine.TryGetValue(key, out var same))
                {
                    Interlocked.Increment(ref equal);
                    if (!NameProducts.IsReference(text))
                    {
                        counts.AddOrUpdate((text, $"equal: {(same.Origin != NameOrigins.Projected ? same.Origin : Band(same))}"), 1, (_, n) => n + 1);
                    }

                    continue;
                }

                Interlocked.Increment(ref onlyLive);
                var category = result.Refused.TryGetValue(key, out var rule) ? $"only-live: refused, {rule}"
                    : result.Linked.TryGetValue(key.Item1, out var records)
                        ? (records.Count == 0 ? "only-live: the linked base word names nobody" : "only-live: the linked base word names another")
                          + (others.TryGetValue(key.Item1, out var alsoLinked) && alsoLinked.Contains(key.Item2) ? "; another reference text's linked word names it"
                              : BaseVerseNames(projection, result, key.Item1, key.Item2) ? "; the base names it elsewhere in the verse"
                              : "; the base verse does not name it")
                          + (words.TryGetValue(key.Item1, out var word) ? word.WrittenAsName ? ", written as a name" : word.Cased ? ", in lower case" : ", script without capitals" : "")
                        : others.TryGetValue(key.Item1, out var elsewhere)
                            ? elsewhere.Contains(key.Item2) ? "only-live: no link to the base; another reference text's linked word names it" : "only-live: no link to the base; linked to another reference text"
                            : NoLink;
                differences.Add(new Difference(text, category, key.Item1, key.Item2,
                    $"live {row.Method} {NameProducts.Number(row.Confidence) ?? "-"} {row.Kind} | {row.Source} | {row.Note}"
                    + (result.LinkedTo.TryGetValue(key.Item1, out var to) ? $" | linked {string.Join("; ", to.Take(3))}" : "")));
            }

            foreach (var (key, row) in mine)
            {
                if (live.ContainsKey(key))
                {
                    continue;
                }

                Interlocked.Increment(ref onlyProjection);
                var beside = liveByWord.TryGetValue(key.Item1, out var theirs) ? "beside another live name" : "on a word live leaves bare";
                var band = row.Origin != NameOrigins.Projected ? row.Origin : Band(row);
                differences.Add(new Difference(text, $"only-projection: {beside}, {band}", key.Item1, key.Item2,
                    $"through {row.Through} {row.Method} {NameProducts.Number(row.Confidence) ?? "-"} {row.Role} | live on word: {(theirs is null ? "-" : string.Join(",", theirs))}"));
            }

            if (writeOverrides && !NameProducts.IsReference(text) && !projection.Store.Exists(NameProducts.Overrides(text)))
            {
                WriteOverrides(projection, result, text, differences, live);
            }

            var unlinked = differences.Where(d => d.Category == NoLink).ToList();
            if (unlinked.Count > 0)
            {
                // The names the projection cannot carry because no final links the word to a reference text: the mapping's to fix.
                Directory.CreateDirectory(Path.Combine(outFolder, "no-link"));
                using var writer = new StreamWriter(Path.Combine(outFolder, "no-link", $"{text}.tsv"), false, new UTF8Encoding(false));
                writer.Write("own\trecord\tlive\n");
                foreach (var row in unlinked.OrderBy(d => WordKey.Parse(d.Own, text)).ThenBy(d => d.Record, StringComparer.Ordinal))
                {
                    writer.Write($"{row.Own}\t{row.Record}\t{row.Detail.Replace('\t', ' ')}\n");
                }
            }

            if (differences.Count == 0)
            {
                return;
            }

            foreach (var group in differences.GroupBy(d => d.Category))
            {
                counts[(text, group.Key)] = group.LongCount();
            }

            var picked = differences
                .Select(d => (Hash: Fnv($"{d.Text} {d.Own} {d.Record}"), Row: d))
                .GroupBy(d => d.Row.Category)
                .SelectMany(g => g.OrderBy(d => d.Hash).Take(perCategory))
                .ToList();
            var verses = Verses(projection.Store, text, picked.Select(p => p.Row.Own).ToHashSet(StringComparer.Ordinal));
            foreach (var (hash, row) in picked)
            {
                string Surface(string reference, string own) =>
                    baseWords.GetOrAdd(reference, r => new Lazy<Dictionary<string, string>>(() => Surfaces(projection.Store, r))).Value.GetValueOrDefault(own) ?? "?";
                var baseWord = "";
                if (row.Detail.StartsWith("through ", StringComparison.Ordinal) && row.Detail.Split(' ') is { Length: > 2 } through && through[1].Length > 0)
                {
                    baseWord = Surface(through[1], through[2]);
                }
                else if (row.Detail.IndexOf(" | linked ", StringComparison.Ordinal) is var at && at >= 0)
                {
                    baseWord = string.Join("; ", row.Detail[(at + 10)..].Split("; ").Select(link => link.Split(' ') is { Length: > 1 } parts
                        ? string.Join(" ", parts[1..].Select(own => Surface(parts[0], own)))
                        : link));
                }

                samples.GetOrAdd(row.Category, _ => []).Add((hash, row, $"{verses.GetValueOrDefault(row.Own, "?")}\t{baseWord}"));
            }
        });

        var categories = counts.GroupBy(c => c.Key.Category).ToDictionary(g => g.Key, g => g.Sum(c => c.Value), StringComparer.Ordinal);
        using (var writer = new StreamWriter(Path.Combine(outFolder, "summary.tsv"), false, new UTF8Encoding(false)))
        {
            writer.Write($"text\tcategory\trows\n");
            foreach (var ((text, category), rows) in counts.OrderBy(c => c.Key.Text, StringComparer.Ordinal).ThenBy(c => c.Key.Category, StringComparer.Ordinal))
            {
                writer.Write($"{text}\t{category}\t{rows}\n");
            }
        }

        using (var writer = new StreamWriter(Path.Combine(outFolder, "categories.tsv"), false, new UTF8Encoding(false)))
        {
            writer.Write($"category\trows\n# equal {equal}, only live {onlyLive}, only projection {onlyProjection}\n");
            foreach (var (category, rows) in categories.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                writer.Write($"{category}\t{rows}\n");
            }
        }

        using (var writer = new StreamWriter(Path.Combine(outFolder, "samples.tsv"), false, new UTF8Encoding(false)))
        {
            writer.Write("category\ttext\town\trecord\tdetail\tverse (the word in [ ])\tbase word\n");
            foreach (var (category, rows) in samples.OrderBy(s => s.Key, StringComparer.Ordinal))
            {
                foreach (var (_, row, context) in rows.OrderBy(r => r.Hash).Take(perCategory))
                {
                    writer.Write($"{category}\t{row.Text}\t{row.Own}\t{row.Record}\t{row.Detail.Replace('\t', ' ')}\t{context}\n");
                }
            }
        }

        return new Outcome(equal, onlyLive, onlyProjection, categories, clock.Elapsed);
    }

    /// <summary>
    /// The overrides that keep what the sample found the database right on: a name the translation writes where its base's
    /// verse names nobody of the kind (<c>add</c>). A gap of the base (its linked word names nobody where another reference
    /// text's does) is filled in the base's settled names, not here, and a <c>differs</c> is never written here.
    /// </summary>
    private static void WriteOverrides(NamesProjection projection, NamesProjection.Projection result, string text, List<Difference> differences, Dictionary<(string, string), LiveName> live)
    {
        var rows = new List<NameOverride>();
        foreach (var difference in differences.Where(d => d.Category.StartsWith("only-live: ", StringComparison.Ordinal)))
        {
            var own = WordKey.Parse(difference.Own, text);
            var role = projection.Records.TryGetValue(difference.Record, out var record) ? NameRoles.OfKind(record.Kind) : NameRoles.Name;
            var source = $"the database's names before the rework ({live[(difference.Own, difference.Record)].Source})";
            if (difference.Category.EndsWith(TranslatorsOwn, StringComparison.Ordinal))
            {
                // Where the base's linked word names another record, the sample found a link error as often as a textual difference:
                // those stay a listed difference until a person writes the differs.
                if (difference.Category.Contains("the linked base word names nobody", StringComparison.Ordinal))
                {
                    rows.Add(new NameOverride(own, difference.Record, OverrideActions.Add, null, role, source, "the translation names it where its base's verse does not"));
                }
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        rows.Sort((a, b) => a.Own.CompareTo(b.Own) is var order && order != 0 ? order : string.CompareOrdinal(a.Record, b.Record));
        using var writer = projection.Store.Create(NameProducts.Overrides(text), ProductKind.Hand, NameOverride.Schema(text),
            ProductHeader.DescribeProducer("forge names-compare --write-overrides"), [],
            header => header.Notes.Add(new("edited", "by hand from here on: add, suppress or differs on one word of this text")));
        foreach (var row in rows)
        {
            writer.Write(row);
        }

        writer.Complete();
    }

    /// <summary>A stable hash, so that the same differences give the same sample on every run.</summary>
    private static ulong Fnv(string value)
    {
        var hash = 14695981039346656037UL;
        foreach (var letter in value)
        {
            hash = (hash ^ letter) * 1099511628211UL;
        }

        return hash;
    }

    private static string Band(ProjectedName row) =>
        row.Confidence switch
        {
            null => "link and name stated",
            >= 0.9 => "0.9 and over",
            >= 0.7 => "0.7 to 0.9",
            >= 0.5 => "0.5 to 0.7",
            _ => "under 0.5",
        };

    /// <summary>Whether a word of the base's verse the word is linked to names the record, though not the word linked.</summary>
    private static bool BaseVerseNames(NamesProjection projection, NamesProjection.Projection result, string own, string record)
    {
        if (!result.LinkedTo.TryGetValue(own, out var links))
        {
            return false;
        }

        foreach (var link in links)
        {
            var parts = link.Split(' ');
            var verses = parts[1..].Select(k => k[..k.LastIndexOf('.')]).ToHashSet(StringComparer.Ordinal);
            if (projection.NamedVerses(parts[0]).Any(v => verses.Contains(v.Verse) && v.Record == record))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>For each word of a text, the records the words of reference texts other than its base name over a link.</summary>
    private static Dictionary<string, HashSet<string>> OtherReferences(NamesProjection projection, string text)
    {
        var bases = NameBases.Of(text, projection.Bases);
        var records = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (pair, reference, first) in projection.ReferencePairs(text))
        {
            var settled = projection.SettledOf(reference);
            using var reader = projection.Store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
            foreach (var row in reader.Rows().Where(r => r.Settled))
            {
                var mine = first ? row.From : row.To;
                var theirs = first ? row.To : row.From;
                if (mine.Length == 0 || theirs.Length == 0 || bases.TryGetValue(NamesProjection.BookOf(mine), out var mineBases) && mineBases[0] == reference)
                {
                    continue;
                }

                var named = MappingText.Split(theirs).SelectMany(t => settled.TryGetValue(t, out var n) ? n.Select(x => x.Record) : []).ToList();
                foreach (var key in MappingText.Split(mine))
                {
                    if (!records.TryGetValue(key, out var set))
                    {
                        records[key] = set = new HashSet<string>(StringComparer.Ordinal);
                    }

                    set.UnionWith(named);
                }
            }
        }

        return records;
    }

    /// <summary>The verse of each word asked for, written out with the word in brackets.</summary>
    private static Dictionary<string, string> Verses(ProductStore store, string text, HashSet<string> keys)
    {
        var wanted = keys.Select(k => k[..k.LastIndexOf('.')]).ToHashSet(StringComparer.Ordinal);
        var verses = new Dictionary<string, List<TextWord>>(StringComparer.Ordinal);
        using (var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text)))
        {
            foreach (var word in reader.Rows())
            {
                var verse = word.Key.Verse.ToString();
                if (wanted.Contains(verse))
                {
                    if (!verses.TryGetValue(verse, out var list))
                    {
                        verses[verse] = list = [];
                    }

                    list.Add(word);
                }
            }
        }

        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!verses.TryGetValue(key[..key.LastIndexOf('.')], out var words))
            {
                continue;
            }

            var line = new StringBuilder();
            foreach (var word in words)
            {
                line.Append(word.Key.Own == key ? $"[{word.Surface}]" : word.Surface).Append(word.Trailer.Replace('\t', ' ').Replace('\n', ' '));
            }

            written[key] = line.ToString().Trim();
        }

        return written;
    }

    private static Dictionary<string, string> Surfaces(ProductStore store, string text)
    {
        var surfaces = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        foreach (var word in reader.Rows())
        {
            surfaces[word.Key.Own] = word.Surface;
        }

        return surfaces;
    }

    public static string Format(IReadOnlyDictionary<string, long> categories) =>
        string.Join(Environment.NewLine, categories.OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => $"  {c.Value.ToString("N0", CultureInfo.InvariantCulture),10}  {c.Key}"));
}
