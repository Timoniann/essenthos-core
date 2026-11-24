namespace Essenthos.Core.Pages;

internal static partial class PageViews
{
    /// <summary>Where the help pages are, by their English address.</summary>
    public const string HelpPath = "/help";

    /// <summary>
    /// How to use the site, written in full: the steps and what is good to know are the page, and an
    /// assistant that runs no script answers a reader from them. The words are the reader's own help
    /// files, published beside its page wording as <c>help</c>; a build without them has no help pages.
    /// </summary>
    public static PageContent? Help(SiteLanguage language, PageWording t, PageAddress address)
    {
        var heading = t.Optional("help.title");
        if (heading is null)
        {
            return null;
        }

        var topics = t.Keys("help.pages");
        var index = language.Path(HelpPath);
        if (address.Slug is not { } slug)
        {
            var lead = t.Text("help.lead");
            var list = Open(language)
                .Open("nav").Link(language.Path("/"), PageContent.SiteName).Close("nav")
                .Element("h1", heading)
                .Element("p", lead)
                .Element("h2", t.Text("help.topics"))
                .Open("ul");
            foreach (var topic in topics)
            {
                list.Open("li")
                    .Link(language.Path($"{HelpPath}/{topic}"), t.Text($"help.pages.{topic}.title"))
                    .Text(" — ")
                    .Text(t.Text($"help.pages.{topic}.summary"))
                    .Close("li");
            }

            list.Close("ul").Element("p", t.Text("help.keys")).Close("article");
            return Page(StatusCodes.Status200OK, t.Titled(heading), heading, lead, list) with
            {
                Canonical = HelpPath,
                Trail = [Start, new Crumb(heading, HelpPath)],
            };
        }

        if (!topics.Contains(slug))
        {
            return null;
        }

        var key = $"help.pages.{slug}";
        var title = t.Text($"{key}.title");
        var summary = t.Text($"{key}.summary");
        var english = $"{HelpPath}/{slug}";
        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Text(" / ").Link(index, heading).Close("nav")
            .Element("h1", title)
            .Element("p", summary)
            .Open("img", attributes:
            [
                ("src", $"/help/{language.Code}/{slug}.webp"),
                ("alt", t.Text("help.picture", ("title", title))),
                ("width", "1280"),
                ("height", "800"),
            ]);
        Lines(body.Element("h2", t.Text("help.steps")), "ol", t, $"{key}.steps");
        Lines(body.Element("h2", t.Text("help.tips")), "ul", t, $"{key}.tips");
        body.Open("p");
        if (t.Optional($"{key}.page") is { } page)
        {
            body.Link(language.Path(page), t.Text("help.open")).Text(" · ");
        }

        body.Link(index, t.Text("help.all")).Close("p")
            .Element("p", t.Text("help.keys"))
            .Close("article");
        return Page(StatusCodes.Status200OK, t.Titled(title), title, summary, body) with
        {
            Canonical = english,
            Trail = [Start, new Crumb(heading, HelpPath), new Crumb(title, english)],
        };
    }

    /// <summary>A help page's numbered lines, in their order, as a list.</summary>
    private static void Lines(HtmlWriter body, string list, PageWording t, string key)
    {
        body.Open(list);
        foreach (var line in t.Keys(key).OrderBy(name => int.TryParse(name, out var at) ? at : int.MaxValue))
        {
            body.Element("li", t.Text($"{key}.{line}"));
        }

        body.Close(list);
    }
}
