using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

internal sealed record LinkedOriginal(int Id, string Slug, string Name, string Language, TextKind Kind, int Links)
{
    /// <summary>Whether any word of the edition carries a Strong number, which is what a count is counted by.</summary>
    public bool Numbered { get; init; } = true;
}

/// <summary>
/// The originals a text is linked to, which is what every count of how a translation renders an
/// original word is counted over. Shared by the API, which reads the pairs the build counted, and
/// the indexes, which count the same pairs from the files, so the two cannot count over different editions.
/// </summary>
internal static class LinkedOriginals
{
    /// <summary>
    /// The originals a text is linked to, in the order a reader is likeliest to mean them: the
    /// critical editions first, then whichever the text is most fully joined to. The first of each
    /// language is the one a single count is made against, so that the Strong page's renderings and
    /// the rendering page's first answer are counted over the same edition. Read from what the build
    /// counted (<c>text_pair_links</c>): counting the pairs' links as asked read about 35 MB of index.
    /// </summary>
    public static async Task<List<LinkedOriginal>> Of(
        AppDbContext db,
        int textId,
        CancellationToken cancellationToken) =>
        Ordered(await db.TextPairLinks
            .AsNoTracking()
            .Where(p => p.TextId == textId)
            .Select(p => new LinkedOriginal(
                p.EditionId,
                p.Edition!.Slug,
                p.Edition.Name,
                p.Edition.Language,
                p.Edition.Kind,
                p.Links)
            {
                Numbered = p.Numbered,
            })
            .ToListAsync(cancellationToken));

    /// <summary>
    /// The non-translation texts linked to a text, in <see cref="Of"/>'s order, for a caller that has
    /// already counted every pair's links.
    /// </summary>
    public static List<LinkedOriginal> Ordered(IEnumerable<LinkedOriginal> originals) =>
    [
        .. originals
            .Where(original => original.Kind != TextKind.Translation)
            .OrderBy(original => original.Kind == TextKind.CriticalEdition ? 0 : 1)
            .ThenByDescending(original => original.Links)
            .ThenBy(original => original.Slug, StringComparer.Ordinal),
    ];

    /// <summary>
    /// The first of each language in <see cref="Of"/> whose words carry Strong numbers, or the first
    /// where none does. A count is counted by the numbers, so an edition without them counts
    /// nothing: the Ethiopic is joined to Swete's Septuagint more than to any Greek New Testament,
    /// and counted over Swete it reached no Greek number at all.
    /// </summary>
    public static List<LinkedOriginal> Primary(IEnumerable<LinkedOriginal> originals) =>
    [
        .. originals.GroupBy(original => original.Language)
            .Select(language => language.FirstOrDefault(original => original.Numbered) ?? language.First()),
    ];

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
