using System.Globalization;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Products;

/// <summary>One product as the last build left it.</summary>
/// <param name="Product">Relative to <c>Resources/</c>.</param>
/// <param name="Sha256">Of the product file, which a product reading this one cites as an input.</param>
/// <param name="Inputs">What it was made from, as its header says.</param>
public sealed record ManifestEntry(
    string Product,
    string Sha256,
    long Rows,
    DateTimeOffset Built,
    string Producer,
    IReadOnlyList<ProductInput> Inputs)
{
    public bool Equals(ManifestEntry? other) =>
        other is not null
        && Product == other.Product
        && Sha256 == other.Sha256
        && Rows == other.Rows
        && Built == other.Built
        && Producer == other.Producer
        && Inputs.SequenceEqual(other.Inputs);

    public override int GetHashCode() => HashCode.Combine(Product, Sha256, Rows, Built, Producer, Inputs.Count);
}

/// <summary>
/// <c>Essenthos/build/manifest.tsv</c>: every product the build has made, with its hash, its row
/// count, when and by what, and the inputs it was made from. The rule of the build reads from here:
/// a product is made when it has no entry, its file is missing or differs from the entry, or any
/// input's hash now differs from the one the entry records.
/// </summary>
public sealed class BuildManifest
{
    public const string Product = "Essenthos/build/manifest.tsv";

    private const string InputsJoin = " ";

    private static readonly ProductSchema<ManifestEntry> Schema = new ProductSchema<ManifestEntry>()
        .Text("product", entry => entry.Product)
        .Text("sha256", entry => entry.Sha256)
        .Integer("rows", entry => entry.Rows)
        .Text("built", entry => entry.Built.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
        .Text("producer", entry => entry.Producer)
        .Text("inputs", entry => string.Join(InputsJoin, entry.Inputs))
        .Reads(row => new ManifestEntry(
            row.Text(0),
            row.Text(1),
            row.Integer(2),
            DateTimeOffset.ParseExact(row.Text(3), "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            row.Text(4),
            [.. row.Text(5).Split(InputsJoin, StringSplitOptions.RemoveEmptyEntries).Select(part => ProductInput.Parse(part))]));

    private readonly string _path;
    private readonly Dictionary<string, ManifestEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    private BuildManifest(string path)
    {
        _path = path;
    }

    /// <param name="root">The <c>Resources/</c> folder.</param>
    public static BuildManifest Load(string root)
    {
        var manifest = new BuildManifest(Path.Combine(Path.GetFullPath(root), Product));
        if (File.Exists(manifest._path))
        {
            using var reader = TsvFormat.Instance.Open(manifest._path, Schema);
            foreach (var entry in reader.Rows())
            {
                manifest._entries[entry.Product] = entry;
            }
        }

        return manifest;
    }

    public ManifestEntry? Get(string product)
    {
        lock (_lock)
        {
            return _entries.GetValueOrDefault(product);
        }
    }

    public IReadOnlyList<ManifestEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries.Values.OrderBy(entry => entry.Product, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>Records a product just written and saves the manifest, so a build that stops keeps what it made.</summary>
    public ManifestEntry Record(ProductHeader header, string sha256, DateTimeOffset built)
    {
        var utc = built.ToUniversalTime();
        var seconds = new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var entry = new ManifestEntry(header.Product, sha256, header.Rows ?? 0, seconds, header.Producer, header.Inputs);
        lock (_lock)
        {
            _entries[entry.Product] = entry;
            Save();
        }

        return entry;
    }

    public void Forget(string product)
    {
        lock (_lock)
        {
            if (_entries.Remove(product))
            {
                Save();
            }
        }
    }

    /// <summary>
    /// Whether the product as recorded was made from exactly these inputs, each at this hash. An
    /// input added, dropped or moved makes it stale; so does no entry at all.
    /// </summary>
    public bool IsCurrent(string product, IReadOnlyList<ProductInput> currentInputs)
    {
        var entry = Get(product);
        if (entry is null || entry.Inputs.Count != currentInputs.Count)
        {
            return false;
        }

        var recorded = entry.Inputs.ToDictionary(input => input.Path, input => input.Sha256, StringComparer.Ordinal);
        return currentInputs.All(input => recorded.TryGetValue(input.Path, out var sha256) && sha256 == input.Sha256);
    }

    private void Save()
    {
        var header = new ProductHeader(Product, [], "the build: every product, its hash, its inputs as they stood");
        using var writer = TsvFormat.Instance.Create(_path, header, Schema);
        writer.WriteAll(_entries.Values.OrderBy(entry => entry.Product, StringComparer.Ordinal));
    }
}
