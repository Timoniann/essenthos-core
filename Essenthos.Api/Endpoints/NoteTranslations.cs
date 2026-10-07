using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A record's notes in the reader's language, where this corpus wrote them and they have been
/// rendered from the English the record says now.
/// </summary>
internal static class NoteTranslations
{
    /// <summary>
    /// The rendered notes, or null: for an English reader, for a record with no notes or none
    /// rendered, and for one whose English was rewritten after it was rendered — the page then shows
    /// the English, which is what the record says. One indexed read.
    /// </summary>
    public static async Task<string?> Of(
        AppDbContext db,
        int entityId,
        string? notes,
        string? language,
        CancellationToken cancellationToken)
    {
        var local = EntityNames.Local(language);
        if (local is null || string.IsNullOrEmpty(notes))
        {
            return null;
        }

        var rendered = await db.EntityNoteTranslations
            .Where(t => t.EntityId == entityId && t.Language == local)
            .Select(t => new { t.Text, t.EnglishSha256 })
            .FirstOrDefaultAsync(cancellationToken);

        return rendered is not null && EnglishNotes.Renders(notes, rendered.EnglishSha256) ? rendered.Text : null;
    }
}
