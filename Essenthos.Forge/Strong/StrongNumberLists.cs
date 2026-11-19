using System.Collections.Concurrent;
using Essenthos.Core.Products;

namespace Essenthos.Core.Strong;

/// <summary>One row of <c>strong/numbers.tsv</c>: a number of one of the lists, the number it is read as, and why.</summary>
internal sealed record StrongListRow(string List, string Number, string? Reads, string Reason)
{
    public static readonly ProductSchema<StrongListRow> Schema = new ProductSchema<StrongListRow>()
        .Text("list", r => r.List)
        .Text("number", r => r.Number)
        .OptionalText("reads", r => r.Reads)
        .Text("reason", r => r.Reason)
        .Reads(row => new StrongListRow(row.Text(0), row.Text(1), row.TextOrNull(2), row.Text(3)));
}

/// <summary>One row of <c>strong/references.tsv</c>: a bare number a dictionary entry's text cites, and the entry it names.</summary>
internal sealed record StrongReferenceRow(string Number, string Written, string Reads, string Reason)
{
    public static readonly ProductSchema<StrongReferenceRow> Schema = new ProductSchema<StrongReferenceRow>()
        .Text("number", r => r.Number)
        .Text("written", r => r.Written)
        .Text("reads", r => r.Reads)
        .Text("reason", r => r.Reason)
        .Reads(row => new StrongReferenceRow(row.Text(0), row.Text(1), row.Text(2), row.Text(3)));
}

/// <summary>
/// The readings of Strong numbers no source writes, as <c>strong/numbers.tsv</c> declares them: where one numbering writes for
/// a word a number another writes otherwise, which numbers count as one, and which numbers a source misplaces. Each product
/// made with a list names the file among its inputs (<see cref="Source"/>).
/// </summary>
internal sealed class StrongNumberLists
{
    public const string Product = "strong/numbers";

    public const string Source = "Essenthos/strong/numbers.tsv";

    public const string FhlPrefix = "fhl-prefix";
    public const string Lemmatised = "lemmatised";
    public const string Suppletion = "suppletion";
    public const string CountedAs = "counted-as";
    public const string Synonym = "synonym";
    public const string ClauseParticle = "clause-particle";

    private static readonly string[] Lists = [FhlPrefix, Lemmatised, Suppletion, CountedAs, Synonym, ClauseParticle];

    private static readonly ConcurrentDictionary<string, Lazy<StrongNumberLists>> Loaded = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _lemmas;
    private readonly Dictionary<string, string> _counted;
    private readonly Dictionary<string, string> _synonyms;

    private StrongNumberLists(IReadOnlyList<StrongListRow> rows)
    {
        var lists = rows.GroupBy(row => row.List, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        Dictionary<string, string> Readings(string list) =>
            lists.GetValueOrDefault(list, []).ToDictionary(row => row.Number, row => row.Reads
                ?? throw new InvalidDataException($"{Source}: the {list} row of {row.Number} reads no number."), StringComparer.Ordinal);

        FhlPrefixes = Readings(FhlPrefix).ToDictionary(pair => int.Parse(pair.Key.AsSpan(1)), pair => pair.Value);
        _lemmas = Readings(Lemmatised);
        Suppletions = Readings(Suppletion);
        _counted = Readings(CountedAs);
        _synonyms = Readings(Synonym);
        ClauseParticles = lists.GetValueOrDefault(ClauseParticle, []).Select(row => row.Number).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The Faith Hope Love foundation's Hebrew prefix numbers, by their value, as the numbers BHSA writes for the same prefix.</summary>
    public IReadOnlyDictionary<int, string> FhlPrefixes { get; }

    /// <summary>The numbers tried instead of a tagged Greek number the verse does not bear, which its derivations cannot resolve.</summary>
    public IReadOnlyDictionary<string, string> Suppletions { get; }

    /// <summary>The Greek conjunctions and particles whose number Maxfield's ASV misplaces on an English content word.</summary>
    public IReadOnlySet<string> ClauseParticles { get; }

    /// <summary>
    /// The number to pair Greek editions on: the lemma's where the tagging traditions disagree, the word's own everywhere
    /// else. Null passes through, because an untagged word pairs on nothing.
    /// </summary>
    public string? Lemma(string? number) => number is null ? null : _lemmas.GetValueOrDefault(number, number);

    /// <summary>Whether Greek editions write this number where another writes another for the same word.</summary>
    public bool Lemmatises(string number) => _lemmas.ContainsKey(number);

    /// <summary>The lemmas' numbers the Greek editions write instead.</summary>
    public IEnumerable<string> Lemmas => _lemmas.Values;

    /// <summary>The number a number is counted with in the tie-breaks, if any.</summary>
    public string? CountedWith(string number) => _counted.GetValueOrDefault(number);

    /// <summary>The number a number is counted as: the first of its group, else itself.</summary>
    public string Canonical(string number) => _synonyms.GetValueOrDefault(number, number);

    public bool Same(string a, string b) => string.Equals(Canonical(a), Canonical(b), StringComparison.Ordinal);

    /// <summary>The lists of the store's file, read once a store.</summary>
    public static StrongNumberLists Of(ProductStore store) => Of(store.ResourcesPath);

    public static StrongNumberLists Of(string resources) =>
        Loaded.GetOrAdd(Path.GetFullPath(resources), path => new Lazy<StrongNumberLists>(() => Read(new ProductStore(path)))).Value;

    private static StrongNumberLists Read(ProductStore store)
    {
        if (!store.Exists(Product))
        {
            throw new FileNotFoundException($"The Strong number lists are not at {store.FullPath(Source)}.", store.FullPath(Source));
        }

        using var reader = store.Open(Product, StrongListRow.Schema);
        var rows = reader.Rows().ToList();
        if (rows.FirstOrDefault(row => !Lists.Contains(row.List)) is { } unknown)
        {
            throw new InvalidDataException($"{Source}: no list is called \"{unknown.List}\" ({unknown.Number}); the lists are {string.Join(", ", Lists)}.");
        }

        if (rows.GroupBy(row => (row.List, row.Number)).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            throw new InvalidDataException($"{Source}: {twice.Key.Number} is in the {twice.Key.List} list twice.");
        }

        return new StrongNumberLists(rows);
    }
}

/// <summary>
/// The bare numbers Strong's Greek dictionary's entries cite as references, as <c>strong/references.tsv</c> declares them,
/// by entry: the number as the entry's text writes it, and the entry it names.
/// </summary>
internal static class StrongReferences
{
    public const string Product = "strong/references";

    public const string Source = "Essenthos/strong/references.tsv";

    public static IReadOnlyDictionary<string, (string Written, string Number)[]> Read(ProductStore store)
    {
        if (!store.Exists(Product))
        {
            throw new FileNotFoundException($"The dictionary's bare references are not at {store.FullPath(Source)}.", store.FullPath(Source));
        }

        using var reader = store.Open(Product, StrongReferenceRow.Schema);
        return reader.Rows().GroupBy(row => row.Number, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(row => (row.Written, row.Reads)).ToArray(), StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, (string Written, string Number)[]> Read(string resources) => Read(new ProductStore(resources));
}
