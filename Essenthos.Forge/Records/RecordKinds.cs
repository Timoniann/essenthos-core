using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

/// <summary>A record by slug with only what the name producers read of it: its kind and the record it is another name for.</summary>
internal sealed record RecordKind(string Slug, string Kind, string? AnotherNameFor)
{
    public static readonly ProductSchema<RecordKind> Schema = new ProductSchema<RecordKind>()
        .Text("slug", r => r.Slug)
        .Text("kind", r => r.Kind)
        .OptionalText("another_name_for", r => r.AnotherNameFor)
        .Reads(row => new RecordKind(row.Text(0), row.Text(1), row.TextOrNull(2)));
}

/// <summary>
/// <c>records/derived/kinds</c>: the three columns of <c>records/records</c> the names-first pairs and the spellings index read,
/// made by records-derive. Its digest moves only when a slug, a kind or an alias does, so an edit of any other column of a
/// record (a sex, its source, a tribe) leaves those producers current.
/// </summary>
internal static class RecordKinds
{
    public const string Product = "records/derived/kinds";

    public static (string Product, long Rows) Write(ProductStore from, ProductStore into)
    {
        var rows = RecordProducers.Read(from, "records/records");
        var columns = RecordProducers.File("records/records").Parsed.Select(c => c.Name).ToList();
        var (slug, kind, other) = (columns.IndexOf("slug"), columns.IndexOf("kind"), columns.IndexOf("another_name_for"));
        using var writer = into.Create(Product, ProductKind.Machine, RecordKind.Schema, ProductHeader.DescribeProducer(RecordProducers.Producer + " kinds"),
            from.Inputs(RecordProducers.Paths(from, "records/records")));
        writer.WriteAll(rows.Select(r => new RecordKind(r[slug]!, r[kind]!, r[other])).OrderBy(r => r.Slug, StringComparer.Ordinal));
        writer.Complete();
        return (Product, writer.Rows);
    }
}
