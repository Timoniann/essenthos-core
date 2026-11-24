using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Machine translations of the English the records carry from their sources, found by that English.
/// Nothing is answered for an English reader, and nothing for English no rendering was made of — the
/// page then shows the source's words, which is what it says.
/// </summary>
internal static class TextTranslations
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    /// <summary>Each English string that has a rendering in <paramref name="language"/>, beside it. One indexed read.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> Of(
        AppDbContext db,
        IEnumerable<string?> english,
        string? language,
        CancellationToken cancellationToken)
    {
        var local = EntityNames.Local(language);
        if (local is null)
        {
            return None;
        }

        var bySha = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var text in english)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                bySha.TryAdd(EnglishNotes.Digest(text), text);
            }
        }

        if (bySha.Count == 0)
        {
            return None;
        }

        var shas = bySha.Keys.ToList();
        var rows = await db.TextTranslations
            .Where(t => t.Language == local && shas.Contains(t.EnglishSha256))
            .Select(t => new { t.EnglishSha256, t.Text })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => bySha[r.EnglishSha256], r => r.Text, StringComparer.Ordinal);
    }

    /// <summary>The rendering of one string from a lookup <see cref="Of"/> made, or null.</summary>
    public static string? In(IReadOnlyDictionary<string, string> rendered, string? english) =>
        english is not null && rendered.TryGetValue(english, out var text) ? text : null;
}
