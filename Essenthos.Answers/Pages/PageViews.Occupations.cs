using Essenthos.Core.Corpus;
using Essenthos.Core.Endpoints;

namespace Essenthos.Core.Pages;

internal static partial class PageViews
{
    public static PageContent OccupationCatalogue(SiteLanguage language, PageWording wording,
        OccupationCatalogueResponse catalogue, bool indexed)
    {
        var title = wording.Text("page.occupations.title");
        var description = wording.Text("page.occupations.description");
        var body = Open(language).Open("nav").Link(language.Path("/"), PageContent.SiteName).Close("nav")
            .Element("h1", title).Element("p", description).Open("ul");
        foreach (var occupation in catalogue.Items)
        {
            body.Open("li").Link(language.Path($"/occupations/{occupation.Slug}"), occupation.Name)
                .Text($" ({occupation.PersonCount})");
            if (occupation.Category == "heavenly_role")
                body.Text(" · ").Element("span", wording.Text("occupation.heavenlyRole"));
            body.Close("li");
        }
        body.Close("ul").Close("article");
        return Page(StatusCodes.Status200OK, wording.Titled(title), title, description, body) with
        {
            Canonical = indexed ? "/occupations" : null, Indexed = indexed,
            PageType = "CollectionPage", Trail = [Start, new Crumb(title, "/occupations")],
        };
    }

    public static PageContent Occupation(SiteLanguage language, PageWording wording,
        OccupationDetailResponse detail, IReadOnlyDictionary<VerseRefResponse, string> references)
    {
        var occupation = detail.Occupation;
        var description = occupation.Description is { Length: > 0 } text ? text
            : wording.Text("occupation.description", ("name", occupation.Name));
        var body = Open(language).Open("nav").Link(language.Path("/"), PageContent.SiteName).Text(Crumb)
            .Link(language.Path("/occupations"), wording.Text("page.occupations.title")).Close("nav")
            .Element("h1", occupation.Name)
            .Element("p", occupation.Category == "heavenly_role" ? wording.Text("occupation.heavenlyRole") : description).Open("ul");
        foreach (var practitioner in detail.People)
        {
            body.Open("li").Link(language.Path($"/people/{practitioner.Person.Slug}"),
                practitioner.Person.LocalName ?? practitioner.Person.Name).Text(" — ").Text(practitioner.RoleName);
            foreach (var reference in practitioner.Verses)
                body.Text(" · ").Link(language.Path(PageAddress.VersePath(reference.BookOrdinal, reference.Chapter, reference.Verse)),
                    references[reference]);
            body.Close("li");
        }
        body.Close("ul").Close("article");
        return Page(StatusCodes.Status200OK, wording.Titled(occupation.Name), occupation.Name, description, body) with
        {
            Canonical = $"/occupations/{occupation.Slug}", Type = "article",
            Trail = Trail(wording, "occupations", occupation.Name, $"/occupations/{occupation.Slug}"),
        };
    }
}
