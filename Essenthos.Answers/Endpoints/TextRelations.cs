using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>How one text stands to another, as the text asked about sees it.</summary>
/// <param name="Relation">translated-from, revised-from, same-family-as or collated-against.</param>
/// <param name="Text">The other text, by its identifier.</param>
/// <param name="Incoming">
/// True where the claim is the other text's about this one: the King James is revised from on the
/// American Standard's row, and the American Standard revised from it on the King James's.
/// </param>
/// <param name="Scope">
/// Canonical book ranges the claim is limited to, <c>1-39</c> or <c>67-79</c>; null for every book.
/// </param>
/// <param name="Source">The work the claim rests on.</param>
internal record TextRelationResponse(
    string Relation,
    string Text,
    bool Incoming,
    string? Scope,
    string? Note,
    string? Source);

internal static class TextRelations
{
    /// <summary>Every relation naming the text, its own claims first and each side in book order.</summary>
    public static async Task<IList<TextRelationResponse>> Of(
        AppDbContext db,
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.TextRelations
            .Where(r => r.FromTextId == textId || r.ToTextId == textId)
            .Select(r => new
            {
                r.Relation,
                Incoming = r.ToTextId == textId,
                Other = r.ToTextId == textId ? r.FromText!.Slug : r.ToText!.Slug,
                r.Scope,
                r.Note,
                r.Source,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .OrderBy(r => r.Incoming)
                .ThenBy(r => FirstBook(r.Scope))
                .ThenBy(r => r.Relation)
                .ThenBy(r => r.Other, StringComparer.Ordinal)
                .Select(r => new TextRelationResponse(
                    EnumSpelling.Of(r.Relation), r.Other, r.Incoming, r.Scope, r.Note, r.Source)),
        ];
    }

    /// <summary>The first book a scope names, so a claim for the whole Bible comes before the halves.</summary>
    private static int FirstBook(string? scope) =>
        scope is null ? 0 : int.TryParse(scope.Split('-', ',')[0], out var first) ? first : int.MaxValue;
}
