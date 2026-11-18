using System.Text.RegularExpressions;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;

namespace Essenthos.Core.Build;

/// <summary>
/// Who makes a product: a person (<c>hand</c>), an export from a database run again when wanted (<c>export</c>), an export
/// taken once that nothing makes again (<c>kept</c>: the store is its only copy, and its exporter refuses to overwrite it),
/// the build from files (<c>build</c>), or the build from the serving database once it is filled (<c>serve</c>:
/// <c>build --into</c> and <c>build --apply</c> run its producer at the end, never <c>forge build</c> alone).
/// </summary>
internal static class ProductMaker
{
    public const string Hand = "hand";
    public const string Export = "export";
    public const string Kept = "kept";
    public const string Build = "build";
    public const string Serve = "serve";
}

/// <summary>
/// One line of <c>build/products</c>: a product pattern under <c>Resources/Essenthos/</c> (no extension; <c>{name}</c> is one
/// path segment bound across the line, <c>*</c> one segment, <c>**</c> any depth), who makes it and with which Forge verb,
/// the inputs it may read as patterns under <c>Resources/</c> (<c>{sources}</c>: the text's declared sources in the
/// catalogue), the serving tables it feeds, and the scope a change to it is applied by (<c>text</c>, <c>pair</c>, <c>all</c>,
/// <c>none</c>).
/// </summary>
internal sealed record ProductDeclaration(string Product, string Maker, string Producer, IReadOnlyList<string> Inputs, IReadOnlyList<string> Tables, string Scope)
{
    public static readonly ProductSchema<ProductDeclaration> Schema = new ProductSchema<ProductDeclaration>()
        .Text("product", d => d.Product)
        .Text("maker", d => d.Maker)
        .Text("producer", d => d.Producer)
        .Text("inputs", d => string.Join(' ', d.Inputs))
        .Text("tables", d => string.Join(' ', d.Tables))
        .Text("scope", d => d.Scope)
        .Reads(row => new ProductDeclaration(row.Text(0), row.Text(1), row.Text(2), Split(row.Text(3)), Split(row.Text(4)), row.Text(5)));

    private static string[] Split(string value) =>
        value is "-" ? [] : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>A product that reads something its declaration does not name, or a product no declaration names.</summary>
internal sealed record UndeclaredRead(string Product, string? Input, string Why)
{
    public override string ToString() => Input is null ? $"{Product}: {Why}" : $"{Product} reads {Input}: {Why}";
}

/// <summary>
/// The declared graph of products (DOC-0224 §13): <c>build/products</c>, kept by hand. The build runs its producers in the
/// order their inputs ask for, and after each one holds every product the store's manifest records against its line: a
/// product reading an input its line does not declare fails the build, as a load step's undeclared read once failed its
/// test.
/// </summary>
internal sealed partial class ProductGraph
{
    public const string Product = "build/products";

    /// <summary>The producer of what the build verb writes itself.</summary>
    public const string SelfProducer = "build";

    private readonly List<(ProductDeclaration Declaration, Regex Pattern)> _lines;

    public ProductGraph(IReadOnlyList<ProductDeclaration> declarations)
    {
        Declarations = declarations;
        _lines = [.. declarations.Select(d => (d, Compile(d.Product, null)))];
        var duplicate = declarations.GroupBy(d => d.Product).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"{Product} declares {duplicate.Key} twice.");
        }

        var bad = declarations.FirstOrDefault(d => d.Maker is not (ProductMaker.Hand or ProductMaker.Export or ProductMaker.Kept or ProductMaker.Build or ProductMaker.Serve)
                                                   || d.Scope is not ("text" or "pair" or "all" or "none"));
        if (bad is not null)
        {
            throw new InvalidDataException($"{Product}: {bad.Product} has maker '{bad.Maker}' and scope '{bad.Scope}'; makers are hand, export, kept, build or serve, scopes text, pair, all or none.");
        }
    }

    public IReadOnlyList<ProductDeclaration> Declarations { get; }

    public static ProductGraph Read(ProductStore store)
    {
        using var reader = store.Open(Product, ProductDeclaration.Schema);
        return new ProductGraph([.. reader.Rows()]);
    }

    /// <summary>The declaration a product (named under <c>Essenthos/</c>, extension or none) answers to, with what its placeholders bound.</summary>
    public (ProductDeclaration Declaration, IReadOnlyDictionary<string, string> Bound)? Find(string product)
    {
        var name = ProductName(product);
        foreach (var (declaration, pattern) in _lines)
        {
            var match = pattern.Match(name);
            if (match.Success)
            {
                return (declaration, pattern.GetGroupNames().Where(g => !int.TryParse(g, out _)).ToDictionary(g => g, g => match.Groups[g].Value));
            }
        }

        return null;
    }

    /// <summary>
    /// The build's producers in an order that makes every input before what reads it: one producer string per step, each
    /// run once. A producer reading what it makes itself (a merge reading the last final) is not a cycle.
    /// </summary>
    public IReadOnlyList<string> Order()
    {
        var needs = Needs();
        var producers = needs.Keys.ToList();
        var ordered = new List<string>();
        var done = new HashSet<string>();
        while (ordered.Count < producers.Count)
        {
            var ready = producers.Where(p => !done.Contains(p) && needs[p].All(done.Contains)).ToList();
            if (ready.Count == 0)
            {
                throw new InvalidDataException(
                    $"{Product} has a cycle among {string.Join(", ", producers.Where(p => !done.Contains(p)))}.");
            }

            ordered.AddRange(ready);
            done.UnionWith(ready);
        }

        return ordered;
    }

    /// <summary>
    /// Every build producer, in the order the lines first name them, with the producers whose products it reads: what must
    /// end before it starts. Producers that need nothing of each other may run at once.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Needs()
    {
        // The build's own state (served, report) is written by the build verb itself, never run as a producer.
        var producers = Declarations.Where(d => d.Maker == ProductMaker.Build && d.Producer != SelfProducer).Select(d => d.Producer).Distinct().ToList();
        var needs = producers.ToDictionary(p => p, _ => new HashSet<string>());
        foreach (var reader in Declarations.Where(d => d.Maker == ProductMaker.Build && d.Producer != SelfProducer))
        {
            var inputs = reader.Inputs.Where(i => i != "{sources}").Select(i => Compile(i, null)).ToList();
            // An input naming one text's product (Essenthos/texts/BSB/frame) is read as the product it names, too.
            var named = reader.Inputs.Where(i => i.StartsWith("Essenthos/", StringComparison.Ordinal))
                .Select(i => SamplePath(i["Essenthos/".Length..])).ToList();
            foreach (var made in Declarations.Where(d => d.Maker == ProductMaker.Build && d.Producer != reader.Producer && d.Producer != SelfProducer))
            {
                var sample = "Essenthos/" + SamplePath(made.Product);
                var pattern = Compile(made.Product, null);
                if (inputs.Any(i => i.IsMatch(sample) || i.IsMatch(Strip(sample))) || named.Any(pattern.IsMatch))
                {
                    needs[reader.Producer].Add(made.Producer);
                }
            }
        }

        return producers.ToDictionary(p => p, p => (IReadOnlySet<string>)needs[p]);
    }

    /// <summary>Every product of the manifest, held against its line: undeclared products and undeclared inputs.</summary>
    public List<UndeclaredRead> Check(BuildManifest manifest, Func<string, IReadOnlyCollection<string>>? sourcesOf = null)
    {
        sourcesOf ??= TextSources;
        var found = new List<UndeclaredRead>();
        foreach (var entry in manifest.Entries)
        {
            if (!entry.Product.StartsWith(ProductStore.EssenthosFolder + "/", StringComparison.Ordinal) || entry.Product == BuildManifest.Product)
            {
                continue;
            }

            if (Find(entry.Product) is not { } line)
            {
                found.Add(new UndeclaredRead(entry.Product, null, $"no line of {Product} declares it"));
                continue;
            }

            var allowed = line.Declaration.Inputs.Where(i => i != "{sources}").Select(i => Compile(i, line.Bound)).ToList();
            var sources = line.Declaration.Inputs.Contains("{sources}") && line.Bound.TryGetValue("text", out var text) ? sourcesOf(text) : [];
            foreach (var input in entry.Inputs)
            {
                if (allowed.Any(p => p.IsMatch(input.Path) || p.IsMatch(Strip(input.Path))) || sources.Contains(input.Path) || sources.Contains(Strip(input.Path)))
                {
                    continue;
                }

                found.Add(new UndeclaredRead(entry.Product, input.Path, $"not among the inputs {line.Declaration.Product} declares"));
            }
        }

        return found;
    }

    /// <summary>
    /// What the build reads and cannot make, missing from the store: every product of a <c>kept</c> or <c>hand</c> line that
    /// names one file, and every product the manifest records under a <c>kept</c> or <c>export</c> line (the aligner's pairs,
    /// each text's ids). A producer reading a missing one would build without it and say nothing.
    /// </summary>
    public List<string> Missing(ProductStore store)
    {
        var missing = new List<string>();
        foreach (var line in Declarations.Where(d => d.Maker is ProductMaker.Kept or ProductMaker.Hand && !d.Product.Contains('{') && !d.Product.Contains('*')))
        {
            if (!Present(store, line.Product))
            {
                missing.Add($"{line.Product} ({line.Maker}, {line.Producer})");
            }
        }

        foreach (var entry in store.Manifest.Entries.Where(e => e.Product.StartsWith(ProductStore.EssenthosFolder + "/", StringComparison.Ordinal)))
        {
            if (Find(entry.Product) is { } line && line.Declaration.Maker is ProductMaker.Kept or ProductMaker.Export
                && !File.Exists(store.FullPath(entry.Product)))
            {
                missing.Add($"{ProductName(entry.Product)} ({line.Declaration.Maker}, {line.Declaration.Producer})");
            }
        }

        return missing;
    }

    /// <summary>
    /// Takes out of the manifest every product no line declares whose file is gone: what a retired line made, once its files
    /// are deleted. One whose file is still there stays, and the graph check names it.
    /// </summary>
    public List<string> Forget(ProductStore store)
    {
        var gone = store.Manifest.Entries
            .Where(e => e.Product.StartsWith(ProductStore.EssenthosFolder + "/", StringComparison.Ordinal) && e.Product != BuildManifest.Product
                        && Find(e.Product) is null && !File.Exists(store.FullPath(e.Product)))
            .Select(e => e.Product)
            .ToList();
        foreach (var product in gone)
        {
            store.Manifest.Forget(product);
        }

        return gone;
    }

    /// <summary>
    /// Takes out of the manifest every <c>serve</c> product that reads an input its line no longer declares: it was made under an
    /// older line, so it is not current, and the serving build (<c>build --into</c>, <c>--apply</c>) makes it again. Without this
    /// a change to a serve line would fail <c>forge build</c> before the serving build could ever make the product again.
    /// </summary>
    public List<string> ForgetOutdatedServed(ProductStore store)
    {
        var outdated = Check(store.Manifest)
            .Where(read => read.Input is not null && Find(read.Product) is { Declaration.Maker: ProductMaker.Serve })
            .Select(read => read.Product)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var product in outdated)
        {
            store.Manifest.Forget(product);
        }

        return outdated;
    }

    /// <summary>The <c>kept</c> products the store holds under a folder of <c>Resources/Essenthos/</c>, by name.</summary>
    public List<string> Kept(ProductStore store, string folder)
    {
        var essenthos = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder);
        var root = Path.Combine(essenthos, folder);
        return !Directory.Exists(root)
            ? []
            : [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => !Path.GetFileName(file).StartsWith('.'))
                .Select(file => ProductName(Path.GetRelativePath(essenthos, file)))
                .Where(name => Find(name)?.Declaration.Maker == ProductMaker.Kept)
                .Order(StringComparer.Ordinal)];
    }

    private static bool Present(ProductStore store, string product) =>
        store.Exists(product) || File.Exists(store.FullPath($"{ProductStore.EssenthosFolder}/{product}"));

    /// <summary>A text's declared sources and the products its stages read, as the converter names its inputs.</summary>
    public static IReadOnlyCollection<string> TextSources(string slug)
    {
        if (TextCatalogue.All.FirstOrDefault(t => t.Slug == slug) is not { } text)
        {
            return [];
        }

        var stages = TextConverter.Stages.Where(stage => stage.AppliesTo(slug)).ToList();
        return
        [
            .. text.Sources.Concat(stages.SelectMany(stage => stage.Sources(slug))).Select(InputHasher.Normalise),
            .. stages.SelectMany(stage => stage.Products(slug)).Select(product => $"{ProductStore.EssenthosFolder}/{product}"),
        ];
    }

    /// <summary>The tables a set of products feeds, from their lines.</summary>
    public IReadOnlyList<string> TablesOf(IEnumerable<string> products) =>
        [.. products.Select(Find).Where(f => f is not null).SelectMany(f => f!.Value.Declaration.Tables).Distinct()];

    private static string ProductName(string product)
    {
        var name = product.Replace('\\', '/');
        if (name.StartsWith(ProductStore.EssenthosFolder + "/", StringComparison.Ordinal))
        {
            name = name[(ProductStore.EssenthosFolder.Length + 1)..];
        }

        return Strip(name);
    }

    private static string Strip(string path) =>
        path.EndsWith(".essx", StringComparison.Ordinal) ? path[..^5]
        : path.EndsWith(".tsv", StringComparison.Ordinal) ? path[..^4]
        : path;

    private static string SamplePath(string pattern) => Placeholder().Replace(pattern.Replace("**", "x").Replace("*", "x"), "x");

    private static Regex Compile(string pattern, IReadOnlyDictionary<string, string>? bound)
    {
        var regex = new System.Text.StringBuilder("^");
        var seen = new HashSet<string>();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '{' && pattern.IndexOf('}', i) is var close and > 0)
            {
                var name = pattern[(i + 1)..close];
                if (bound is not null && bound.TryGetValue(name, out var value))
                {
                    regex.Append(Regex.Escape(value));
                }
                else if (seen.Add(name))
                {
                    regex.Append($"(?<{name}>[^/-]+)");
                }
                else
                {
                    regex.Append($@"\k<{name}>");
                }

                i = close;
            }
            else if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                regex.Append(".*");
                i++;
            }
            else if (pattern[i] == '*')
            {
                regex.Append("[^/]+");
            }
            else
            {
                regex.Append(Regex.Escape(pattern[i].ToString()));
            }
        }

        return new Regex(regex.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Placeholder();
}
