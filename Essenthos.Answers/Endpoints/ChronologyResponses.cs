using System.Text.Json.Serialization;
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

    /// <summary>
    /// The title in the reader's language, a machine translation of <see cref="Name"/>; null for an
    /// English reader and where none is held. Never the source's words, and shown as a translation.
    /// </summary>
    public string? LocalName { get; init; }

    /// <summary>The description in the reader's language, a machine translation, as <see cref="LocalName"/>.</summary>
    public string? LocalDescription { get; init; }

    /// <summary>The datasets the event's page credits, as <see cref="DatasetCredits"/> names them; on one event's answer alone.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<DatasetCreditResponse>? Credits { get; init; }

    /// <summary>
    /// Every period whose span in some reckoning reaches <see cref="YearFromCreation"/>, as <c>/v1/periods</c> gives it:
    /// the page keeps the ones its own reckoning places the year in, so it needs no list of every period. On one
    /// event's answer alone.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<TimelinePeriodResponse>? Periods { get; init; }

    /// <summary>The source's notes in the reader's language, a machine translation, as <see cref="LocalName"/>.</summary>
    public string? LocalNotes { get; init; }

    /// <summary>
    /// Where it happened in the reader's language: the name the reader's text gives the place
    /// <see cref="LocationSlug"/> names, or else a machine translation of the source's words.
    /// </summary>
    public string? LocalLocation { get; init; }

    /// <summary>The region and the country of the time in the reader's language, machine translations.</summary>
    public string? LocalRegion { get; init; }

    public string? LocalRegionAtTheTime { get; init; }

    /// <summary>
    /// The reckoning whose year a list ordered by a reader's preference shows this event by: the
    /// first of the reckonings asked for that dates it. Null where no preference was asked for, and
    /// where none of them dates it.
    /// </summary>
    public string? ShownBy { get; init; }

    /// <summary>
    /// The verses the source's description quotes, one apiece in the order it cites them, for a page to
    /// quote from its own texts in the reader's language. The source's quotation itself is never sent:
    /// it is the New American Standard Bible's words (PRB-1678), and <see cref="Description"/> keeps
    /// only what the compiler wrote himself. Empty where the description quotes nothing.
    /// </summary>
    public IList<VerseRefResponse> Quoted { get; init; } = [];

    /// <summary>The passages <see cref="Quoted"/> was read from, kept until a chapter's end is known.</summary>
    [JsonIgnore]
    public IReadOnlyList<QuotedVerses.Passage> Passages { get; init; } = [];

    /// <summary>
    /// What the source's notes say the year rests on, where they flag it: <c>hypothesis</c> where they
    /// open the compiler's inference (<c>*HYPOTHESIS:</c>), <c>outside-scripture</c> where the year comes
    /// from outside the Bible (<c>*EXTRA-BIBLICAL SOURCE(S)/HISTORY:</c>). The source marks such an
    /// event with an asterisk after its title, which is taken off the title and said here instead.
    /// </summary>
    public string? Mark { get; init; }

    /// <summary>
    /// A world event's Wikipedia article in the reader's language, the English one standing in where that
    /// Wikipedia has none, from the event's own Wikidata item. Null for Scripture's events and where no
    /// Wikipedia kept here has an article on the item.
    /// </summary>
    public EntityWikipediaResponse? Wikipedia { get; init; }
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

/// <param name="Total">How many events the narrowing holds.</param>
/// <param name="Items">The page of them asked for.</param>
internal record EventListResponse(int Total, IList<EventListItemResponse> Items)
{
    /// <summary>Where the page starts in the whole list: the skip asked for, or where <c>around</c> put it.</summary>
    public int Skip { get; init; }

    /// <summary>
    /// The centuries the whole narrowed list runs through, in its order, each with how many of its events
    /// fall in it and where the first of them stands, so a list can head its rows by century and go to
    /// one. Empty where the list was not ordered by a reader's reckonings.
    /// </summary>
    public IList<EventCenturyResponse> Centuries { get; init; } = [];
}

/// <param name="Century">The century: negative before the common era (<c>-10</c> is the tenth century BCE), positive after.</param>
/// <param name="Count">How many of the list's events fall in it.</param>
/// <param name="First">Where the first of them stands in the list, counting from zero.</param>
internal record EventCenturyResponse(int Century, int Count, int First);

/// <summary>
/// Where an event stands in a narrowed and ordered list, counting from one, and its neighbours there.
/// The position is null where the narrowing leaves the event out; a neighbour is null at an end.
/// </summary>
internal record EventStepResponse(int? Position, int Total, EventNeighbourResponse? Previous, EventNeighbourResponse? Next)
{
    /// <summary>The events around this one in the same list and order, this one among them; empty where it is not in the list.</summary>
    public IList<EventListItemResponse> Near { get; init; } = [];

    /// <summary>Where <see cref="Near"/> starts in the whole list, counting from zero.</summary>
    public int NearSkip { get; init; }
}

/// <summary>A neighbour in a list: what a link to it names.</summary>
internal record EventNeighbourResponse(string Slug, string Name, string? LocalName);

/// <summary>
/// One event as a line of a list names it: its title, kind, place, first verse, the year each reckoning gives it and
/// the reckoning it is shown by. The description, the notes and each reckoning's arithmetic are the event's own answer's
/// (<c>/v1/events/{slug}</c>); a list is for finding the event.
/// </summary>
internal record EventListItemResponse(
    string Slug,
    string Name,
    string? Kind,
    string Realm,
    int? YearFromCreation,
    VerseRefResponse? Reference,
    string? Location,
    string? Region,
    string Source,
    string? SourceId,
    IList<EventListDateResponse> Dates)
{
    public string? LocalName { get; init; }

    public string? LocalLocation { get; init; }

    public string? LocationSlug { get; init; }

    public string? LocalRegion { get; init; }

    /// <summary>The reckoning whose year the list orders and shows it by; null where the list was not ordered by reckonings.</summary>
    public string? ShownBy { get; init; }

    public IList<VerseRefResponse> Quoted { get; init; } = [];

    public string? Mark { get; init; }

    public EntityWikipediaResponse? Wikipedia { get; init; }

    public static EventListItemResponse Of(EventResponse e, string? shownBy) =>
        new(e.Slug, e.Name, e.Kind, e.Realm, e.YearFromCreation, e.Reference, e.Location, e.Region, e.Source, e.SourceId,
            [.. e.Dates.Select(d => new EventListDateResponse(d.Chronology, d.Name, d.Year, d.BceYear, d.Era))])
        {
            LocalName = e.LocalName,
            LocalLocation = e.LocalLocation,
            LocationSlug = e.LocationSlug,
            LocalRegion = e.LocalRegion,
            ShownBy = shownBy,
            Quoted = e.Quoted,
            Mark = e.Mark,
            Wikipedia = e.Wikipedia,
        };
}

/// <summary>One reckoning's year for an event in a list, as <see cref="EventDateResponse"/> gives it, without the workings.</summary>
internal record EventListDateResponse(string Chronology, string Name, int? Year, int? BceYear, string Era);

internal record EventRefResponse(string Slug, string Name);

/// <summary>A person or a place, named just enough to link to.</summary>
internal record NamedEntityResponse(string Slug, string Name, string? Distinguisher);

internal record PeriodRefResponse(string Slug, string Name, string? Kind, int? StartYear, int? EndYear)
{
    /// <summary>The name in the reader's language, as <see cref="TimelinePeriodResponse.LocalName"/>.</summary>
    public string? LocalName { get; init; }
}

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

    /// <summary>The name in the reader's language, as <see cref="TimelinePeriodResponse.LocalName"/>.</summary>
    public string? LocalName { get; init; }
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

    /// <summary>The title in the reader's language, a machine translation; null where none is held.</summary>
    public string? LocalName { get; init; }

    /// <summary>The region in the reader's language, a machine translation; null where none is held.</summary>
    public string? LocalRegion { get; init; }
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

    /// <summary>
    /// The name in the reader's language: a life by the person's name, a reign, a prophet's or a judge's
    /// time by the person's name in a set phrase, any other period by the rendering held of its English
    /// name. Null where the reader reads English and where no rendering is held.
    /// </summary>
    public string? LocalName { get; init; }
}
