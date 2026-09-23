using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What one chapter speaks of: the people, places, peoples, titles and terms it names, the events
/// the sources set in it, and the periods it falls in — drawn from records the encyclopedia already
/// holds, for a panel beside the reader.
///
/// A record is here because a word of some text in the chapter names it, or because the record's
/// own list of verses says it is named here. Both, not either: the word layer reaches every text
/// the corpus aligns, so a man the English calls <em>he</em> and the Hebrew names is still found,
/// and the verse lists reach the places the word layer has not resolved. Both are addressed in the
/// shared canonical frame, which is the numbering the reader's rows already use.
/// </summary>
internal static class ContextEndpoints
{
    /// <summary>
    /// The one kind of period a chapter is never placed in. Eras, reigns, judgeships and the spans
    /// below them say where a chapter stands; a lifetime does not, because every chapter falls inside
    /// the lives of dozens of people it never mentions.
    /// </summary>
    private const string LifeKind = "life";

    public static void MapContext(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/context/{book}/{chapter:int}", async (
            string book,
            int chapter,
            [FromQuery] string? language,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var ordinal = BookReferences.ResolveOrdinal(book);
            if (ordinal is null)
            {
                return ApiResults.NotFound(BookReferences.FormatHint(book));
            }

            var chapterCount = await canon.ChapterCount(ordinal.Value, cancellationToken);
            if (chapter < 1 || chapter > chapterCount)
            {
                return ApiResults.NotFound(
                    $"{BookReferences.Name(ordinal.Value)} has {chapterCount} chapters in the shared numbering, " +
                    $"so there is no chapter {chapter}.");
            }

            return Results.Ok(await Context(db, ordinal.Value, chapter, language, cancellationToken));
        });
    }

    internal static async Task<ChapterContextResponse> Context(
        AppDbContext db,
        int book,
        int chapter,
        string? language,
        CancellationToken cancellationToken)
    {
        var verses = await Annotations.InChapter(db, book, chapter, cancellationToken);

        // Disputed rows are left out: they are the references the source itself would not assign
        // to the record, and listing them here would assign them.
        var stated = await db.EntityVerses
            .Where(v => v.CanonicalBook == book && v.CanonicalChapter == chapter && !v.Disputed)
            .Select(v => new { v.Entity!.Slug, v.CanonicalVerse })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var row in stated)
        {
            if (!verses.TryGetValue(row.Slug, out var at))
            {
                verses[row.Slug] = at = [];
            }

            at.Add(row.CanonicalVerse);
        }

        var slugs = verses.Keys.ToList();
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new
            {
                e.Id,
                e.Slug,
                e.Kind,
                e.Name,
                e.Distinguisher,
                e.PlaceKind,
                Location = e.Location == null
                    ? null
                    : new EntityLocationResponse(
                        e.Location.Longitude,
                        e.Location.Latitude,
                        e.Location.Kind,
                        e.Location.Score / EncyclopediaEndpoints.ScoreScale),
            })
            .ToListAsync(cancellationToken);

        var local = await EntityNames.Of(db, [.. records.Select(r => r.Id)], language, cancellationToken);
        var described = await Descriptors.Of(db, slugs, language, cancellationToken);

        var entities = records
            .Select(r => new ContextEntityResponse(
                r.Slug,
                EnumSpelling.Of(r.Kind),
                r.Name,
                r.Distinguisher,
                [.. verses[r.Slug]])
            {
                LocalName = local.GetValueOrDefault(r.Id),
                Descriptor = described.GetValueOrDefault(r.Slug),
                PlaceKind = r.PlaceKind,
                Location = r.Location,
            })
            .OrderByDescending(e => e.Verses.Count)
            .ThenBy(e => e.LocalName ?? e.Name, StringComparer.CurrentCulture)
            .ThenBy(e => e.Slug, StringComparer.Ordinal)
            .ToList();

        var events = await db.Events
            .Where(e => e.CanonicalBook == book && e.CanonicalChapter == chapter)
            .OrderBy(e => e.CanonicalVerse)
            .ThenBy(e => e.YearFromCreation)
            .ThenBy(e => e.SequenceInYear == null)
            .ThenBy(e => e.SequenceInYear)
            .ThenBy(e => e.Slug)
            .Select(EncyclopediaEndpoints.Rows)
            .ToListAsync(cancellationToken);

        var (periods, dated) = await Periods(db, book, chapter, events, cancellationToken);

        return new ChapterContextResponse(
            new BookRefResponse(book, BookReferences.Name(book), BookReferences.Slug(book)),
            chapter,
            entities,
            [.. events.Select(row => EncyclopediaEndpoints.Event(row, NoPlaces))],
            periods,
            dated);
    }

    private static readonly IReadOnlyDictionary<string, string> NoPlaces = new Dictionary<string, string>();

    /// <summary>
    /// The periods a chapter falls in, and what placed it there.
    ///
    /// <para>
    /// A chapter carries no year of its own; the events set in it do. So a period holds the chapter
    /// where it opens or closes on one of them, or where one of their years falls inside it. The
    /// test is half-open, because the event that ends an era usually begins the next: Jeroboam's
    /// reign begins in the year Solomon's ends, and 1 Kings 12 is the first chapter of the
    /// divided kingdom, not the last of the united one.
    /// </para>
    ///
    /// <para>
    /// A chapter no event is set in is placed by the dated events on either side of it in the same
    /// book, and only in a period that holds both: Exodus 25 lies between the covenant at Sinai and the
    /// building of the tabernacle, so it is in the wilderness whichever year between them it describes.
    /// Where only one side has an event the chapter is not placed at all, because one neighbour
    /// says where the book was, not where this chapter is.
    /// </para>
    /// </summary>
    private static async Task<(IList<PeriodRefResponse> Periods, string? Dated)> Periods(
        AppDbContext db,
        int book,
        int chapter,
        IReadOnlyList<EncyclopediaEndpoints.EventRow> events,
        CancellationToken cancellationToken)
    {
        var candidates = db.Periods.Where(p =>
            p.Realm == Database.Entities.Realms.Scripture && p.Kind != LifeKind
            && p.StartYear != null && p.EndYear != null);

        var years = events
            .Select(row => row.Event.YearFromCreation)
            .OfType<int>()
            .Distinct()
            .ToList();

        if (years.Count > 0)
        {
            var eventIds = events.Select(row => row.Event.Id).ToList();
            var holding = await candidates
                .Where(p => (p.StartEventId != null && eventIds.Contains(p.StartEventId.Value))
                            || (p.EndEventId != null && eventIds.Contains(p.EndEventId.Value))
                            || years.Any(y => p.StartYear <= y && (y < p.EndYear || y == p.StartYear)))
                .OrderBy(p => p.Level).ThenBy(p => p.StartYear).ThenBy(p => p.Slug)
                .Select(p => new PeriodRefResponse(p.Slug, p.Name, p.Kind, p.StartYear, p.EndYear))
                .ToListAsync(cancellationToken);
            return (holding, holding.Count > 0 ? ChapterPeriods.FromItsEvents : null);
        }

        var dated = db.Events.Where(e =>
            e.CanonicalBook == book && e.CanonicalChapter != null && e.YearFromCreation != null);
        var before = await dated
            .Where(e => e.CanonicalChapter < chapter)
            .OrderByDescending(e => e.CanonicalChapter).ThenByDescending(e => e.CanonicalVerse)
            .ThenByDescending(e => e.YearFromCreation)
            .Select(e => e.YearFromCreation)
            .FirstOrDefaultAsync(cancellationToken);
        var after = await dated
            .Where(e => e.CanonicalChapter > chapter)
            .OrderBy(e => e.CanonicalChapter).ThenBy(e => e.CanonicalVerse).ThenBy(e => e.YearFromCreation)
            .Select(e => e.YearFromCreation)
            .FirstOrDefaultAsync(cancellationToken);
        if (before is not { } one || after is not { } other)
        {
            return ([], null);
        }

        var (from, to) = (Math.Min(one, other), Math.Max(one, other));
        var around = await candidates
            .Where(p => p.StartYear <= from && to <= p.EndYear)
            .OrderBy(p => p.Level).ThenBy(p => p.StartYear).ThenBy(p => p.Slug)
            .Select(p => new PeriodRefResponse(p.Slug, p.Name, p.Kind, p.StartYear, p.EndYear))
            .ToListAsync(cancellationToken);
        return (around, around.Count > 0 ? ChapterPeriods.FromTheEventsAround : null);
    }
}

/// <summary>How a chapter was placed in its periods, as <see cref="ChapterContextResponse.PeriodsFrom"/> spells it.</summary>
internal static class ChapterPeriods
{
    public const string FromItsEvents = "events";

    public const string FromTheEventsAround = "neighbours";
}

/// <param name="Entities">
/// Every record the chapter names, the most often named first: by how many of its verses name the
/// record, then by the name shown.
/// </param>
/// <param name="Events">The events the sources set in this chapter, in the order of its verses.</param>
/// <param name="Periods">The periods the chapter falls in, the widest first.</param>
/// <param name="PeriodsFrom">
/// <c>events</c> where the chapter's own events placed it, <c>neighbours</c> where it has none and
/// the dated events on either side of it in the book did, and null where nothing placed it.
/// </param>
internal record ChapterContextResponse(
    BookRefResponse Book,
    int Chapter,
    IList<ContextEntityResponse> Entities,
    IList<EventResponse> Events,
    IList<PeriodRefResponse> Periods,
    string? PeriodsFrom);

/// <param name="Kind">person, place, people, title or term.</param>
/// <param name="Verses">The verses of this chapter that name it, in the shared numbering.</param>
internal record ContextEntityResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    IList<int> Verses)
{
    /// <summary>The name in the language asked for, where the corpus has one.</summary>
    public string? LocalName { get; init; }

    public EntityDescriptorResponse? Descriptor { get; init; }

    /// <summary>A place's kind as its source classifies it; null for everything else.</summary>
    public string? PlaceKind { get; init; }

    /// <summary>Where a place is, where the gazetteer identifies it; null for a place it cannot.</summary>
    public EntityLocationResponse? Location { get; init; }
}
