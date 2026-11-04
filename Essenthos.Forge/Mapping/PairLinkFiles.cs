using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The links a rule over a pair's links reads, from the pair's method files rather than the final: the rules
/// ran over whatever the database held for the pair when they ran, which was the methods that come before
/// them here. A rule never reads its own file, a later rule's, the placed names or EVIDENTIA's, each of which
/// reads it in turn, so the build graph stays acyclic.
/// </summary>
internal static class PairLinkFiles
{
    /// <summary>The methods every rule over the pair's links reads: none of their producers reads a rule's file.</summary>
    private static readonly string[] Drawn =
    [
        MappingMethods.Stated, MappingMethods.Strong, MappingMethods.Aligner, MappingMethods.Composed, MappingMethods.Lexical,
    ];

    /// <summary>The methods a rule's producer reads, in the order the database passes ran: the name lists, then the possessives.</summary>
    public static IReadOnlyList<string> Read(string method) => method switch
    {
        MappingMethods.NameList => Drawn,
        MappingMethods.Possessive => [.. Drawn, MappingMethods.NameList],
        _ => throw new ArgumentException($"No rule over the pair's links writes '{method}'.", nameof(method)),
    };

    /// <summary>The files of those methods the pair has, relative to Resources.</summary>
    public static IEnumerable<string> Inputs(ProductStore store, string pair, string method) =>
        Read(method).Select(read => store.Find(MappingProducts.Of(pair, read))).OfType<string>();

    /// <summary>Every row of those files, by method.</summary>
    public static IEnumerable<MappingRow> Rows(ProductStore store, string pair, string method)
    {
        foreach (var read in Read(method))
        {
            var product = MappingProducts.Of(pair, read);
            if (!store.Exists(product))
            {
                continue;
            }

            using var reader = store.Open(product, MappingRow.Schema);
            foreach (var row in reader.Rows())
            {
                yield return row;
            }
        }
    }

    /// <summary>A text's word ids (<see cref="ProductText"/>) by own key.</summary>
    public static Dictionary<string, int> Ids(ProductText text)
    {
        var ids = new Dictionary<string, int>(text.Words.Count, StringComparer.Ordinal);
        for (var id = 0; id < text.Words.Count; id++)
        {
            ids[text.Words[id].Key.Own.ToString()] = id;
        }

        return ids;
    }

    /// <summary>The ids of a row's keys a text holds; a key it no longer holds is left out.</summary>
    public static List<int> Words(string keys, Dictionary<string, int> ids) =>
        [.. MappingText.Split(keys).Select(key => ids.TryGetValue(key, out var id) ? id : -1).Where(id => id >= 0)];
}
