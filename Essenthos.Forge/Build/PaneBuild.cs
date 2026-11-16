using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>
/// One pane of a text as its product keeps it: the chapter, the words, the body's size and hash, the key of what it was read
/// from (<see cref="PaneInputs"/>) and the body itself, gzip in base64.
/// </summary>
internal sealed record PaneRow(int Book, int Chapter, long Words, long Bytes, long Compressed, string Hash, string Inputs, string Body)
{
    public static readonly ProductSchema<PaneRow> Schema = new ProductSchema<PaneRow>()
        .Integer("book", r => r.Book)
        .Integer("chapter", r => r.Chapter)
        .Integer("words", r => r.Words)
        .Integer("bytes", r => r.Bytes)
        .Integer("compressed", r => r.Compressed)
        .Text("hash", r => r.Hash)
        .Text("inputs", r => r.Inputs)
        .Text("body", r => r.Body)
        .Reads(row => new PaneRow((int)row.Integer(0), (int)row.Integer(1), row.Integer(2), row.Integer(3), row.Integer(4), row.Text(5), row.Text(6),
            row.Text(7)));

    public static readonly string[] Columns = ["book", "chapter", "words", "bytes", "compressed", "hash", "inputs", "body"];
}

/// <summary>
/// What a pane run did to a text: whether its product or its rows in the table changed, how many panes it has, how many were
/// read from the database, how many rows of the table were copied or deleted to match the product, and the size of what was read.
/// </summary>
internal sealed record PaneOutcome(string Text, bool Made, int Panes, int Read, int Copied, long Words, long Bytes, long Compressed, TimeSpan Elapsed);

/// <summary>
/// The parallel view's panes (DOC-0224 §12, NOT-0216 D2): one row of <c>pane</c> per text and canonical chapter, read from
/// the served tables by <see cref="Panes"/> and written ahead of time. A product of the serving stage (<c>build/products</c>,
/// maker <c>serve</c>): <c>panes/&lt;SLUG&gt;</c> keeps every pane of the text, its body and the key of what it was read
/// from. A pane whose key moved (or that the product lacks) is read again from the database; every other one is copied, so a
/// fresh <c>build --into</c> from a store whose panes are current reads none, and <c>build --apply</c> reads only the
/// chapters a change reaches. The table is then made to hold exactly the product's panes, by hash: a body it holds already
/// is left, a missing or different one copied, one the product no longer lists deleted, a text at a time in a transaction.
/// </summary>
internal sealed class PaneBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    public static string Product(string text) => $"panes/{text}";

    /// <summary>
    /// The panes of the texts named (every text when null). A text whose product is current for its inputs is only matched in
    /// the table; any other has the key of each of its chapters worked out, and a chapter is read from the database where its
    /// key moved, the product lacks it or <paramref name="force"/> is set.
    /// </summary>
    public async Task<IReadOnlyList<PaneOutcome>> Run(IReadOnlyCollection<string>? only, bool force, int parallel, CancellationToken cancellationToken)
    {
        var texts = await Texts(cancellationToken);
        var held = await Held(cancellationToken);
        var pairs = MappingProducts.Pairs(store).Select(MappingProducts.Split).ToList();
        var named = texts.Where(t => only is null || only.Contains(t.Slug)).ToList();
        var outcomes = new PaneOutcome[named.Count];
        var keyed = new List<Making>();
        var current = new List<(int Index, TextRow Text)>();
        for (var i = 0; i < named.Count; i++)
        {
            var text = named[i];
            var inputs = Inputs(text, texts, pairs);
            var product = Product(text.Slug);
            if (!force && Stored(product) && !store.NeedsBuild(product, ProductKind.Machine, inputs))
            {
                current.Add((i, text));
                continue;
            }

            keyed.Add(new Making(i, text, inputs));
        }

        await Parallel.ForEachAsync(current, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(parallel, 8), CancellationToken = cancellationToken },
            async (item, token) =>
            {
                var clock = Stopwatch.StartNew();
                var rows = Read(Product(item.Text.Slug), bodies: false)!;
                var copied = await Match(item.Text, rows, held.GetValueOrDefault(item.Text.Id) ?? [], null, token);
                outcomes[item.Index] = new PaneOutcome(item.Text.Slug, copied > 0, rows.Count, 0, copied, 0, 0, 0, clock.Elapsed);
            });

        if (keyed.Count == 0)
        {
            return outcomes;
        }

        var clock = Stopwatch.StartNew();
        var keys = PaneInputs.Read(store, [.. keyed.Select(m => m.Text.Slug)], pairs, parallel);
        logger.LogInformation("Panes: the chapters' inputs of {Texts} texts read in {Seconds:0.0} s", keyed.Count, clock.Elapsed.TotalSeconds);
        await Parallel.ForEachAsync(keyed, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken },
            async (making, token) => await Plan(making, keys, force, token));
        logger.LogInformation("Panes: {Read} of {Panes} chapters to read from the database", keyed.Sum(m => m.Chapters.Count),
            keyed.Sum(m => m.Keys.Count));
        foreach (var making in keyed.Where(m => m.Chapters.Count == 0))
        {
            outcomes[making.Index] = await Finish(making, held, cancellationToken);
        }

        // Every chapter to read of every text in one queue, <parallel> at once, so no text's last chapters leave the rest idle;
        // a text is written and matched as soon as its last chapter is read.
        await Parallel.ForEachAsync(keyed.SelectMany(m => m.Chapters.Select(chapter => (Making: m, chapter.Book, chapter.Chapter))),
            new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                var making = item.Making;
                using (var scope = services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                    if (await Panes.Read(db, making.Text.Id, making.Text.Slug, item.Book, item.Chapter, making.Reach.GetValueOrDefault(item.Book), token) is { } pane)
                    {
                        making.Made.Add(pane);
                    }
                }

                if (Interlocked.Decrement(ref making.Left) == 0)
                {
                    outcomes[making.Index] = await Finish(making, held, token);
                }
            });

        return outcomes;
    }

    /// <summary>A text whose chapters' keys are worked out: its chapters, their keys, what it reaches, and the panes read so far.</summary>
    private sealed class Making(int index, TextRow text, List<ProductInput> inputs)
    {
        public int Index { get; } = index;

        public TextRow Text { get; } = text;

        public List<ProductInput> Inputs { get; } = inputs;

        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        /// <summary>Every chapter the text has a pane for, with its key.</summary>
        public Dictionary<(int Book, int Chapter), string> Keys { get; set; } = [];

        /// <summary>The chapters to read from the database.</summary>
        public IReadOnlyList<(int Book, int Chapter)> Chapters { get; private set; } = [];

        /// <summary>The stored panes whose key is the chapter's now, kept as they are.</summary>
        public List<PaneRow> Kept { get; set; } = [];

        public Dictionary<int, int> Reach { get; private set; } = [];

        public void Planned(IReadOnlyList<(int Book, int Chapter)> chapters, Dictionary<int, int> reach)
        {
            Chapters = chapters;
            Reach = reach;
            Left = chapters.Count;
        }

        public ConcurrentBag<PaneBody> Made { get; } = [];

        public int Left;
    }

    /// <summary>
    /// A text's panes, the kept and the read in chapter order, written as its product and matched in the table. A chapter that
    /// reads as empty has no pane (and is read again the next time its text is keyed).
    /// </summary>
    private async Task<PaneOutcome> Finish(Making making, IReadOnlyDictionary<int, Dictionary<(int, int), string>> held, CancellationToken cancellationToken)
    {
        var text = making.Text;
        var made = making.Made.ToDictionary(p => (p.Book, p.Chapter));
        List<PaneRow> rows =
        [
            .. making.Kept.Concat(made.Values.Select(pane => new PaneRow(pane.Book, pane.Chapter, pane.Words, pane.Bytes, pane.Gzip.Length, pane.Hash,
                    making.Keys[(pane.Book, pane.Chapter)], Convert.ToBase64String(pane.Gzip))))
                .OrderBy(p => p.Book).ThenBy(p => p.Chapter),
        ];
        using (var writer = store.Create(Product(text.Slug), ProductKind.Machine, PaneRow.Schema, ProductHeader.DescribeProducer("forge panes"), making.Inputs,
                   header =>
                   {
                       header.Notes.Add(new("pane-format", Panes.Format.ToString(CultureInfo.InvariantCulture)));
                       header.Notes.Add(new("pane-inputs", PaneInputs.Version.ToString(CultureInfo.InvariantCulture)));
                   }))
        {
            foreach (var row in rows)
            {
                writer.Write(row);
            }

            writer.Complete();
        }

        store.Save();
        var copied = await Match(text, rows, held.GetValueOrDefault(text.Id) ?? [], made, cancellationToken);
        var outcome = new PaneOutcome(text.Slug, true, rows.Count, made.Count, copied, made.Values.Sum(p => (long)p.Words), made.Values.Sum(p => (long)p.Bytes),
            made.Values.Sum(p => (long)p.Gzip.Length), making.Clock.Elapsed);
        logger.LogInformation("Panes of {Text}: {Panes} chapters, {Read} read ({Words} words, {Bytes} bytes of JSON, {Compressed} compressed), {Copied} copied, in {Seconds:0.0} s",
            text.Slug, outcome.Panes, outcome.Read, outcome.Words, outcome.Bytes, outcome.Compressed, outcome.Copied, outcome.Elapsed.TotalSeconds);
        return outcome;
    }

    /// <summary>A text as its panes need it.</summary>
    internal sealed record TextRow(int Id, string Slug, string Language);

    private async Task<List<TextRow>> Texts(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Texts.AsNoTracking().OrderBy(t => t.Slug).Select(t => new TextRow(t.Id, t.Slug, t.Language)).ToListAsync(cancellationToken);
    }

    /// <summary>The hash of every pane the table holds, per text and chapter.</summary>
    private async Task<Dictionary<int, Dictionary<(int, int), string>>> Held(CancellationToken cancellationToken)
    {
        var held = new Dictionary<int, Dictionary<(int, int), string>>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT text_id, canonical_book, canonical_chapter, hash FROM pane", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var text = reader.GetInt32(0);
            (held.TryGetValue(text, out var panes) ? panes : held[text] = [])[(reader.GetInt32(1), reader.GetInt32(2))] = reader.GetString(3);
        }

        return held;
    }

    /// <summary>Whether the text's product keeps its panes' bodies and keys (one written before they were kept does not).</summary>
    private bool Stored(string product)
    {
        if (store.Find(product) is not { } relative)
        {
            return false;
        }

        using var reader = store.Open(product);
        return reader.Columns.Select(c => c.Name).SequenceEqual(PaneRow.Columns);
    }

    /// <summary>A text's stored panes, with their bodies or without (an empty body), or null where its product keeps none.</summary>
    private List<PaneRow>? Read(string product, bool bodies)
    {
        if (!Stored(product))
        {
            return null;
        }

        var relative = store.Find(product)!;
        var rows = new List<PaneRow>();
        if (store.FormatOf(relative) is EssxFormat)
        {
            using var essx = EssxReader.Open(store.FullPath(relative));
            foreach (var row in essx.Table("rows").Rows(bodies ? PaneRow.Columns : PaneRow.Columns[..^1]))
            {
                rows.Add(new PaneRow((int)row.Integer(0), (int)row.Integer(1), row.Integer(2), row.Integer(3), row.Integer(4), row.Text(5), row.Text(6),
                    bodies ? row.Text(7) : ""));
            }

            return rows;
        }

        using var reader = store.Open(product, PaneRow.Schema);
        rows.AddRange(reader.Rows().Select(row => bodies ? row : row with { Body = "" }));
        return rows;
    }

    /// <summary>
    /// The products the tables a text's panes read were loaded from, where this store holds them: the text's own
    /// products and names; every pair it is in, with the other text's words and text (whose kind decides a witness);
    /// the records; the Strong candidates; the word ids' collisions; for Greek, every Greek text's words (a gloss by
    /// spelling) and the lexicon; for Ge'ez, the lexicons; for a text paired with the text whose words refer back to others, those
    /// references, the same source's parsings and that text's names on the words referred to.
    /// </summary>
    internal List<ProductInput> Inputs(TextRow text, IReadOnlyList<TextRow> texts, IReadOnlyList<(string From, string To)> pairs)
    {
        var products = new List<string>
        {
            $"texts/{text.Slug}/words", $"texts/{text.Slug}/verses", $"texts/{text.Slug}/notes", $"texts/{text.Slug}/books",
            $"texts/{text.Slug}/text", $"texts/{text.Slug}/frame", NameProducts.Projected(text.Slug), WordIdCollision.Product,
        };
        foreach (var (from, to) in pairs.Where(p => p.From == text.Slug || p.To == text.Slug))
        {
            var other = from == text.Slug ? to : from;
            products.Add(MappingProducts.Of(MappingProducts.Pair(from, to), MappingProducts.Final));
            products.Add($"texts/{other}/words");
            products.Add($"texts/{other}/text");
        }

        products.AddRange(PaneInputs.RecordProducts);
        products.Add(TableProducts.Product("word_strong"));
        if (text.Slug == PaneInputs.ReferringText || pairs.Any(p => (p.From, p.To) == (text.Slug, PaneInputs.ReferringText)
                                                                   || (p.From, p.To) == (PaneInputs.ReferringText, text.Slug)))
        {
            products.Add(PaneInputs.Referents);
            products.Add(TableProducts.Product("word_parsing"));
            products.Add(NameProducts.Projected(PaneInputs.ReferringText));
        }

        if (text.Language == "grc")
        {
            products.AddRange(texts.Where(t => t.Language == "grc").Select(t => $"texts/{t.Slug}/words"));
            products.Add(TableProducts.Product("lexicon_gloss"));
        }

        if (text.Language == "gez")
        {
            products.Add(TableProducts.Product("lexicon_gloss"));
            products.Add(TableProducts.Product("geez_lexicon_entry"));
        }

        return [.. store.Inputs(products.Distinct(StringComparer.Ordinal).Select(store.Find).OfType<string>().Order(StringComparer.Ordinal))];
    }

    /// <summary>
    /// The chapters a text has panes for and the chapters it reaches in each book, each chapter's key, and which of them are
    /// read from the database: those whose stored pane was read under another key, or that have none.
    /// </summary>
    private async Task Plan(Making making, PaneInputs keys, bool force, CancellationToken cancellationToken)
    {
        var text = making.Text;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var held = (await db.VerseReferences
                .Where(r => r.IsPrimary && r.Verse!.TextId == text.Id)
                .Select(r => new { r.CanonicalBook, r.CanonicalChapter })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(r => (r.CanonicalBook, r.CanonicalChapter)).ToList();

        // The chapters the text reaches in each book of the shared frame, as the reader steps through them (CanonIndex).
        var reach = await db.VerseReferences
            .Where(r => r.Verse!.TextId == text.Id)
            .GroupBy(r => r.CanonicalBook)
            .Select(g => new { g.Key, Chapters = g.Max(r => r.CanonicalChapter) })
            .ToDictionaryAsync(g => g.Key, g => g.Chapters, cancellationToken);
        var chapters = Panes.Chapters(held);
        making.Keys = keys.Keys(text.Slug, making.Inputs, chapters);
        var stored = force ? [] : (Read(Product(text.Slug), bodies: true) ?? []).ToDictionary(p => (p.Book, p.Chapter));
        making.Kept = [.. chapters.Select(c => stored.GetValueOrDefault(c)).OfType<PaneRow>().Where(p => p.Inputs == making.Keys[(p.Book, p.Chapter)])];
        var kept = making.Kept.Select(p => (p.Book, p.Chapter)).ToHashSet();
        making.Planned([.. chapters.Where(c => !kept.Contains(c))], reach);
    }

    /// <summary>
    /// The table made to hold exactly these panes of the text, in one transaction: a pane it holds under the same hash is left,
    /// one it lacks or holds otherwise is copied (from <paramref name="made"/> where it was just read, else from the product),
    /// one the text no longer has is deleted. A failure leaves the panes it had. Returns how many rows were copied or deleted.
    /// </summary>
    private async Task<int> Match(TextRow text, IReadOnlyList<PaneRow> rows, IReadOnlyDictionary<(int, int), string> held,
        IReadOnlyDictionary<(int, int), PaneBody>? made, CancellationToken cancellationToken)
    {
        var wanted = rows.ToDictionary(r => (r.Book, r.Chapter));
        var gone = held.Where(h => !wanted.TryGetValue(h.Key, out var row) || row.Hash != h.Value).Select(h => h.Key).ToList();
        var copy = rows.Where(r => !held.TryGetValue((r.Book, r.Chapter), out var hash) || hash != r.Hash).ToList();
        if (gone.Count == 0 && copy.Count == 0)
        {
            return 0;
        }

        var bodies = new Dictionary<(int, int), byte[]>();
        Dictionary<(int, int), PaneRow>? stored = null;
        foreach (var row in copy)
        {
            if (made?.GetValueOrDefault((row.Book, row.Chapter)) is { } pane)
            {
                bodies[(row.Book, row.Chapter)] = pane.Gzip;
                continue;
            }

            if (row.Body.Length == 0)
            {
                stored ??= (Read(Product(text.Slug), bodies: true) ?? []).ToDictionary(s => (s.Book, s.Chapter));
                var body = stored[(row.Book, row.Chapter)];
                if (body.Hash != row.Hash)
                {
                    throw new InvalidOperationException($"{Product(text.Slug)} changed while its panes were copied ({row.Book}:{row.Chapter}).");
                }

                bodies[(row.Book, row.Chapter)] = Convert.FromBase64String(body.Body);
                continue;
            }

            bodies[(row.Book, row.Chapter)] = Convert.FromBase64String(row.Body);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (gone.Count > 0)
        {
            await using var delete = new NpgsqlCommand(
                """
                DELETE FROM pane p USING unnest(@books, @chapters) AS d(book, chapter)
                WHERE p.text_id = @text AND p.canonical_book = d.book AND p.canonical_chapter = d.chapter
                """, connection, transaction);
            delete.Parameters.AddWithValue("text", text.Id);
            delete.Parameters.AddWithValue("books", gone.Select(g => g.Item1).ToArray());
            delete.Parameters.AddWithValue("chapters", gone.Select(g => g.Item2).ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var import = await connection.BeginBinaryImportAsync(
                         "COPY pane (text_id, canonical_book, canonical_chapter, body, hash) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var row in copy)
            {
                await import.StartRowAsync(cancellationToken);
                await import.WriteAsync(text.Id, NpgsqlDbType.Integer, cancellationToken);
                await import.WriteAsync(row.Book, NpgsqlDbType.Integer, cancellationToken);
                await import.WriteAsync(row.Chapter, NpgsqlDbType.Integer, cancellationToken);
                await import.WriteAsync(bodies[(row.Book, row.Chapter)], NpgsqlDbType.Bytea, cancellationToken);
                await import.WriteAsync(row.Hash, NpgsqlDbType.Text, cancellationToken);
            }

            await import.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return copy.Count + gone.Count(g => !wanted.ContainsKey(g));
    }
}
