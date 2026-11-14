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

    /// <summary>God, by address: <see cref="ChapterFamily.Deities"/>.</summary>
    internal static readonly string[] Deities = ChapterFamily.Deities;

    internal static async Task<EntityFamilyResponse> Family(
        AppDbContext db,
        IReadOnlyCollection<string> slugs,
        string? language,
        CancellationToken cancellationToken,
        bool generated = true,
        string? prose = null)
    {
        // The cards the build made for this pair of languages (record_name, record_line); a pair it did not make is composed
        // here, the same way.
        var words = ReaderLanguages.Prose(prose, language);
        var people = language is { Length: > 0 } && words is { Length: > 0 }
            ? await Stored(db, slugs, language, words, cancellationToken)
            : [];
        if (people.Count == 0)
        {
            people = await RecordCards.Of(db, slugs, language, prose, cancellationToken);
        }

        var ids = people.Select(p => p.Id).ToList();

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

        var pictured = await ImageEndpoints.Leading(db, slugs, cancellationToken, generated);
        var order = slugs.Select((slug, index) => (slug, index)).ToDictionary(p => p.slug, p => p.index, StringComparer.Ordinal);

        return new EntityFamilyResponse(
        [
            .. people
                .OrderBy(p => order.GetValueOrDefault(p.Slug))
                .Select(p => new FamilyMemberResponse(
                    p.Slug,
                    p.Name,
                    p.LocalName,
                    p.Distinguisher,
                    p.Sex,
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
                    LocalDistinguisher = p.Local,
                    DescriptorLine = p.Described,
                    Thumbnail = pictured.GetValueOrDefault(p.Slug),
                    AlsoCalled = p.AlsoCalled,
                }),
        ]);
    }

    /// <summary>The cards the build made of these records for this pair of languages, in one statement; empty where it made none.</summary>
    private static async Task<List<RecordCard>> Stored(
        AppDbContext db, IReadOnlyCollection<string> slugs, string names, string prose, CancellationToken cancellationToken) =>
        await (
                from n in db.RecordNames
                join l in db.RecordLines on new { n.Names, n.Slug } equals new { l.Names, l.Slug }
                join e in db.Entities on n.Slug equals e.Slug
                where n.Names == names && l.Prose == prose && slugs.Contains(n.Slug)
                select new RecordCard(e.Id, n.Slug, n.Kind, n.Name, n.LocalName, n.Distinguisher, n.Sex, n.AlsoCalled, l.LocalLine, l.DescriptorLine))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The document of the family a person is in (<see cref="FamilyDocuments"/>), as the build stored it for this pair of
    /// languages, in one statement; composed here for a pair it did not make. Null where the slug is no person's.
    /// </summary>
    internal static async Task<(string Body, string Hash)?> Component(
        AppDbContext db, string slug, string? language, string? prose, CancellationToken cancellationToken)
    {
        var names = language is { Length: > 0 } ? language : RecordCards.English;
        var words = ReaderLanguages.Prose(prose, names)!;
        var stored = await (
                from m in db.FamilyMembers
                join c in db.FamilyComponents on m.Component equals c.Component
                where m.Slug == slug && c.Prose == words && c.Names == names
                select new { c.Body, c.Hash })
            .FirstOrDefaultAsync(cancellationToken);
        if (stored is not null)
        {
            return (stored.Body, stored.Hash);
        }

        return await FamilyDocuments.Read(db, slug, names, words, cancellationToken) is { } read ? (read.Body, read.Hash) : null;
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

    /// <summary>
    /// What the corpus says the person is, in the words of the language asked for, as the index
    /// phrases it — <em>дочка Ізмаїла, дружина Ісава</em> — where there is no
    /// <see cref="LocalDistinguisher"/>; null where there is one, where nothing is said, or where it
    /// could only be said in another language.
    /// </summary>
    public string? DescriptorLine { get; init; }

    /// <summary>The picture the person's page leads with, to show small; null where there is none.</summary>
    public EntityThumbnailResponse? Thumbnail { get; init; }

    /// <summary>The name a renamed person's record is filed under, which the tree's finder also knows them by.</summary>
    public string? AlsoCalled { get; init; }
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
