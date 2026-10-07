using Essenthos.Core.Corpus;
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

    /// <summary>
    /// God, by address. He is nobody's kin on a family tree: "his Son" in 1 Corinthians 1:9 and
    /// Luke's "Adam, which was the son of God" are not descent, and a tree that read them so would
    /// stand God the Father beside Joseph as Jesus's other parent.
    /// </summary>
    internal static readonly string[] Deities = ["yhvh", "yhvh-2", "holy-spirit"];

    internal static async Task<EntityFamilyResponse> Family(
        AppDbContext db,
        IReadOnlyCollection<string> slugs,
        string? language,
        CancellationToken cancellationToken,
        bool generated = true,
        string? prose = null)
    {
        var people = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug, e.Name, e.Distinguisher, e.Sex })
            .ToListAsync(cancellationToken);
        var ids = people.Select(p => p.Id).ToList();
        var ours = await OursOnlyRecords.Among(db, [.. people.Select(p => p.Slug)], cancellationToken);

        // Both directions, as the entity page reads them: a father is recorded once, and reading
        // one side only would give Isaac a father and Abraham no son.
        var rows = await db.EntityRelationships
            .Where(r => ids.Contains(r.FromEntityId) || ids.Contains(r.ToEntityId))
            .OrderBy(r => r.Id)
            .Where(r => !ChapterFamily.Types.Contains(r.Type)
                        || !(Deities.Contains(r.From!.Slug) || Deities.Contains(r.To!.Slug)))
            .Select(r => new
            {
                r.FromEntityId,
                r.ToEntityId,
                r.Type,
                From = r.From!.Slug,
                To = r.To!.Slug,
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
            })
            .ToListAsync(cancellationToken);

        VerseRefResponse? Cited(int? book, int? chapter, int? verse) =>
            book is { } b && chapter is { } c && verse is { } v
                ? new VerseRefResponse(b, BookReferences.Name(b), BookReferences.Slug(b), c, v)
                : null;

        var local = await EntityNames.Of(db, ids, language, cancellationToken);
        var lines = await EntityDistinguishers.OfSlugs(
            db, [.. people.Select(p => p.Slug)], ReaderLanguages.Prose(prose, language), cancellationToken);
        var pictured = await ImageEndpoints.Leading(db, slugs, cancellationToken, generated);
        var order = slugs.Select((slug, index) => (slug, index)).ToDictionary(p => p.slug, p => p.index, StringComparer.Ordinal);

        return new EntityFamilyResponse(
        [
            .. people
                .OrderBy(p => order.GetValueOrDefault(p.Slug))
                .Select(p => new FamilyMemberResponse(
                    p.Slug,
                    p.Name,
                    local.GetValueOrDefault(p.Id),
                    OursOnlyRecords.Line(ours, p.Slug, p.Distinguisher),
                    ours.TryGetValue(p.Slug, out var own) ? own.Sex : p.Sex,
                    [
                        .. rows.Where(r => r.FromEntityId == p.Id)
                            .Select(r => new FamilyTieResponse(r.Type, false, r.To)
                            {
                                Reference = Cited(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse),
                            })
                            .Concat(rows.Where(r => r.ToEntityId == p.Id)
                                .Select(r => new FamilyTieResponse(r.Type, true, r.From)
                                {
                                    Reference = Cited(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse),
                                }))
                            .Where(tie => tie.Slug != p.Slug)
                            .OrderBy(tie => tie.Reference is null)
                            .DistinctBy(tie => (tie.Type, tie.Inward, tie.Slug)),
                    ])
                {
                    LocalDistinguisher = EntityDistinguishers.For(ours, lines, p.Slug),
                    Thumbnail = pictured.GetValueOrDefault(p.Slug),
                }),
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
    IList<FamilyTieResponse> Ties)
{
    /// <summary>Our own line under the name in the language asked for, where this corpus rendered one.</summary>
    public string? LocalDistinguisher { get; init; }

    /// <summary>The picture the person's page leads with, to show small; null where there is none.</summary>
    public EntityThumbnailResponse? Thumbnail { get; init; }
}

/// <summary>
/// One relationship as its pair: <c>son-of</c> read inward on Abraham's side is Isaac.
/// </summary>
/// <param name="Inward">True where the row names this person second, exactly as on the entity page.</param>
internal sealed record FamilyTieResponse(string Type, bool Inward, string Slug)
{
    /// <summary>The verse the tie was read from, where the row names one; a tree labels a second father by it.</summary>
    public VerseRefResponse? Reference { get; init; }
}
