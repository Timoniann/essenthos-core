using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Descriptions;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Build;

/// <summary>One record's card in one language of names, and its line in the product's language of words.</summary>
internal sealed record RecordCardRow(
    string Names, string Slug, string Kind, string Name, string? LocalName, string? Distinguisher, string? Sex, string? AlsoCalled,
    string? Line, bool Quoted, string? LocalLine, string? DescriptorLine)
{
    public static readonly ProductSchema<RecordCardRow> Schema = new ProductSchema<RecordCardRow>()
        .Text("names", r => r.Names)
        .Text("slug", r => r.Slug)
        .Text("kind", r => r.Kind)
        .Text("name", r => r.Name)
        .OptionalText("local_name", r => r.LocalName)
        .OptionalText("distinguisher", r => r.Distinguisher)
        .OptionalText("sex", r => r.Sex)
        .OptionalText("also_called", r => r.AlsoCalled)
        .OptionalText("line", r => r.Line)
        .Boolean("quoted", r => r.Quoted)
        .OptionalText("local_line", r => r.LocalLine)
        .OptionalText("descriptor_line", r => r.DescriptorLine)
        .Reads(row => new RecordCardRow(row.Text(0), row.Text(1), row.Text(2), row.Text(3), row.TextOrNull(4), row.TextOrNull(5),
            row.TextOrNull(6), row.TextOrNull(7), row.TextOrNull(8), row.Boolean(9), row.TextOrNull(10), row.TextOrNull(11)));

    public static RecordCardRow Of(string names, string prose, RecordCard card) => new(
        names, card.Slug, card.Kind, card.Name, card.LocalName, card.Distinguisher, card.Sex, card.AlsoCalled,
        card.Line(prose), card.Quoted(prose), card.Local, card.Described);

    public RecordCard Card => new(0, Slug, Kind, Name, LocalName, Distinguisher, Sex, AlsoCalled, LocalLine, DescriptorLine);
}

/// <summary>One family's document in one language of names, in the product's language of words.</summary>
internal sealed record FamilyComponentRow(long Family, string Names, long People, long Bytes, long Gzip, string Hash, string Body)
{
    public static readonly ProductSchema<FamilyComponentRow> Schema = new ProductSchema<FamilyComponentRow>()
        .Integer("family", r => r.Family)
        .Text("names", r => r.Names)
        .Integer("people", r => r.People)
        .Integer("bytes", r => r.Bytes)
        .Integer("gzip", r => r.Gzip)
        .Text("hash", r => r.Hash)
        .Text("body", r => r.Body)
        .Reads(row => new FamilyComponentRow(row.Integer(0), row.Text(1), row.Integer(2), row.Integer(3), row.Integer(4), row.Text(5), row.Text(6)));
}

/// <summary>What a family run did: the products made, the rows the tables took, and the largest document.</summary>
internal sealed record FamilyOutcome(int Made, long Rows, long LargestGzip, string Largest, TimeSpan Elapsed);

/// <summary>
/// The cards and the family documents (TSK-1666), products of the serving stage (<c>build/products</c>, maker <c>serve</c>):
/// <c>record-cards/{prose}</c> holds every record's name in every language of names the corpus holds names in and its line in
/// that language of words (<see cref="RecordCards"/>), loaded as <c>record_name</c> and <c>record_line</c>;
/// <c>family-components/{prose}</c> holds every family's document (<see cref="FamilyDocuments"/>), loaded as
/// <c>family_component</c>. A product is made again where its inputs or its composer's version moved; a table that does not
/// hold the rows of a current product (a fresh database) takes them from the product, and nothing is composed.
/// </summary>
internal sealed class FamilyBuild(string connectionString, ProductStore store, IServiceProvider services, ILogger logger)
{
    public static string CardsProduct(string prose) => $"record-cards/{prose}";

    public static string FamiliesProduct(string prose) => $"family-components/{prose}";

    private const string Producer = "forge families";

    private const string VersionNote = "version";

    /// <summary>Over this a document is too large to send whole to every reader of a tree, and the run says so.</summary>
    public const long LargestGzipWarned = 150 * 1024;

    /// <summary>How many records and families the run composes again from the tables to hold the stored ones to.</summary>
    private const int Sample = 12;

    public async Task<FamilyOutcome> Run(bool force, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var names = (await db.EntityNameForms.Select(f => f.Language).Distinct().ToListAsync(cancellationToken))
            .Append(RecordCards.English).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var slugs = await db.Entities.OrderBy(e => e.Slug).Select(e => e.Slug).ToListAsync(cancellationToken);
        var texts = await db.Texts.OrderBy(t => t.Slug).Select(t => t.Slug).ToListAsync(cancellationToken);
        var cardInputs = store.Inputs(RecordFiles.All.Select(f => store.Find(f.Product))
            .Concat(texts.Select(t => store.Find($"texts/{t}/index/spellings")))
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        var made = 0;
        long rows = 0;
        var cards = new Dictionary<string, List<RecordCardRow>>(StringComparer.Ordinal);
        foreach (var prose in FamilyDocuments.ProseLanguages)
        {
            var product = CardsProduct(prose);
            if (!force && Current<RecordCardRow>(product, cardInputs, RecordCards.Version) is { } stored)
            {
                cards[prose] = stored;
                continue;
            }

            var composed = new List<RecordCardRow>();
            foreach (var language in names)
            {
                foreach (var batch in slugs.Chunk(FamilyDocuments.Batch))
                {
                    composed.AddRange((await RecordCards.Of(db, batch, language, prose, cancellationToken))
                        .Select(card => RecordCardRow.Of(language, prose, card)));
                }
            }

            composed = [.. composed.OrderBy(r => r.Names, StringComparer.Ordinal).ThenBy(r => r.Slug, StringComparer.Ordinal)];
            Write(product, RecordCardRow.Schema, composed, cardInputs, RecordCards.Version);
            logger.LogInformation("Record cards in {Prose}: {Rows} cards over {Names} languages of names in {Seconds:0.0} s",
                prose, composed.Count, names.Count, clock.Elapsed.TotalSeconds);
            cards[prose] = composed;
            made++;
        }

        var members = await db.FamilyMembers.OrderBy(m => m.Slug).Select(m => new { m.Slug, m.Sex, m.Component }).ToListAsync(cancellationToken);
        var ties = await db.FamilyTies
            .Select(t => new { t.Component, Tie = new FamilyDocuments.Tie(t.From!.Slug, t.Type, t.To!.Slug, t.CanonicalBook, t.CanonicalChapter, t.CanonicalVerse) })
            .ToListAsync(cancellationToken);
        var relations = await FamilyDocuments.Relations(db, null, cancellationToken);
        var families = new Dictionary<string, List<FamilyComponentRow>>(StringComparer.Ordinal);
        foreach (var prose in FamilyDocuments.ProseLanguages)
        {
            var product = FamiliesProduct(prose);
            var inputs = store.Inputs(new[] { CardsProduct(prose), FamilyGraph.TiesProduct, FamilyGraph.MembersProduct, "records/facts/relationships" }
                .Select(store.Find).OfType<string>());
            if (!force && Current<FamilyComponentRow>(product, inputs, FamilyDocuments.Version) is { } stored)
            {
                families[prose] = stored;
                continue;
            }

            var composed = new List<FamilyComponentRow>();
            var tiesOf = ties.ToLookup(t => t.Component, t => t.Tie);
            foreach (var language in cards[prose].Select(c => c.Names).Distinct(StringComparer.Ordinal))
            {
                var shown = cards[prose].Where(c => c.Names == language).ToDictionary(c => c.Slug, c => c.Card, StringComparer.Ordinal);
                foreach (var family in members.GroupBy(m => m.Component).OrderBy(g => g.Key))
                {
                    var people = FamilyDocuments.People(family.Select(m => (m.Slug, m.Sex)), relations);
                    var (body, hash) = FamilyDocuments.Compose(family.Key, people, [.. tiesOf[family.Key]], shown, prose);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    composed.Add(new FamilyComponentRow(family.Key, language, people.Count, bytes.Length, Gzipped(bytes), hash, body));
                }
            }

            Write(product, FamilyComponentRow.Schema, composed, inputs, FamilyDocuments.Version);
            logger.LogInformation("Family documents in {Prose}: {Rows} documents in {Seconds:0.0} s", prose, composed.Count, clock.Elapsed.TotalSeconds);
            families[prose] = composed;
            made++;
        }

        store.Save();
        rows += await Match(cards, families, cancellationToken);
        await Check(db, cards, families, names, cancellationToken);

        var largest = families.Values.SelectMany(f => f).MaxBy(f => f.Gzip);
        if (largest is { Gzip: > LargestGzipWarned })
        {
            logger.LogWarning("The largest family document, family {Family} of {People} people, is {Gzip} bytes gzipped, over {Most}",
                largest.Family, largest.People, largest.Gzip, LargestGzipWarned);
        }

        return new FamilyOutcome(made, rows, largest?.Gzip ?? 0,
            largest is null ? "" : $"family {largest.Family}, {largest.People} people, {largest.Bytes} bytes, {largest.Gzip} gzipped",
            clock.Elapsed);
    }

    /// <summary>A product's rows where it is current for these inputs and was made by this version; null where it must be made.</summary>
    private List<T>? Current<T>(string product, IReadOnlyList<ProductInput> inputs, int version) where T : class
    {
        if (store.NeedsBuild(product, ProductKind.Machine, inputs))
        {
            return null;
        }

        using var reader = store.Open(product, Schema<T>());
        var said = reader.Header?.Notes.FirstOrDefault(n => n.Key == VersionNote).Value;
        return said == version.ToString(CultureInfo.InvariantCulture) ? [.. reader.Rows()] : null;
    }

    private static ProductSchema<T> Schema<T>() where T : class =>
        typeof(T) == typeof(RecordCardRow) ? (ProductSchema<T>)(object)RecordCardRow.Schema : (ProductSchema<T>)(object)FamilyComponentRow.Schema;

    private void Write<T>(string product, ProductSchema<T> schema, IEnumerable<T> rows, IReadOnlyList<ProductInput> inputs, int version)
    {
        using var writer = store.Create(product, ProductKind.Machine, schema, ProductHeader.DescribeProducer(Producer), inputs,
            header => header.Notes.Add(new(VersionNote, version.ToString(CultureInfo.InvariantCulture))));
        foreach (var row in rows)
        {
            writer.Write(row);
        }

        writer.Complete();
    }

    private static long Gzipped(byte[] body)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(body);
        }

        return buffer.Length;
    }

    /// <summary>
    /// The tables made to hold exactly the products' rows: a language of words whose rows differ in count or hash from its
    /// product's is replaced in one transaction, and <c>record_name</c> (the same in every language of words) from the English
    /// cards. Returns the rows written.
    /// </summary>
    private async Task<long> Match(
        IReadOnlyDictionary<string, List<RecordCardRow>> cards,
        IReadOnlyDictionary<string, List<FamilyComponentRow>> families,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        long written = 0;
        var english = cards[RecordCards.English];
        var nameHash = Digest(english.Select(r => string.Join('\u001f', r.Names, r.Slug, r.Kind, r.Name, r.LocalName, r.Distinguisher, r.Sex, r.AlsoCalled)));
        if (await Held(connection, "SELECT count(*), coalesce(md5(string_agg(concat_ws(chr(31), names, slug, kind, name, coalesce(local_name, ''), coalesce(distinguisher, ''), coalesce(sex, ''), coalesce(also_called, '')), chr(30) ORDER BY names COLLATE \"C\", slug COLLATE \"C\")), '') FROM record_name", null, cancellationToken)
            != (english.Count, nameHash))
        {
            await Replace(connection, "DELETE FROM record_name", null,
                "COPY record_name (names, slug, kind, name, local_name, distinguisher, sex, also_called) FROM STDIN (FORMAT BINARY)",
                english, async (copy, r) =>
                {
                    await copy.WriteAsync(r.Names, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Slug, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Kind, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Name, NpgsqlDbType.Text, cancellationToken);
                    await Nullable(copy, r.LocalName, cancellationToken);
                    await Nullable(copy, r.Distinguisher, cancellationToken);
                    await Nullable(copy, r.Sex, cancellationToken);
                    await Nullable(copy, r.AlsoCalled, cancellationToken);
                }, cancellationToken);
            written += english.Count;
        }

        foreach (var (prose, rows) in cards)
        {
            var hash = Digest(rows.Select(r => string.Join('\u001f', r.Names, r.Slug, r.Line, r.Quoted ? "t" : "f", r.LocalLine, r.DescriptorLine)));
            if (await Held(connection, "SELECT count(*), coalesce(md5(string_agg(concat_ws(chr(31), names, slug, coalesce(line, ''), CASE WHEN quoted THEN 't' ELSE 'f' END, coalesce(local_line, ''), coalesce(descriptor_line, '')), chr(30) ORDER BY names COLLATE \"C\", slug COLLATE \"C\")), '') FROM record_line WHERE prose = @prose", prose, cancellationToken)
                == (rows.Count, hash))
            {
                continue;
            }

            await Replace(connection, "DELETE FROM record_line WHERE prose = @prose", prose,
                "COPY record_line (prose, names, slug, line, quoted, local_line, descriptor_line) FROM STDIN (FORMAT BINARY)",
                rows, async (copy, r) =>
                {
                    await copy.WriteAsync(prose, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Names, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Slug, NpgsqlDbType.Text, cancellationToken);
                    await Nullable(copy, r.Line, cancellationToken);
                    await copy.WriteAsync(r.Quoted, NpgsqlDbType.Boolean, cancellationToken);
                    await Nullable(copy, r.LocalLine, cancellationToken);
                    await Nullable(copy, r.DescriptorLine, cancellationToken);
                }, cancellationToken);
            written += rows.Count;
        }

        foreach (var (prose, rows) in families)
        {
            var hash = Digest(rows.OrderBy(r => r.Names, StringComparer.Ordinal).ThenBy(r => r.Family).Select(r => $"{r.Names}\u001f{r.Family}\u001f{r.Hash}"));
            if (await Held(connection, "SELECT count(*), coalesce(md5(string_agg(concat_ws(chr(31), names, component, hash), chr(30) ORDER BY names COLLATE \"C\", component)), '') FROM family_component WHERE prose = @prose", prose, cancellationToken)
                == (rows.Count, hash))
            {
                continue;
            }

            await Replace(connection, "DELETE FROM family_component WHERE prose = @prose", prose,
                "COPY family_component (component, prose, names, body, hash, people) FROM STDIN (FORMAT BINARY)",
                rows, async (copy, r) =>
                {
                    await copy.WriteAsync(checked((int)r.Family), NpgsqlDbType.Integer, cancellationToken);
                    await copy.WriteAsync(prose, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Names, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Body, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(r.Hash, NpgsqlDbType.Text, cancellationToken);
                    await copy.WriteAsync(checked((int)r.People), NpgsqlDbType.Integer, cancellationToken);
                }, cancellationToken);
            written += rows.Count;
        }

        if (written > 0)
        {
            await using var analyze = new NpgsqlCommand("ANALYZE record_name, record_line, family_component", connection);
            await analyze.ExecuteNonQueryAsync(cancellationToken);
        }

        return written;
    }

    /// <summary>The md5 the database's <c>string_agg(…, chr(30))</c> gives over the same fields, empty for no rows.</summary>
    private static string Digest(IEnumerable<string> lines)
    {
        var joined = string.Join('\u001e', lines);
        return joined.Length == 0 ? "" : Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private static async Task<(int, string)> Held(NpgsqlConnection connection, string sql, string? prose, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (prose is not null)
        {
            command.Parameters.AddWithValue("prose", prose);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (checked((int)reader.GetInt64(0)), reader.GetString(1));
    }

    private static async Task Nullable(NpgsqlBinaryImporter copy, string? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await copy.WriteNullAsync(cancellationToken);
        }
        else
        {
            await copy.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);
        }
    }

    private static async Task Replace<T>(
        NpgsqlConnection connection, string delete, string? prose, string copySql, IEnumerable<T> rows,
        Func<NpgsqlBinaryImporter, T, Task> write, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(delete, connection, transaction))
        {
            if (prose is not null)
            {
                command.Parameters.AddWithValue("prose", prose);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var copy = await connection.BeginBinaryImportAsync(copySql, cancellationToken))
        {
            foreach (var row in rows)
            {
                await copy.StartRowAsync(cancellationToken);
                await write(copy, row);
            }

            await copy.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// A sample of the stored cards and documents composed again from the tables as they stand: a record drawn from every part
    /// of the list in two pairs of languages, and the families of the people drawn. A difference fails the run, naming it.
    /// </summary>
    private async Task Check(
        AppDbContext db,
        IReadOnlyDictionary<string, List<RecordCardRow>> cards,
        IReadOnlyDictionary<string, List<FamilyComponentRow>> families,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var differences = new List<string>();
        var pairs = new[] { (Prose: RecordCards.English, Names: RecordCards.English), (Prose: "ukr", Names: names.Contains("ukr") ? "ukr" : RecordCards.English) };
        foreach (var (prose, language) in pairs)
        {
            var stored = cards[prose].Where(r => r.Names == language).ToList();
            var drawn = Enumerable.Range(0, Sample).Select(i => stored[(int)((long)i * stored.Count / Sample)]).ToList();
            var again = (await RecordCards.Of(db, [.. drawn.Select(r => r.Slug)], language, prose, cancellationToken))
                .ToDictionary(c => c.Slug, c => RecordCardRow.Of(language, prose, c), StringComparer.Ordinal);
            differences.AddRange(drawn.Where(r => again.GetValueOrDefault(r.Slug) != r).Select(r => $"card {prose}/{language} {r.Slug}"));

            var persons = await db.FamilyMembers.OrderBy(m => m.Slug).Select(m => m.Slug).ToListAsync(cancellationToken);
            foreach (var slug in Enumerable.Range(0, Sample).Select(i => persons[(int)((long)i * persons.Count / Sample)]))
            {
                if (await FamilyDocuments.Read(db, slug, language, prose, cancellationToken) is { } read
                    && families[prose].FirstOrDefault(f => f.Family == read.Family && f.Names == language)?.Hash != read.Hash)
                {
                    differences.Add($"family of {slug} {prose}/{language}");
                }
            }
        }

        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"Stored cards or family documents differ from those composed again from the tables: {string.Join(", ", differences)}. " +
                "Raise RecordCards.Version or FamilyDocuments.Version with a change to what they compose, then build again.");
        }

        logger.LogInformation("Family check: {Cards} cards and {Families} families composed again equal the stored ones", 2 * Sample, 2 * Sample);
    }
}
