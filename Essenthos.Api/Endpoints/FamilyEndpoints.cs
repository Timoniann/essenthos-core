using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Many people at once, each with nothing but what a family tree is drawn from: the name, the line
/// that tells namesakes apart, the sex, and every relationship as its bare pair.
///
/// A tree that grows a generation at a time asks for a whole generation in one go. The entity page
/// is the wrong thing to ask for it: David's is 86 KB, of which the relationships' witnesses and
/// credits are 61 KB, and a tree four generations deep holds a hundred people. What a tree needs of
/// each is under a kilobyte.
/// </summary>
internal static class FamilyEndpoints
{
    /// <summary>
    /// The most people one request answers for. A tree asks for one ring of relatives at a time, and
    /// the widest ring the corpus holds — Jacob's grandchildren — is far below it.
    /// </summary>
    public const int MostPeople = 400;

    internal static async Task<EntityFamilyResponse> Family(
        AppDbContext db,
        IReadOnlyCollection<string> slugs,
        string? language,
        CancellationToken cancellationToken)
    {
        var people = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug, e.Name, e.Distinguisher, e.Sex })
            .ToListAsync(cancellationToken);
        var ids = people.Select(p => p.Id).ToList();

        // Both directions, as the entity page reads them: a father is recorded once, and reading
        // one side only would give Isaac a father and Abraham no son.
        var rows = await db.EntityRelationships
            .Where(r => ids.Contains(r.FromEntityId) || ids.Contains(r.ToEntityId))
            .OrderBy(r => r.Id)
            .Select(r => new { r.FromEntityId, r.ToEntityId, r.Type, From = r.From!.Slug, To = r.To!.Slug })
            .ToListAsync(cancellationToken);

        var local = await EntityNames.Of(db, ids, language, cancellationToken);
        var order = slugs.Select((slug, index) => (slug, index)).ToDictionary(p => p.slug, p => p.index, StringComparer.Ordinal);

        return new EntityFamilyResponse(
        [
            .. people
                .OrderBy(p => order.GetValueOrDefault(p.Slug))
                .Select(p => new FamilyMemberResponse(
                    p.Slug,
                    p.Name,
                    local.GetValueOrDefault(p.Id),
                    p.Distinguisher,
                    p.Sex,
                    [
                        .. rows.Where(r => r.FromEntityId == p.Id)
                            .Select(r => new FamilyTieResponse(r.Type, false, r.To))
                            .Concat(rows.Where(r => r.ToEntityId == p.Id)
                                .Select(r => new FamilyTieResponse(r.Type, true, r.From)))
                            .Where(tie => tie.Slug != p.Slug)
                            .Distinct(),
                    ])),
        ]);
    }

    /// <summary>
    /// The slugs a request names, once each: <c>slugs=moses,aaron</c>. Null where it names none or
    /// more than <see cref="MostPeople"/>.
    /// </summary>
    internal static IReadOnlyList<string>? Requested(string? slugs)
    {
        var named = (slugs ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return named.Count is 0 or > MostPeople ? null : named;
    }
}

/// <summary>The people a family request named, in the order it named them; unknown slugs are left out.</summary>
internal sealed record EntityFamilyResponse(IList<FamilyMemberResponse> People);

/// <summary>
/// One person as a family tree needs them.
/// </summary>
/// <param name="LocalName">The name in the language asked for, as on the index; null where there is none.</param>
/// <param name="Ties">Every relationship, read from this person's side, once each.</param>
internal sealed record FamilyMemberResponse(
    string Slug,
    string Name,
    string? LocalName,
    string? Distinguisher,
    string? Sex,
    IList<FamilyTieResponse> Ties);

/// <summary>
/// One relationship as its pair: <c>son-of</c> read inward on Abraham's side is Isaac.
/// </summary>
/// <param name="Inward">True where the row names this person second, exactly as on the entity page.</param>
internal sealed record FamilyTieResponse(string Type, bool Inward, string Slug);
