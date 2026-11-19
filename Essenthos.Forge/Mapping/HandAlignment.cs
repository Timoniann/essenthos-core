using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// A pair's hand alignment as its merge settled it, the answer key a measurement against a stated source reads: every link of
/// the pair's <c>final</c> with a stated claim, and every link a ruling of the pair pins in a verse the stated rows cover. A
/// stated row a ruling refuses is no claim of the final and is not here; the shape the ruling pins in its place is, so a
/// correction made in <c>mapping/&lt;pair&gt;/rulings</c> reaches the key the way it reaches the site.
/// </summary>
internal static class HandAlignment
{
    /// <param name="Absence">The link says its words have no counterpart (<see cref="MappingMethods.IsAbsence"/>).</param>
    public sealed record Link(IReadOnlyList<WordKey> From, IReadOnlyList<WordKey> To, bool Absence, string Source);

    /// <param name="source">The beginning of the stated rows' source to read (one alignment of several); null reads every stated row.</param>
    public static List<Link> Read(ProductStore store, string pair, string? source = null)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        bool Wanted(string rowSource) => source is null || rowSource.StartsWith(source, StringComparison.Ordinal);

        var verses = new HashSet<VerseKey>();
        if (store.Exists(MappingProducts.Of(pair, MappingMethods.Stated)))
        {
            using var stated = store.Open(MappingProducts.Of(pair, MappingMethods.Stated), MappingRow.Schema);
            foreach (var row in stated.Rows().Where(r => Wanted(r.Source)))
            {
                verses.UnionWith(MappingText.Split(row.From).Select(key => WordKey.Parse(key, fromText).Verse));
            }
        }

        var claims = new Dictionary<long, List<FinalRow>>();
        using (var final = store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema))
        {
            foreach (var row in final.Rows())
            {
                (claims.TryGetValue(row.Id, out var list) ? list : claims[row.Id] = []).Add(row);
            }
        }

        var links = new List<Link>();
        foreach (var rows in claims.Values)
        {
            var settled = rows.FirstOrDefault(r => r.Settled) ?? rows[0];
            var from = MappingText.Split(settled.From).Select(key => WordKey.Parse(key, fromText)).ToList();
            var stated = rows.FirstOrDefault(r => r.Method == MappingMethods.Stated && Wanted(r.Source));
            var ruled = settled.Method == MappingMethods.Ruling && from.Any(key => verses.Contains(key.Verse));
            if (stated is null && !ruled)
            {
                continue;
            }

            links.Add(new Link(from, [.. MappingText.Split(settled.To).Select(key => WordKey.Parse(key, toText))],
                MappingMethods.IsAbsence(settled.Relation), (stated ?? settled).Source));
        }

        return links;
    }
}
