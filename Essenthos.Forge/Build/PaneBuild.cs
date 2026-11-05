using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>One pane of a text as its product lists it: the chapter, the words, the body's size and hash.</summary>
internal sealed record PaneRow(int Book, int Chapter, long Words, long Bytes, long Compressed, string Hash)
{
    public static readonly ProductSchema<PaneRow> Schema = new ProductSchema<PaneRow>()
        .Integer("book", r => r.Book)
        .Integer("chapter", r => r.Chapter)
        .Integer("words", r => r.Words)
        .Integer("bytes", r => r.Bytes)
        .Integer("compressed", r => r.Compressed)
        .Text("hash", r => r.Hash)
        .Reads(row => new PaneRow((int)row.Integer(0), (int)row.Integer(1), row.Integer(2), row.Integer(3), row.Integer(4), row.Text(5)));
}

/// <summary>What a pane run made: per text, made or current, with its panes, words and bytes.</summary>
internal sealed record PaneOutcome(string Text, bool Made, int Panes, long Words, long Bytes, long Compressed, TimeSpan Elapsed);

/// <summary>
/// The parallel view's panes (DOC-0224 §12, NOT-0216 D2): one row of <c>pane</c> per text and canonical chapter, read
/// from the served tables by <see cref="Panes"/> and written ahead of time. A product of the serving stage
/// (<c>build/products</c>, maker <c>serve</c>): <c>build --into</c> makes every text's panes once the database is filled,
/// <c>build --apply</c> and <c>forge panes</c> make again only the texts whose inputs moved, a text at a time, each
/// replaced in a transaction of its own. <c>panes/&lt;SLUG&gt;</c> lists what the table holds for the text and names
/// the products its panes were read from, which is what tells a text whose inputs moved.
/// </summary>
internal sealed class PaneBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    public static string Product(string text) => $"panes/{text}";

    /// <summary>The record files whose tables a pane reads: the records and their kinds, names, titles' bearers, occupations.</summary>
    private static readonly string[] RecordProducts =
    [
        "records/records", "records/names", "records/facts/titles", "records/facts/occupations",
        "records/facts/person-occupations", "records/derived/occupation-words",
    ];

    /// <summary>
    /// The panes of the texts named (every text when null): a text is made when <paramref name="force"/> is set, its
    /// product is missing or its inputs moved, or the table does not hold the panes its product lists.
    /// </summary>
    public async Task<IReadOnlyList<PaneOutcome>> Run(IReadOnlyCollection<string>? only, bool force, int parallel, CancellationToken cancellationToken)
    {
        var texts = await Texts(cancellationToken);
        var held = await Held(cancellationToken);
        var pairs = MappingProducts.Pairs(store).Select(MappingProducts.Split).ToList();
        var named = texts.Where(t => only is null || only.Contains(t.Slug)).ToList();
        var outcomes = new PaneOutcome[named.Count];
        var work = new List<Making>();
        for (var i = 0; i < named.Count; i++)
        {
            var text = named[i];
            var inputs = Inputs(text, texts, pairs);
            var product = Product(text.Slug);
            if (!force && !store.NeedsBuild(product, ProductKind.Machine, inputs) && Listed(product) == held.GetValueOrDefault(text.Id))
            {
                outcomes[i] = new PaneOutcome(text.Slug, false, held.GetValueOrDefault(text.Id), 0, 0, 0, TimeSpan.Zero);
                continue;
            }

            work.Add(new Making(i, text, inputs));
        }

        // Every chapter of every text to make in one queue, <parallel> at once, so no text's last chapters leave the
        // rest idle; a text is replaced and listed as soon as its last chapter is read.
        await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken },
            async (making, token) => await Plan(making, token));
        foreach (var making in work.Where(m => m.Chapters.Count == 0))
        {
            outcomes[making.Index] = await Finish(making, cancellationToken);
        }

        await Parallel.ForEachAsync(work.SelectMany(m => m.Chapters.Select(chapter => (Making: m, chapter.Book, chapter.Chapter))),
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
                    outcomes[making.Index] = await Finish(making, token);
                }
            });

        return outcomes;
    }

    /// <summary>A text whose panes are being made: its chapters, what it reaches, and the panes read so far.</summary>
    private sealed class Making(int index, TextRow text, List<ProductInput> inputs)
    {
        public int Index { get; } = index;

        public TextRow Text { get; } = text;

        public List<ProductInput> Inputs { get; } = inputs;

        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        public IReadOnlyList<(int Book, int Chapter)> Chapters { get; private set; } = [];

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

    /// <summary>A text's panes, in chapter order, replaced in the table and listed in its product.</summary>
    private async Task<PaneOutcome> Finish(Making making, CancellationToken cancellationToken)
    {
        var text = making.Text;
        List<PaneBody> panes = [.. making.Made.OrderBy(p => p.Book).ThenBy(p => p.Chapter)];
        await Replace(text.Id, panes, cancellationToken);
        using (var writer = store.Create(Product(text.Slug), ProductKind.Machine, PaneRow.Schema, ProductHeader.DescribeProducer("forge panes"), making.Inputs,
                   header => header.Notes.Add(new("pane-format", Panes.Format.ToString(CultureInfo.InvariantCulture)))))
        {
            foreach (var pane in panes)
            {
                writer.Write(new PaneRow(pane.Book, pane.Chapter, pane.Words, pane.Bytes, pane.Gzip.Length, pane.Hash));
            }

            writer.Complete();
        }

        store.Save();
        var outcome = new PaneOutcome(text.Slug, true, panes.Count, panes.Sum(p => (long)p.Words), panes.Sum(p => (long)p.Bytes),
            panes.Sum(p => (long)p.Gzip.Length), making.Clock.Elapsed);
        logger.LogInformation("Panes of {Text}: {Panes} chapters, {Words} words, {Bytes} bytes of JSON, {Compressed} compressed, in {Seconds:0.0} s",
            text.Slug, outcome.Panes, outcome.Words, outcome.Bytes, outcome.Compressed, outcome.Elapsed.TotalSeconds);
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

    /// <summary>How many panes the table holds per text.</summary>
    private async Task<Dictionary<int, int>> Held(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Panes.GroupBy(p => p.TextId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
    }

    /// <summary>How many panes a text's product lists, or -1 where there is none.</summary>
    private int Listed(string product)
    {
        if (!store.Exists(product))
        {
            return -1;
        }

        using var reader = store.Open(product, PaneRow.Schema);
        return reader.Rows().Count();
    }

    /// <summary>
    /// The products the tables a text's panes read were loaded from, where this store holds them: the text's own
    /// products and names; every pair it is in, with the other text's words and text (whose kind decides a witness);
    /// the records; the Strong candidates; for Greek, every Greek text's words (a gloss by spelling) and the lexicon;
    /// for Ge'ez, the lexicons.
    /// </summary>
    internal List<ProductInput> Inputs(TextRow text, IReadOnlyList<TextRow> texts, IReadOnlyList<(string From, string To)> pairs)
    {
        var products = new List<string>
        {
            $"texts/{text.Slug}/words", $"texts/{text.Slug}/verses", $"texts/{text.Slug}/notes", $"texts/{text.Slug}/books",
            $"texts/{text.Slug}/text", $"texts/{text.Slug}/frame", NameProducts.Projected(text.Slug),
        };
        foreach (var (from, to) in pairs.Where(p => p.From == text.Slug || p.To == text.Slug))
        {
            var other = from == text.Slug ? to : from;
            products.Add(MappingProducts.Of(MappingProducts.Pair(from, to), MappingProducts.Final));
            products.Add($"texts/{other}/words");
            products.Add($"texts/{other}/text");
        }

        products.AddRange(RecordProducts);
        products.Add(TableProducts.Product("word_strong"));
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

    /// <summary>The chapters a text's panes are made for, and the chapters it reaches in each book.</summary>
    private async Task Plan(Making making, CancellationToken cancellationToken)
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
        making.Planned(Panes.Chapters(held), reach);
    }

    /// <summary>A text's panes replaced in one transaction: a failure leaves the panes it had.</summary>
    private async Task Replace(int textId, List<PaneBody> panes, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = new NpgsqlCommand("DELETE FROM pane WHERE text_id = @text", connection, transaction))
        {
            delete.Parameters.AddWithValue("text", textId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(
                         "COPY pane (text_id, canonical_book, canonical_chapter, body, hash) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var pane in panes)
            {
                await copy.StartRowAsync(cancellationToken);
                await copy.WriteAsync(textId, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(pane.Book, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(pane.Chapter, NpgsqlDbType.Integer, cancellationToken);
                await copy.WriteAsync(pane.Gzip, NpgsqlDbType.Bytea, cancellationToken);
                await copy.WriteAsync(pane.Hash, NpgsqlDbType.Text, cancellationToken);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
