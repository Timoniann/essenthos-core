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
    private async Task<PageContent> OccupationCatalogue(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var catalogue = await OccupationEndpoints.Catalogue(db, address.Language.Corpus, address.Language.Corpus,
            address.Query is { Length: > 120 } query ? query[..120] : address.Query, address.Category, address.Skip, 100, cancellationToken);
        return PageViews.OccupationCatalogue(address.Language, wording, catalogue, address.Indexed);
    }

    /// <summary>How many of an occupation's people its page lists.</summary>
    private const int OccupationPeopleListed = 100;

    private async Task<PageContent?> Occupation(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var language = address.Language;
        var document = await StoredOccupation(address.Slug!, language, cancellationToken)
                       ?? await OccupationDocument(address.Slug!, language, cancellationToken);
        if (document is null) return null;
        var occupation = document.Occupation;
        var references = occupation.People.SelectMany(p => p.Verses).Distinct().ToList();
        var labels = new Dictionary<VerseRefResponse, string>();
        foreach (var reference in references)
            labels[reference] = wording.Reference(await BookName(language, wording, reference.BookOrdinal, cancellationToken),
                reference.Chapter, reference.Verse);
        return PageViews.Occupation(language, wording, occupation, labels) with { Data = document.Data };
    }

    /// <summary>
    /// What an occupation's page in this language is written from, composed from the database as a page asked for composes
    /// it; null where there is no such occupation in this language. The build stores it (TSK-1819).
    /// </summary>
    internal async Task<OccupationDocument?> OccupationDocument(string slug, SiteLanguage language, CancellationToken cancellationToken)
    {
        var occupation = await OccupationEndpoints.Detail(db, slug, language.Corpus, language.Corpus,
            take: OccupationPeopleListed, cancellationToken: cancellationToken);
        if (occupation is null) return null;
        // The reader's page asks for the first people a page at a time; they are the first of these,
        // so that answer is carried as it would be given rather than asked of the database again.
        var first = occupation with
        {
            Take = PageData.OccupationPeople,
            People = [.. occupation.People.Take(PageData.OccupationPeople)],
        };
        return new OccupationDocument(PageDocuments.Version, PageDocuments.Build, occupation, await Conditions(null, language, cancellationToken))
        {
            Data = PageData.Of([(PageData.OccupationAddress(occupation.Occupation.Slug, language), PageData.Json(first))]),
        };
    }

    /// <summary>An occupation's stored document, where it may be read as things stand.</summary>
    private async Task<OccupationDocument?> StoredOccupation(string slug, SiteLanguage language, CancellationToken cancellationToken) =>
        await Stored(PageDocuments.OccupationKind, language, slug, PageDocuments.Occupation, cancellationToken) is { } stored
        && await Holds(stored.Conditions, null, language, cancellationToken)
            ? stored
            : null;

    /// <summary>
    /// The first page of an occupation's people as <c>/v1/occupations/{slug}</c> answers it in this language, read from the
    /// occupation's stored document; null where there is none that may be read as things stand.
    /// </summary>
    internal async Task<ReadOnlyMemory<byte>?> StoredOccupationAnswer(string slug, SiteLanguage language, CancellationToken cancellationToken) =>
        await StoredOccupation(slug, language, cancellationToken) is { } stored
            ? PageData.Answer(stored.Data, PageData.OccupationAddress(slug, language))
            : null;
}
