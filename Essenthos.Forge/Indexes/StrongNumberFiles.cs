using Essenthos.Core.Strong;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Indexes;

/// <summary>
/// One row of <c>texts/&lt;SLUG&gt;/numbers</c>: a word and a Strong number it carries for marking (<see cref="ChainedNumbers"/>). A word
/// rendering several original words as one unit (P7) has a row per number, its first number's first.
/// </summary>
/// <param name="Hops">0 for the word's own number, else the links between it and the word whose own number it is.</param>
/// <param name="Source">The word it took the number from, <c>TEXT key</c>; null for its own.</param>
/// <param name="Confidence">The confidence of the link it took it over; null for its own and over a stated or ruled link.</param>
/// <param name="Linked">On a word with its own number: the number a sure link gives it instead, where none gives its own.</param>
/// <param name="LinkedSource">The word lending <paramref name="Linked"/>.</param>
internal sealed record StrongNumberRow(WordKey Key, string Number, int Hops, string? Source, double? Confidence, string? Linked, string? LinkedSource)
{
    public static ProductSchema<StrongNumberRow> Schema(string text) => new ProductSchema<StrongNumberRow>()
        .OwnWordKey("own", r => r.Key)
        .Text("number", r => r.Number)
        .Integer("hops", r => r.Hops)
        .OptionalText("source", r => r.Source)
        .OptionalText("confidence", r => MappingText.Confidence(r.Confidence))
        .OptionalText("linked", r => r.Linked)
        .OptionalText("linked_source", r => r.LinkedSource)
        .Reads(row => new StrongNumberRow(WordKey.Parse(row.Text(0), text), row.Text(1), row.Int32(2), row.TextOrNull(3),
            MappingText.Confidence(row.TextOrNull(4)), row.TextOrNull(5), row.TextOrNull(6)));
}

/// <summary>One text's line of <c>build/strong-disagreements</c>.</summary>
internal sealed record StrongDisagreementRow(string Text, long Own, long FromOriginals, long FromTranslations, long Chained, long Disagreements, string Examples)
{
    public static readonly ProductSchema<StrongDisagreementRow> Schema = new ProductSchema<StrongDisagreementRow>()
        .Text("text", r => r.Text)
        .Integer("own", r => r.Own)
        .Integer("from_originals", r => r.FromOriginals)
        .Integer("from_translations", r => r.FromTranslations)
        .Integer("chained", r => r.Chained)
        .Integer("disagreements", r => r.Disagreements)
        .Text("examples", r => r.Examples)
        .Reads(row => new StrongDisagreementRow(row.Text(0), row.Integer(1), row.Integer(2), row.Integer(3), row.Integer(4), row.Integer(5), row.Text(6)));
}

/// <summary>
/// The Strong numbers of every text's words as files (TSK-1822): <c>texts/&lt;SLUG&gt;/numbers</c>, one per text, made from the
/// text's words, the finals of its pairs, and of every text those pairs join it to the numbers that text was given where it
/// comes before it in <see cref="ChainedNumbers.Order"/>, its words' own numbers where it comes after. A file is made only
/// when one of those moved, so the texts are made in waves, each after the texts it reads. <c>build/strong-disagreements</c>
/// counts, per text, where a word's own number differs from what its sure links give, from the files' headers.
/// <c>word_rendered_strong</c> is loaded from the files (<see cref="Load(string, ProductStore, int, CancellationToken)"/>).
/// </summary>
internal static class StrongNumberFiles
{
    /// <summary>Raised with any change to the rule; a file made by another version is made again whatever its inputs say.</summary>
    public const string Version = "4";

    /// <summary>The editions whose words' parts of speech class every number of the originals (<see cref="OriginalWords"/>).</summary>
    private static readonly string[] Classing = [Loading.BhsaTextSource.Slug, Loading.NestleTextSource.Slug];

    public const string VersionNote = "numbers-version";

    public const string Disagreements = "build/strong-disagreements";

    private const string Producer = "forge strong-numbers";

    private const int Examples = 5;

    public static string Product(string text) => $"texts/{text}/numbers";

    /// <summary>The texts in the order they are numbered (<see cref="ChainedNumbers.Order"/>).</summary>
    public static List<string> Order(IndexContext context) =>
        ChainedNumbers.Order(
            context.Texts.ToDictionary(t => t.Key, t => t.Value.Kind == TextKind.Translation, StringComparer.Ordinal),
            context.Pairs.Select(MappingProducts.Split));

    /// <summary>Every text's numbers where an input moved, then the disagreements.</summary>
    public static List<IndexOutcome> Make(IndexContext context, bool force, int parallel)
    {
        var order = Order(context);
        var place = order.Select((text, at) => (text, at)).ToDictionary(t => t.text, t => t.at, StringComparer.Ordinal);
        var wave = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var text in order)
        {
            wave[text] = context.PairsOf(text).Select(p => p.Other).Where(other => place[other] < place[text])
                .Select(other => wave[other] + 1).DefaultIfEmpty(0).Max();
        }

        var lenders = new ConcurrentDictionary<(string Text, bool Numbered), Lazy<ChainedNumbers.Lender>>();
        var classes = new Lazy<(Dictionary<string, WordClass> Classes, HashSet<string> Names)>(() => Classes(context.Store, StrongNumberLists.Of(context.Store)));
        var outcomes = new ConcurrentBag<IndexOutcome>();
        foreach (var texts in order.GroupBy(text => wave[text]).OrderBy(g => g.Key))
        {
            Parallel.ForEach(texts, new ParallelOptions { MaxDegreeOfParallelism = parallel }, text =>
                outcomes.Add(Make(context, text, place, lenders, classes, force)));
        }

        outcomes.Add(Summarise(context, order, force));
        context.Store.Save();
        return [.. outcomes.OrderBy(o => o.Text, StringComparer.Ordinal)];
    }

    private static IndexOutcome Make(
        IndexContext context, string text, Dictionary<string, int> place, ConcurrentDictionary<(string, bool), Lazy<ChainedNumbers.Lender>> lenders,
        Lazy<(Dictionary<string, WordClass> Classes, HashSet<string> Names)> classes, bool force)
    {
        var clock = Stopwatch.StartNew();
        var store = context.Store;
        var lists = StrongNumberLists.Of(store);
        var product = Product(text);
        var pairs = context.PairsOf(text).OrderBy(p => p.Pair, StringComparer.Ordinal).ToList();
        var others = pairs.Select(p => p.Other).Distinct().Order(StringComparer.Ordinal).ToList();
        var translation = context.Texts[text].Kind == TextKind.Translation;
        bool Before(string other) => place[other] < place[text];
        bool Original(string other) => context.Texts[other].Kind != TextKind.Translation;
        var inputs = new List<ProductInput> { store.ProductInput(TextField.ProductOf(text)), store.ProductInput(TextWord.ProductOf(text)) };
        foreach (var other in others)
        {
            inputs.Add(store.ProductInput(TextField.ProductOf(other)));
            inputs.Add(store.ProductInput(Before(other) ? Product(other) : TextWord.ProductOf(other)));
            if (translation && Before(other) && Original(other))
            {
                inputs.Add(store.ProductInput(TextWord.ProductOf(other)));
            }
        }

        if (translation)
        {
            inputs.AddRange(Classing.Where(slug => store.Exists(TextWord.ProductOf(slug)) && !others.Contains(slug))
                .Select(slug => store.ProductInput(TextWord.ProductOf(slug))));
        }

        inputs.AddRange(pairs.Select(p => store.ProductInput(MappingProducts.Of(p.Pair, MappingProducts.Final))));
        inputs.Add(store.ProductInput(StrongNumberLists.Product));
        inputs.Add(store.ProductInput(TableProducts.Prefixes));
        if (!force && !NeedsBuild(store, product, inputs))
        {
            return new IndexOutcome(text, "numbers", false, 0, clock.Elapsed);
        }

        var lending = others.ToDictionary(other => other, other => lenders.GetOrAdd((other, Before(other)), key => new Lazy<ChainedNumbers.Lender>(() =>
        {
            var (numbers, also) = key.Item2
                ? Numbers(store, key.Item1)
                : (context.EditionOf(key.Item1).Numbers.ToDictionary(n => n.Key, n => (n.Value, 0), StringComparer.Ordinal), null);
            return Original(key.Item1)
                ? new ChainedNumbers.Lender(lists, true, numbers, classes.Value.Classes, Finite(store, key.Item1), classes.Value.Names, also)
                : new ChainedNumbers.Lender(lists, false, numbers, also: also);
        })).Value, StringComparer.Ordinal);
        var words = new List<(WordKey Key, string? Own, string Form)>();
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text)))
        {
            var served = StrongTerms.MayServe(reader.Header);
            foreach (var word in reader.Rows())
            {
                words.Add((word.Key, served ? word.Strong : null, word.Normalised ?? word.Surface.ToLowerInvariant()));
                if (translation && Supplied(word))
                {
                    supplied.Add(word.Key.Own);
                }
            }
        }

        var keys = words.ToDictionary(w => w.Key.Own, w => w.Key, StringComparer.Ordinal);
        var numbered = ChainedNumbers.Resolve(words.Select(w => (w.Key.Own, w.Own, w.Form)).ToList(), Links(store, pairs, lending), lending,
            lists, translation ? new GroupNumbers(context.Texts[text].Language, lists) : null, translation ? supplied : null);
        var disagreeing = numbered.Where(n => n.Linked is not null).ToList();
        var examples = Enumerable.Range(0, Math.Min(Examples, disagreeing.Count))
            .Select(i => disagreeing[(int)((long)i * disagreeing.Count / Math.Min(Examples, disagreeing.Count))])
            .Select(n => $"{n.Key} {n.Number}, {n.Linked} by {n.LinkedSource}");
        long rows;
        using (var writer = store.Create(product, ProductKind.Machine, StrongNumberRow.Schema(text), ProductHeader.DescribeProducer(Producer), inputs, header =>
               {
                   header.Notes.Add(new(VersionNote, Version));
                   header.Notes.Add(new("reliable", ChainedNumbers.Reliable.ToString(CultureInfo.InvariantCulture)));
                   header.Notes.Add(new("own", Count(numbered, n => n.Hops == 0)));
                   header.Notes.Add(new("from-originals", Count(numbered, n => n.Hops > 0 && lending[Text(n.Source!)].Original)));
                   header.Notes.Add(new("from-translations", Count(numbered, n => n.Hops > 0 && !lending[Text(n.Source!)].Original)));
                   header.Notes.Add(new("chained", Count(numbered, n => n.Hops > 1)));
                   header.Notes.Add(new("several", Count(numbered, n => n.Also is not null)));
                   header.Notes.Add(new("disagreements", disagreeing.Count.ToString(CultureInfo.InvariantCulture)));
                   header.Notes.Add(new("examples", disagreeing.Count == 0 ? "-" : string.Join(" | ", examples)));
               }))
        {
            foreach (var n in numbered)
            {
                writer.Write(new StrongNumberRow(keys[n.Key], n.Number, n.Hops, n.Source, n.Confidence, n.Linked, n.LinkedSource));
                foreach (var (number, source) in n.Also ?? [])
                {
                    writer.Write(new StrongNumberRow(keys[n.Key], number, n.Hops, source, n.Confidence, null, null));
                }
            }

            writer.Complete();
            rows = writer.Rows;
        }

        return new IndexOutcome(text, "numbers", true, rows, clock.Elapsed, $"{disagreeing.Count} disagreements");
    }

    /// <summary>The settled renders and equals links of the pairs, read as they are used; a pair whose other text lends no number is not read.</summary>
    private static IEnumerable<ChainedNumbers.Joined> Links(
        ProductStore store, IEnumerable<(string Pair, string Other, bool First)> pairs, Dictionary<string, ChainedNumbers.Lender> lending)
    {
        foreach (var (pair, other, first) in pairs.Where(p => lending[p.Other].Lends))
        {
            using var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
            foreach (var row in reader.Rows().Where(row => row.Settled && row.Relation is "renders" or "equals"))
            {
                yield return new ChainedNumbers.Joined(
                    [.. MappingText.Split(first ? row.From : row.To)], other, [.. MappingText.Split(first ? row.To : row.From)], row.Confidence);
            }
        }
    }

    private static string Text(string source) => source[..source.IndexOf(' ', StringComparison.Ordinal)];

    /// <summary>
    /// Whether a translation marks the word as its translators' own (P1, PRB-1869): a supplied run, but not the Berean's braces,
    /// which mark the auxiliaries a verb's tense implies («{will}») and render that verb.
    /// </summary>
    internal static bool Supplied(TextWord word) => word.Supplied is not null && word.SuppliedMark != "{}";

    /// <summary>
    /// The class of every number of the originals, by the part of speech most of its words in BHSA and Nestle 1904 have, and the
    /// numbers most of whose words are proper names.
    /// </summary>
    private static (Dictionary<string, WordClass> Classes, HashSet<string> Names) Classes(ProductStore store, StrongNumberLists lists)
    {
        var parts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var named = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slug in Classing.Where(slug => store.Exists(TextWord.ProductOf(slug))))
        {
            using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
            foreach (var word in reader.Rows().Where(w => w.Strong is not null))
            {
                var part = OriginalWords.Part(word.Morphology) ?? string.Empty;
                if (!parts.TryGetValue(word.Strong!, out var counted))
                {
                    parts[word.Strong!] = counted = new Dictionary<string, int>(StringComparer.Ordinal);
                }

                counted[part] = counted.GetValueOrDefault(part) + 1;
                if (OriginalWords.Name(part, word.Surface))
                {
                    named[word.Strong!] = named.GetValueOrDefault(word.Strong!) + 1;
                }
            }
        }

        return (parts.ToDictionary(p => p.Key, p => OriginalWords.Of(p.Key, p.Value.MaxBy(c => c.Value).Key, lists.Prefixes), StringComparer.Ordinal),
            parts.Where(p => named.GetValueOrDefault(p.Key) * 2 > p.Value.Values.Sum()).Select(p => p.Key).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>The keys of an original's words that are finite verbs.</summary>
    private static HashSet<string> Finite(ProductStore store, string text)
    {
        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        return reader.Rows().Where(w => OriginalWords.Finite(w.Morphology)).Select(w => w.Key.Own).ToHashSet(StringComparer.Ordinal);
    }

    private static string Count(List<ChainedNumbers.Numbered> numbered, Func<ChainedNumbers.Numbered, bool> which) =>
        numbered.Count(which).ToString(CultureInfo.InvariantCulture);

    private static bool NeedsBuild(ProductStore store, string product, IReadOnlyList<ProductInput> inputs)
    {
        if (store.NeedsBuild(product, ProductKind.Machine, inputs))
        {
            return true;
        }

        using var reader = store.Open(product);
        return reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value != Version;
    }

    /// <summary>The numbers a text's file gives its words, by own key: the first, and the further ones a word carries with it.</summary>
    private static (Dictionary<string, (string Number, int Hops)> Numbers, Dictionary<string, string[]>? Also) Numbers(ProductStore store, string text)
    {
        var numbers = new Dictionary<string, (string Number, int Hops)>(StringComparer.Ordinal);
        var also = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var reader = store.Open(Product(text), StrongNumberRow.Schema(text));
        foreach (var row in reader.Rows())
        {
            if (!numbers.TryAdd(row.Key.Own, (row.Number, row.Hops)))
            {
                if (!also.TryGetValue(row.Key.Own, out var further))
                {
                    also[row.Key.Own] = further = [];
                }

                further.Add(row.Number);
            }
        }

        return (numbers, also.Count == 0 ? null : also.ToDictionary(a => a.Key, a => a.Value.ToArray(), StringComparer.Ordinal));
    }

    /// <summary>One line per text, in the order they are numbered, from each file's header.</summary>
    private static IndexOutcome Summarise(IndexContext context, List<string> order, bool force)
    {
        var clock = Stopwatch.StartNew();
        var store = context.Store;
        var numbered = order.Where(text => store.Exists(Product(text))).ToList();
        var inputs = numbered.Select(text => store.ProductInput(Product(text))).ToList();
        if (!force && !store.NeedsBuild(Disagreements, ProductKind.Hand, inputs))
        {
            return new IndexOutcome("-", "disagreements", false, 0, clock.Elapsed);
        }

        using var writer = store.Create(Disagreements, ProductKind.Hand, StrongDisagreementRow.Schema, ProductHeader.DescribeProducer(Producer), inputs,
            header => header.Notes.Add(new("reliable", ChainedNumbers.Reliable.ToString(CultureInfo.InvariantCulture))));
        foreach (var text in numbered)
        {
            using var reader = store.Open(Product(text));
            var notes = reader.Header!.Notes.ToDictionary(n => n.Key, n => n.Value, StringComparer.Ordinal);
            long Note(string name) => long.Parse(notes[name], CultureInfo.InvariantCulture);
            writer.Write(new StrongDisagreementRow(text, Note("own"), Note("from-originals"), Note("from-translations"), Note("chained"),
                Note("disagreements"), notes["examples"]));
        }

        writer.Complete();
        return new IndexOutcome("-", "disagreements", true, writer.Rows, clock.Elapsed);
    }

    /// <summary>
    /// Puts every text's numbers into <c>word_rendered_strong</c> of a database whose table is empty, the texts on
    /// <paramref name="parallel"/> connections at once; the rows written.
    /// </summary>
    public static async Task<long> Load(string connectionString, ProductStore store, int parallel, CancellationToken cancellationToken)
    {
        Dictionary<string, int> ids;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            ids = await TextIds(connection, cancellationToken);
        }

        var words = WordIdTable.For(store);
        long rows = 0;
        await Parallel.ForEachAsync(ids.Where(t => store.Exists(Product(t.Key))),
            new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, async (text, token) =>
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(token);
                Interlocked.Add(ref rows, await Copy(connection, store, words, text.Key, text.Value, token));
            });
        return rows;
    }

    /// <summary>Replaces the rows of the texts named with their files' on the caller's connection and transaction; the rows written.</summary>
    public static async Task<long> Load(NpgsqlConnection connection, NpgsqlTransaction transaction, ProductStore store, IReadOnlyCollection<string> texts,
        CancellationToken cancellationToken)
    {
        var ids = await TextIds(connection, cancellationToken);
        var words = WordIdTable.For(store);
        long rows = 0;
        foreach (var text in texts.Where(ids.ContainsKey))
        {
            // A verse's id leads with its text's (RowIds.Verse), so a text's rows are one range of the key.
            await using (var delete = new NpgsqlCommand("DELETE FROM word_rendered_strong WHERE verse_id >= @first AND verse_id < @next", connection, transaction))
            {
                delete.Parameters.AddWithValue("first", RowIds.Verse(ids[text], 0, 0, 0));
                delete.Parameters.AddWithValue("next", (long)RowIds.Verse(ids[text], 0, 0, 0) + (1L << 24));
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            if (store.Exists(Product(text)))
            {
                rows += await Copy(connection, store, words, text, ids[text], cancellationToken);
            }
        }

        return rows;
    }

    private static async Task<long> Copy(NpgsqlConnection connection, ProductStore store, WordIdTable words, string text, int id, CancellationToken cancellationToken)
    {
        var verses = TextLoad.NumberedVerses(store, text, id).ToDictionary(v => v.Row.Own, v => v.Id);
        long rows = 0;
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY word_rendered_strong (verse_id, word_id, strong_number) FROM STDIN (FORMAT BINARY)", cancellationToken);
        using var reader = store.Open(Product(text), StrongNumberRow.Schema(text));
        foreach (var row in reader.Rows())
        {
            if (!verses.TryGetValue(row.Key.Verse, out var verse))
            {
                throw new InvalidDataException($"{Product(text)}: {row.Key} stands in a verse texts/{text}/verses does not hold.");
            }

            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(verse, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(words.Of(row.Key), NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(row.Number, NpgsqlDbType.Text, cancellationToken);
            rows++;
        }

        await writer.CompleteAsync(cancellationToken);
        return rows;
    }

    private static async Task<Dictionary<string, int>> TextIds(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT slug, id FROM text", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids[reader.GetString(0)] = reader.GetInt32(1);
        }

        return ids;
    }
}
