using Essenthos.Core.Corpus;
using Essenthos.Core.Products;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// One line of a text's frame file: a verse of the edition, or a run of consecutive ones, and the
/// frame addresses it stands at.
/// </summary>
/// <param name="Own">The edition's verse, by its own address; the first of the run.</param>
/// <param name="Through">The last verse number of the run, null for one verse. Every verse of a run has
/// the first one's letter and stands one row further on at each of its addresses.</param>
/// <param name="Canonical">The frame addresses of <paramref name="Own"/>, the first its primary one.</param>
/// <param name="Relation">What the line says, for the reader of the file: <c>moved</c> (one other address),
/// <c>shares</c> (an address another verse of the edition also stands at), <c>spans</c> (several),
/// <c>twin</c> (its own address and the same verse under the passage's other name), <c>title</c> (one of
/// them is a psalm's title verse). The loader reads the addresses, not this.</param>
internal sealed record FrameRow(VerseKey Own, int? Through, IReadOnlyList<VerseKey> Canonical, string Relation)
{
    public static readonly ProductSchema<FrameRow> Schema = new ProductSchema<FrameRow>()
        .VerseKey("own", row => row.Own)
        .OptionalInteger("through", row => row.Through)
        .Text("canonical", row => string.Join(',', row.Canonical))
        .Text("relation", row => row.Relation)
        .Reads(row => new FrameRow(
            VerseKey.Parse(row.Text(0)),
            row.Int32OrNull(1),
            [.. row.Text(2).Split(',').Select(key => VerseKey.Parse(key))],
            row.Text(3)));

    /// <summary>The verses the line stands for, each with its addresses.</summary>
    public IEnumerable<(VerseKey Own, IReadOnlyList<VerseKey> Canonical)> Verses()
    {
        var last = Through ?? Own.Verse;
        for (var step = 0; step <= last - Own.Verse; step++)
        {
            yield return (Own with { Verse = Own.Verse + step },
                [.. Canonical.Select(address => address with { Verse = address.Verse + step })]);
        }
    }
}

/// <summary>
/// A text's frame as a product, <c>texts/&lt;SLUG&gt;/frame</c>: where each of its verses stands in the
/// shared frame. A verse not listed stands at its own address, so the file lists only the verses the
/// frame moves; runs of consecutive verses with one shift are one line.
/// </summary>
internal static class FrameFile
{
    public static string Product(string slug) => $"texts/{slug}/frame";

    public const string Relations =
        "relation: moved | shares | spans | twin | title, for the reader of the file; the loader reads the addresses";

    /// <summary>
    /// The lines for a text, from each verse's own address and its frame addresses in order, the first
    /// the primary one. A verse standing only at its own address is left out.
    /// </summary>
    public static List<FrameRow> Rows(IEnumerable<(VerseKey Own, IReadOnlyList<VerseKey> Canonical)> verses)
    {
        var placed = verses
            .Where(verse => verse.Canonical is not [var only] || only != AtOwn(verse.Own))
            .OrderBy(verse => verse.Own)
            .ToList();
        var primaries = verses.GroupBy(verse => verse.Canonical[0]).ToDictionary(group => group.Key, group => group.Count());

        var rows = new List<FrameRow>();
        foreach (var (own, canonical) in placed)
        {
            var relation = Relation(own, canonical, primaries);
            if (rows.Count > 0 && Continues(rows[^1], own, canonical, relation))
            {
                rows[^1] = rows[^1] with { Through = own.Verse };
                continue;
            }

            rows.Add(new FrameRow(own, null, canonical, relation));
        }

        return rows;
    }

    /// <summary>The address a verse stands at when nothing moves it: its own, without its letter.</summary>
    public static VerseKey AtOwn(VerseKey own) => new(own.Book, own.Chapter, own.Verse);

    private static string Relation(VerseKey own, IReadOnlyList<VerseKey> canonical, IReadOnlyDictionary<VerseKey, int> primaries)
    {
        if (canonical.Any(address => address.Verse == CanonicalReference.TitleVerse))
        {
            return "title";
        }

        if (canonical is [var only])
        {
            return primaries[only] > 1 ? "shares" : "moved";
        }

        return canonical is [var first, var second] && first == AtOwn(own)
                                                    && TwinPassages.Other((first.Book, first.Chapter, first.Verse)) is { } twin
                                                    && second == new VerseKey(twin.Book, twin.Chapter, twin.Verse)
            ? "twin"
            : "spans";
    }

    /// <summary>
    /// Whether the verse carries the line before it on: the next verse with the same letter, with as many
    /// addresses, each one row further on, and nothing shared. A run that opens at a title goes on as moved.
    /// </summary>
    private static bool Continues(FrameRow line, VerseKey own, IReadOnlyList<VerseKey> canonical, string relation)
    {
        var step = (line.Through ?? line.Own.Verse) - line.Own.Verse + 1;
        return line.Own with { Verse = line.Own.Verse + step } == own
               && relation != "shares" && line.Relation != "shares"
               && (relation == line.Relation || (line.Relation == "title" && relation == "moved"))
               && canonical.Count == line.Canonical.Count
               && canonical.Select((address, index) => address == line.Canonical[index] with
               {
                   Verse = line.Canonical[index].Verse + step,
               }).All(same => same);
    }
}
