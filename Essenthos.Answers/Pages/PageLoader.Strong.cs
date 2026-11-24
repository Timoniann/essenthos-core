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
    private async Task<PageContent?> Strong(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var language = address.Language;
        var document = await Stored(PageDocuments.StrongKind, language, address.Slug!, PageDocuments.Strong, cancellationToken) is { } stored
                       && await Holds(stored.Conditions, null, language, cancellationToken)
            ? stored
            : await StrongDocument(address.Slug!, language, cancellationToken);
        return document is null ? null : await Written(document, language, wording, cancellationToken);
    }

    /// <summary>
    /// What a Strong number's page in this language is written from, composed from the database as a page asked for
    /// composes it; null where there is no such entry. The build stores it (TSK-1670).
    /// </summary>
    internal async Task<StrongDocument?> StrongDocument(string number, SiteLanguage language, CancellationToken cancellationToken)
    {
        if (await StrongEndpoints.Detail(db, number, language.Corpus, cancellationToken) is not { } entry)
        {
            return null;
        }

        var conditions = await Conditions(null, language, cancellationToken);
        var answers = new List<(string, string)> { (PageData.StrongAddress(number, language), PageData.Json(entry)) };
        if (entry.Morpheme)
        {
            return new StrongDocument(PageDocuments.Version, PageDocuments.Build, number, null, null, entry.Definition, null, null, false, null, null, [], [], conditions)
            {
                Data = PageData.Of(answers),
            };
        }

        // How the page's own text renders the number and where it stands, as the reader's page asks
        // for them in that text: both are carried, and both are written out for whoever runs no script.
        var document = new StrongDocument(
            PageDocuments.Version,
            PageDocuments.Build,
            entry.StrongNumber,
            entry.Lemma,
            entry.Transliteration,
            entry.Translation?.Definition ?? entry.Definition,
            entry.Translation?.Derivation ?? entry.Derivation,
            entry.Translation?.DetailedDefinition ?? entry.DetailedDefinition,
            false,
            null,
            null,
            [],
            [],
            conditions);
        if (await Chosen(language, cancellationToken) is { } chosen && await canon.Text(chosen.Slug, cancellationToken) is { } text)
        {
            var renderings = await StrongEndpoints.RenderingsOf(
                db, number, text.Id, text.Slug, PageData.StrongRenderings, cancellationToken);
            answers.Add((PageData.StrongRenderingsAddress(number, chosen.Slug), PageData.Json(renderings)));
            var map = await StrongBookEndpoints.Map(db, number, [text], cancellationToken);
            answers.Add((PageData.StrongBooksAddress(number, chosen.Slug), PageData.Json(map)));
            document = document with
            {
                Rendered = true,
                Uses = map.Occurrences > 0 ? map.Occurrences : null,
                Edition = map.Edition,
                Renderings = [.. renderings.Renderings.Select(r => new StrongRenderingCount(r.Text, r.Count))],
                Books =
                [
                    .. map.Books.Where(row => row.Occurrences > 0).OrderBy(row => row.BookOrdinal)
                        .Select(row => new StrongBookTally(row.BookOrdinal, row.Occurrences)),
                ],
            };
        }

        return document with { Data = PageData.Of(answers) };
    }

    /// <summary>A Strong number's page written from its document, in this build's wording.</summary>
    private async Task<PageContent> Written(StrongDocument document, SiteLanguage language, PageWording wording, CancellationToken cancellationToken)
    {
        var view = new StrongView(
            document.Number, document.Lemma, document.Transliteration, document.Definition, document.Derivation, document.Outline);
        if (document.Rendered && await Chosen(language, cancellationToken) is { } chosen)
        {
            var books = new List<StrongBookCount>(document.Books.Count);
            foreach (var row in document.Books)
            {
                books.Add(new StrongBookCount(await BookName(language, wording, row.Book, cancellationToken), row.Count));
            }

            var witness = document.Edition is { } edition
                ? (await Texts(cancellationToken)).FirstOrDefault(t => t.Slug == edition)
                : null;
            view = view with
            {
                Uses = document.Uses,
                CountedIn = witness is null ? document.Edition : TextName(wording, witness),
                RenderedIn = TextName(wording, chosen),
                Renderings = document.Renderings,
                Books = books,
            };
        }

        return PageViews.Strong(language, wording, view) with { Data = document.Data };
    }
}
