using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;

namespace Essenthos.Core.Desk;

/// <param name="Name">The text's English name, as its declaration in the build gives it.</param>
/// <param name="Testaments">The text's words in each testament it holds.</param>
/// <param name="Strong">Where the Strong numbers on its words come from, if it has any of its own.</param>
/// <param name="Reach">How its words reach each original it has a mapping to.</param>
internal sealed record TextMapping(
    string Text,
    string Name,
    string? NameNative,
    string Kind,
    string Language,
    IReadOnlyList<TestamentWords> Testaments,
    StrongSource Strong,
    IReadOnlyList<OriginalReach> Reach);

/// <param name="Testament"><c>OT</c> (books 1-39) or <c>NT</c> (books 40-66).</param>
/// <param name="Words">Words the edition prints and does not mark as supplied: the words a link could name.</param>
/// <param name="Supplied">Words the edition marks as the translators' own (italics, brackets), which have no counterpart.</param>
/// <param name="Strong">Of <paramref name="Words"/>, those carrying a Strong number on the word itself.</param>
/// <param name="ThroughLinks">Of <paramref name="Words"/>, those without a number of their own that a link names to an original of the testament, so they have one through it.</param>
internal sealed record TestamentWords(string Testament, int Words, int Supplied, int Strong, int ThroughLinks);

/// <param name="Kind"><c>own</c> (the edition tags its words), <c>laid</c> (laid on from a numbered edition of the same translation) or <c>none</c>.</param>
/// <param name="Served">For laid numbers, whether the numbering's terms let the site show them; null otherwise.</param>
/// <param name="Note">What the conversion said about where the numbers come from.</param>
internal sealed record StrongSource(string Kind, bool? Served, string? Note);

/// <param name="Original">The original's identifier: BHSA, NESTLE1904 or TR1894.</param>
/// <param name="Words">The text's words in the testament the original holds, which could be linked.</param>
/// <param name="Linked">Of those, the words a link of the final mapping names.</param>
/// <param name="Agreed">Of the linked words, those whose link two or more independent witnesses give: two methods, or two datasets stating it.</param>
/// <param name="Reached">Words of the original that a link names.</param>
/// <param name="OriginalWords">The original's words in the testament.</param>
/// <param name="Methods">The linked words by the method whose claim settles each word's link, most first.</param>
/// <param name="Bands">The linked words by how sure the settling claim is: <c>stated</c>, <c>0.9</c>, <c>0.7</c>, <c>0.5</c>, <c>low</c>.</param>
internal sealed record OriginalReach(
    string Original,
    string Testament,
    int Words,
    int Linked,
    int Agreed,
    int Reached,
    int OriginalWords,
    IReadOnlyList<MethodShare> Methods,
    IReadOnlyList<KeyCount> Bands);

/// <param name="Sources">Whose statement or which route the method's links rest on, most first.</param>
internal sealed record MethodShare(string Method, int Words, IReadOnlyList<KeyCount> Sources);

/// <param name="Key">What the files read were when counted: their names, recorded hashes, sizes and write times.</param>
internal sealed record MappingSnapshot(string Key, string At, double Seconds, IReadOnlyList<TextMapping> Texts);

/// <summary>
/// How every text of the build store reaches the originals, counted from the files alone: each text's
/// words and the final mapping of every pair to BHSA, NESTLE1904 and TR1894. A word is linked when a
/// link of the final names it on a side whose other side is not empty. Where a word is in several
/// links, the one most methods agree on counts, then the surest. Reading the whole store takes tens of
/// seconds, which is why the console keeps what it counted and counts again only when a file moved.
/// </summary>
internal static class MappingCount
{
    public const string Ot = "OT";

    public const string Nt = "NT";

    /// <summary>The originals the page measures every text against, with the testament each holds.</summary>
    public static readonly (string Slug, string Testament)[] Originals = [("BHSA", Ot), ("NESTLE1904", Nt), ("TR1894", Nt)];

    /// <summary>Bumped when what is counted changes, so a kept count made by older code is not trusted.</summary>
    private const string Version = "3";

    private const int Parallelism = 4;

    private static readonly string[] BandKeys = ["low", "0.5", "0.7", "0.9", "stated"];

    private static readonly Regex Through = new(@"through (\S+?)(?=[ ;,]|$)", RegexOptions.CultureInvariant);

    /// <summary>
    /// What the count depends on, read without opening any file: each words, declaration and final file's
    /// name, the hash the build recorded for it, its size and its write time.
    /// </summary>
    public static string Key(string essenthos)
    {
        var manifest = Path.Combine(essenthos, "build", "manifest.tsv");
        var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(manifest))
        {
            foreach (var line in File.ReadLines(manifest))
            {
                var tab = line.IndexOf('\t');
                if (tab > 0 && !line.StartsWith('#'))
                {
                    var next = line.IndexOf('\t', tab + 1);
                    recorded[line[..tab]] = next > tab ? line[(tab + 1)..next] : line[(tab + 1)..];
                }
            }
        }

        var lines = new List<string> { Version };
        foreach (var file in Inputs(essenthos))
        {
            var info = new FileInfo(file);
            var relative = "Essenthos/" + Path.GetRelativePath(essenthos, file).Replace('\\', '/');
            lines.Add($"{relative}\t{recorded.GetValueOrDefault(relative)}\t{info.Length}\t{info.LastWriteTimeUtc.Ticks}");
        }

        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    public static MappingSnapshot Count(string essenthos, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var key = Key(essenthos);
        var store = new ProductStore(Path.GetDirectoryName(essenthos)!);
        var slugs = TextSlugs(essenthos);
        var originals = Originals
            .Where(o => slugs.Contains(o.Slug))
            .ToDictionary(o => o.Slug, o => Words.Read(WordsPath(essenthos, o.Slug)), StringComparer.Ordinal);

        var counted = new ConcurrentBag<TextMapping>();
        Parallel.ForEach(
            slugs,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = cancellationToken },
            slug => counted.Add(CountText(essenthos, store, slug, originals.GetValueOrDefault(slug), originals)));

        return new MappingSnapshot(
            key,
            JsonFiles.Now(),
            Math.Round(clock.Elapsed.TotalSeconds, 1),
            [.. counted.OrderBy(t => t.Text, StringComparer.Ordinal)]);
    }

    /// <summary>The name a source goes by on the page: the dataset for a statement, the middle texts for a composition.</summary>
    public static string SourceName(string method, string source)
    {
        if (source.StartsWith("SIL.Machine", StringComparison.Ordinal))
        {
            var middles = Through.Matches(source).Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToList();
            return middles.Count == 0 ? "direct" : "via:" + string.Join('+', middles);
        }

        (string Marker, string Name)[] known =
        [
            ("Clear Bible Alignments", "Clear"),
            ("Berean", "Berean"),
            ("Door43", "Door43"),
            ("door43", "Door43"),
            ("unfoldingWord", "Door43"),
            ("BSOJ", "Door43"),
            ("Open Hebrew Bible", "OHB"),
            ("TAHOT", "TAHOT"),
            ("KJV2003", "KJV2003"),
            ("Zefania", "Zefania KJV+"),
            ("byztxt", "byztxt"),
            ("EVIDENTIA", "EVIDENTIA"),
        ];
        foreach (var (marker, name) in known)
        {
            if (source.Contains(marker, StringComparison.Ordinal))
            {
                return name;
            }
        }

        switch (method)
        {
            case "strong":
                return source.Contains(" carries", StringComparison.Ordinal) ? "own" : "laid";
            case "ruling" or "manual":
                return "owner";
            case "names" or "possessive" or "rule":
                return method;
        }

        var end = source.IndexOfAny([',', ';', ':']);
        var first = (end > 0 ? source[..end] : source).Trim();
        return first.Length <= 48 ? first : first[..47] + "…";
    }

    public static string Testament(int book) => book is >= 1 and <= 39 ? Ot : book is >= 40 and <= 66 ? Nt : "";

    private static IEnumerable<string> Inputs(string essenthos)
    {
        foreach (var slug in TextSlugs(essenthos))
        {
            yield return WordsPath(essenthos, slug);
            if (DeclarationPath(essenthos, slug) is { } declaration)
            {
                yield return declaration;
            }

            foreach (var (original, _) in Originals)
            {
                var final = FinalPath(essenthos, slug, original);
                if (slug != original && File.Exists(final))
                {
                    yield return final;
                }
            }
        }
    }

    private static List<string> TextSlugs(string essenthos)
    {
        var texts = Path.Combine(essenthos, "texts");
        return !Directory.Exists(texts)
            ? []
            : [.. Directory.EnumerateDirectories(texts)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(slug => File.Exists(WordsPath(essenthos, slug)))
                .Order(StringComparer.Ordinal)];
    }

    private static string WordsPath(string essenthos, string slug) => Path.Combine(essenthos, "texts", slug, "words" + EssxLayout.Extension);

    private static string FinalPath(string essenthos, string slug, string original) =>
        Path.Combine(essenthos, "mapping", $"{slug}-{original}", "final" + EssxLayout.Extension);

    private static string? DeclarationPath(string essenthos, string slug) =>
        new[] { ".tsv", EssxLayout.Extension }.Select(extension => Path.Combine(essenthos, "texts", slug, "text" + extension)).FirstOrDefault(File.Exists);

    private static TextMapping CountText(
        string essenthos, ProductStore store, string slug, Words? own, IReadOnlyDictionary<string, Words> originals)
    {
        var words = own ?? Words.Read(WordsPath(essenthos, slug));
        var declaration = Declaration(store, slug);
        var throughLinks = new bool[words.Count];
        var reach = new List<OriginalReach>();
        foreach (var (original, testament) in Originals)
        {
            var final = FinalPath(essenthos, slug, original);
            if (original == slug || !File.Exists(final) || !originals.TryGetValue(original, out var other))
            {
                continue;
            }

            reach.Add(Reach(final, original, testament, words, other, throughLinks));
        }

        var testaments = new List<TestamentWords>();
        foreach (var testament in new[] { Ot, Nt })
        {
            int printed = 0, supplied = 0, strong = 0, through = 0;
            for (var i = 0; i < words.Count; i++)
            {
                if (words.Testament[i] != testament)
                {
                    continue;
                }

                if (words.Supplied[i])
                {
                    supplied++;
                    continue;
                }

                if (!words.Linkable[i])
                {
                    continue;
                }

                printed++;
                if (words.Strong[i])
                {
                    strong++;
                }
                else if (throughLinks[i])
                {
                    through++;
                }
            }

            if (printed + supplied > 0)
            {
                testaments.Add(new TestamentWords(testament, printed, supplied, strong, through));
            }
        }

        return new TextMapping(
            slug,
            declaration.GetValueOrDefault("name") ?? slug,
            declaration.GetValueOrDefault("name-native"),
            declaration.GetValueOrDefault("kind") ?? "",
            declaration.GetValueOrDefault("language") ?? "",
            testaments,
            words.StrongSource,
            reach);
    }

    private static Dictionary<string, string?> Declaration(ProductStore store, string slug)
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
        var product = $"texts/{slug}/text";
        if (!store.Exists(product))
        {
            return fields;
        }

        using var reader = store.Open(product);
        foreach (var row in reader.Rows())
        {
            if (row.Length >= 2 && row[0] is { } field)
            {
                fields.TryAdd(field, row[1]);
            }
        }

        return fields;
    }

    /// <summary>One pair's final mapping, word by word: the link each word of the text is counted under, and what it says.</summary>
    private static OriginalReach Reach(string path, string original, string testament, Words words, Words other, bool[] throughLinks)
    {
        // Per word of the text: the best link's rank (methods agreeing, then band) and what settled it.
        var rank = new int[words.Count];
        var settledMethod = new int[words.Count];
        var settledSource = new int[words.Count];
        var methods = new List<string>();
        var sources = new List<(int Method, string Name)>();
        var methodIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var sourceIds = new Dictionary<(int, string), int>();
        var reached = new bool[other.Count];
        var lookup = words.Index.GetAlternateLookup<ReadOnlySpan<char>>();
        var otherLookup = other.Index.GetAlternateLookup<ReadOnlySpan<char>>();

        var link = new LinkBuffer();
        using var file = EssxReader.Open(path);
        var table = file.Tables[0];
        string[] columns = ["id", "from", "to", "settled", "method", "confidence", "source"];
        for (var g = 0; g < table.RowGroups; g++)
        {
            var group = table.ReadGroup(g, columns);
            var ids = group.Column("id");
            var from = group.Column("from");
            var to = group.Column("to");
            var settled = group.Column("settled");
            var method = group.Column("method");
            var confidence = group.Column("confidence");
            var source = group.Column("source");
            for (var row = 0; row < group.Rows; row++)
            {
                var id = ids.Integer(row) ?? 0;
                if (id != link.Id)
                {
                    Flush();
                    link.Start(id);
                }

                var name = Method(method.Text(row) ?? "");

                // Two datasets stating the same link are two witnesses, as two methods arriving at it are.
                link.Witnesses.Add(name == "stated" ? "stated:" + SourceName(name, source.Text(row) ?? "") : name);
                if (link.From is null)
                {
                    link.From = from.Text(row);
                    link.To = to.Text(row);
                }

                if (settled.Boolean(row) == true)
                {
                    var methodId = Id(methodIds, methods, name);
                    link.SettledMethod = methodId;
                    link.Band = Band(confidence.Text(row));
                    var sourceName = SourceName(name, source.Text(row) ?? "");
                    if (!sourceIds.TryGetValue((methodId, sourceName), out var sourceId))
                    {
                        sourceIds[(methodId, sourceName)] = sourceId = sources.Count;
                        sources.Add((methodId, sourceName));
                    }

                    link.SettledSource = sourceId;
                }
            }
        }

        Flush();

        var linked = 0;
        var agreed = 0;
        var possible = 0;
        var byMethod = new int[methods.Count];
        var bySource = new int[sources.Count];
        var bands = new int[BandKeys.Length];
        for (var i = 0; i < words.Count; i++)
        {
            if (words.Testament[i] != testament || !words.Linkable[i])
            {
                continue;
            }

            possible++;
            if (rank[i] == 0)
            {
                continue;
            }

            linked++;
            throughLinks[i] = true;
            if (rank[i] >> 3 >= 2)
            {
                agreed++;
            }

            bands[(rank[i] & 7) - 1]++;
            byMethod[settledMethod[i]]++;
            bySource[settledSource[i]]++;
        }

        var shares = methods
            .Select((name, id) => new MethodShare(
                name,
                byMethod[id],
                [.. sources.Select((s, sid) => (s, sid))
                    .Where(x => x.s.Method == id && bySource[x.sid] > 0)
                    .Select(x => new KeyCount(x.s.Name, bySource[x.sid]))
                    .OrderByDescending(k => k.Count)
                    .ThenBy(k => k.Key, StringComparer.Ordinal)]))
            .Where(m => m.Words > 0)
            .OrderByDescending(m => m.Words)
            .ThenBy(m => m.Method, StringComparer.Ordinal)
            .ToList();

        var originalWords = 0;
        var originalReached = 0;
        for (var i = 0; i < other.Count; i++)
        {
            if (other.Testament[i] == testament && other.Linkable[i])
            {
                originalWords++;
                if (reached[i])
                {
                    originalReached++;
                }
            }
        }

        return new OriginalReach(
            original,
            testament,
            possible,
            linked,
            agreed,
            originalReached,
            originalWords,
            shares,
            [.. BandKeys.Select((key, b) => new KeyCount(key, bands[b])).Reverse()]);

        void Flush()
        {
            if (link.From is not { Length: > 0 } fromKeys || link.To is not { Length: > 0 } toKeys || link.SettledMethod < 0)
            {
                return;
            }

            var score = (Math.Min(link.Witnesses.Count, 15) << 3) | link.Band;
            foreach (var range in fromKeys.AsSpan().Split(' '))
            {
                if (lookup.TryGetValue(fromKeys.AsSpan()[range], out var word) && score > rank[word])
                {
                    rank[word] = score;
                    settledMethod[word] = link.SettledMethod;
                    settledSource[word] = link.SettledSource;
                }
            }

            foreach (var range in toKeys.AsSpan().Split(' '))
            {
                if (otherLookup.TryGetValue(toKeys.AsSpan()[range], out var word))
                {
                    reached[word] = true;
                }
            }
        }
    }

    /// <summary>The names pass and names-first place the same kind of link; they count as one method.</summary>
    private static string Method(string method) => method == "name-list" ? "names" : method;

    /// <summary>1 under 0.5, 2 from 0.5, 3 from 0.7, 4 from 0.9, 5 where a source states it or a person ruled it.</summary>
    private static int Band(string? confidence)
    {
        if (confidence is null || !double.TryParse(confidence, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 5;
        }

        return value >= 0.9 ? 4 : value >= 0.7 ? 3 : value >= 0.5 ? 2 : 1;
    }

    private static int Id(Dictionary<string, int> ids, List<string> names, string name)
    {
        if (!ids.TryGetValue(name, out var id))
        {
            ids[name] = id = names.Count;
            names.Add(name);
        }

        return id;
    }

    private sealed class LinkBuffer
    {
        public long Id { get; private set; } = long.MinValue;

        public string? From { get; set; }

        public string? To { get; set; }

        public HashSet<string> Witnesses { get; } = new(StringComparer.Ordinal);

        public int SettledMethod { get; set; } = -1;

        public int SettledSource { get; set; }

        public int Band { get; set; }

        public void Start(long id)
        {
            Id = id;
            From = null;
            To = null;
            Witnesses.Clear();
            SettledMethod = -1;
            SettledSource = 0;
            Band = 0;
        }
    }

    /// <summary>A text's words as the count needs them: each word's own key, testament, and what it carries.</summary>
    private sealed class Words
    {
        private Words(int count)
        {
            Index = new Dictionary<string, int>(count, StringComparer.Ordinal);
            Testament = new string[count];
            Linkable = new bool[count];
            Supplied = new bool[count];
            Strong = new bool[count];
        }

        public int Count => Testament.Length;

        public Dictionary<string, int> Index { get; }

        public string[] Testament { get; }

        /// <summary>Printed, not elided and not supplied.</summary>
        public bool[] Linkable { get; }

        public bool[] Supplied { get; }

        public bool[] Strong { get; }

        public StrongSource StrongSource { get; private set; } = new("none", null, null);

        public static Words Read(string path)
        {
            using var file = EssxReader.Open(path);
            var table = file.Tables[0];
            var words = new Words((int)table.RowCount);
            var names = table.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            var wanted = new[] { "own", "elided", "supplied", "strong" }.Where(names.Contains).ToArray();
            var at = 0;
            var tagged = false;
            for (var g = 0; g < table.RowGroups; g++)
            {
                var group = table.ReadGroup(g, wanted);
                var own = (KeyData)group.Column("own");
                var elided = names.Contains("elided") ? group.Column("elided") : null;
                var supplied = names.Contains("supplied") ? group.Column("supplied") : null;
                var strong = names.Contains("strong") ? group.Column("strong") : null;
                for (var row = 0; row < group.Rows; row++, at++)
                {
                    words.Index.TryAdd(own.Text(row)!, at);
                    words.Testament[at] = Testament((int)own.Book.Values[row]);
                    words.Supplied[at] = supplied is not null && !supplied.IsNull(row);
                    words.Linkable[at] = !words.Supplied[at] && elided?.Boolean(row) != true;
                    words.Strong[at] = strong is not null && !strong.IsNull(row);
                    tagged |= words.Strong[at];
                }
            }

            var notes = file.Header.Notes;
            var note = notes.FirstOrDefault(n => n.Key == "strong").Value;
            var served = notes.FirstOrDefault(n => n.Key == "strong-served").Value;
            words.StrongSource = served is not null
                ? new StrongSource("laid", served.StartsWith("yes", StringComparison.OrdinalIgnoreCase), note)
                : new StrongSource(tagged ? "own" : "none", null, tagged ? note : null);
            return words;
        }
    }
}
