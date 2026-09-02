using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The people and places the text names, what it calls them, how they stand to one another, and
/// when the source thinks things happened to them.
///
/// A reference here is a canonical verse, not a word. That is what the data states — BibleData
/// tags verses — and stating it at the verse is honest where claiming a word would not be. The
/// word-level layer exists (STEPBible's TIPNR names a person at each occurrence with a
/// disambiguated Strong number) and is a separate load; <c>entity_name.strong_number</c> is the
/// column it will arrive through.
/// </summary>
internal static class EncyclopediaEndpoints
{
    private const int MostPerPage = 100;

    public static void MapEncyclopedia(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/entities", async (
            [FromQuery] string? q,
            [FromQuery] string? kind,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entities = db.Entities.AsQueryable();

            if (kind is { Length: > 0 })
            {
                if (kind is not ("person" or "place"))
                {
                    return Results.BadRequest(new ProblemResponse(
                        $"\"{kind}\" is not a kind of entity. Try person or place."));
                }

                var wanted = kind == "place" ? EntityKind.Place : EntityKind.Person;
                entities = entities.Where(e => e.Kind == wanted);
            }

            if (q is { Length: > 0 })
            {
                // The name as printed, and every other name the entity is called by — Peter is
                // Simon, Cephas and Simon Bar-Jonah, and a search that only reads the headword
                // finds him under one of the four.
                var like = LikePatterns.Containing(q);
                entities = entities.Where(e =>
                    EF.Functions.ILike(e.Name, like)
                    || EF.Functions.ILike(e.Slug, like)
                    || e.Names.Any(n => EF.Functions.ILike(n.Label, like)));
            }

            var total = await entities.CountAsync(cancellationToken);
            var page = await entities
                .OrderBy(e => e.Name).ThenBy(e => e.Slug)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 40, 1, MostPerPage))
                .Select(e => new EntitySummaryResponse(
                    e.Slug,
                    EnumSpelling.Of(e.Kind),
                    e.Name,
                    e.Distinguisher,
                    e.Verses.Count))
                .ToListAsync(cancellationToken);

            return Results.Ok(new EntityListResponse(total, page));
        });

        routes.MapGet("/entities/{slug}", async (
            string slug,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entity = await db.Entities
                .Where(e => e.Slug == slug)
                .Select(e => new
                {
                    e.Id,
                    e.Slug,
                    e.Kind,
                    e.Name,
                    e.Distinguisher,
                    e.Sex,
                    e.Tribe,
                    e.PlaceKind,
                    e.ModernEquivalent,
                    e.Notes,
                    e.OpenBibleId,
                    e.Source,
                    References = e.Verses.Count,
                    Disputed = e.Verses.Count(v => v.Disputed),
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (entity is null)
            {
                return ApiResults.NotFound($"There is no person or place \"{slug}\".");
            }

            var names = await db.EntityNames
                .Where(n => n.EntityId == entity.Id)
                .Select(n => new EntityNameResponse(
                    n.Label, n.Hebrew, n.HebrewTransliterated, n.Greek, n.GreekTransliterated,
                    n.Meaning, n.StrongNumber, n.Kind))
                .ToListAsync(cancellationToken);

            // Both directions at once: a father is not recorded twice, so reading only one side
            // would give Isaac a father and no sons.
            var outward = await db.EntityRelationships
                .Where(r => r.FromEntityId == entity.Id)
                .Select(r => new EntityRelationshipResponse(
                    r.Type, r.Category, r.To!.Slug, r.To.Name, r.To.Distinguisher, false,
                    Reference(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse), r.Notes))
                .ToListAsync(cancellationToken);

            var inward = await db.EntityRelationships
                .Where(r => r.ToEntityId == entity.Id)
                .Select(r => new EntityRelationshipResponse(
                    r.Type, r.Category, r.From!.Slug, r.From.Name, r.From.Distinguisher, true,
                    Reference(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse), r.Notes))
                .ToListAsync(cancellationToken);

            var events = await db.Events
                .Where(e => e.EntityId == entity.Id)
                .OrderBy(e => e.YearFromCreation)
                .Select(e => new EventRow(e, e.Entity!.Slug, e.Entity.Name))
                .ToListAsync(cancellationToken);

            return Results.Ok(new EntityResponse(
                entity.Slug,
                EnumSpelling.Of(entity.Kind),
                entity.Name,
                entity.Distinguisher,
                entity.Sex,
                entity.Tribe,
                entity.PlaceKind,
                entity.ModernEquivalent,
                entity.Notes,
                entity.OpenBibleId,
                entity.Source,
                entity.References,
                entity.Disputed,
                names,
                [.. outward, .. inward],
                [.. events.Select(Event)]));
        });

        routes.MapGet("/entities/{slug}/references", async (
            string slug,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entity = await db.Entities.Where(e => e.Slug == slug).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (entity == 0)
            {
                return ApiResults.NotFound($"There is no person or place \"{slug}\".");
            }

            var references = db.EntityVerses.Where(v => v.EntityId == entity);
            var total = await references.CountAsync(cancellationToken);
            var page = await references
                .OrderBy(v => v.CanonicalBook).ThenBy(v => v.CanonicalChapter).ThenBy(v => v.CanonicalVerse)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(v => new { v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse, v.Label, v.Disputed })
                .ToListAsync(cancellationToken);

            return Results.Ok(new EntityReferenceListResponse(
                total,
                [
                    .. page.Select(v => new EntityReferenceResponse(
                        new BookRefResponse(
                            v.CanonicalBook,
                            BookReferences.Name(v.CanonicalBook),
                            BookReferences.Slug(v.CanonicalBook)),
                        v.CanonicalChapter,
                        v.CanonicalVerse,
                        v.Label,
                        v.Disputed)),
                ]));
        });

        // The timeline. Ordered by the year from creation rather than the BCE year, because that is
        // the number the source computes and the one every event has.
        routes.MapGet("/events", async (
            [FromQuery] string? entity,
            [FromQuery] int? fromYear,
            [FromQuery] int? toYear,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var events = db.Events.AsQueryable();

            if (entity is { Length: > 0 })
            {
                events = events.Where(e => e.Entity!.Slug == entity);
            }

            if (fromYear is { } from)
            {
                events = events.Where(e => e.YearFromCreation >= from);
            }

            if (toYear is { } to)
            {
                events = events.Where(e => e.YearFromCreation <= to);
            }

            var total = await events.CountAsync(cancellationToken);
            var page = await events
                .OrderBy(e => e.YearFromCreation).ThenBy(e => e.Slug)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(e => new EventRow(e, e.Entity == null ? null : e.Entity.Slug,
                    e.Entity == null ? null : e.Entity.Name))
                .ToListAsync(cancellationToken);

            return Results.Ok(new EventListResponse(total, [.. page.Select(Event)]));
        });
    }

    private static VerseRefResponse? Reference(int? book, int? chapter, int? verse) =>
        book is { } ordinal && chapter is { } inChapter && verse is { } atVerse
            ? new VerseRefResponse(
                ordinal, BookReferences.Name(ordinal), BookReferences.Slug(ordinal), inChapter, atVerse)
            : null;

    /// <summary>
    /// The event with the two fields that need a join, read in the query rather than off a
    /// navigation property. Projecting <c>e.Entity.Slug</c> through a method EF cannot translate
    /// left every one of the 572 events without a person on it, while the filter that uses the
    /// same navigation worked — so the timeline could select by a person and never name one.
    /// </summary>
    private sealed record EventRow(Database.Entities.Event Event, string? EntitySlug, string? EntityName);

    /// <summary>
    /// Which side of the era a year falls. The source counts forward from the creation without a
    /// sign, so its year 3,969 answers <c>8</c> meaning AD 8, and nothing in the number says so.
    /// The turn is at 3,961, and it is arithmetic rather than a guess: below it the BCE year is
    /// 3,962 less the count, above it the AD year is the count less 3,961.
    /// </summary>
    private const int LastYearBeforeChrist = 3961;

    private static EventResponse Event(EventRow row) => Event(row.Event, row.EntitySlug, row.EntityName);

    private static EventResponse Event(
        Database.Entities.Event e,
        string? entitySlug,
        string? entityName) => new(
        e.Slug,
        e.Name,
        e.Description,
        e.Kind,
        entitySlug,
        entityName,
        e.YearFromCreation,
        e.BceYear,
        e.AgeAtEvent,
        e.Calculation,
        Reference(e.CanonicalBook, e.CanonicalChapter, e.CanonicalVerse),
        e.Location,
        e.UssherAnnoMundi,
        e.UssherBceYear,
        e.UssherParagraph,
        e.ShulmanAnnoMundi,
        e.Notes)
    {
        Era = e.YearFromCreation is { } year && year > LastYearBeforeChrist ? "AD" : "BCE",
    };
}

internal record EntitySummaryResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    int References);

internal record EntityListResponse(int Total, IList<EntitySummaryResponse> Items);

/// <param name="Disputed">
/// References the source itself cannot resolve. BibleData holds the God of Israel and Jesus as one
/// entity; 1,416 New Testament references say only "God" or "Lord", and which of the two is meant
/// is a reading of the text rather than a fact about the dataset.
/// </param>
internal record EntityResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    string? Sex,
    string? Tribe,
    string? PlaceKind,
    string? ModernEquivalent,
    string? Notes,
    string? OpenBibleId,
    string Source,
    int References,
    int Disputed,
    IList<EntityNameResponse> Names,
    IList<EntityRelationshipResponse> Relationships,
    IList<EventResponse> Events);

internal record EntityNameResponse(
    string Label,
    string? Hebrew,
    string? HebrewTransliterated,
    string? Greek,
    string? GreekTransliterated,
    string? Meaning,
    string? StrongNumber,
    string? Kind);

/// <param name="Category">
/// <c>explicit</c> where a verse says it, <c>inferred</c> where the source worked it out. Keeping
/// the two apart is the same discipline the link table applies to words.
/// </param>
/// <param name="Inward">
/// True when this is the other entity's relationship read backwards — Isaac is recorded as the son
/// of Abraham, and Abraham's page shows the same row from his side.
/// </param>
internal record EntityRelationshipResponse(
    string Type,
    string Category,
    string Slug,
    string Name,
    string? Distinguisher,
    bool Inward,
    VerseRefResponse? Reference,
    string? Notes);

internal record EntityReferenceResponse(
    BookRefResponse Book,
    int Chapter,
    int Verse,
    string? Label,
    bool Disputed);

internal record EntityReferenceListResponse(int Total, IList<EntityReferenceResponse> Items);

/// <param name="Calculation">
/// The arithmetic that produced the year, in a sentence, so it can be checked rather than
/// believed. This is why this dataset was chosen over the others.
/// </param>
/// <param name="UssherAnnoMundi">
/// What Ussher makes it, and what Shulman makes it after Seder Olam. Beside the figure rather than
/// instead of it: a reader is owed the disagreement, not a winner.
/// </param>
internal record EventResponse(
    string Slug,
    string Name,
    string? Description,
    string? Kind,
    string? EntitySlug,
    string? EntityName,
    int? YearFromCreation,
    int? BceYear,
    int? AgeAtEvent,
    string? Calculation,
    VerseRefResponse? Reference,
    string? Location,
    int? UssherAnnoMundi,
    int? UssherBceYear,
    string? UssherParagraph,
    int? ShulmanAnnoMundi,
    string? Notes)
{
    /// <summary>
    /// <c>BCE</c> or <c>AD</c>. The source writes its year without a sign and keeps counting past
    /// the turn, so its <c>8</c> may mean 8 BCE or AD 8 and the number alone cannot say which.
    /// </summary>
    public string Era { get; init; } = "BCE";
}

internal record EventListResponse(int Total, IList<EventResponse> Items);
