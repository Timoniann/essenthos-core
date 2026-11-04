using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Postgres;

namespace Essenthos.Core.Conversion;

/// <summary>
/// A text's words product into a serving table with <c>COPY</c>: every column to one of its name, the key over its
/// parts, the text as a constant. The <c>strong</c> column is left out where the words product says the terms of the
/// numbering laid onto it do not let it be served (<see cref="StrongTerms"/>), so the table holds no number there
/// while the mapping, which reads the product, keeps every one.
/// </summary>
internal static class TextWordLoad
{
    public static IReadOnlyList<CopyColumn> Columns(EssxTable table, string slug, bool strongServed) =>
        [.. EssxCopy.DefaultColumns(table, ("text", slug)).Where(column => strongServed || column.Source != "strong")];

    /// <summary>Whether a text's words product lets the served database carry its Strong numbers.</summary>
    public static bool StrongServed(ProductStore store, string slug)
    {
        using var reader = store.Open(TextWord.ProductOf(slug));
        return StrongTerms.MayServe(reader.Header);
    }

    public static async Task<CopyResult> Load(string connectionString, ProductStore store, string slug, string targetTable, int streams = 16,
        CancellationToken cancellation = default)
    {
        var path = store.FullPath(store.Find(TextWord.ProductOf(slug))
                                  ?? throw new FileNotFoundException($"{slug} has no words product under {store.ResourcesPath}."));
        IReadOnlyList<CopyColumn> columns;
        using (var file = EssxReader.Open(path))
        {
            columns = Columns(file.Tables[0], slug, StrongServed(store, slug));
        }

        return await EssxCopy.Load(connectionString, path, targetTable, columns, streams, CopyMode.Text, cancellation: cancellation);
    }
}
