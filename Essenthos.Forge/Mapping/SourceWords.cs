using System.Collections.Concurrent;
using System.Globalization;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>One row of <c>sources/words.tsv</c>: a word of one of a source's lists, what it reads as where the list maps, and why.</summary>
internal sealed record SourceWordRow(string Source, string List, string Word, string? Reads, string Reason)
{
    public static readonly ProductSchema<SourceWordRow> Schema = new ProductSchema<SourceWordRow>()
        .Text("source", r => r.Source)
        .Text("list", r => r.List)
        .Text("word", r => r.Word)
        .OptionalText("reads", r => r.Reads)
        .Text("reason", r => r.Reason)
        .Reads(row => new SourceWordRow(row.Text(0), row.Text(1), row.Text(2), row.TextOrNull(3), row.Text(4)));
}

/// <summary>One row of <c>sources/parameters.tsv</c>: a number a source is read with, and why.</summary>
internal sealed record SourceParameterRow(string Source, string Parameter, string Value, string Reason)
{
    public static readonly ProductSchema<SourceParameterRow> Schema = new ProductSchema<SourceParameterRow>()
        .Text("source", r => r.Source)
        .Text("parameter", r => r.Parameter)
        .Text("value", r => r.Value)
        .Text("reason", r => r.Reason)
        .Reads(row => new SourceParameterRow(row.Text(0), row.Text(1), row.Text(2), row.Text(3)));
}

/// <summary>
/// The word lists a source is read with, as <c>sources/words.tsv</c> declares them: the words of a translation a source's
/// defects or conventions turn on, each list named by its source and what it is for. Each product made with a list names
/// the file among its inputs (<see cref="Source"/>). A list nobody reads, a word twice in a list or a list asked for and
/// absent is refused.
/// </summary>
internal sealed class SourceWords
{
    public const string Product = "sources/words";

    public const string Source = "Essenthos/sources/words.tsv";

    /// <summary>Every list a reader asks for, by source; a row of any other is refused.</summary>
    private static readonly Dictionary<string, string[]> Lists = new(StringComparer.Ordinal)
    {
        [MaxfieldAsv] = [AddedNoun],
        [Berean] = [BereanSuppliedConjunction, BereanWawConjunction, BereanOpeningConjunction, BereanSubjectPart, BereanPrefix, BereanPrefixPart,
            BereanOpeningPreposition],
        [ClearBible] = [ClearBibleSpanishAnd, ClearBibleSpanishBeforeNames, ClearBibleFrenchAnd, ClearBibleHindiAnd, ClearBibleHebrewAlso, ClearBibleGreekAlso],
    };

    public const string MaxfieldAsv = "maxfield-asv";
    public const string AddedNoun = "added-noun";

    public const string Berean = "berean";
    public const string BereanSuppliedConjunction = "supplied-conjunction";
    public const string BereanWawConjunction = "waw-conjunction";
    public const string BereanOpeningConjunction = "opening-conjunction";
    public const string BereanSubjectPart = "subject-part";
    public const string BereanPrefix = "prefix";
    public const string BereanPrefixPart = "prefix-part";
    public const string BereanOpeningPreposition = "opening-preposition";

    public const string ClearBible = "clear-bible";
    public const string ClearBibleSpanishAnd = "spa-and";
    public const string ClearBibleSpanishBeforeNames = "spa-before-names";
    public const string ClearBibleFrenchAnd = "fra-and";
    public const string ClearBibleHindiAnd = "hin-and";
    public const string ClearBibleHebrewAlso = "heb-also";
    public const string ClearBibleGreekAlso = "grc-also";

    private static readonly ConcurrentDictionary<string, Lazy<SourceWords>> Loaded = new(StringComparer.Ordinal);

    private readonly Dictionary<(string Source, string List), List<SourceWordRow>> _lists;

    private SourceWords(Dictionary<(string Source, string List), List<SourceWordRow>> lists) => _lists = lists;

    /// <summary>The words of a list, compared as <paramref name="comparer"/> says.</summary>
    public IReadOnlySet<string> Set(string source, string list, StringComparer comparer) =>
        Rows(source, list).Select(row => row.Word).ToHashSet(comparer);

    /// <summary>The words of a mapping list, each with what it reads as.</summary>
    public IReadOnlyDictionary<string, string> Map(string source, string list, StringComparer comparer) =>
        Rows(source, list).ToDictionary(row => row.Word, row => row.Reads
            ?? throw new InvalidDataException($"{Source}: \"{row.Word}\" of {source}'s {list} reads nothing."), comparer);

    /// <summary>Whether the file names a list.</summary>
    public bool Has(string source, string list) => _lists.ContainsKey((source, list));

    private List<SourceWordRow> Rows(string source, string list) =>
        _lists.TryGetValue((source, list), out var rows)
            ? rows
            : throw new InvalidDataException($"{Source} has no {list} list of {source}, which is read.");

    /// <summary>The lists of the store's file, read once a store.</summary>
    public static SourceWords Of(ProductStore store) => Of(store.ResourcesPath);

    public static SourceWords Of(string resources) =>
        Loaded.GetOrAdd(Path.GetFullPath(resources), path => new Lazy<SourceWords>(() => Read(new ProductStore(path)))).Value;

    private static SourceWords Read(ProductStore store)
    {
        if (!store.Exists(Product))
        {
            throw new FileNotFoundException($"The sources' word lists are not at {store.FullPath(Source)}.", store.FullPath(Source));
        }

        using var reader = store.Open(Product, SourceWordRow.Schema);
        var lists = new Dictionary<(string, string), List<SourceWordRow>>();
        foreach (var row in reader.Rows())
        {
            if (!Lists.TryGetValue(row.Source, out var known) || !known.Contains(row.List))
            {
                throw new InvalidDataException(
                    $"{Source}: no reader asks for a {row.List} list of {row.Source} (\"{row.Word}\"); the lists are "
                    + string.Join("; ", Lists.Select(source => $"{source.Key}: {string.Join(", ", source.Value)}")) + ".");
            }

            if (!lists.TryGetValue((row.Source, row.List), out var rows))
            {
                lists[(row.Source, row.List)] = rows = [];
            }

            if (rows.Any(other => string.Equals(other.Word, row.Word, StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"{Source}: \"{row.Word}\" is in {row.Source}'s {row.List} list twice.");
            }

            rows.Add(row);
        }

        return new SourceWords(lists);
    }
}

/// <summary>
/// The numbers a source is read with, as <c>sources/parameters.tsv</c> declares them: shares and limits measured on a
/// source, each named by its source. Each product made with one names the file among its inputs (<see cref="Source"/>).
/// </summary>
internal sealed class SourceParameters
{
    public const string Product = "sources/parameters";

    public const string Source = "Essenthos/sources/parameters.tsv";

    /// <summary>Every parameter a reader asks for, by source; a row of any other is refused.</summary>
    private static readonly Dictionary<string, string[]> Known = new(StringComparer.Ordinal)
    {
        [SourceWords.Berean] = [BereanPlacedRows, BereanPlacedBoth],
    };

    public const string BereanPlacedRows = "placed-rows";
    public const string BereanPlacedBoth = "placed-both";

    private static readonly ConcurrentDictionary<string, Lazy<SourceParameters>> Loaded = new(StringComparer.Ordinal);

    private readonly Dictionary<(string Source, string Parameter), double> _values;

    private SourceParameters(Dictionary<(string Source, string Parameter), double> values) => _values = values;

    /// <summary>A parameter's value.</summary>
    public double Value(string source, string parameter) =>
        _values.TryGetValue((source, parameter), out var value)
            ? value
            : throw new InvalidDataException($"{Source} has no {parameter} of {source}, which is read.");

    /// <summary>The parameters of the store's file, read once a store.</summary>
    public static SourceParameters Of(ProductStore store) => Of(store.ResourcesPath);

    public static SourceParameters Of(string resources) =>
        Loaded.GetOrAdd(Path.GetFullPath(resources), path => new Lazy<SourceParameters>(() => Read(new ProductStore(path)))).Value;

    private static SourceParameters Read(ProductStore store)
    {
        if (!store.Exists(Product))
        {
            throw new FileNotFoundException($"The sources' parameters are not at {store.FullPath(Source)}.", store.FullPath(Source));
        }

        using var reader = store.Open(Product, SourceParameterRow.Schema);
        var values = new Dictionary<(string, string), double>();
        foreach (var row in reader.Rows())
        {
            if (!Known.TryGetValue(row.Source, out var known) || !known.Contains(row.Parameter))
            {
                throw new InvalidDataException($"{Source}: no reader asks for {row.Parameter} of {row.Source}.");
            }

            if (!double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidDataException($"{Source}: {row.Parameter} of {row.Source} is \"{row.Value}\", which is not a number.");
            }

            if (!values.TryAdd((row.Source, row.Parameter), value))
            {
                throw new InvalidDataException($"{Source}: {row.Parameter} of {row.Source} is given twice.");
            }
        }

        return new SourceParameters(values);
    }
}
