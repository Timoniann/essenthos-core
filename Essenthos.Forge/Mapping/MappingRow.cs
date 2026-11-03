using System.Globalization;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// One method's claim that some words of one text correspond to some words of another, as a method
/// file of <c>mapping/&lt;FROM&gt;-&lt;TO&gt;/&lt;method&gt;</c> holds it (DOC-0224 §6.1). A link several
/// methods arrived at is a row in each of their files, with the same <see cref="Id"/>.
/// </summary>
/// <param name="Id">The link's id in the corpus it was exported from, kept so that ids never change; null for a row a generator wrote since.</param>
/// <param name="From">The words of the first text, own keys in key order, space-separated; empty for <c>omits</c>.</param>
/// <param name="To">The words of the second text, the same way; empty for <c>expands</c>.</param>
/// <param name="Relation">renders, equals, expands, omits or transposes.</param>
/// <param name="Method">The method file the row belongs to (<see cref="MappingMethods"/>).</param>
/// <param name="Confidence">Null where a source states it or a person ruled it.</param>
/// <param name="Source">Who says so: the file, the algorithm and its version, or the person.</param>
/// <param name="Evidence">What only this method knows: an EVIDENTIA decision and its tier, a Strong pairing's kind, the note a source left. Never loaded.</param>
/// <param name="Roles">Words of the first text that only write another word's form or belong to its phrase: <c>key=role@head</c>, space-separated.</param>
/// <param name="Status">Null while the row stands; what the merge decided otherwise (<see cref="MappingStatus"/>).</param>
internal sealed record MappingRow(
    long? Id,
    string From,
    string To,
    string Relation,
    string Method,
    double? Confidence,
    string Source,
    string? Evidence,
    string? Roles,
    string? Status)
{
    public static readonly ProductSchema<MappingRow> Schema = new ProductSchema<MappingRow>()
        .OptionalInteger("id", r => r.Id)
        .Text("from", r => r.From)
        .Text("to", r => r.To)
        .Text("relation", r => r.Relation)
        .Text("method", r => r.Method)
        .OptionalText("confidence", r => MappingText.Confidence(r.Confidence))
        .Text("source", r => r.Source)
        .OptionalText("evidence", r => r.Evidence)
        .OptionalText("roles", r => r.Roles)
        .OptionalText("status", r => r.Status)
        .Reads(row => new MappingRow(
            row.IntegerOrNull(0), row.Text(1), row.Text(2), row.Text(3), row.Text(4),
            MappingText.Confidence(row.TextOrNull(5)), row.Text(6), row.TextOrNull(7), row.TextOrNull(8), row.TextOrNull(9)));

    /// <summary>The words the row names, which decide whether two rows are one link.</summary>
    public string Shape => From + "|" + To;
}

/// <summary>
/// One claim of a link of the merged mapping, <c>mapping/&lt;pair&gt;/final</c>: the only mapping file a
/// database receives. A link is the run of rows with one <see cref="Id"/>; the row marked
/// <see cref="Settled"/> is the claim the link shows as its own.
/// </summary>
internal sealed record FinalRow(
    long Id,
    string From,
    string To,
    string Relation,
    string? Roles,
    bool Settled,
    string Method,
    double? Confidence,
    string Source)
{
    public static readonly ProductSchema<FinalRow> Schema = new ProductSchema<FinalRow>()
        .Integer("id", r => r.Id)
        .Text("from", r => r.From)
        .Text("to", r => r.To)
        .Text("relation", r => r.Relation)
        .OptionalText("roles", r => r.Roles)
        .Boolean("settled", r => r.Settled)
        .Text("method", r => r.Method)
        .OptionalText("confidence", r => MappingText.Confidence(r.Confidence))
        .Text("source", r => r.Source)
        .Reads(row => new FinalRow(
            row.Integer(0), row.Text(1), row.Text(2), row.Text(3), row.TextOrNull(4), row.Boolean(5), row.Text(6),
            MappingText.Confidence(row.TextOrNull(7)), row.Text(8)));
}

/// <summary>A line of a pair's merge report, <c>mapping/&lt;pair&gt;/report</c>.</summary>
/// <param name="Kind">rows, links, claims, withdrawn, refused, stale, needs, unlinked-verses, unchecked.</param>
/// <param name="Method">The method the line is about.</param>
/// <param name="By">For withdrawn rows, the method that won; for needs, what must run.</param>
internal sealed record ReportRow(string Kind, string Method, string By, long Rows, string Note)
{
    public static readonly ProductSchema<ReportRow> Schema = new ProductSchema<ReportRow>()
        .Text("kind", r => r.Kind)
        .Text("method", r => r.Method)
        .Text("by", r => r.By)
        .Integer("rows", r => r.Rows)
        .Text("note", r => r.Note)
        .Reads(row => new ReportRow(row.Text(0), row.Text(1), row.Text(2), row.Integer(3), row.Text(4)));
}

/// <summary>
/// The text hash of every verse a pair's method files cite, as it was when they were made,
/// <c>mapping/&lt;pair&gt;/hashes</c>. The merge compares it with each text's verse manifest: a verse
/// whose hash moved has rows that may name other words now (DOC-0224 §6.2).
/// </summary>
internal sealed record VerseHashRow(string Text, VerseKey Own, string Hash)
{
    public static readonly ProductSchema<VerseHashRow> Schema = new ProductSchema<VerseHashRow>()
        .Text("text", r => r.Text)
        .VerseKey("own", r => r.Own)
        .Text("h_text", r => r.Hash)
        .Reads(row => new VerseHashRow(row.Text(0), VerseKey.Parse(row.Text(1)), row.Text(2)));
}

/// <summary>
/// A ruling on a pair's mapping, <c>mapping/&lt;pair&gt;/rulings</c>, kept by hand (DOC-0224 §6.4.4).
/// <c>refuse</c> with both sides suppresses that shape whatever proposes it; <c>refuse</c> with one side
/// and a source takes those words out of that source's rows; <c>accept</c> pins a shape, which then wins
/// every competition and is written as a link of its own where no method proposes it.
/// </summary>
/// <param name="Source">The beginning of the source a word ruling is about; empty for any.</param>
internal sealed record RulingRow(string Action, string From, string To, string Relation, string Source, string Reason)
{
    public const string Refuse = "refuse";
    public const string Accept = "accept";

    public static readonly ProductSchema<RulingRow> Schema = new ProductSchema<RulingRow>()
        .Text("action", r => r.Action)
        .Text("from", r => r.From)
        .Text("to", r => r.To)
        .Text("relation", r => r.Relation)
        .Text("source", r => r.Source)
        .Text("reason", r => r.Reason)
        .Reads(row => new RulingRow(row.Text(0), row.Text(1), row.Text(2), row.Text(3), row.Text(4), row.Text(5)));
}

/// <summary>Where each pair's new links take their ids from: <c>mapping/pairs</c>, one ordinal per pair, never reused.</summary>
internal sealed record PairOrdinalRow(string Pair, long Ordinal)
{
    public static readonly ProductSchema<PairOrdinalRow> Schema = new ProductSchema<PairOrdinalRow>()
        .Text("pair", r => r.Pair)
        .Integer("ordinal", r => r.Ordinal)
        .Reads(row => new PairOrdinalRow(row.Text(0), row.Integer(1)));
}

/// <summary>What the merge writes into a method file's <c>status</c> column.</summary>
internal static class MappingStatus
{
    public static string WithdrawnBy(string method) => $"withdrawn-by {method}";

    public const string RefusedByRuling = "refused-by ruling";

    public const string NarrowedByRuling = "narrowed-by ruling";

    public const string Stale = "stale";
}

/// <summary>The product names of the mapping.</summary>
internal static class MappingProducts
{
    public const string Folder = "mapping";

    public const string Final = "final";

    public const string Report = "report";

    public const string Hashes = "hashes";

    public const string Rulings = "rulings";

    public const string PairOrdinals = Folder + "/pairs";

    /// <summary>The products of a pair folder that are not a method's claims.</summary>
    public static readonly string[] NotMethods = [Final, Report, Hashes, Rulings];

    public static string Pair(string from, string to) => $"{from}-{to}";

    public static string Of(string pair, string name) => $"{Folder}/{pair}/{name}";

    public static (string From, string To) Split(string pair)
    {
        var dash = pair.IndexOf('-');
        if (dash <= 0 || dash == pair.Length - 1 || pair.IndexOf('-', dash + 1) >= 0)
        {
            throw new FormatException($"'{pair}' is not a pair (FROM-TO).");
        }

        return (pair[..dash], pair[(dash + 1)..]);
    }

    /// <summary>The pairs that have a folder under <c>mapping/</c> in this store, in ordinal order.</summary>
    public static List<string> Pairs(ProductStore store)
    {
        var folder = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, Folder);
        return Directory.Exists(folder)
            ? [.. Directory.GetDirectories(folder).Select(Path.GetFileName).OfType<string>().Where(name => name.Contains('-')).Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>The method files of a pair folder, by product name.</summary>
    public static List<string> Methods(ProductStore store, string pair)
    {
        var folder = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, Folder, pair);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return
        [
            .. Directory.GetFiles(folder)
                .Where(path => !Path.GetFileName(path).StartsWith('.'))
                .Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .Where(name => !NotMethods.Contains(name))
                .Distinct().Order(StringComparer.Ordinal),
        ];
    }
}

/// <summary>How the mapping files write what is not a key.</summary>
internal static class MappingText
{
    public static string? Confidence(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    public static double? Confidence(string? text) => text is null ? null : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Own keys in key order, distinct, space-separated.</summary>
    public static string Keys(IEnumerable<WordKey> keys) => string.Join(' ', keys.Distinct().Order().Select(k => k.Own));

    public static IEnumerable<string> Split(string keys) =>
        keys.Length == 0 ? [] : keys.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The first key a row is sorted by: its first word of the first text, or of the second for an omission.</summary>
    public static (int Side, WordKey Key) SortKey(string from, string to, string fromText, string toText)
    {
        if (from.Length > 0)
        {
            var space = from.IndexOf(' ');
            return (0, WordKey.Parse(space < 0 ? from : from[..space], fromText));
        }

        if (to.Length > 0)
        {
            var space = to.IndexOf(' ');
            return (1, WordKey.Parse(space < 0 ? to : to[..space], toText));
        }

        return (2, default);
    }
}
