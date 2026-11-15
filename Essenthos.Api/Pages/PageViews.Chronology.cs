namespace Essenthos.Core.Pages;

/// <summary>A link a page writes: where it goes, in English, and what it says.</summary>
internal sealed record PageLink(string Href, string Text);

/// <summary>An event's date in one reckoning, its year in the page language's words.</summary>
/// <param name="Working">How the reckoning arrives at it, in the source's English; null on other pages.</param>
internal sealed record ReckonedDate(string Reckoning, string Year, string? Working);

/// <summary>A period's years in one reckoning; no reckoning where the period carries its own years.</summary>
internal sealed record ReckonedSpan(string? Reckoning, string Years);

/// <summary>An event as its page shows it, in the page language's words wherever the corpus has them.</summary>
/// <param name="Year">Its year in the default reckoning.</param>
internal sealed record EventView(string Slug, string Name, string? Year)
{
    /// <summary>What kind of event the source calls it, in its English; null on other pages.</summary>
    public string? Kind { get; init; }

    /// <summary>
    /// What the compiler wrote of it himself, in the page's language where it is held in it. Never a
    /// quotation of the Bible: the source's quotations are the NASB's, and their verses are quoted from
    /// the page language's own text instead (<see cref="Verses"/>).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>The description is the source's English on a page in another language.</summary>
    public bool DescriptionInEnglish { get; init; }

    /// <summary>What the source's notes say the year rests on, in the page's words; null where they flag nothing.</summary>
    public string? Mark { get; init; }

    public PageLink? Place { get; init; }

    /// <summary>The land the source puts it in, in its English; null on other pages.</summary>
    public string? Region { get; init; }

    public IReadOnlyList<ReckonedDate> Dates { get; init; } = [];

    /// <summary>
    /// The verses written of it, each with its words in the text the page's language reads: those the
    /// source's description quotes, or else the verse the event is read from.
    /// </summary>
    public IReadOnlyList<(PageLink Verse, string? Text)> Verses { get; init; } = [];

    public PageLink? Person { get; init; }

    public string? Source { get; init; }

    public string? Image { get; init; }

    /// <summary>A world event's Wikipedia article, the reader's language's or the English one standing in for it.</summary>
    public PageArticle? Wikipedia { get; init; }
}

/// <summary>An article elsewhere: its address, its title and the language it is written in.</summary>
internal sealed record PageArticle(string Href, string Title, string Language);

/// <summary>A period as its page shows it.</summary>
/// <param name="Years">Its years in the default reckoning, or its own where it carries them.</param>
internal sealed record PeriodView(string Slug, string Name, string? Years)
{
    public string? Kind { get; init; }

    /// <summary>What the source notes of it, in its own English.</summary>
    public string? Notes { get; init; }

    public string? Region { get; init; }

    public PageLink? Parent { get; init; }

    public PageLink? Person { get; init; }

    public IReadOnlyList<ReckonedSpan> Spans { get; init; } = [];

    public PageLink? Opens { get; init; }

    public PageLink? Closes { get; init; }

    /// <summary>The first of the events inside it, each with its year.</summary>
    public IReadOnlyList<(PageLink Event, string Year)> Inside { get; init; } = [];

    public int InsideTotal { get; init; }

    public IReadOnlyList<PageLink> Nested { get; init; } = [];

    public string? Source { get; init; }
}

internal static partial class PageViews
{
    /// <summary>
    /// An event's page, in the order the reader's page shows it: its name and year, the verses written of
    /// it quoted in the page language's text, what the compiler adds of his own, its year in each
    /// reckoning as a table, and whom it happened to.
    /// </summary>
    public static PageContent Event(SiteLanguage language, PageWording t, EventView view)
    {
        var english = $"/events/{view.Slug}";
        var named = view.Year is null ? view.Name : $"{view.Name} ({view.Year})";
        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Text(Crumb)
            .Link(language.Path(PageSections.Named("events").Path), t.Text("page.events.title")).Close("nav")
            .Element("h1", view.Name);
        Facts(body, language, view.Mark is null ? view.Year : $"{view.Year} ({view.Mark})", view.Kind, view.Place, view.Region);
        if (view.Wikipedia is { } article)
        {
            body.Open("p")
                .Text(t.Text(article.Language == language.Code ? "event.wikipedia" : "event.wikipediaInEnglish") + ": ")
                .Link(article.Href, article.Title, rel: "noreferrer")
                .Close("p");
        }

        if (view.Verses.Count > 0)
        {
            body.Element("h2", t.Text("event.written"));
            foreach (var (verse, text) in view.Verses)
            {
                body.Open("p").Link(language.Path(verse.Href), verse.Text);
                if (text is { Length: > 0 })
                {
                    body.Text(" " + text);
                }

                body.Close("p");
            }
        }

        if (view.Description is { Length: > 0 } compilerSays)
        {
            body.Element("p", compilerSays, lang: view.DescriptionInEnglish ? "en" : null);
        }

        if (view.Dates.Count > 0)
        {
            body.Element("h2", t.Text("event.when")).Open("table").Open("thead").Open("tr")
                .Element("th", t.Text("event.reckoning")).Element("th", t.Text("event.year"));
            var worked = view.Dates.Any(date => date.Working is { Length: > 0 });
            if (worked)
            {
                body.Element("th", t.Text("event.working"));
            }

            body.Close("tr").Close("thead").Open("tbody");
            foreach (var date in view.Dates)
            {
                body.Open("tr").Element("td", date.Reckoning).Element("td", date.Year);
                if (worked)
                {
                    body.Element("td", date.Working ?? "", lang: language.IsEnglish ? null : "en");
                }

                body.Close("tr");
            }

            body.Close("tbody").Close("table");
        }

        if (view.Person is { } person)
        {
            body.Element("h2", t.Text("event.whom")).Open("p").Link(language.Path(person.Href), person.Text).Close("p");
        }

        Credit(body, t, view.Source);
        body.Close("article");

        var said = view.Verses.FirstOrDefault(verse => verse.Text is { Length: > 0 }).Text
                   ?? (view.DescriptionInEnglish ? null : view.Description);
        var description = said is { Length: > 0 }
            ? Fitted(said, fit => Closed(named) + " " + fit)
            : t.Text("event.description", ("name", named));
        return Page(StatusCodes.Status200OK, t.Titled(named), view.Name, description, body) with
        {
            Canonical = english,
            Image = view.Image,
            Type = "article",
            Trail = Trail(t, "events", view.Name, english),
        };
    }

    /// <summary>
    /// A period's page: where it sits, its years in each reckoning, the events that open and close it
    /// and the first of those inside it, and the periods nested in it.
    /// </summary>
    public static PageContent Period(SiteLanguage language, PageWording t, PeriodView view)
    {
        var english = $"/periods/{view.Slug}";
        var named = view.Years is null ? view.Name : $"{view.Name} ({view.Years})";
        var body = Open(language).Open("nav").Link(language.Path("/"), PageContent.SiteName);
        if (view.Parent is { } parent)
        {
            body.Text(Crumb).Link(language.Path(parent.Href), parent.Text);
        }

        body.Close("nav").Element("h1", view.Name);
        Facts(body, language, view.Years, view.Kind, null, view.Region);
        if (view.Notes is { Length: > 0 } notes)
        {
            body.Element("p", notes, lang: language.IsEnglish ? null : "en");
        }

        if (view.Person is { } person)
        {
            body.Open("p").Link(language.Path(person.Href), person.Text).Close("p");
        }

        if (view.Spans.Count > 0)
        {
            body.Element("h2", t.Text("period.spans")).Open("ul");
            foreach (var span in view.Spans)
            {
                body.Element("li", span.Reckoning is null ? span.Years : $"{span.Reckoning}: {span.Years}");
            }

            body.Close("ul");
        }

        if (view.Opens is not null || view.Closes is not null)
        {
            body.Open("ul");
            foreach (var (label, bound) in new[] { (t.Text("period.opens"), view.Opens), (t.Text("period.closes"), view.Closes) })
            {
                if (bound is not null)
                {
                    body.Open("li").Text(label + ": ").Link(language.Path(bound.Href), bound.Text).Close("li");
                }
            }

            body.Close("ul");
        }

        if (view.Inside.Count > 0)
        {
            body.Element("h2", $"{t.Text("period.inside")} ({view.InsideTotal})").Open("ul");
            foreach (var (inside, year) in view.Inside)
            {
                body.Open("li").Link(language.Path(inside.Href), inside.Text).Text($" ({year})").Close("li");
            }

            body.Close("ul");
        }

        if (view.Nested.Count > 0)
        {
            body.Element("h2", t.Text("period.nested")).Open("ul");
            foreach (var nested in view.Nested)
            {
                body.Open("li").Link(language.Path(nested.Href), nested.Text).Close("li");
            }

            body.Close("ul");
        }

        Credit(body, t, view.Source);
        body.Close("article");

        var said = language.IsEnglish && view.Notes is { Length: >= LeastQuoteLength } notesSaid ? notesSaid : null;
        var description = said is not null
            ? Fitted(said, fit => Closed(named) + " " + fit)
            : t.Text("period.description", ("name", named));
        return Page(StatusCodes.Status200OK, t.Titled(named), view.Name, description, body) with
        {
            Canonical = english,
            Type = "article",
            Trail = Trail(t, "timeline", view.Name, english),
        };
    }

    /// <summary>A year in the page language's words: <c>41 BCE</c>, <c>41 до н. е.</c>.</summary>
    public static string Year(PageWording t, int year, bool commonEra) =>
        t.Text(commonEra ? "year.ce" : "year.bce", ("year", year.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>A position on a reckoning's axis as a year of either era, as the reader writes it.</summary>
    public static string AxisYear(PageWording t, int annoMundi, int zero) =>
        annoMundi > zero ? Year(t, annoMundi - zero, true) : Year(t, zero - annoMundi + 1, false);

    /// <summary>A stretch of a reckoning's axis, one year where it begins and ends in the same.</summary>
    public static string AxisSpan(PageWording t, int from, int to, int zero) =>
        from == to ? AxisYear(t, from, zero) : $"{AxisYear(t, from, zero)} – {AxisYear(t, to, zero)}";

    private static void Facts(HtmlWriter body, SiteLanguage language, string? year, string? kind, PageLink? place, string? region)
    {
        if (year is null && kind is null && place is null && region is null)
        {
            return;
        }

        body.Open("p");
        var first = true;
        foreach (var fact in new[] { year, kind })
        {
            if (fact is { Length: > 0 })
            {
                body.Text((first ? "" : " · ") + fact);
                first = false;
            }
        }

        if (place is not null)
        {
            body.Text(first ? "" : " · ").Link(language.Path(place.Href), place.Text);
            first = false;
        }

        if (region is { Length: > 0 })
        {
            body.Text((first ? "" : " · ") + region);
        }

        body.Close("p");
    }

    private static void Credit(HtmlWriter body, PageWording t, string? source)
    {
        if (source is { Length: > 0 })
        {
            body.Element("p", t.Text("takenFrom", ("source", source)));
        }
    }
}
