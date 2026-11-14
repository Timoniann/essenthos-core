using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The kings of Israel and Judah side by side, the rulers of the nations the text brings into their
/// reigns, and the prophets it places in their days, each placement with its verse.
///
/// <para>
/// Each reign comes with its period's years in every reckoning, as <c>/v1/timeline</c> dates them,
/// and the reckonings with them, so the kings beside a chapter are drawn from this answer alone and
/// a reader switching reckoning moves them from what is already in memory. The world history behind
/// the nations is named by slug only: the kings' page, which draws it, has the whole timeline.
/// </para>
///
/// <para>
/// Beside each king goes what the text says of his doing, as the verses it is quoted from and never
/// as their words, which a reader is shown in his own language; the age a verse gives him; and the
/// carryings away and the return, each by the ruler's year its verse gives.
/// </para>
/// </summary>
internal static class KingsEndpoints
{
    private const string Wikidata = "http://www.wikidata.org/entity/";

    /// <summary>The language the headwords, and so the throne names beside them, are written in.</summary>
    private const string Headword = "eng";

    /// <summary>
    /// The world-history records, from Wikidata, drawn behind each nation's rulers: its dynasties,
    /// its empire and the battles the text's own events stand beside. Chosen by hand for the four
    /// centuries of the kings, because the world layer holds cities founded in Sicily and dynasties of
    /// Zhou in the same years.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> WorldHistory = new Dictionary<string, string[]>
    {
        [RulerRealms.Egypt] =
        [
            "Q212728", "Q737629", "Q748170", "Q754749", "Q737648", "Q621917", "Q662410", "Q913818", "Q544239",
            "Q926624",
        ],
        [RulerRealms.Assyria] = ["Q10914393", "Q849474", "Q253208", "Q1267122", "Q612286"],
        [RulerRealms.Babylon] = ["Q624887", "Q633560", "Q1252915", "Q175447", "Q28169617"],
        [RulerRealms.Persia] = ["Q20437507", "Q389688"],
    };

    public static void MapKings(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/timeline/kings", async (
            [FromQuery] string? language,
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await Kings(db, language, settings.Is(SiteSettings.GeneratedImages), cancellationToken)))
            .KeptUntilRelease();
    }

    internal static async Task<KingsTimelineResponse> Kings(
        AppDbContext db,
        string? language,
        bool generatedImages,
        CancellationToken cancellationToken)
    {
        var reigns = await db.RulerReigns
            .OrderBy(r => r.Position).ThenBy(r => r.Id)
            .Select(r => new
            {
                r.EntityId,
                r.Entity!.Slug,
                r.Entity.Name,
                r.Entity.Sex,
                r.Realm,
                Period = r.Period == null ? null : r.Period.Slug,
                r.DrawnUnder,
                r.Shared,
                r.Fallback,
            })
            .ToListAsync(cancellationToken);

        var statements = await db.ReignStatements
            .OrderBy(s => s.CanonicalBook).ThenBy(s => s.CanonicalChapter).ThenBy(s => s.CanonicalVerse)
            .ThenBy(s => s.Id)
            .Select(s => new
            {
                s.EntityId,
                Person = s.Entity!.Slug,
                PersonName = s.Entity.Name,
                PersonSex = s.Entity.Sex,
                s.RulerEntityId,
                Ruler = s.Ruler!.Slug,
                RulerName = s.Ruler.Name,
                RulerSex = s.Ruler.Sex,
                s.Role,
                s.Kind,
                s.Year,
                s.CountedFrom,
                s.ThroughEntityId,
                Through = s.Through == null ? null : s.Through.Slug,
                ThroughName = s.Through == null ? null : s.Through.Name,
                ThroughSex = s.Through == null ? null : s.Through.Sex,
                s.CanonicalBook,
                s.CanonicalChapter,
                s.CanonicalVerse,
                s.EndVerse,
            })
            .ToListAsync(cancellationToken);

        var lengths = await db.ReignLengths
            .ToDictionaryAsync(l => l.EntityId, cancellationToken);

        var local = EntityNames.Local(language);
        var thrones = (await db.ThroneNames
                .Where(n => n.Language == Headword || n.Language == local)
                .ToListAsync(cancellationToken))
            .GroupBy(n => n.EntityId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var fields = await db.ProphetFields
            .OrderBy(f => f.EntityId).ThenBy(f => f.Position)
            .Select(f => new
            {
                f.EntityId,
                f.Realm,
                f.Kind,
                f.PlaceEntityId,
                Place = f.Place == null ? null : f.Place.Slug,
                PlaceName = f.Place == null ? null : f.Place.Name,
                f.CanonicalBook,
                f.CanonicalChapter,
                f.CanonicalVerse,
                f.EndVerse,
            })
            .ToListAsync(cancellationToken);

        var verdicts = (await db.RulerVerdicts
                .Include(v => v.Witnesses).ThenInclude(w => w.Passages)
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .ToDictionary(v => v.EntityId);

        var ages = (await db.StatedAges
                .OrderBy(a => a.Position)
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.EntityId);

        var events = await db.ReignEvents
            .OrderBy(e => e.Position).ThenBy(e => e.Id)
            .Select(e => new
            {
                e.Slug,
                e.Kind,
                e.Realm,
                Timeline = e.TimelineEvent == null ? null : e.TimelineEvent.Slug,
                Ruler = e.Ruler!.Slug,
                e.Year,
                e.CanonicalBook,
                e.CanonicalChapter,
                e.CanonicalVerse,
                e.EndVerse,
            })
            .ToListAsync(cancellationToken);

        var rulers = reigns.GroupBy(r => r.EntityId).Select(g => g.First()).ToList();
        var ruling = rulers.Select(r => r.EntityId).ToHashSet();
        var people = statements
            .Select(s => (Id: s.EntityId, Slug: s.Person, Name: s.PersonName, Sex: s.PersonSex))
            .Concat(statements.Select(s => (Id: s.RulerEntityId, Slug: s.Ruler, Name: s.RulerName, Sex: s.RulerSex)))
            .Concat(statements
                .Where(s => s.ThroughEntityId is not null)
                .Select(s => (Id: s.ThroughEntityId!.Value, Slug: s.Through!, Name: s.ThroughName!, Sex: s.ThroughSex)))
            .Where(p => !ruling.Contains(p.Id))
            .DistinctBy(p => p.Id)
            .ToList();

        var places = fields.Where(f => f.PlaceEntityId is not null).Select(f => f.PlaceEntityId!.Value).Distinct();
        var names = await EntityNames.Of(
            db, [.. ruling, .. people.Select(p => p.Id), .. places], language, cancellationToken);
        var pictures = await ImageEndpoints.Leading(
            db, [.. rulers.Select(r => r.Slug), .. people.Select(p => p.Slug)], cancellationToken, generatedImages);
        var (periodYears, eventYears) = await Years(
            db,
            [.. reigns.Where(r => r.Period is not null).Select(r => r.Period!).Distinct()],
            [.. events.Where(e => e.Timeline is not null).Select(e => e.Timeline!).Distinct()],
            cancellationToken);

        return new KingsTimelineResponse(
            EncyclopediaEndpoints.LastYearBeforeChrist,
            await EncyclopediaEndpoints.Chronologies(db, cancellationToken),
            [
                .. rulers.Select(r => new RulerResponse(
                    r.Slug,
                    r.Name,
                    names.GetValueOrDefault(r.EntityId),
                    r.Realm,
                    [
                        .. reigns
                            .Where(reign => reign.EntityId == r.EntityId && reign.Period is not null)
                            .Select(reign => new RulerPeriodResponse(
                                reign.Period!,
                                reign.DrawnUnder,
                                reign.Shared,
                                reign.Fallback,
                                periodYears.GetValueOrDefault(reign.Period!) ?? new Dictionary<string, int[]>())),
                    ],
                    lengths.TryGetValue(r.EntityId, out var length)
                        ? new ReignLengthResponse(
                            length.Years,
                            length.Months,
                            length.Days,
                            BookReferences.At(length.CanonicalBook, length.CanonicalChapter, length.CanonicalVerse)!)
                        : null,
                    thrones.TryGetValue(r.EntityId, out var throne) ? Throne(throne, local) : null,
                    verdicts.TryGetValue(r.EntityId, out var verdict) ? Verdict(verdict) : null,
                    [.. ages[r.EntityId].Select(Age)],
                    r.Sex,
                    pictures.GetValueOrDefault(r.Slug))),
            ],
            [
                .. people.Select(p => new ReignPersonResponse(
                    p.Slug,
                    p.Name,
                    names.GetValueOrDefault(p.Id),
                    [
                        .. fields.Where(f => f.EntityId == p.Id).Select(f => new ProphetFieldResponse(
                            f.Realm,
                            f.Kind,
                            f.Place is null
                                ? null
                                : new ReignPlaceResponse(
                                    f.Place, f.PlaceName!, names.GetValueOrDefault(f.PlaceEntityId!.Value)),
                            BookReferences.At(f.CanonicalBook, f.CanonicalChapter, f.CanonicalVerse)!,
                            f.EndVerse)),
                    ],
                    [.. ages[p.Id].Select(Age)],
                    p.Sex,
                    pictures.GetValueOrDefault(p.Slug))),
            ],
            [
                .. statements.Select(s => new ReignStatementResponse(
                    s.Person,
                    s.Ruler,
                    s.Role,
                    s.Kind,
                    s.Year,
                    s.CountedFrom,
                    s.Through,
                    BookReferences.At(s.CanonicalBook, s.CanonicalChapter, s.CanonicalVerse)!,
                    s.EndVerse)),
            ],
            await Lands(db, cancellationToken),
            [
                .. events.GroupBy(e => e.Slug).Select(g => new ReignEventResponse(
                    g.Key,
                    g.First().Kind,
                    g.First().Realm,
                    g.First().Timeline,
                    g.First().Timeline is { } timeline ? eventYears.GetValueOrDefault(timeline) : null,
                    [
                        .. g.Select(e => new ReignEventDatingResponse(
                            e.Ruler,
                            e.Year,
                            BookReferences.At(e.CanonicalBook, e.CanonicalChapter, e.CanonicalVerse)!,
                            e.EndVerse)),
                    ])),
            ]);
    }

    /// <summary>
    /// The years of the reign periods and of the timeline's events this answer names, in every
    /// reckoning, read as <c>/v1/timeline</c> reads them.
    /// </summary>
    private static async Task<(Dictionary<string, Dictionary<string, int[]>> Periods, Dictionary<string, Dictionary<string, int>> Events)> Years(
        AppDbContext db,
        IReadOnlyCollection<string> periodSlugs,
        IReadOnlyCollection<string> eventSlugs,
        CancellationToken cancellationToken)
    {
        var periods = await db.Periods
            .Where(p => periodSlugs.Contains(p.Slug))
            .Select(p => new { p.Slug, p.StartEventId, p.EndEventId, p.StartYear, p.EndYear })
            .ToListAsync(cancellationToken);
        var events = await db.Events
            .Where(e => eventSlugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug })
            .ToListAsync(cancellationToken);

        var anchors = periods
            .SelectMany(p => new[] { p.StartEventId, p.EndEventId })
            .OfType<int>()
            .Concat(events.Select(e => e.Id))
            .Distinct()
            .ToList();
        var years = await EncyclopediaEndpoints.EventYears(
            db, db.EventDates.Where(d => anchors.Contains(d.EventId)), cancellationToken);

        return (
            periods.ToDictionary(
                p => p.Slug,
                p => EncyclopediaEndpoints.Span(years, p.StartEventId, p.EndEventId, p.StartYear, p.EndYear)),
            events
                .Where(e => years.ContainsKey(e.Id))
                .ToDictionary(e => e.Slug, e => years[e.Id]));
    }

    private static RulerVerdictResponse Verdict(RulerVerdict verdict)
    {
        var witnesses = verdict.Witnesses.OrderBy(w => w.Position).ToList();
        return new RulerVerdictResponse(
            verdict.Mark,
            witnesses.All(w => w.Basis == VerdictBases.Text) ? VerdictBases.Text : VerdictBases.Reading,
            [
                .. witnesses.Select(w => new VerdictSourceResponse(
                    w.Witness,
                    w.Mark,
                    w.Basis,
                    [
                        .. w.Passages.OrderBy(p => p.Position).Select(p => new ReignPassageResponse(
                            BookReferences.At(p.CanonicalBook, p.CanonicalChapter, p.CanonicalVerse)!,
                            p.EndVerse)),
                    ])),
            ]);
    }

    private static StatedAgeResponse Age(StatedAge age) =>
        new(
            age.Kind,
            age.Years,
            age.About,
            BookReferences.At(age.CanonicalBook, age.CanonicalChapter, age.CanonicalVerse)!);

    private static ThroneNameResponse? Throne(IReadOnlyList<ThroneName> rows, string? local)
    {
        var english = rows.FirstOrDefault(n => n.Language == Headword);
        if (english is null)
        {
            return null;
        }

        return new ThroneNameResponse(
            english.Name,
            rows.FirstOrDefault(n => n.Language == local)?.Name,
            BookReferences.At(english.CanonicalBook, english.CanonicalChapter, english.CanonicalVerse)!);
    }

    private static async Task<IList<ReignLandResponse>> Lands(AppDbContext db, CancellationToken cancellationToken)
    {
        var uris = WorldHistory.Values.SelectMany(ids => ids).Select(id => Wikidata + id).ToList();
        var periods = await db.Periods
            .Where(p => p.Uri != null && uris.Contains(p.Uri))
            .Select(p => new { p.Slug, p.Uri })
            .ToListAsync(cancellationToken);
        var events = await db.Events
            .Where(e => e.Uri != null && uris.Contains(e.Uri))
            .Select(e => new { e.Slug, e.Uri })
            .ToListAsync(cancellationToken);

        return
        [
            .. WorldHistory.Select(land =>
            {
                var here = land.Value.Select(id => Wikidata + id).ToHashSet(StringComparer.Ordinal);
                var banded = periods.Where(p => here.Contains(p.Uri!)).ToList();

                // An empire's founding is an event and its span a period; the span says both.
                return new ReignLandResponse(
                    land.Key,
                    [.. banded.Select(p => p.Slug)],
                    [
                        .. events
                            .Where(e => here.Contains(e.Uri!) && banded.All(p => p.Uri != e.Uri))
                            .Select(e => e.Slug),
                    ]);
            }),
        ];
    }
}

/// <param name="LastAnnoMundiBeforeTheCommonEra">
/// The turn of the axis the periods' own years are counted on, as <c>/v1/timeline</c> states it.
/// </param>
/// <param name="Chronologies">The reckonings the reigns' years are given in.</param>
/// <param name="Rulers">In the order of each kingdom's succession.</param>
/// <param name="People">
/// Everyone else a statement names: the prophets, and Zerubbabel and Shealtiel, in whose days they
/// spoke though neither was a king.
/// </param>
/// <param name="Lands">The world-history periods and events drawn behind each nation's rulers.</param>
/// <param name="Events">The carryings away of Israel and of Judah and the return, in the order they happened.</param>
internal record KingsTimelineResponse(
    int LastAnnoMundiBeforeTheCommonEra,
    IList<ChronologyResponse> Chronologies,
    IList<RulerResponse> Rulers,
    IList<ReignPersonResponse> People,
    IList<ReignStatementResponse> Statements,
    IList<ReignLandResponse> Lands,
    IList<ReignEventResponse> Events);

/// <param name="LocalName">The name in the language asked for, where the corpus has one.</param>
/// <param name="Realm">The kingdom he ruled: <c>united</c>, <c>israel</c>, <c>judah</c>, or a nation.</param>
/// <param name="Reigns">The periods he is drawn by; empty for a ruler no reckoning dates.</param>
/// <param name="Reigned">How long the text says he reigned, where it says.</param>
/// <param name="Throne">
/// The name he reigned under, where his record is headed by another: Jehoiakim for Eliakim.
/// </param>
/// <param name="Verdict">
/// What the text says of his doing; null for a ruler of the nations, and for a king of whom the text
/// tells nothing to judge him by.
/// </param>
/// <param name="Ages">The ages the text gives him; two where two verses give two.</param>
/// <param name="Sex">As his record gives it, so that his doing is said in the words for a man or a woman.</param>
/// <param name="Picture">The picture his page leads with, where it has one.</param>
internal record RulerResponse(
    string Slug,
    string Name,
    string? LocalName,
    string Realm,
    IList<RulerPeriodResponse> Reigns,
    ReignLengthResponse? Reigned,
    ThroneNameResponse? Throne,
    RulerVerdictResponse? Verdict,
    IList<StatedAgeResponse> Ages,
    string? Sex,
    EntityThumbnailResponse? Picture);

/// <param name="Mark"><c>right</c>, <c>evil</c> or <c>mixed</c>, over everything the histories say of him.</param>
/// <param name="Basis">
/// <c>text</c> where every source says of him that he did right or did evil; <c>reading</c> where any
/// of them is read from what it tells.
/// </param>
/// <param name="Sources">Samuel, Kings and Chronicles apart, each as it judges him.</param>
internal record RulerVerdictResponse(string Mark, string Basis, IList<VerdictSourceResponse> Sources);

/// <param name="Source"><c>samuel</c>, <c>kings</c> or <c>chronicles</c>.</param>
/// <param name="Mark">The mark this history alone leaves him with.</param>
/// <param name="Basis"><c>text</c> or <c>reading</c>.</param>
/// <param name="Passages">The verses to quote, in the order they are to be read.</param>
internal record VerdictSourceResponse(string Source, string Mark, string Basis, IList<ReignPassageResponse> Passages);

/// <param name="EndVerse">The last verse, where it takes more than one to say it.</param>
internal record ReignPassageResponse(VerseRefResponse Verse, int? EndVerse);

/// <param name="Kind"><c>accession</c> for his age when he began to reign, <c>death</c> for his age when he died.</param>
/// <param name="About">The verse says <em>about</em>.</param>
internal record StatedAgeResponse(string Kind, int Years, bool About, VerseRefResponse Verse);

/// <param name="Kind"><c>exile</c> or <c>return</c>.</param>
/// <param name="Realm">The kingdom it befell: <c>israel</c> or <c>judah</c>.</param>
/// <param name="Event">The same event's slug in <c>/v1/timeline</c>, where the chronologies date one.</param>
/// <param name="Years">That event's year in each reckoning that dates it.</param>
/// <param name="Datings">Every verse that sets it in a ruler's days.</param>
internal record ReignEventResponse(
    string Slug,
    string Kind,
    string Realm,
    string? Event,
    IDictionary<string, int>? Years,
    IList<ReignEventDatingResponse> Datings);

/// <param name="Ruler">The ruler whose year dates it.</param>
/// <param name="Year">His year the verse gives; null where it says only that it was in his days.</param>
/// <param name="EndVerse">The last verse, where it takes more than one to say it.</param>
internal record ReignEventDatingResponse(string Ruler, int? Year, VerseRefResponse Verse, int? EndVerse);

/// <param name="LocalName">The name in the language asked for, where one is held.</param>
/// <param name="Verse">Where the text gives him the name.</param>
internal record ThroneNameResponse(string Name, string? LocalName, VerseRefResponse Verse);

/// <summary>The text's length of a reign — years, months or days, as the verse counts it.</summary>
internal record ReignLengthResponse(int? Years, int? Months, int? Days, VerseRefResponse Verse);

/// <param name="Period">The period's slug, as <c>/v1/timeline</c> sends it.</param>
/// <param name="Over">The kingdom it is drawn under, where it is not the ruler's own.</param>
/// <param name="Shared">A co-regency, a rival reign or a disputed one.</param>
/// <param name="Fallback">Drawn only where a reckoning dates none of the ruler's other periods.</param>
/// <param name="Years">
/// The period's first and last year in each reckoning that can state both, as <c>/v1/timeline</c>
/// gives them; under the empty key where the period carries its own years.
/// </param>
internal record RulerPeriodResponse(
    string Period,
    string? Over,
    bool Shared,
    bool Fallback,
    IDictionary<string, int[]> Years);

/// <param name="Fields">
/// For a prophet, where the text says he prophesied, came from and was sent, in the order a reader
/// meets them; empty for anyone else.
/// </param>
/// <param name="Ages">The ages the text gives him, which for a prophet it almost never does.</param>
/// <param name="Sex">As the record gives it.</param>
/// <param name="Picture">The picture the record's page leads with, where it has one.</param>
internal record ReignPersonResponse(
    string Slug,
    string Name,
    string? LocalName,
    IList<ProphetFieldResponse> Fields,
    IList<StatedAgeResponse> Ages,
    string? Sex,
    EntityThumbnailResponse? Picture);

/// <param name="Realm">
/// <c>united</c>, <c>israel</c>, <c>judah</c>, <c>exile</c>, <c>return</c>, or the nation he was sent to.
/// </param>
/// <param name="Kind"><c>prophesied</c>, <c>from</c> or <c>sent</c>.</param>
/// <param name="Place">The town or land the verse names, where it names one.</param>
/// <param name="EndVerse">The last verse, where it takes more than one to say it.</param>
internal record ProphetFieldResponse(
    string Realm,
    string Kind,
    ReignPlaceResponse? Place,
    VerseRefResponse Verse,
    int? EndVerse);

internal record ReignPlaceResponse(string Slug, string Name, string? LocalName);

/// <param name="Person">Who was in the ruler's days: a prophet, a foreign ruler, or a king who began to reign.</param>
/// <param name="Role"><c>prophet</c>, <c>nation</c>, <c>accession</c> or <c>subject</c>.</param>
/// <param name="Kind">
/// How the verse says it: <c>superscription</c>, <c>dated</c>, <c>narrative</c>, <c>record</c>, or
/// <c>concerning</c> for a word about the ruler that does not say it was spoken while he reigned.
/// </param>
/// <param name="Year">The ruler's year the verse gives, where it gives one.</param>
/// <param name="Count">
/// What <paramref name="Year"/> counts from where it is not the accession: <c>captivity</c>, or
/// <c>death</c> for the year he died.
/// </param>
/// <param name="Through">The son the word came to, for a father whose page lists it.</param>
/// <param name="EndVerse">The last verse, where it takes more than one to say it.</param>
internal record ReignStatementResponse(
    string Person,
    string Ruler,
    string Role,
    string Kind,
    int? Year,
    string? Count,
    string? Through,
    VerseRefResponse Verse,
    int? EndVerse);

/// <param name="Periods">Slugs of world periods in <c>/v1/timeline</c>.</param>
/// <param name="Events">Slugs of world events in <c>/v1/timeline</c>.</param>
internal record ReignLandResponse(string Realm, IList<string> Periods, IList<string> Events);
