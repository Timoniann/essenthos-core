using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The titles and glosses of a names block in a reader's language, keyed by the English they render.
/// A name itself is not rendered here: it comes from the text the reader chose.
/// </summary>
internal static class EntityNameTranslations
{
    /// <summary>
    /// The kinds of label that are words rather than a name — <em>King of Israel</em>, <em>the land
    /// flowing with milk and honey</em> — and so are shown in the reader's language.
    /// </summary>
    public static readonly HashSet<string> RenderedKinds = new(StringComparer.Ordinal)
    {
        "title", "description", "term", "collective",
    };

    public static async Task<Dictionary<string, string>> Of(
        AppDbContext db,
        IEnumerable<string?> english,
        string? language,
        CancellationToken cancellationToken)
    {
        var local = EntityNames.Local(language);
        var asked = english.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (local is null || asked.Count == 0)
        {
            return [];
        }

        return await db.EntityNameTranslations
            .Where(t => t.Language == local && asked.Contains(t.English))
            .ToDictionaryAsync(t => t.English, t => t.Text, StringComparer.Ordinal, cancellationToken);
    }
}
