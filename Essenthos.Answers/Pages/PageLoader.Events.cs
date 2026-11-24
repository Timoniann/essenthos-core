using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json.Serialization.Metadata;

namespace Essenthos.Core.Pages;

internal sealed partial class PageLoader
{
    private async Task<PageContent?> Event(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var language = address.Language;
        // The event as the reader's script asks for it in this language: the page carries it and is written from it.
        var detail = await EncyclopediaEndpoints.EventDetail(
            db, settings, address.Slug!, language.Corpus, language.Corpus, cancellationToken, periods);
        if (detail is null)
        {
            return null;
        }

        var reckonings = await Reckonings(cancellationToken);
        var shownFirst = detail.Dates.FirstOrDefault(d => reckonings.Any(r => r.IsDefault && r.Slug == d.Chronology))
                         ?? detail.Dates.FirstOrDefault();
        var english = language.IsEnglish;

        PageLink? place = null;
        if (detail.LocationSlug is { } placeSlug)
        {
            place = await Entity(placeSlug, language, cancellationToken) is { } found
                ? new PageLink(PageAddress.RecordPath(found.Kind, found.Slug), found.Shown)
                : null;
        }

        // The verses the source's description quotes, or the verse the event is read from, in the page
        // language's own text: never the source's quotation, which is the NASB's (PRB-1678).
        IReadOnlyList<VerseRefResponse> written = detail.Quoted.Count > 0
            ? [.. detail.Quoted]
            : detail.Reference is { } reference ? [reference] : [];
        var texts = await TextsAt(
            language, [.. written.Select(v => Address(v.BookOrdinal, v.Chapter, v.Verse))], cancellationToken);
        var verses = new List<(PageLink, string?)>(written.Count);
        foreach (var verse in written)
        {
            verses.Add((
                new PageLink(
                    PageAddress.VersePath(verse.BookOrdinal, verse.Chapter, verse.Verse),
                    wording.Reference(await BookName(language, wording, verse.BookOrdinal, cancellationToken), verse.Chapter, verse.Verse)),
                texts.GetValueOrDefault(Address(verse.BookOrdinal, verse.Chapter, verse.Verse))));
        }

        PageLink? person = null;
        string? image = null;
        if (detail.EntitySlug is { } slug && await Entity(slug, language, cancellationToken) is { } entity)
        {
            person = new PageLink(PageAddress.RecordPath(entity.Kind, entity.Slug), entity.Shown);
            image = await Picture(entity.Slug, language.Corpus, cancellationToken);
        }

        var view = new EventView(detail.Slug, detail.LocalName ?? detail.Name, shownFirst is null ? null : DateYear(wording, shownFirst))
        {
            Kind = english ? detail.Kind : null,
            Description = detail.LocalDescription ?? detail.Description,
            DescriptionInEnglish = !english && detail.LocalDescription is null,
            Mark = detail.Mark switch
            {
                EventMarks.Hypothesis => wording.Text("event.hypothesis"),
                EventMarks.OutsideScripture => wording.Text("event.outsideScripture"),
                _ => null,
            },
            Place = place,
            Region = string.Join(", ", new[]
            {
                place is null ? detail.LocalLocation ?? (english ? detail.Location : null) : null,
                detail.LocalRegion ?? (english ? detail.Region : null),
            }.Where(part => !string.IsNullOrWhiteSpace(part))) is { Length: > 0 } where ? where : null,
            Dates =
            [
                .. detail.Dates
                    .Where(date => date.BceYear is not null)
                    .Select(date => new ReckonedDate(
                        date.Name, DateYear(wording, date), english ? date.Calculation : null)),
            ],
            Verses = verses,
            Person = person,
            Source = detail.Source,
            Image = image,
            Wikipedia = detail.Wikipedia is { } article ? new PageArticle(article.Url, article.Title, article.Language) : null,
        };
        return PageViews.Event(language, wording, view) with
        {
            Data = PageData.Of([(PageData.EventAddress(detail.Slug, language), PageData.Json(detail))]),
        };
    }

    /// <summary>A date's year as the event's own reckoning puts it, in the page language's words.</summary>
    private static string DateYear(PageWording wording, EventDateResponse date) =>
        PageViews.Year(wording, date.BceYear ?? 0, date.Era == "CE");

    private async Task<PageContent?> Period(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var language = address.Language;
        var detail = await EncyclopediaEndpoints.PeriodDetail(db, address.Slug!, cancellationToken, language.Corpus, language.Corpus, settings);
        if (detail is null)
        {
            return null;
        }

        var english = language.IsEnglish;
        var reckonings = await Reckonings(cancellationToken);
        var own = detail.Years.TryGetValue("", out var carried) ? carried : null;
        var spans = own is not null
            ? [new ReckonedSpan(null, PageViews.AxisSpan(wording, own[0], own[1], EncyclopediaEndpoints.LastYearBeforeChrist))]
            : reckonings
                .Where(r => detail.Years.ContainsKey(r.Slug))
                .Select(r => new ReckonedSpan(
                    r.Name, PageViews.AxisSpan(wording, detail.Years[r.Slug][0], detail.Years[r.Slug][1], r.LastAnnoMundiBeforeTheCommonEra)))
                .ToList();
        var shown = own is not null
            ? spans[0].Years
            : reckonings.FirstOrDefault(r => r.IsDefault && detail.Years.ContainsKey(r.Slug)) is { } first
                ? spans.First(span => span.Reckoning == first.Name).Years
                : spans.FirstOrDefault()?.Years;

        // The events inside are found on the axis every event's own year is counted on, as the reader does.
        var frame = reckonings.FirstOrDefault(r => r.LastAnnoMundiBeforeTheCommonEra == EncyclopediaEndpoints.LastYearBeforeChrist)
                    ?? reckonings.FirstOrDefault(r => r.IsDefault);
        var span = own ?? (frame is not null && detail.Years.TryGetValue(frame.Slug, out var framed) ? framed : null);
        var inside = new List<(PageLink, string)>();
        var total = 0;
        if (span is not null)
        {
            var within = db.Events.Where(e => e.YearFromCreation >= span[0] && e.YearFromCreation <= span[1]);
            var order = await EncyclopediaEndpoints.InOrder(within, cancellationToken);
            total = order.Count;
            var listed = order.Take(PeriodEventsShown).ToList();
            var titled = await db.Events
                .Where(e => listed.Contains(e.Slug))
                .Select(e => new { e.Slug, e.Name, e.YearFromCreation })
                .ToListAsync(cancellationToken);
            foreach (var one in EncyclopediaEndpoints.Ordered(titled, listed, e => e.Slug))
            {
                inside.Add((new PageLink($"/events/{one.Slug}", EventMarks.Unstarred(one.Name)),
                    one.YearFromCreation is { } year
                        ? PageViews.AxisYear(wording, year, EncyclopediaEndpoints.LastYearBeforeChrist)
                        : ""));
            }
        }

        PageLink? person = null;
        if (detail.Entity is { } named && await Entity(named.Slug, language, cancellationToken) is { } entity)
        {
            person = new PageLink(PageAddress.RecordPath(entity.Kind, entity.Slug), entity.Shown);
        }

        var view = new PeriodView(detail.Slug, detail.LocalName ?? detail.Name, shown)
        {
            Kind = english ? detail.Kind : null,
            Notes = detail.Notes,
            Region = english ? detail.Region : null,
            Parent = detail.Parent is { } parent ? new PageLink($"/periods/{parent.Slug}", parent.LocalName ?? parent.Name) : null,
            Person = person,
            Spans = spans,
            Opens = detail.Opens is { } opens ? new PageLink($"/events/{opens.Slug}", opens.LocalName ?? opens.Name) : null,
            Closes = detail.Closes is { } closes ? new PageLink($"/events/{closes.Slug}", closes.LocalName ?? closes.Name) : null,
            Inside = inside,
            InsideTotal = total,
            Nested = [.. detail.Inside.Select(child => new PageLink($"/periods/{child.Slug}", child.LocalName ?? child.Name))],
            Source = detail.Source,
        };
        return PageViews.Period(language, wording, view) with
        {
            Data = PageData.Of([(PageData.PeriodAddress(detail.Slug, language), PageData.Json(detail))]),
        };
    }

    /// <summary>The reckonings, in their order, which change only with the corpus.</summary>
    private Task<IList<ChronologyResponse>> Reckonings(CancellationToken cancellationToken) =>
        cache.Remember("reckonings", PageCache.Corpus, () => EncyclopediaEndpoints.Chronologies(db, cancellationToken));
}
