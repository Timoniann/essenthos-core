using System.Text.RegularExpressions;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;
using Essenthos.Core.Records;

namespace Essenthos.Core.Build;

/// <summary>
/// One line of <c>build/licences</c>: what one layer of a source under <c>Resources/</c> is licensed under, what of it is
/// served, and where the reader is told whose it is.
/// </summary>
internal sealed record SourceLicence(
    string Source,
    string Layer,
    string Served,
    string Licence,
    string Redistribution,
    IReadOnlyList<string> Credit,
    string Declared,
    IReadOnlyList<string> Exception,
    string Note)
{
    public static readonly ProductSchema<SourceLicence> Schema = new ProductSchema<SourceLicence>()
        .Text("source", l => l.Source)
        .Text("layer", l => l.Layer)
        .Text("served", l => l.Served)
        .Text("licence", l => l.Licence)
        .Text("redistribution", l => l.Redistribution)
        .Text("credit", l => Join(l.Credit))
        .Text("declared", l => l.Declared)
        .Text("exception", l => Join(l.Exception))
        .Text("note", l => l.Note)
        .Reads(row => new SourceLicence(row.Text(0), row.Text(1), row.Text(2), row.Text(3), row.Text(4), Split(row.Text(5)), row.Text(6),
            Split(row.Text(7)), row.Text(8)));

    /// <summary>Whether a product may hold what this layer is: anything but a prohibited or unknown grant, unless the owner keeps it.</summary>
    public bool Grants => Redistribution is not (SourceLicences.Prohibited or SourceLicences.Unknown);

    public override string ToString() => $"{Source} ({Layer})";

    private static string Join(IReadOnlyList<string> values) => values.Count == 0 ? "-" : string.Join(' ', values);

    private static string[] Split(string value) =>
        value is "-" ? [] : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Something the build may not serve, or serves only because the owner keeps it (<see cref="Excepted"/>): a source a product
/// reads, a text's declaration, a picture.
/// </summary>
internal sealed record LicenceFinding(string Subject, string Why, bool Excepted = false)
{
    public override string ToString() => $"{Subject}: {Why}";
}

/// <summary>
/// The licence of every source the build may read, declared once in <c>build/licences</c> (PRB-1545), and the checks that hold
/// the build to it. Whatever a product reads can reach the served corpus and every release, so a source nobody granted, one
/// whose terms forbid passing it on, or one kept only to measure against, is refused before it is built into anything
/// served; a source the owner keeps although nothing grants it is named as an exception on every build instead.
/// </summary>
internal sealed partial class SourceLicences
{
    public const string Product = "build/licences";

    public const string Content = "content";
    public const string Derived = "derived";
    public const string Never = "never";

    public const string Prohibited = "prohibited";
    public const string Unknown = "unknown";

    /// <summary>This project's own work, under the licence the owner sets for all of it (<see cref="Configuration.OwnWorkLicences"/>).</summary>
    public const string Own = "own";

    public const string PictureCredit = "picture";
    public const string MapCredit = "map";

    private const string Essenthos = ProductStore.EssenthosFolder;

    /// <summary>The kept products that hold a dataset's rows, each to be declared with the dataset's licence.</summary>
    private const string External = Essenthos + "/external/";

    private static readonly HashSet<string> Redistributions =
    [
        .. Enum.GetValues<Redistribution>().Select(EnumSpelling.Of), Own,
    ];

    private readonly Dictionary<string, List<SourceLicence>> _bySource;

    public SourceLicences(IReadOnlyList<SourceLicence> rows)
    {
        Rows = rows;
        var problems = rows.SelectMany(Problems).ToList();
        var duplicate = rows.GroupBy(r => (r.Source, r.Layer)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            problems.Add($"{duplicate.Key.Source} ({duplicate.Key.Layer}) is declared twice");
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException($"{Product}: {string.Join("; ", problems)}.");
        }

        _bySource = rows.GroupBy(r => r.Source, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    public IReadOnlyList<SourceLicence> Rows { get; }

    public static SourceLicences Read(ProductStore store)
    {
        if (!store.Exists(Product))
        {
            throw new FileNotFoundException(
                $"The store holds no {Product}, so nothing says what any source may be served under. It is carried in git beside build/products.");
        }

        using var reader = store.Open(Product, SourceLicence.Schema);
        return new SourceLicences([.. reader.Rows()]);
    }

    /// <summary>The layers of the source a path under <c>Resources/</c> belongs to: the row whose source is its longest leading run of segments.</summary>
    public IReadOnlyList<SourceLicence> For(string path)
    {
        var source = path.Replace('\\', '/').TrimEnd('/');
        while (!_bySource.ContainsKey(source))
        {
            var cut = source.LastIndexOf('/');
            if (cut < 0)
            {
                return [];
            }

            source = source[..cut];
        }

        return _bySource[source];
    }

    /// <summary>
    /// What reading <paramref name="input"/> into <paramref name="product"/> would serve that may not be: nothing declares the
    /// source, every layer of it is <c>never</c>, or a layer a product holds grants nothing and the owner does not keep it.
    /// </summary>
    public IEnumerable<LicenceFinding> Read(string product, string input)
    {
        var subject = $"{product} reads {input}";
        var layers = For(input);
        if (layers.Count == 0)
        {
            yield return new LicenceFinding(subject, $"no line of {Product} declares the source; read its licence at the source and declare it");
            yield break;
        }

        if (layers.All(layer => layer.Served == Never))
        {
            yield return new LicenceFinding(subject, $"{layers[0].Source} is never to be read into a product ({layers[0].Note})");
            yield break;
        }

        foreach (var layer in layers.Where(layer => layer.Served != Never && !layer.Grants))
        {
            yield return layer.Exception.Count > 0
                ? new LicenceFinding($"{layer}", $"{layer.Redistribution} ({layer.Licence}), kept by the owner: {string.Join(", ", layer.Exception)}", Excepted: true)
                : new LicenceFinding(subject, $"{layer}'s licence is {layer.Redistribution} ({layer.Licence}), which grants nothing to serve");
        }
    }

    /// <summary>
    /// What can be told before any producer runs: every source a line of the graph declares is declared here, and every source of
    /// every catalogued text may be read.
    /// </summary>
    public List<LicenceFinding> CheckDeclared(ProductGraph graph)
    {
        var found = new List<LicenceFinding>();
        foreach (var line in graph.Declarations)
        {
            foreach (var input in line.Inputs.Where(i => i != "{sources}" && !i.StartsWith(Essenthos + "/", StringComparison.Ordinal)))
            {
                var fixedPart = string.Join('/', input.Split('/').TakeWhile(segment => !segment.Contains('*') && !segment.Contains('{')));
                if (fixedPart.Length == 0 || (For(fixedPart).Count == 0 && !Rows.Any(r => r.Source.StartsWith(fixedPart + "/", StringComparison.Ordinal))))
                {
                    found.Add(new LicenceFinding($"{ProductGraph.Product}: {line.Product} reads {input}",
                        $"no line of {Product} declares what it reads"));
                }
            }
        }

        foreach (var text in TextCatalogue.All)
        {
            foreach (var source in ProductGraph.TextSources(text.Slug).Where(s => !s.StartsWith(Essenthos + "/", StringComparison.Ordinal)))
            {
                found.AddRange(Read($"texts/{text.Slug}", source));
            }
        }

        return Distinct(found);
    }

    /// <summary>
    /// What the products as made read: every input the manifest records of every product, and every product under
    /// <c>external/</c>, which holds a dataset's rows and records no input.
    /// </summary>
    public List<LicenceFinding> CheckManifest(IEnumerable<ManifestEntry> entries)
    {
        var found = new List<LicenceFinding>();
        foreach (var entry in entries.Where(e => e.Product != BuildManifest.Product))
        {
            foreach (var input in entry.Inputs.Select(i => i.Path.Replace('\\', '/')).Where(p => !p.StartsWith(Essenthos + "/", StringComparison.Ordinal)))
            {
                found.AddRange(Read(entry.Product, input));
            }

            if (entry.Product.StartsWith(External, StringComparison.Ordinal) && entry.Inputs.Count == 0)
            {
                found.AddRange(Read(entry.Product, entry.Product));
            }
        }

        return Distinct(found);
    }

    /// <summary>
    /// Every catalogued text the store holds, by its declaration (<c>texts/&lt;SLUG&gt;/text</c>), held to what
    /// <see cref="TextDefinition.Validate"/> holds a definition to: a text made before the check was there is checked too.
    /// </summary>
    public static List<LicenceFinding> CheckTexts(ProductStore store, IEnumerable<string>? slugs = null)
    {
        var found = new List<LicenceFinding>();
        foreach (var slug in (slugs ?? TextCatalogue.All.Select(t => t.Slug)).Where(slug => store.Exists(TextField.ProductOf(slug))))
        {
            using var reader = store.Open(TextField.ProductOf(slug), TextField.Schema);
            var fields = reader.Rows().GroupBy(f => f.Field).ToDictionary(g => g.Key, g => g.First().Value);
            var redistribution = fields.GetValueOrDefault("redistribution") is { } spelled && Redistributions.Contains(spelled) && spelled != Own
                ? EnumSpelling.ToRedistribution(spelled)
                : Redistribution.Unknown;
            if (TextDefinition.Refusal(slug, fields.GetValueOrDefault("source-url"), fields.GetValueOrDefault("licence"), redistribution) is { } why)
            {
                found.Add(new LicenceFinding(TextField.ProductOf(slug), why));
            }
        }

        return found;
    }

    /// <summary>
    /// Every picture the records serve has a credit and a licence a picture may be shown under (<see cref="Licences.Shown"/>);
    /// the project's own label only on a picture it made.
    /// </summary>
    public static List<LicenceFinding> CheckPictures(ProductStore store)
    {
        const string product = "records/facts/images";
        var found = new List<LicenceFinding>();
        if (!store.Exists(product))
        {
            return found;
        }

        var columns = RecordProducers.File(product).Parsed.Select(c => c.Name).ToList();
        int slug = columns.IndexOf("slug"), kind = columns.IndexOf("kind"), file = columns.IndexOf("file"), credit = columns.IndexOf("credit"),
            licence = columns.IndexOf("licence");
        foreach (var row in RecordProducers.Read(store, product))
        {
            var subject = $"{product}: {row[file]} of {row[slug]}";
            if (string.IsNullOrWhiteSpace(row[credit]))
            {
                found.Add(new LicenceFinding(subject, "no credit: every picture is shown with who made it"));
            }

            var ours = row[kind] is EntityImageLoader.Generated or EntityImageLoader.Rendered;
            if (string.IsNullOrWhiteSpace(row[licence]))
            {
                found.Add(new LicenceFinding(subject, "no licence: every picture is shown with what it may be used under"));
            }
            else if (ours ? row[licence] != Licences.OwnWork : !Licences.Shown(row[licence]!))
            {
                found.Add(new LicenceFinding(subject, ours
                    ? $"a picture of ours under \"{row[licence]}\", where ours say \"{Licences.OwnWork}\""
                    : $"\"{row[licence]}\" is no licence a picture may be shown under (the gazetteer's codes, Creative Commons BY and BY-SA)"));
            }
        }

        return found;
    }

    /// <summary>Everything the build checks before a database is touched, after the producers ran.</summary>
    public List<LicenceFinding> CheckMade(ProductStore store) =>
        [.. CheckManifest(store.Manifest.Entries), .. CheckTexts(store), .. CheckPictures(store)];

    private static List<LicenceFinding> Distinct(List<LicenceFinding> found) =>
        [.. found.DistinctBy(f => f.Excepted ? f.Subject : f.ToString())];

    private static IEnumerable<string> Problems(SourceLicence row)
    {
        var name = row.ToString();
        if (row.Source.Length == 0 || row.Source.Contains('\\') || row.Source.EndsWith('/') || row.Source.Contains('*'))
        {
            yield return $"{name}: a source is a path under Resources/ with forward slashes and no wildcard";
        }

        if (row.Served is not (Content or Derived or Never))
        {
            yield return $"{name}: served is content, derived or never, not \"{row.Served}\"";
        }

        if (!Redistributions.Contains(row.Redistribution))
        {
            yield return $"{name}: redistribution is one of {string.Join(", ", Redistributions)}, not \"{row.Redistribution}\"";
        }

        if (string.IsNullOrWhiteSpace(row.Licence) || row.Licence == "-")
        {
            yield return $"{name}: no licence; write what the source states, or \"No licence stated\"";
        }

        if (row.Served != Never && row.Credit.Count == 0)
        {
            yield return $"{name}: served with no credit (RUL-0181)";
        }

        foreach (var credit in row.Credit)
        {
            if (Credit(credit) is { } problem)
            {
                yield return $"{name}: {problem}";
            }
        }

        if (row.Exception.Count > 0 && (row.Grants || row.Served == Never))
        {
            yield return $"{name}: an exception is for a source served although it grants nothing; this one {(row.Served == Never ? "is never served" : "grants " + row.Redistribution)}";
        }

        if (row.Exception.Any(e => !Ticket().IsMatch(e)))
        {
            yield return $"{name}: an exception names the tickets the owner keeps it under (PRB-1332), not \"{string.Join(' ', row.Exception)}\"";
        }
    }

    private static string? Credit(string credit)
    {
        if (credit is PictureCredit or MapCredit)
        {
            return null;
        }

        if (credit.StartsWith("dataset:", StringComparison.Ordinal))
        {
            var id = credit["dataset:".Length..];
            return Datasets.All.Any(d => d.Id == id) ? null : $"credit {credit}: no dataset of Datasets.All has that id";
        }

        if (credit.StartsWith("text:", StringComparison.Ordinal))
        {
            var slug = credit["text:".Length..];
            return slug == "*" || TextCatalogue.All.Any(t => t.Slug == slug) ? null : $"credit {credit}: no catalogued text has that slug";
        }

        return $"credit {credit}: a credit is dataset:<id>, text:<SLUG>, text:*, {PictureCredit} or {MapCredit}";
    }

    [GeneratedRegex(@"^[A-Z]{3}-\d{4}$")]
    private static partial Regex Ticket();
}
