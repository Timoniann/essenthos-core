using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <param name="Calculation">
/// The arithmetic that produced the year, in a sentence, so it can be checked rather than
/// believed. This is why this dataset was chosen over the others.
/// </param>
/// <param name="Dates">
/// Every reckoning's answer, beside each other rather than one instead of the rest. A reader is
/// owed the disagreement, not a winner — and they disagree in 406 of the 419 events they share.
/// </param>
/// <param name="Source">
/// Who compiled this row and under what licence. Per row rather than per corpus, because they
/// differ: the scripture chronology is CC BY 4.0 and the world layer CC0. A page showing one
/// licence for both would assert what neither of them says.
/// </param>
/// <param name="BceYear">
/// <see cref="YearFromCreation"/> as a reader writes it, in the default reckoning, with
/// <see cref="EventResponse.Era"/> saying on which side of the turn — so past it this is a common
/// era year, exactly as it is in <c>Dates</c>. It is that reckoning's own entry in <c>Dates</c>
/// and not BibleData's <c>bce_year</c> column, which disagrees with the arithmetic on one event
/// and is empty on 266 the arithmetic dates without trouble.
/// </param>
/// <param name="Location">
/// Where it happened, as the source wrote it. <see cref="EventResponse.LocationSlug"/> is the
/// place of that name where the corpus holds exactly one.
/// </param>
/// <param name="NameSource">
/// Whose words <paramref name="Name"/> is — <c>source</c> where the dataset titled its own row,
/// <c>quoted</c> where it wrote no title and the opening of what it did write stands in, and
/// <c>generated</c> where the title was written for this corpus, in which case
/// <paramref name="Notes"/> says by what and when. Ussher's Annals are 7,000 untitled paragraphs,
/// so the distinction is the difference between quoting a chronologer and putting words in his
/// mouth.
/// </param>
/// <param name="Region">
/// Where in the world, for the world layer: today's country, or the last known, as Wikidata states
/// it — not the polity of the time, which is <see cref="EventResponse.RegionAtTheTime"/>. The
/// Battle of Himera is in Italy here, and that is a place to find it by rather than a claim about
/// who held Sicily in 480 BCE.
/// </param>
internal record EventResponse(
    string Slug,
    string Name,
    string NameSource,
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
    string Realm,
    string? Region,
    string? Uri,
    string? Notes,
    string Source,
    string? SourceId,
    IList<EventDateResponse> Dates)
{
    /// <summary>
    /// The place <see cref="Location"/> names: the one place of that name recorded at the verse the
    /// source names it at, or else the only place of that name; null where neither settles it. The
    /// words are the source's and stand whatever this says.
    /// </summary>
    public string? LocationSlug { get; init; }

    /// <summary>
    /// The country of the time, where the source states one whose own years contain the event's —
    /// beside <c>Region</c>, which is today's country or the last known and never the polity of the
    /// time. Null for everything outside the world layer and for most of it.
    /// </summary>
    public string? RegionAtTheTime { get; init; }

    /// <summary>
    /// Whether a source states where this falls inside its year, or whether the year is the whole
    /// of what anybody said.
    ///
    /// False for most of the corpus, and that is the honest answer rather than a missing one: a
    /// year holds ninety-five events and only some of them were ever put in an order.
    /// Where this is true the position in a list of events is a claim; where it is false the
    /// position is only stable, and a reader should be told so rather than left to infer a
    /// chronology from it.
    /// </summary>
    public bool Sequenced { get; init; }

    /// <summary>
    /// <c>BCE</c> or <c>CE</c>, saying which side of the turn <see cref="BceYear"/> falls on: the
    /// year from creation carries no sign and keeps counting past the turn, so a bare <c>8</c>
    /// could mean either side of it. The same two words as <see cref="EventDateResponse.Era"/>,
    /// and the same answer, because both are the default reckoning's.
    /// </summary>
    public string Era { get; init; } = "BCE";
}

/// <param name="Calculation">
/// The arithmetic that produced this reckoning's year, where the reckoning shows its working.
/// </param>
/// <param name="BceYear">
/// The year as a reader writes it, with <paramref name="Era"/> saying which side of the turn: the
/// reckoning's own figure where it states one, and otherwise worked out from
/// <paramref name="Year"/> and the reckoning's zero. <see cref="EventDateResponse.Stated"/> says
/// which. Null where the reckoning gives neither.
/// </param>
/// <param name="Year">
/// Years from the reckoning's own creation. Null where the reckoning states only its year of the
/// common era, which Ussher's Annals do for the paragraphs whose anno mundi column contradicts
/// their other two.
/// </param>
internal record EventDateResponse(
    string Chronology,
    string Name,
    int? Year,
    int? BceYear,
    string Era,
    int? EarliestYear,
    int? LatestYear,
    string? Calculation,
    string? Citation,
    string? Notes)
{
    /// <summary>
    /// Whether <see cref="BceYear"/> is the figure the reckoning's source printed, rather than one
    /// worked out from the year from creation. They differ by a year wherever the reckoning's year
    /// opens in the autumn — Ussher's creation is 4004 BC in his words and 4003 by the subtraction —
    /// and a reader is owed which one is in front of them.
    /// </summary>
    public bool Stated { get; init; }
}

/// <summary>
/// Where the source says an event happened: its words, lower-cased to be matched on, and the verse
/// at which it names them, where it names one.
/// </summary>
internal readonly record struct EventLocation(string Name, int? Book, int? Chapter, int? Verse)
{
    public static EventLocation? Of(string? location, int? book, int? chapter, int? verse) =>
        string.IsNullOrWhiteSpace(location)
            ? null
            : new EventLocation(location.ToLowerInvariant(), book, chapter, verse);

    public static EventLocation? Of(Database.Entities.Event e) =>
        Of(e.Location, e.LocationBook, e.LocationChapter, e.LocationVerse);
}

internal record EventListResponse(int Total, IList<EventResponse> Items);

internal record EventRefResponse(string Slug, string Name);

/// <summary>A person or a place, named just enough to link to.</summary>
internal record NamedEntityResponse(string Slug, string Name, string? Distinguisher);

internal record PeriodRefResponse(string Slug, string Name, string? Kind, int? StartYear, int? EndYear);

/// <param name="Opens">
/// The two events that bound it. A period is anchored to them rather than to years, which is why
/// switching reckoning moves the band as well as the marks inside it.
/// </param>
internal record PeriodResponse(
    string Slug,
    string Name,
    string? Kind,
    int Level,
    string Realm,
    string? Region,
    string? Uri,
    string? Notes,
    string Source,
    string? SourceId,
    PeriodRefResponse? Parent,
    NamedEntityResponse? Entity,
    EventRefResponse? Opens,
    EventRefResponse? Closes,
    IList<PeriodRefResponse> Inside)
{
    /// <summary>
    /// Start and end in every chronology that can state both, exactly as the timeline sends them:
    /// a chronology that cannot is absent, and a period anchored to no events is keyed by the empty
    /// string, because its years are its own and belong to no reckoning.
    /// </summary>
    public IDictionary<string, int[]> Years { get; init; } = new Dictionary<string, int[]>();
}

/// <param name="LastAnnoMundiBeforeTheCommonEra">The base reckoning's, as the timeline sends it.</param>
internal record PeriodListResponse(
    int LastAnnoMundiBeforeTheCommonEra,
    IList<TimelinePeriodResponse> Items);

/// <param name="LastAnnoMundiBeforeTheCommonEra">The base reckoning's, as the timeline sends it.</param>
internal record ChronologyListResponse(
    int LastAnnoMundiBeforeTheCommonEra,
    IList<ChronologyResponse> Chronologies);

/// <param name="LastAnnoMundiBeforeTheCommonEra">
/// The year from creation that is 1 BCE, so a client can turn every year on this axis into an
/// astronomical one by subtracting it — and can do so without a <c>Date</c>, which is the point.
/// The dataset counts forward from the creation without a sign, and 3,961 is where its era turns.
/// </param>
/// <param name="LastAnnoMundiBeforeTheCommonEra">
/// The year from creation that is 1 BCE in the base reckoning, BibleData's, so a client can turn a
/// year on this axis into an astronomical one by subtracting it — and can do so without a
/// <c>Date</c>, which is the point. Each chronology carries its own; this is the base's, which the
/// years of a period anchored to no event are counted on.
/// </param>
/// <summary>
/// The pictures the timeline shows small beside its events and periods, read from the encyclopedia
/// rather than found anew: an event's is the one its person's page leads with, or failing that its
/// place's — the place its own page links to, by the same rule. Nothing is looked up that the
/// encyclopedia does not already show, and most events have neither.
/// </summary>
internal sealed class TimelinePictures
{
    private readonly Dictionary<string, EntityThumbnailResponse> _leading;
    private readonly Dictionary<EventLocation, string> _places;

    private TimelinePictures(
        Dictionary<string, EntityThumbnailResponse> leading,
        Dictionary<EventLocation, string> places)
    {
        _leading = leading;
        _places = places;
    }

    public static async Task<TimelinePictures> Of(
        AppDbContext db,
        IReadOnlyCollection<(string? Entity, EventLocation? Location)> events,
        IEnumerable<string?> periodEntities,
        bool generated,
        CancellationToken cancellationToken)
    {
        var places = await EncyclopediaEndpoints.PlacesNamed(db, events.Select(e => e.Location), cancellationToken);
        var slugs = events.Select(e => e.Entity)
            .Concat(places.Values)
            .Concat(periodEntities)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new TimelinePictures(await ImageEndpoints.Leading(db, slugs, cancellationToken, generated), places);
    }

    public EntityThumbnailResponse? ForEvent(string? entity, EventLocation? location) =>
        Leading(entity) ?? Leading(location is { } at ? _places.GetValueOrDefault(at) : null);

    public EntityThumbnailResponse? ForPeriod(string? entity) => Leading(entity);

    private EntityThumbnailResponse? Leading(string? slug) => slug is null ? null : _leading.GetValueOrDefault(slug);
}

internal record TimelineResponse(
    int LastAnnoMundiBeforeTheCommonEra,
    IList<ChronologyResponse> Chronologies,
    IList<TimelineEventResponse> Items,
    IList<TimelinePeriodResponse> Periods);

/// <summary>
/// One reckoning of when things happened, and what it rests on.
///
/// Sent alongside the dates rather than resolved into them. The base reckoning and Ussher disagree
/// in 406 of the 419 events they share and by as much as 278 years; Ussher and Shulman agree on
/// none of their 267 and stand 477 years apart at the Tower of Babel. That disagreement is a
/// finding rather than a defect — a corpus that picked one and hid the others would be asserting a
/// chronology no chronologer holds.
/// </summary>
internal record ChronologyResponse(
    string Slug,
    string Name,
    string? Authority,
    string? Basis,
    string? Source,
    int LastAnnoMundiBeforeTheCommonEra,
    bool IsDefault);

/// <param name="Sequenced">
/// Whether this event's place among the others of its year is something a source stated, or only
/// where the list happened to put it.
///
/// The items arrive in the order they are to be drawn, and for most of them that order inside a
/// year is arbitrary — the year is all anybody said. Ussher dates ninety-five paragraphs to AD 33
/// and narrates them in order, so those are a sequence and the crucifixion precedes the
/// resurrection because he says so. Without this a client cannot tell one from the other, and would
/// have to read an arbitrary order as a chronology.
/// </param>
/// <param name="Years">
/// The year each chronology gives this event, keyed by chronology slug. A chronology that says
/// nothing about it is absent rather than null — silence and zero are different facts.
/// </param>
internal record TimelineEventResponse(
    string Slug,
    string Name,
    string? Kind,
    string Realm,
    string? Region,
    string? Uri,
    string? EntitySlug,
    bool Sequenced,
    IDictionary<string, int> Years)
{
    /// <summary>
    /// The picture its person's page leads with, or its place's where the person has none; null for
    /// most, which the encyclopedia has no picture for. Sent only by the timeline.
    /// </summary>
    public EntityThumbnailResponse? Picture { get; init; }

    /// <summary>
    /// The country of the time, where the source states one; <c>Region</c> is today's country or the
    /// last known. See <see cref="EventResponse.RegionAtTheTime"/>.
    /// </summary>
    public string? RegionAtTheTime { get; init; }
}

/// <param name="Level">
/// Which row to draw it on — 0 the eras, 1 the spans of rule and captivity, 2 the lives and
/// ministries — and not its depth in the tree. Every period hangs off an era whatever its level,
/// so nothing sits under a level-1 period: of the 178 that have a parent, 87 are level 1 and 91
/// level 2, and all 178 point at a level-0 era.
/// </param>
/// <param name="ParentSlug">
/// The era this period <em>opens in</em>, which is not always an era that contains it. Drawing a
/// child bar inside its parent's bar without reading <see cref="Years"/> puts it outside in 42 of
/// the 178: a lifespan crosses the Flood because the man outlived it, and the 430 years from the
/// promise to the covenant end two eras after they begin. Those 42 say how far they overrun in
/// <see cref="Notes"/>. The years are the containment; this is only where the band starts.
/// </param>
/// <param name="Years">
/// Start and end, per chronology, as a two-element array. Only a chronology that can state both
/// ends appears. The empty key is a period that carries its own years and belongs to no reckoning.
/// </param>
internal record TimelinePeriodResponse(
    string Slug,
    string Name,
    string? Kind,
    int Level,
    string Realm,
    string? Region,
    string? Uri,
    string? ParentSlug,
    string? EntitySlug,
    string? Notes,
    IDictionary<string, int[]> Years)
{
    /// <summary>The picture its person's page leads with; null where there is none, and on the period list.</summary>
    public EntityThumbnailResponse? Picture { get; init; }
}
