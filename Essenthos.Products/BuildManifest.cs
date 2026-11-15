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
/// <para>
/// Several processes may build into one <c>Resources/</c> (PRB-1486). Each save takes
/// <see cref="LockFile"/>, reads the file again when another process wrote it since, keeps every
/// entry it did not change itself, and writes the merge in place whole.
/// </para>
/// </summary>
public sealed class BuildManifest
{
    public const string Product = "Essenthos/build/manifest.tsv";

    /// <summary>Held across processes around each read-merge-write of the manifest; beside it in <c>build/</c>, which git ignores.</summary>
    public const string LockFile = "Essenthos/build/manifest.lock";

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
    private readonly string _lockPath;
    private readonly Dictionary<string, ManifestEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>Products this process recorded, rehashed or forgot since its last save: its own word on them wins the merge.</summary>
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);

    private readonly object _lock = new();

    /// <summary>The file as this process last read or wrote it, so a save reads it again only when another process wrote it since.</summary>
    private (long Bytes, DateTime Written)? _seen;

    private BuildManifest(string root)
    {
        _path = Path.Combine(root, Product);
        _lockPath = Path.Combine(root, LockFile);
    }

    /// <param name="root">The <c>Resources/</c> folder.</param>
    public static BuildManifest Load(string root)
    {
        var manifest = new BuildManifest(Path.GetFullPath(root));
        foreach (var entry in manifest.ReadFile())
        {
            manifest._entries[entry.Product] = entry;
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
            _changed.Add(entry.Product);
            Save();
        }

        return entry;
    }

    /// <summary>Records another hash for products whose files did not change, as a change of the hashing rule needs; saves once.</summary>
    public void Rehash(IEnumerable<(string Product, string Sha256)> products)
    {
        lock (_lock)
        {
            var any = false;
            foreach (var (product, sha256) in products)
            {
                if (_entries.TryGetValue(product, out var entry))
                {
                    _entries[product] = entry with { Sha256 = sha256 };
                    _changed.Add(product);
                    any = true;
                }
            }

            if (any)
            {
                Save();
            }
        }
    }

    /// <summary>
    /// Takes a product out of the manifest. Saved also when this process never held the entry but another process wrote the
    /// file since this one last saw it: the entry may be there, and forgetting only the memory would leave it on disk.
    /// </summary>
    public void Forget(string product)
    {
        lock (_lock)
        {
            if (_entries.Remove(product) || Stat() != _seen)
            {
                _changed.Add(product);
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

    /// <summary>
    /// Under the cross-process lock: takes in what other processes wrote since this one last saw the
    /// file, keeps this process's own changes over it, and writes the whole through a temporary file
    /// moved into place. Called under <see cref="_lock"/>.
    /// </summary>
    private void Save()
    {
        using var held = FileLock.Acquire(_lockPath);
        if (Stat() != _seen)
        {
            var onDisk = ReadFile().ToDictionary(entry => entry.Product, StringComparer.Ordinal);
            foreach (var product in _entries.Keys.Where(product => !_changed.Contains(product) && !onDisk.ContainsKey(product)).ToList())
            {
                _entries.Remove(product);
            }

            foreach (var (product, entry) in onDisk.Where(pair => !_changed.Contains(pair.Key)))
            {
                _entries[product] = entry;
            }
        }

        var header = new ProductHeader(Product, [], "the build: every product, its hash, its inputs as they stood");
        using (var writer = TsvFormat.Instance.Create(_path, header, Schema))
        {
            writer.WriteAll(_entries.Values.OrderBy(entry => entry.Product, StringComparer.Ordinal));
        }

        _seen = Stat();
        _changed.Clear();
    }

    private List<ManifestEntry> ReadFile()
    {
        var seen = Stat();
        if (seen is null)
        {
            _seen = null;
            return [];
        }

        using var reader = TsvFormat.Instance.Open(_path, Schema);
        var entries = reader.Rows().ToList();
        _seen = seen;
        return entries;
    }

    private (long Bytes, DateTime Written)? Stat()
    {
        var file = new FileInfo(_path);
        return file.Exists ? (file.Length, file.LastWriteTimeUtc) : null;
    }
}
