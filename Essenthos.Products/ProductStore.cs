using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Products;

/// <summary>
/// Which format a product is written in. People and agents edit a <see cref="Hand"/> file and
/// review it in git, so it is TSV for good; a <see cref="Machine"/> product is written and read by
/// the build alone, and its format is whatever <see cref="ProductStore.MachineFormat"/> says: the
/// binary ESSX (NOT-0216).
/// </summary>
public enum ProductKind
{
    Hand,
    Machine,
}

/// <summary>
/// The products under one <c>Resources/</c> folder: where each one is, how it is written and read,
/// what its inputs hash to and whether it is current. A producer names a product by its path
/// under <c>Resources/Essenthos/</c> without an extension (<c>texts/KJV/words</c>) and its kind,
/// and never a file format; the store chooses the extension, hashes the inputs, writes the header,
/// records the result in the manifest. An agent proving a step points a store at a scratch
/// <c>Resources/</c> of its own.
/// </summary>
public sealed class ProductStore
{
    public const string EssenthosFolder = "Essenthos";

    private readonly Lazy<InputHasher> _hasher;
    private readonly Lazy<BuildManifest> _manifest;

    /// <param name="resourcesPath">The <c>Resources/</c> folder.</param>
    /// <param name="machineFormat">What machine products are written in: the binary ESSX unless a caller says otherwise.</param>
    public ProductStore(string resourcesPath, IProductFormat? machineFormat = null, bool rehash = false)
    {
        ResourcesPath = Path.GetFullPath(resourcesPath);
        MachineFormat = machineFormat ?? EssxFormat.Instance;
        _hasher = new Lazy<InputHasher>(() => new InputHasher(ResourcesPath) { Rehash = rehash });
        _manifest = new Lazy<BuildManifest>(() => BuildManifest.Load(ResourcesPath));
    }

    public string ResourcesPath { get; }

    public IProductFormat MachineFormat { get; }

    public static IProductFormat HandFormat => TsvFormat.Instance;

    public InputHasher Hasher => _hasher.Value;

    public BuildManifest Manifest => _manifest.Value;

    /// <summary>The path a product of this kind has relative to <c>Resources/</c>: <c>Essenthos/texts/KJV/words.tsv</c>.</summary>
    public string RelativePath(string product, ProductKind kind) =>
        $"{EssenthosFolder}/{CheckName(product)}{Format(kind).Extension}";

    public string FullPath(string product, ProductKind kind) =>
        Path.Combine(ResourcesPath, RelativePath(product, kind).Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A path under <c>Resources/</c> as the file system has it.</summary>
    public string FullPath(string relativePath) =>
        Path.Combine(ResourcesPath, InputHasher.Normalise(relativePath).Replace('/', Path.DirectorySeparatorChar));

    public bool Exists(string product) => Find(product) is not null;

    /// <summary>The relative path of the product's file, whichever kind it was written as, or null.</summary>
    public string? Find(string product)
    {
        foreach (var kind in new[] { ProductKind.Machine, ProductKind.Hand })
        {
            var relative = RelativePath(product, kind);
            if (File.Exists(FullPath(relative)))
            {
                return relative;
            }
        }

        return null;
    }

    public IProductFormat Format(ProductKind kind) => kind == ProductKind.Hand ? HandFormat : MachineFormat;

    /// <summary>The input record of any file or folder under <c>Resources/</c>, hashed through the memory.</summary>
    public ProductInput Input(string relativePath) => Hasher.Input(relativePath);

    public IReadOnlyList<ProductInput> Inputs(params IEnumerable<string> relativePaths) => Hasher.Inputs(relativePaths);

    /// <summary>The input record of another product, found by name.</summary>
    public ProductInput ProductInput(string product) =>
        Hasher.Input(Find(product) ?? throw new FileNotFoundException($"Product '{product}' has not been made under {ResourcesPath}."));

    /// <summary>
    /// Whether the product must be made: no file, no manifest entry, a file that is not the one the
    /// manifest recorded, or an input whose hash moved since.
    /// </summary>
    public bool NeedsBuild(string product, ProductKind kind, IReadOnlyList<ProductInput> inputs)
    {
        var relative = RelativePath(product, kind);
        var entry = Manifest.Get(relative);
        if (entry is null || !File.Exists(FullPath(relative)) || !Manifest.IsCurrent(relative, inputs))
        {
            return true;
        }

        return Hasher.Hash(relative) != entry.Sha256;
    }

    /// <summary>
    /// Opens a writer for the product. On <see cref="IProductWriter{T}.Complete"/> the file is moved
    /// into place, hashed, and recorded in the manifest with its inputs; on anything else the previous
    /// file stays and the manifest says nothing new.
    /// </summary>
    public IProductWriter<T> Create<T>(string product, ProductKind kind, ProductSchema<T> schema, string producer, IReadOnlyList<ProductInput> inputs, Action<ProductHeader>? annotate = null)
    {
        var relative = RelativePath(product, kind);
        var header = new ProductHeader(relative, inputs, producer);
        annotate?.Invoke(header);
        var writer = Format(kind).Create(FullPath(relative), header, schema);
        return new RecordingWriter<T>(writer, () => Manifest.Record(header, Hasher.Refresh(relative), DateTimeOffset.UtcNow));
    }

    /// <summary>Opens the product by name, whichever kind it was written as.</summary>
    public IProductReader<T> Open<T>(string product, ProductSchema<T> schema)
    {
        var relative = Find(product) ?? throw new FileNotFoundException($"Product '{product}' has not been made under {ResourcesPath}.");
        return FormatOf(relative).Open(FullPath(relative), schema);
    }

    public IProductReader<string?[]> Open(string product)
    {
        var relative = Find(product) ?? throw new FileNotFoundException($"Product '{product}' has not been made under {ResourcesPath}.");
        return FormatOf(relative).Open(FullPath(relative));
    }

    /// <summary>The format that reads a file, by its extension.</summary>
    public IProductFormat FormatOf(string relativePath) =>
        relativePath.EndsWith(MachineFormat.Extension, StringComparison.Ordinal) ? MachineFormat : HandFormat;

    /// <summary>Writes the memory of hashes out; once at the end of a build.</summary>
    public void Save()
    {
        if (_hasher.IsValueCreated)
        {
            Hasher.Save();
        }
    }

    private string CheckName(string product)
    {
        var name = product.Replace('\\', '/').Trim('/');
        if (name.Length == 0 || name.Contains(' ') || name.Split('/').Contains("..")
            || name.EndsWith(HandFormat.Extension, StringComparison.Ordinal) || name.EndsWith(MachineFormat.Extension, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A product is named by its path under Resources/Essenthos/ with no extension: '{product}'.", nameof(product));
        }

        return name;
    }

    private sealed class RecordingWriter<T>(IProductWriter<T> inner, Action onComplete) : IProductWriter<T>
    {
        private bool _completed;

        public string Path => inner.Path;

        public long Rows => inner.Rows;

        public void Write(T row) => inner.Write(row);

        public void Complete()
        {
            if (_completed)
            {
                return;
            }

            inner.Complete();
            onComplete();
            _completed = true;
        }

        public void Dispose() => inner.Dispose();
    }
}
