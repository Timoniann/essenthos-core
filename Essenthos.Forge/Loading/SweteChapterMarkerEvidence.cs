using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal static class SweteChapterMarkerEvidence
{
    public static async Task GuardRemoved(AppDbContext db, long[] words, CancellationToken token)
    {
        var known = await db.Words.CountAsync(w => words.Contains(w.Id)
            && w.Text!.Slug == SweteTextSource.Slug && w.Surface == "XX" && w.Position == 60
            && w.Verse!.Book!.CanonicalOrdinal == 10 && w.Verse.ChapterNumber == 19
            && w.Verse.Number == 43 && w.Verse.Label == "", token);
        if (words.Length != 1 || known != 1)
            throw new InvalidOperationException("The removed word is not the known Swete chapter marker; nothing changed.");

        if (await db.WordEntities.AnyAsync(a => words.Contains(a.WordId), token)
            || await db.Links.AnyAsync(l => l.Words.Any(w => words.Contains(w.WordId))
                && !((l.Method == LinkMethod.Aligner && l.Claims.All(c => c.Method == LinkMethod.Aligner))
                    || (l.Method == LinkMethod.Lexical && l.Relation == LinkRelation.Expands
                        && l.FromText!.Slug == SweteTextSource.Slug && l.ToText!.Slug == SeptuagintTextSource.Slug
                        && l.Provenance!.Source == SeptuagintLinkLoader.Source
                        && l.Words.Count == 1 && l.Words.All(w => w.Side == LinkSide.From)
                        && l.Claims.All(c => c.Method == LinkMethod.Lexical
                            && c.Provenance!.Source == SeptuagintLinkLoader.Source))), token))
            throw new InvalidOperationException("The chapter marker has protected annotation or source/manual/accepted evidence; nothing changed.");
    }
}
