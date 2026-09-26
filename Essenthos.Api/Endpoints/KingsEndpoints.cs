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
/// No years are sent. A reign is a period <c>/v1/timeline</c> already dates in every reckoning, and
/// the rulers name theirs, so a reader switching reckoning moves the kings with the rest of the
/// timeline from what is already in memory. What this adds is only what the periods cannot say:
/// whose each reign is, which kingdom it is drawn under, and who the text says was there.
/// </para>
/// </summary>
internal static class KingsEndpoints
{
    private const string Wikidata = "http://www.wikidata.org/entity/";

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
            CancellationToken cancellationToken) =>
            Results.Ok(await Kings(db, language, cancellationToken)));
    }

    internal static async Task<KingsTimelineResponse> Kings(
        AppDbContext db,
        string? language,
        CancellationToken cancellationToken)
    {
        var reigns = await db.RulerReigns
            .OrderBy(r => r.Position).ThenBy(r => r.Id)
            .Select(r => new
            {
                r.EntityId,
                r.Entity!.Slug,
                r.Entity.Name,
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
                s.RulerEntityId,
                Ruler = s.Ruler!.Slug,
                RulerName = s.Ruler.Name,
                s.Role,
                s.Kind,
                s.Year,
                s.CountedFrom,
                s.ThroughEntityId,
                Through = s.Through == null ? null : s.Through.Slug,
                ThroughName = s.Through == null ? null : s.Through.Name,
                s.CanonicalBook,
                s.CanonicalChapter,
                s.CanonicalVerse,
                s.EndVerse,
            })
            .ToListAsync(cancellationToken);

        var rulers = reigns.GroupBy(r => r.EntityId).Select(g => g.First()).ToList();
        var ruling = rulers.Select(r => r.EntityId).ToHashSet();
        var people = statements
            .Select(s => (Id: s.EntityId, Slug: s.Person, Name: s.PersonName))
            .Concat(statements.Select(s => (Id: s.RulerEntityId, Slug: s.Ruler, Name: s.RulerName)))
            .Concat(statements
                .Where(s => s.ThroughEntityId is not null)
                .Select(s => (Id: s.ThroughEntityId!.Value, Slug: s.Through!, Name: s.ThroughName!)))
            .Where(p => !ruling.Contains(p.Id))
            .DistinctBy(p => p.Id)
            .ToList();

        var local = await EntityNames.Of(
            db, [.. ruling, .. people.Select(p => p.Id)], language, cancellationToken);

        return new KingsTimelineResponse(
            [
                .. rulers.Select(r => new RulerResponse(
                    r.Slug,
                    r.Name,
                    local.GetValueOrDefault(r.EntityId),
                    r.Realm,
                    [
                        .. reigns
                            .Where(reign => reign.EntityId == r.EntityId && reign.Period is not null)
                            .Select(reign => new RulerPeriodResponse(
                                reign.Period!, reign.DrawnUnder, reign.Shared, reign.Fallback)),
                    ])),
            ],
            [.. people.Select(p => new ReignPersonResponse(p.Slug, p.Name, local.GetValueOrDefault(p.Id)))],
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
            await Lands(db, cancellationToken));
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

/// <param name="Rulers">In the order of each kingdom's succession.</param>
/// <param name="People">
/// Everyone else a statement names: the prophets, and Zerubbabel and Shealtiel, in whose days they
/// spoke though neither was a king.
/// </param>
/// <param name="Lands">The world-history periods and events drawn behind each nation's rulers.</param>
internal record KingsTimelineResponse(
    IList<RulerResponse> Rulers,
    IList<ReignPersonResponse> People,
    IList<ReignStatementResponse> Statements,
    IList<ReignLandResponse> Lands);

/// <param name="LocalName">The name in the language asked for, where the corpus has one.</param>
/// <param name="Realm">The kingdom he ruled: <c>united</c>, <c>israel</c>, <c>judah</c>, or a nation.</param>
/// <param name="Reigns">The periods he is drawn by; empty for a ruler no reckoning dates.</param>
internal record RulerResponse(
    string Slug,
    string Name,
    string? LocalName,
    string Realm,
    IList<RulerPeriodResponse> Reigns);

/// <param name="Period">The period's slug, as <c>/v1/timeline</c> sends it.</param>
/// <param name="Over">The kingdom it is drawn under, where it is not the ruler's own.</param>
/// <param name="Shared">A co-regency, a rival reign or a disputed one.</param>
/// <param name="Fallback">Drawn only where a reckoning dates none of the ruler's other periods.</param>
internal record RulerPeriodResponse(string Period, string? Over, bool Shared, bool Fallback);

internal record ReignPersonResponse(string Slug, string Name, string? LocalName);

/// <param name="Person">Who was in the ruler's days: a prophet, a foreign ruler, or a king who began to reign.</param>
/// <param name="Role"><c>prophet</c>, <c>nation</c> or <c>accession</c>.</param>
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
