using Essenthos.Core.Products;

namespace Essenthos.Core.Desk;

/// <summary>One product's rows as text, with its column names; a column is found by name.</summary>
internal sealed record BuildTable(IReadOnlyList<string> Columns, IReadOnlyList<string?[]> Rows)
{
    public int Column(string name) =>
        Columns.ToList().IndexOf(name) is var index and >= 0
            ? index
            : throw new InvalidDataException($"The build's file has no column '{name}'; it has {string.Join(", ", Columns)}.");
}

/// <summary>
/// The files the build reads and the site is loaded from (DOC-0224): the products under <c>Essenthos/</c>
/// of <see cref="DeskPaths.BuildResources"/>. A list in the console says what they hold, because they and
/// not the review lists are what reaches the site. The console never writes them: a decision is a line of
/// the owner's log, which an agent applies to the file it concerns (<c>forge owner-apply</c>).
/// </summary>
internal sealed class BuildFiles(DeskPaths paths)
{
    private readonly Lock _lock = new();

    private readonly Dictionary<string, (DateTime Written, BuildTable Table)> _read = new(StringComparer.Ordinal);

    public string Folder => Path.Combine(paths.BuildResources, ProductStore.EssenthosFolder);

    /// <summary>
    /// A product by its name under <c>Essenthos/</c> without an extension, or null where the build has not
    /// made it in this folder: the record files are there once the cut-over export has written them.
    /// Kept while the file is unchanged, since every page and count asks.
    /// </summary>
    public BuildTable? Read(string product)
    {
        var store = new ProductStore(paths.BuildResources);
        if (store.Find(product) is not { } relative)
        {
            return null;
        }

        var path = store.FullPath(relative);
        var written = File.GetLastWriteTimeUtc(path);
        lock (_lock)
        {
            if (_read.TryGetValue(path, out var known) && known.Written == written)
            {
                return known.Table;
            }
        }

        BuildTable table;
        using (var reader = store.Open(product))
        {
            table = new BuildTable([.. reader.Columns.Select(c => c.Name)], [.. reader.Rows().Select(row => (string?[])row.Clone())]);
        }

        lock (_lock)
        {
            _read[path] = (written, table);
        }

        return table;
    }
}
