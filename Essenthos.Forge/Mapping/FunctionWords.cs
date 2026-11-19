using System.Collections.Concurrent;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>What a function word is, as <c>lexicon/function-words.tsv</c> names it.</summary>
internal enum FunctionKind
{
    None,
    Article,
    Conjunction,

    /// <summary>An adverb that joins clauses (<em>then</em>, <em>so</em>, <em>now</em>, <em>when</em>, <em>as</em>).</summary>
    Connective,
    Particle,
    Pronoun,
    Preposition,
    Auxiliary,
    Determiner,
    Adverb,
}

/// <summary>One row of <c>lexicon/function-words.tsv</c>.</summary>
internal sealed record FunctionWordRow(string Language, string Word, string Kind)
{
    public static readonly ProductSchema<FunctionWordRow> Schema = new ProductSchema<FunctionWordRow>()
        .Text("language", r => r.Language)
        .Text("word", r => r.Word)
        .Text("kind", r => r.Kind)
        .Reads(row => new FunctionWordRow(row.Text(0), row.Text(1), row.Text(2)));
}

/// <summary>
/// The words of a language that render no word of the original of their own, or only a particle, and what each is, as
/// <c>lexicon/function-words.tsv</c> declares them. Each product made with them names the file among its inputs
/// (<see cref="Source"/>).
/// </summary>
internal sealed class FunctionWords
{
    public const string Product = "lexicon/function-words";

    public const string Source = "Essenthos/lexicon/function-words.tsv";

    private static readonly ConcurrentDictionary<string, Lazy<FunctionWords>> Loaded = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Dictionary<string, FunctionKind>> _languages;

    private FunctionWords(Dictionary<string, Dictionary<string, FunctionKind>> languages) => _languages = languages;

    /// <summary>What a word of a language is, as printed (its case and any apostrophe aside); <see cref="FunctionKind.None"/> for a word of content.</summary>
    public FunctionKind Kind(string language, string word) =>
        _languages.TryGetValue(language, out var words) ? words.GetValueOrDefault(Bare(word)) : FunctionKind.None;

    /// <summary>Whether a word of a language, as printed, is one of them.</summary>
    public bool Contains(string language, string word) => Kind(language, word) != FunctionKind.None;

    /// <summary>Whether a language has any.</summary>
    public bool Lists(string language) => _languages.ContainsKey(language);

    private static string Bare(string word) => word.Trim('\'', '’', '‘');

    /// <summary>The words of the store's file, read once a store.</summary>
    public static FunctionWords Of(ProductStore store) => Of(store.ResourcesPath);

    public static FunctionWords Of(string resources) =>
        Loaded.GetOrAdd(Path.GetFullPath(resources), path => new Lazy<FunctionWords>(() => Read(new ProductStore(path)))).Value;

    private static FunctionWords Read(ProductStore store)
    {
        if (!store.Exists(Product))
        {
            throw new FileNotFoundException($"The function words are not at {store.FullPath(Source)}.", store.FullPath(Source));
        }

        using var reader = store.Open(Product, FunctionWordRow.Schema);
        var languages = new Dictionary<string, Dictionary<string, FunctionKind>>(StringComparer.Ordinal);
        foreach (var row in reader.Rows())
        {
            if (!Enum.TryParse<FunctionKind>(row.Kind, ignoreCase: true, out var kind) || kind == FunctionKind.None)
            {
                throw new InvalidDataException(
                    $"{Source}: \"{row.Word}\" is of no kind called \"{row.Kind}\"; the kinds are " +
                    $"{string.Join(", ", Enum.GetNames<FunctionKind>().Skip(1).Select(name => name.ToLowerInvariant()))}.");
            }

            if (!languages.TryGetValue(row.Language, out var words))
            {
                languages[row.Language] = words = new Dictionary<string, FunctionKind>(StringComparer.OrdinalIgnoreCase);
            }

            if (!words.TryAdd(row.Word, kind))
            {
                throw new InvalidDataException($"{Source}: \"{row.Word}\" ({row.Language}) is listed twice.");
            }
        }

        return new FunctionWords(languages);
    }
}
