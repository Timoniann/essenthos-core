using System.Collections.Concurrent;
using System.Diagnostics;
using Essenthos.Core.Comparison;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Every link of a database written out as the mapping's method files, one folder per pair of texts and one
/// file per method (DOC-0224 §6.1, §15.3): each claim of each link a row, its words by key, its link's id kept.
/// Re-runnable: the export of cut-over day is this again. It also seeds a pair's rulings from the rulings held
/// in code and in <c>link-rulings.json</c>, where the pair has no rulings file yet.
/// </summary>
internal static class MappingExport
{
    public const string Producer = "forge mapping-export";

    public sealed record PairExport(string Pair, IReadOnlyDictionary<string, long> Rows, long Links, int Rulings);

    public static async Task<List<PairExport>> Run(
        string connection, ProductStore store, IReadOnlySet<string>? only, int parallel, ILogger logger, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        var database = db.Database;
        var keys = await WordKeys.Load(db, reverse: false, cancellationToken);
        logger.LogInformation("{Words} word keys read in {Elapsed}", keys.Words, clock.Elapsed);
        var provenances = await Provenances.Load(db, notes: true, cancellationToken);
        logger.LogInformation("Provenances read in {Elapsed}", clock.Elapsed);
        var hashes = await VerseTextHashes(db, keys, cancellationToken);
        logger.LogInformation("Verse hashes computed in {Elapsed}", clock.Elapsed);
        var seeds = await RulingSeeds(db, keys, store.ResourcesPath, cancellationToken);

        var pairs = (await PairSnapshot.Pairs(db, cancellationToken))
            .Select(p => (p.From, p.To, Name: MappingProducts.Pair(keys.Slug(p.From), keys.Slug(p.To))))
            .Where(p => only is null || only.Contains(p.Name))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
        PairOrdinals.Ensure(store, pairs.Select(p => p.Name));

        var done = new ConcurrentBag<PairExport>();
        await Parallel.ForEachAsync(pairs, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, async (pair, token) =>
        {
            await using var own = new NpgsqlConnection(connection);
            await own.OpenAsync(token);
            var links = await PairSnapshot.Read(own, pair.From, pair.To, token);
            var written = Write(store, pair.Name, keys.Slug(pair.From), keys.Slug(pair.To), links, keys, provenances, hashes, database);
            var rulings = SeedRulings(store, pair.Name, seeds.GetValueOrDefault(pair.Name) ?? []);
            done.Add(new PairExport(pair.Name, written, links.Count, rulings));
            logger.LogInformation("{Pair}: {Links} links, {Rows} rows, at {Elapsed}", pair.Name, links.Count, written.Values.Sum(), clock.Elapsed);
        });

        store.Save();
        return [.. done.OrderBy(p => p.Pair, StringComparer.Ordinal)];
    }

    private static Dictionary<string, long> Write(
        ProductStore store, string pair, string fromText, string toText, List<StoredLink> links, WordKeys keys,
        Provenances provenances, (string?[] Text, string?[] Fold) hashes, string database)
    {
        var byMethod = new Dictionary<string, List<(int Side, WordKey First, MappingRow Row)>>(StringComparer.Ordinal);
        var verses = new HashSet<int>();
        foreach (var link in links)
        {
            var from = Side(link.From, fromText, keys, verses);
            var to = Side(link.To, toText, keys, verses);
            var roles = link.Roles.Count == 0
                ? null
                : string.Join(' ', link.Roles.OrderBy(r => keys.Key(r.Word)).Select(r => $"{keys.Own(r.Word)}={r.Role}@{keys.Own(r.Head)}"));
            var (side, first) = from.Keys.Count > 0 ? (0, from.Keys[0]) : to.Keys.Count > 0 ? (1, to.Keys[0]) : (2, default);
            foreach (var claim in link.Claims)
            {
                var (source, note) = provenances[claim.Provenance];
                var method = MappingMethods.Classify(claim.Method, source);
                if (!byMethod.TryGetValue(method, out var rows))
                {
                    byMethod[method] = rows = [];
                }

                rows.Add((side, first, new MappingRow(from.Text, to.Text, link.Relation, method, claim.Confidence, source, note, roles, null)));
            }
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (method, rows) in byMethod.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            rows.Sort(static (a, b) => Order(a.Side, a.First, a.Row, b.Side, b.First, b.Row));
            using var writer = store.Create(MappingProducts.Of(pair, method), ProductKind.Machine, MappingRow.Schema,
                ProductHeader.DescribeProducer(Producer), [], header => header.Notes.Add(new("exported", $"from {database}")));
            foreach (var (_, _, row) in rows)
            {
                writer.Write(row);
            }

            writer.Complete();
            counts[method] = rows.Count;
        }

        using (var writer = store.Create(MappingProducts.Of(pair, MappingProducts.Hashes), ProductKind.Machine, VerseHashRow.Schema,
                   ProductHeader.DescribeProducer(Producer), [], header => header.Notes.Add(new("exported", $"from {database}"))))
        {
            foreach (var row in verses
                         .Select(v => new VerseHashRow(keys.Slug(keys.TextOfVerse(v)), keys.VerseKeyOf(v), hashes.Text[v] ?? string.Empty, hashes.Fold[v]))
                         .OrderBy(r => r.Text, StringComparer.Ordinal).ThenBy(r => r.Own))
            {
                writer.Write(row);
            }

            writer.Complete();
        }

        return counts;
    }

    /// <summary>The order every mapping file is written in: by its first key, then by the words, then by who says it.</summary>
    internal static int Order(int sideA, WordKey firstA, MappingRow a, int sideB, WordKey firstB, MappingRow b)
    {
        var order = sideA.CompareTo(sideB);
        if (order == 0) order = firstA.CompareTo(firstB);
        if (order == 0) order = string.CompareOrdinal(a.From, b.From);
        if (order == 0) order = string.CompareOrdinal(a.To, b.To);
        if (order == 0) order = string.CompareOrdinal(a.Method, b.Method);
        if (order == 0) order = string.CompareOrdinal(a.Source, b.Source);
        if (order == 0) order = Nullable.Compare(a.Confidence, b.Confidence);
        if (order == 0) order = string.CompareOrdinal(a.Evidence, b.Evidence);
        return order;
    }

    private static (string Text, List<WordKey> Keys) Side(List<long> words, string text, WordKeys keys, HashSet<int> verses)
    {
        var list = new List<WordKey>(words.Count);
        foreach (var word in words)
        {
            var key = keys.Key(word);
            if (key.Text != text)
            {
                throw new InvalidDataException($"Word {word} ({key}) is on a link of {text}'s side but belongs to {key.Text}.");
            }

            list.Add(key);
            verses.Add(keys.VerseOf(word));
        }

        list.Sort();
        return (string.Join(' ', list.Select(k => k.Own)), list);
    }

    /// <summary>
    /// The text hash of every verse (<see cref="VerseHasher"/>), by verse id: what the texts' manifests say once P1 writes them;
    /// and the verse's fold (<see cref="VerseFold"/>), which tells a later correction of marks from one of words.
    /// </summary>
    private static async Task<(string?[] Text, string?[] Fold)> VerseTextHashes(NpgsqlConnection db, WordKeys keys, CancellationToken cancellationToken)
    {
        var languages = new Dictionary<int, string?>();
        await using (var command = new NpgsqlCommand("SELECT id, language FROM text", db))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                languages[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        await using var max = new NpgsqlCommand("SELECT coalesce(max(id), 0) FROM verse", db);
        var hashes = new string?[Convert.ToInt32(await max.ExecuteScalarAsync(cancellationToken)) + 1];
        var folds = new string?[hashes.Length];
        var surfaces = new List<string>();
        void Fold(int verse)
        {
            folds[verse] = VerseFold.Of(surfaces, languages.GetValueOrDefault(keys.TextOfVerse(verse)));
            surfaces.Clear();
        }

        using var hasher = new VerseHasher();
        var current = 0;
        using (var export = db.BeginBinaryExport("COPY (SELECT verse_id, text, trailer FROM word ORDER BY verse_id, position) TO STDOUT (FORMAT BINARY)"))
        {
            while (export.StartRow() >= 0)
            {
                var verse = export.Read<int>(NpgsqlDbType.Integer);
                if (verse != current)
                {
                    if (current != 0)
                    {
                        hashes[current] = hasher.Finish().Text;
                        Fold(current);
                    }

                    current = verse;
                }

                var surface = export.Read<string>(NpgsqlDbType.Text);
                surfaces.Add(surface);
                hasher.Add(new VerseWord(surface, export.Read<string>(NpgsqlDbType.Text)));
            }
        }

        if (current != 0)
        {
            hashes[current] = hasher.Finish().Text;
            Fold(current);
        }

        return (hashes, folds);
    }

    /// <summary>
    /// The rulings the corpus applies today, as rows of the pairs they rule on: the statistical pairings held
    /// in <see cref="RejectedRenderings"/>, which no aligner may draw, and <c>link-rulings.json</c>, where one
    /// stated source's statement about one word is set aside for another's.
    /// </summary>
    private static async Task<Dictionary<string, List<RulingRow>>> RulingSeeds(
        NpgsqlConnection db, WordKeys keys, string resources, CancellationToken cancellationToken)
    {
        var seeds = new Dictionary<string, List<RulingRow>>(StringComparer.Ordinal);
        void Add(string pair, RulingRow row)
        {
            if (!seeds.TryGetValue(pair, out var list))
            {
                seeds[pair] = list = [];
            }

            list.Add(row);
        }

        foreach (var ruling in RejectedRenderings.All)
        {
            foreach (var (fromWord, toWord) in await RejectedRenderings.Locate(db, [ruling], cancellationToken))
            {
                var (from, to) = (keys.Key(fromWord), keys.Key(toWord));
                var why = $"{ruling.From.Surface} / {ruling.To.Surface}: {ruling.Reason}";
                Add(MappingProducts.Pair(from.Text, to.Text), new RulingRow(RulingRow.Refuse, from.Own, to.Own, "renders", CompositionPipeline.Machine, why));
                Add(MappingProducts.Pair(to.Text, from.Text), new RulingRow(RulingRow.Refuse, to.Own, from.Own, "renders", CompositionPipeline.Machine, why));
            }
        }

        foreach (var ruling in LinkRulings.Read(resources).All)
        {
            if (!LinkRulings.Address(ruling.Reference, out var book, out var chapter, out var verse))
            {
                continue;
            }

            await using var command = new NpgsqlCommand(
                """
                SELECT w.id, w.text, t.language FROM verse_reference r
                JOIN verse v ON v.id = r.verse_id JOIN text t ON t.id = v.text_id JOIN word w ON w.verse_id = v.id
                WHERE r.is_primary AND t.slug = @witness AND r.canonical_book = @book AND r.canonical_chapter = @chapter AND r.canonical_verse = @verse
                ORDER BY v.chapter_number, v.number, w.position
                """, db);
            command.Parameters.AddWithValue("witness", ruling.Witness);
            command.Parameters.AddWithValue("book", book);
            command.Parameters.AddWithValue("chapter", chapter);
            command.Parameters.AddWithValue("verse", verse);
            var words = new List<(long Id, string Surface)>();
            string? language = null;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    words.Add((reader.GetInt64(0), reader.GetString(1)));
                    language = reader.IsDBNull(2) ? null : reader.GetString(2);
                }
            }

            if (LinkRulings.Locate([.. words.Select(w => w.Surface)], ruling.Word, ruling.Occurrence, language) is not { } at)
            {
                continue;
            }

            var source = ruling.Over switch
            {
                LinkRulings.Berean => BereanSource,
                LinkRulings.ClearBible => ClearBibleSource,
                _ => ruling.Over,
            };
            Add(MappingProducts.Pair(ruling.Text, ruling.Witness), new RulingRow(RulingRow.Refuse, string.Empty, keys.Own(words[at].Id), string.Empty, source,
                $"{ruling.Reference} {ruling.Word} ({ruling.Occurrence}): {ruling.Stands} stands over {ruling.Over}. {ruling.Reason}"));
        }

        return seeds;
    }

    private const string BereanSource = "Berean Standard Bible translation tables";

    private const string ClearBibleSource = "Clear Bible Alignments";

    /// <summary>Writes the pair's rulings file from the seeds, unless one is there: it is kept by hand from then on.</summary>
    private static int SeedRulings(ProductStore store, string pair, List<RulingRow> seeds)
    {
        var product = MappingProducts.Of(pair, MappingProducts.Rulings);
        if (seeds.Count == 0 || store.Exists(product))
        {
            return 0;
        }

        using var writer = store.Create(product, ProductKind.Hand, RulingRow.Schema, ProductHeader.DescribeProducer(Producer), [],
            header => header.Comments.Add(
                "Rulings on this pair, kept by hand. refuse with both sides: that shape is never a link, whatever proposes it (source: only from that source). " +
                "refuse with one side and a source: those words leave that source's rows. accept: the shape is pinned and wins every competition."));
        foreach (var row in seeds.OrderBy(r => r.From, StringComparer.Ordinal).ThenBy(r => r.To, StringComparer.Ordinal))
        {
            writer.Write(row);
        }

        writer.Complete();
        return seeds.Count;
    }
}

/// <summary>Each pair's ordinal, the top bits of its links' ids (<see cref="LinkIds"/>, <see cref="PairOrdinalRow"/>); a listed pair keeps its own.</summary>
internal static class PairOrdinals
{
    public static Dictionary<string, long> Read(ProductStore store)
    {
        if (!store.Exists(MappingProducts.PairOrdinals))
        {
            return new(StringComparer.Ordinal);
        }

        using var reader = store.Open(MappingProducts.PairOrdinals, PairOrdinalRow.Schema);
        return reader.Rows().ToDictionary(r => r.Pair, r => r.Ordinal, StringComparer.Ordinal);
    }

    /// <summary>Gives every pair not yet listed the next ordinal, in name order; a listed pair keeps its own.</summary>
    public static Dictionary<string, long> Ensure(ProductStore store, IEnumerable<string> pairs)
    {
        lock (typeof(PairOrdinals))
        {
            var ordinals = Read(store);
            var missing = pairs.Where(p => !ordinals.ContainsKey(p)).Distinct().Order(StringComparer.Ordinal).ToList();
            if (missing.Count == 0)
            {
                return ordinals;
            }

            var next = ordinals.Count == 0 ? 1 : ordinals.Values.Max() + 1;
            foreach (var pair in missing)
            {
                ordinals[pair] = next++;
            }

            using var writer = store.Create(MappingProducts.PairOrdinals, ProductKind.Machine, PairOrdinalRow.Schema,
                ProductHeader.DescribeProducer(MappingExport.Producer), []);
            foreach (var (pair, ordinal) in ordinals.OrderBy(p => p.Value))
            {
                writer.Write(new PairOrdinalRow(pair, ordinal));
            }

            writer.Complete();
            return ordinals;
        }
    }
}
