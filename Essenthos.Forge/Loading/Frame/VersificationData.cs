using System.Collections.Concurrent;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Products;

namespace Essenthos.Core.Loading.Frame;

/// <summary>One row of a versification file, by its column names, with the file it came from.</summary>
internal sealed class VersificationLine(string file, int row, IReadOnlyDictionary<string, string> fields)
{
    /// <summary>Which row of the file it is, counting from one after the column line.</summary>
    public int Row => row;

    public string this[string column] => fields[column];

    /// <summary>The cell, or null where it is empty or <c>-</c>.</summary>
    public string? Optional(string column) => fields[column] is "" or "-" ? null : fields[column];

    public string Required(string column) =>
        Optional(column) ?? throw Fail($"the column \"{column}\" is empty");

    public Versification Tradition(string column) =>
        Enum.TryParse<Versification>(this[column], ignoreCase: true, out var tradition) && Enum.IsDefined(tradition)
            ? tradition
            : throw Fail($"\"{this[column]}\" is not a numbering tradition ({string.Join(", ", Enum.GetNames<Versification>())})");

    public FormatException Fail(string problem) => new($"{file} row {row}: {problem}.");
}

/// <summary>
/// The versification rules that are this project's rather than the versification data's (TVTMS):
/// hand-edited product files under <c>Resources/Essenthos/versification/</c>, each row with what was
/// read to write it. They are inputs of every text's frame, named with their hashes in its header.
/// </summary>
internal sealed class VersificationData
{
    /// <summary>The numbering schemes TVTMS names, by tradition (<see cref="VersificationRules"/>).</summary>
    public const string SchemesProduct = "versification/schemes";

    /// <summary>Rows of TVTMS the texts contradict (<see cref="TvtmsCorrections"/>).</summary>
    public const string CorrectionsProduct = "versification/corrections";

    /// <summary>Passages no scheme of TVTMS describes (<see cref="TvtmsSupplements"/>).</summary>
    public const string SupplementsProduct = "versification/supplements";

    /// <summary>Books an edition numbers in another tradition (<see cref="BookTraditions"/>).</summary>
    public const string BookTraditionsProduct = "versification/book-traditions";

    /// <summary>An edition's own verses read one by one (<see cref="LetteredEditions"/>).</summary>
    public const string EditionReadingsProduct = "versification/edition-readings";

    public static IReadOnlyList<string> Products { get; } =
        [SchemesProduct, CorrectionsProduct, SupplementsProduct, BookTraditionsProduct, EditionReadingsProduct];

    private static readonly ConcurrentDictionary<string, Lazy<VersificationData>> Loaded =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, Versification>> _books;

    private VersificationData(ProductStore store)
    {
        Store = store;
        (Schemes, Undivided) = VersificationRules.ReadSchemes(this);
        Corrections = TvtmsCorrections.Read(this);
        Supplements = TvtmsSupplements.Read(this);
        _books = BookTraditions.Read(this);
        Editions = LetteredEditions.Read(this);
    }

    /// <summary>The rules as the files under this <c>Resources/</c> folder hold them, read once per process.</summary>
    public static VersificationData Of(string resources) =>
        Loaded.GetOrAdd(Path.GetFullPath(resources), path => new Lazy<VersificationData>(() => new VersificationData(new ProductStore(path)))).Value;

    public ProductStore Store { get; }

    /// <summary>The scheme names of each tradition, the one it is named after first, in the order they are weighed.</summary>
    public IReadOnlyDictionary<Versification, string[]> Schemes { get; }

    /// <summary>The undivided form of a scheme, and the divided scheme it is written against.</summary>
    public IReadOnlyDictionary<string, string> Undivided { get; }

    public IReadOnlyList<TvtmsCorrection> Corrections { get; }

    public IReadOnlyList<TvtmsSupplement> Supplements { get; }

    public LetteredEditions Editions { get; }

    /// <summary>
    /// The books of this edition numbered in a tradition of their own, or in none the data describes;
    /// empty for most.
    /// </summary>
    public IReadOnlyDictionary<int, Versification> Books(string slug) =>
        _books.GetValueOrDefault(slug) ?? new Dictionary<int, Versification>();

    /// <summary>The files as they stand now, each with its hash, for the header of a product made from them.</summary>
    public IReadOnlyList<ProductInput> Inputs() => [.. Products.Select(Store.ProductInput)];

    /// <summary>
    /// The rows of one file, refusing a file whose columns are not the ones named. Every column is
    /// text; a cell that says nothing is empty or <c>-</c>.
    /// </summary>
    public IReadOnlyList<VersificationLine> Rows(string product, params string[] columns)
    {
        var schema = columns
            .Select((column, index) => (column, index))
            .Aggregate(new ProductSchema<string?[]>(), (built, column) => built.OptionalText(column.column, row => row[column.index]))
            .Reads(row => row.ToTexts());
        var file = Path.GetFileName(Store.Find(product) ?? product);

        using var reader = Store.Open(product, schema);
        return
        [
            .. reader.Rows().Select((fields, row) => new VersificationLine(file, row + 1,
                columns.Select((column, index) => (column, index)).ToDictionary(pair => pair.column, pair => fields[pair.index]?.Trim() ?? string.Empty))),
        ];
    }
}
