using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// One thing a person is known for, as a clause that reads after <em>known for</em>, and the verses
/// that say it.
/// </summary>
/// <param name="Text">The clause in the language asked for, or in English where it has not been rendered into it.</param>
/// <param name="Language">The language <paramref name="Text"/> is in, as the texts are tagged: <c>eng</c>, <c>ukr</c>, <c>deu</c>, <c>spa</c>.</param>
/// <param name="References">The runs of verses it rests on, in the order they are cited; a run of one verse begins and ends at it.</param>
internal sealed record KnownForResponse(string Text, string Language, IList<VerseRangeResponse> References);

/// <summary>What a person is known for, in the language a line's own words are said in.</summary>
internal static class KnownForClauses
{
    private const string English = "eng";

    /// <summary>The person's clauses in order, or null where nothing is said of what they are known for.</summary>
    public static async Task<IList<KnownForResponse>?> Of(
        AppDbContext db,
        int entityId,
        string? language,
        CancellationToken cancellationToken)
    {
        var local = EntityNames.Local(language);
        var clauses = await db.EntityKnownFor
            .Where(k => k.EntityId == entityId)
            .OrderBy(k => k.Ordinal)
            .Select(k => new
            {
                k.English,
                Local = local == null ? null : k.Texts.Where(t => t.Language == local).Select(t => t.Text).FirstOrDefault(),
                References = k.References
                    .OrderBy(r => r.Position)
                    .Select(r => new { r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse, r.EndChapter, r.EndVerse })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        if (clauses.Count == 0)
        {
            return null;
        }

        return
        [
            .. clauses.Select(clause => new KnownForResponse(
                clause.Local ?? clause.English,
                clause.Local is null ? English : local!,
                [
                    .. clause.References.Select(r => new VerseRangeResponse(
                        BookReferences.At(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse)!,
                        BookReferences.At(r.CanonicalBook, r.EndChapter, r.EndVerse)!)),
                ])),
        ];
    }
}
