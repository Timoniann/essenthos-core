using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The word a final's key names, worked out from the words products rather than read from a database: its id is its key's
/// (<see cref="WordIdTable"/>), and it is a word where the text's words product holds the key. The texts' ids are the
/// database's, or the ones the text load gives them (<see cref="TextLoad.Slugs"/>, numbered from 1), so the links can be
/// read before the words are in.
/// </summary>
internal sealed class ProductWords : ILinkWords
{
    private readonly Dictionary<string, int> _textIds;
    private readonly Dictionary<int, long[]> _ids;
    private readonly WordIdTable _table;

    private ProductWords(Dictionary<string, int> textIds, Dictionary<int, long[]> ids, WordIdTable table)
    {
        _textIds = textIds;
        _ids = ids;
        _table = table;
    }

    public int? TextId(string slug) => _textIds.TryGetValue(slug, out var id) ? id : null;

    /// <summary>The word a key names, or null where the text holds no word at it.</summary>
    public long? Id(int textId, WordKey key)
    {
        var id = _table.Of(key);
        return _ids.TryGetValue(textId, out var ids) && Array.BinarySearch(ids, id) >= 0 ? id : null;
    }

    /// <summary>The words of the texts named (every text with words when null), the texts numbered as the text load numbers them.</summary>
    public static ProductWords Read(ProductStore store, IReadOnlyCollection<string>? texts = null)
    {
        var slugs = TextLoad.Slugs(store);
        return Read(store, slugs.Select((slug, i) => (slug, i + 1)).ToDictionary(t => t.slug, t => t.Item2, StringComparer.Ordinal), texts);
    }

    /// <summary>The same with the texts' ids as a database holds them.</summary>
    public static async Task<ProductWords> Read(ProductStore store, NpgsqlConnection connection, IReadOnlyCollection<string>? texts,
        CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand("SELECT id, slug FROM text", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                ids[reader.GetString(1)] = reader.GetInt32(0);
            }
        }

        return Read(store, ids, texts);
    }

    private static ProductWords Read(ProductStore store, Dictionary<string, int> textIds, IReadOnlyCollection<string>? texts)
    {
        var table = WordIdTable.For(store);
        var wanted = textIds.Keys.Where(t => (texts is null || texts.Contains(t)) && store.Exists(TextWord.ProductOf(t))).ToList();
        var ids = new long[wanted.Count][];
        Parallel.For(0, wanted.Count, i =>
        {
            var words = WordIdCollisions.Keys(store, wanted[i]).Select(table.Of).ToArray();
            Array.Sort(words);
            ids[i] = words;
        });
        return new ProductWords(textIds, wanted.Select((t, i) => (textIds[t], ids[i])).ToDictionary(p => p.Item1, p => p.Item2), table);
    }
}

/// <summary>What a final's keys are resolved through: the words products (<see cref="ProductWords"/>) or a database's words.</summary>
internal interface ILinkWords
{
    int? TextId(string slug);

    long? Id(int textId, WordKey key);
}
