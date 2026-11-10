using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;

namespace Essenthos.Core.Conversion;

/// <summary>
/// A word whose id is not its key's first hash (<see cref="WordIds"/>): another word of the corpus took that id first in key
/// order (<see cref="With"/>), or it was 0. <c>texts/word-id-collisions</c> holds one row per such word, and nothing more, so it
/// stays the same file while no collision comes or goes, however the words change.
/// </summary>
internal sealed record WordIdCollision(WordKey Word, int Attempt, long Id, WordKey? With)
{
    public const string Product = "texts/word-id-collisions";

    public static readonly ProductSchema<WordIdCollision> Schema = new ProductSchema<WordIdCollision>()
        .Text("word", c => c.Word.ToString())
        .Integer("attempt", c => c.Attempt)
        .Integer("id", c => c.Id)
        .OptionalText("with", c => c.With?.ToString())
        .Reads(row => new WordIdCollision(WordKey.Parse(row.Text(0)), row.Int32(1), row.Integer(2),
            row.TextOrNull(3) is { } with ? WordKey.Parse(with) : null));
}

/// <summary>Every word's id, by key: its key's hash, or the id <c>texts/word-id-collisions</c> gives it instead.</summary>
internal sealed class WordIdTable
{
    private readonly Dictionary<WordKey, long> _rehashed;

    private WordIdTable(IEnumerable<WordIdCollision> collisions) => _rehashed = collisions.ToDictionary(c => c.Word, c => c.Id);

    public int Collisions => _rehashed.Count;

    public long Of(WordKey key) => _rehashed.TryGetValue(key, out var id) ? id : WordIds.Of(key);

    /// <summary>
    /// The table of the store's words: from <c>texts/word-id-collisions</c> when it is current with every words product, else
    /// worked out from the words products themselves, writing nothing.
    /// </summary>
    public static WordIdTable For(ProductStore store)
    {
        var slugs = WordIdCollisions.Texts(store);
        return WordIdCollisions.IsCurrent(store, WordIdCollisions.Inputs(store, slugs))
            ? Read(store)
            : new WordIdTable(WordIdCollisions.Find(store, slugs).Collisions);
    }

    private static WordIdTable Read(ProductStore store)
    {
        using var reader = store.Open(WordIdCollision.Product, WordIdCollision.Schema);
        return new WordIdTable(reader.ReadAll());
    }

    internal static WordIdTable Of(IEnumerable<WordIdCollision> collisions) => new(collisions);
}

/// <summary>
/// Finds the words of the corpus whose first ids meet (<see cref="WordIds.Rehash"/>) and writes them as
/// <c>texts/word-id-collisions</c>, made by <c>forge convert</c> after the texts whenever a words product moved.
/// </summary>
internal static class WordIdCollisions
{
    public sealed record Outcome(bool Written, long Words, IReadOnlyList<WordIdCollision> Collisions)
    {
        public override string ToString() => Written
            ? $"word ids: {Words} words, {Collisions.Count} collisions{(Collisions.Count == 0 ? string.Empty : ": " + string.Join(", ", Collisions.Select(c => $"{c.Word} (|{c.Attempt}, beside {c.With?.ToString() ?? "0"})")))}"
            : "word ids: no words product moved";
    }

    /// <summary>The catalogued texts the store holds words of.</summary>
    public static IReadOnlyList<string> Texts(ProductStore store) =>
        [.. TextCatalogue.All.Select(t => t.Slug).Where(slug => store.Exists(TextWord.ProductOf(slug))).Order(StringComparer.Ordinal)];

    public static IReadOnlyList<ProductInput> Inputs(ProductStore store, IReadOnlyList<string> slugs) =>
        store.Inputs(slugs.Select(slug => store.Find(TextWord.ProductOf(slug))!));

    public static bool IsCurrent(ProductStore store, IReadOnlyList<ProductInput> inputs) =>
        !store.NeedsBuild(WordIdCollision.Product, ProductKind.Machine, inputs);

    /// <summary>Writes the collisions of the store's words when a words product moved since they were written.</summary>
    public static Outcome Update(ProductStore store, bool force = false)
    {
        var slugs = Texts(store);
        var inputs = Inputs(store, slugs);
        if (!force && IsCurrent(store, inputs))
        {
            return new Outcome(false, 0, []);
        }

        var found = Find(store, slugs);
        using var writer = store.Create(WordIdCollision.Product, ProductKind.Machine, WordIdCollision.Schema,
            ProductHeader.DescribeProducer("forge convert"), inputs);
        writer.WriteAll(found.Collisions);
        return found with { Written = true };
    }

    /// <summary>Every key of every text hashed, the first ids sorted, and the keys whose first id is 0 or shared given theirs.</summary>
    internal static Outcome Find(ProductStore store, IReadOnlyList<string> slugs)
    {
        var perText = new long[slugs.Count][];
        Parallel.For(0, slugs.Count, i => perText[i] = [.. Keys(store, slugs[i]).Select(key => WordIds.Of(key))]);
        var firsts = new long[perText.Sum(ids => (long)ids.Length)];
        var at = 0;
        foreach (var ids in perText)
        {
            ids.CopyTo(firsts, at);
            at += ids.Length;
        }

        Array.Sort(firsts);
        var contestedIds = new HashSet<long>();
        for (var i = 0; i < firsts.Length; i++)
        {
            if (firsts[i] == 0 || (i > 0 && firsts[i] == firsts[i - 1]))
            {
                contestedIds.Add(firsts[i]);
            }
        }

        var contested = new List<WordKey>();
        if (contestedIds.Count > 0)
        {
            for (var i = 0; i < slugs.Count; i++)
            {
                if (perText[i].Any(contestedIds.Contains))
                {
                    contested.AddRange(Keys(store, slugs[i]).Where(key => contestedIds.Contains(WordIds.Of(key))));
                }
            }
        }

        var rehashed = WordIds.Rehash(contested, id => Array.BinarySearch(firsts, id) >= 0);
        return new Outcome(false, firsts.Length,
            [.. rehashed.Select(r => new WordIdCollision(r.Key, r.Attempt, r.Id, r.With)).OrderBy(c => c.Word)]);
    }

    /// <summary>The keys of a text's words, in the product's order; from an ESSX product only the key column is decoded.</summary>
    internal static IEnumerable<WordKey> Keys(ProductStore store, string slug)
    {
        var relative = store.Find(TextWord.ProductOf(slug)) ?? throw new FileNotFoundException($"{TextWord.ProductOf(slug)} has not been made.");
        if (relative.EndsWith(EssxLayout.Extension, StringComparison.Ordinal))
        {
            using var file = EssxReader.Open(store.FullPath(relative));
            var table = file.Table(EssxFormat.TableName);
            for (var g = 0; g < table.RowGroups; g++)
            {
                var group = table.ReadGroup(g, ["own"]);
                var own = group.Column("own");
                for (var r = 0; r < group.Rows; r++)
                {
                    yield return own.WordKey(r, slug) ?? throw new InvalidDataException($"{relative}: a word without a key.");
                }
            }

            yield break;
        }

        using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
        foreach (var word in reader.Rows())
        {
            yield return word.Key;
        }
    }
}
