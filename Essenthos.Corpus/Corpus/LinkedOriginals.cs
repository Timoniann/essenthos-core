using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

internal sealed record LinkedOriginal(int Id, string Slug, string Name, string Language, TextKind Kind, int Links);

/// <summary>
/// The originals a text is linked to, which is what every count of how a translation renders an
/// original word is counted over. Shared by the API, which counts as it is asked, and the load,
/// which counts the lexicon's cards ahead of it, so the two cannot count over different editions.
/// </summary>
internal static class LinkedOriginals
{
    /// <summary>
    /// The texts this one is linked to by anything at all, asked as one existence probe per text
    /// on the index that leads with both ends of a link. The distinct over the link table it
    /// replaces read every one of the King James's 2.3 million links to learn eight numbers.
    /// </summary>
    public static Task<List<int>> Neighbours(AppDbContext db, int textId, CancellationToken cancellationToken) =>
        db.Texts
            .Where(t => t.Id != textId
                        && (db.Links.Any(l => l.FromTextId == textId && l.ToTextId == t.Id)
                            || db.Links.Any(l => l.FromTextId == t.Id && l.ToTextId == textId)))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The originals a text is linked to, in the order a reader is likeliest to mean them: the
    /// critical editions first, then whichever the text is most fully joined to. The first of each
    /// language is the one a single count is made against, so that the Strong page's renderings and
    /// the rendering page's first answer are counted over the same edition.
    /// </summary>
    public static async Task<List<LinkedOriginal>> Of(
        AppDbContext db,
        int textId,
        CancellationToken cancellationToken)
    {
        var neighbours = await Neighbours(db, textId, cancellationToken);
        var originals = await db.Texts
            .Where(t => neighbours.Contains(t.Id) && t.Kind != TextKind.Translation)
            .Select(t => new LinkedOriginal(
                t.Id,
                t.Slug,
                t.Name,
                t.Language,
                t.Kind,
                db.Links.Count(l => l.FromTextId == textId && l.ToTextId == t.Id)
                + db.Links.Count(l => l.FromTextId == t.Id && l.ToTextId == textId)))
            .ToListAsync(cancellationToken);

        return
        [
            .. originals
                .OrderBy(original => original.Kind == TextKind.CriticalEdition ? 0 : 1)
                .ThenByDescending(original => original.Links)
                .ThenBy(original => original.Slug, StringComparer.Ordinal),
        ];
    }

    /// <summary>The first of each language in <see cref="Of"/>.</summary>
    public static List<LinkedOriginal> Primary(IEnumerable<LinkedOriginal> originals) =>
        [.. originals.GroupBy(original => original.Language).Select(language => language.First())];

    /// <summary>
    /// The one edition a count of how the text reaches <paramref name="number"/> is made over: the first
    /// of <paramref name="primary"/> that can carry the number at all, since a Hebrew number stands only
    /// in a Hebrew or Aramaic text and a Greek one only in a Greek text.
    /// </summary>
    public static LinkedOriginal? WitnessFor(IEnumerable<LinkedOriginal> primary, string number) =>
        primary.FirstOrDefault(original => Writes(original.Language, number));

    private static bool Writes(string language, string number) =>
        number.StartsWith('G') ? language == "grc" : language is "hbo" or "arc";
}
