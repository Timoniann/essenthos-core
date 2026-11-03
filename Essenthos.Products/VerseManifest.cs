namespace Essenthos.Core.Products;

/// <summary>
/// One line of a text's <c>verses</c> product: the verse's own key, where the edition prints it in
/// its chapter, how many words it has, and its two hashes. A product that cites the verse joins
/// against this line; a key gone from the manifest or a hash that moved marks the citing rows stale.
/// </summary>
/// <param name="Own">The verse's own address.</param>
/// <param name="Sequence">Print order inside the chapter, from 1; not always the number's order.</param>
/// <param name="Words">Word count, which alone catches nothing a hash does not, but is what a report prints.</param>
public sealed record VerseManifestRow(VerseKey Own, int Sequence, int Words, VerseHashes Hashes)
{
    public static readonly ProductSchema<VerseManifestRow> Schema = new ProductSchema<VerseManifestRow>()
        .VerseKey("own", row => row.Own)
        .Integer("seq", row => row.Sequence)
        .Integer("words", row => row.Words)
        .Text("h_text", row => row.Hashes.Text)
        .Text("h_ann", row => row.Hashes.Annotation)
        .Reads(row => new VerseManifestRow(
            Products.VerseKey.Parse(row.Text(0)),
            row.Int32(1),
            row.Int32(2),
            new VerseHashes(row.Text(3), row.Text(4))));

    /// <summary>The product name of a text's manifest, for <see cref="ProductStore"/>.</summary>
    public static string ProductOf(string text) => $"texts/{text}/verses";
}

/// <summary>
/// Which products of a verse a change between two manifests invalidates. Comparing by key, a verse
/// is <see cref="Gone"/>, <see cref="New"/>, changed in its text, changed only in its annotation,
/// or the same. Rows pairing printed words follow <see cref="VerseChange.Text"/>; rows reading
/// lemma, number or gloss follow either.
/// </summary>
public enum VerseChange
{
    Same,
    Annotation,
    Text,
    New,
    Gone,
}

public static class VerseManifests
{
    public static VerseChange Compare(VerseHashes? before, VerseHashes? after)
    {
        if (before is null)
        {
            return after is null ? VerseChange.Same : VerseChange.New;
        }

        if (after is null)
        {
            return VerseChange.Gone;
        }

        if (before.Value.Text != after.Value.Text)
        {
            return VerseChange.Text;
        }

        return before.Value.Annotation != after.Value.Annotation ? VerseChange.Annotation : VerseChange.Same;
    }

    /// <summary>Every key of either manifest that is not the same in both, with what happened to it.</summary>
    public static IReadOnlyList<(VerseKey Key, VerseChange Change)> Diff(
        IReadOnlyDictionary<VerseKey, VerseHashes> before,
        IReadOnlyDictionary<VerseKey, VerseHashes> after)
    {
        var changes = new List<(VerseKey, VerseChange)>();
        foreach (var key in before.Keys.Union(after.Keys).Order())
        {
            var change = Compare(
                before.TryGetValue(key, out var old) ? old : null,
                after.TryGetValue(key, out var current) ? current : null);
            if (change != VerseChange.Same)
            {
                changes.Add((key, change));
            }
        }

        return changes;
    }
}
