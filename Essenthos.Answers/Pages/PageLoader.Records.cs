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
    private async Task<PageContent?> Record(PageAddress address, PageWording wording, CancellationToken cancellationToken)
    {
        var language = address.Language;
        if (await StoredRecord(address.Slug!, language, cancellationToken) is { } stored)
        {
            return await Written(stored, language, wording, cancellationToken);
        }

        if (await Entity(address.Slug!, language, cancellationToken, address.RecordKind) is not { } entity)
        {
            // A record withdrawn with nothing in its place is gone, not missing, so a search engine
            // forgets the address rather than asking again.
            return WithdrawnAddresses.IsWithdrawn(address.Slug!)
                ? PageViews.NotFound(language, wording) with { Status = StatusCodes.Status410Gone }
                : null;
        }

        // A record folded into another is found by its old address too, and sent to the one that stays,
        // so a search engine moves what it knew of the old page to the new rather than keeping both.
        if (entity.Slug != address.Slug)
        {
            return PageViews.NotFound(language, wording) with
            {
                Status = StatusCodes.Status301MovedPermanently,
                MovedTo = PageAddress.RecordPath(entity.Kind, entity.Slug),
            };
        }

        return await Written(await Composed(entity, language, cancellationToken), language, wording, cancellationToken);
    }

    /// <summary>A record's stored document, where it may be read as things stand.</summary>
    private async Task<RecordDocument?> StoredRecord(string slug, SiteLanguage language, CancellationToken cancellationToken) =>
        await Stored(PageDocuments.RecordKind, language, slug, PageDocuments.Record, cancellationToken) is { } stored
        && await Holds(stored.Conditions, stored.Slug, language, cancellationToken)
            ? stored
            : null;

    /// <summary>
    /// The record as <c>/v1/entities/{slug}?language=&amp;prose=</c> answers it in this language, read from the record's
    /// stored document; null where there is none that may be read as things stand, or where the answer it carries is another
    /// record's, whose picture the document's conditions do not speak for.
    /// </summary>
    internal async Task<ReadOnlyMemory<byte>?> StoredEntityAnswer(string slug, SiteLanguage language, CancellationToken cancellationToken) =>
        await StoredRecord(slug, language, cancellationToken) is { } stored
        && PageData.Answer(stored.Data, PageData.EntityAddress(slug, language)) is { } answer
        && PageData.Slug(answer.Span) == stored.Slug
            ? answer
            : (ReadOnlyMemory<byte>?)null;

    /// <summary>
    /// What a record's page in this language is written from, composed from the database as a page asked for composes it;
    /// null where the slug is not a record's own address. The build stores it (TSK-1670).
    /// </summary>
    internal async Task<RecordDocument?> RecordDocument(string slug, SiteLanguage language, CancellationToken cancellationToken) =>
        await Entity(slug, language, cancellationToken) is { } entity && entity.Slug == slug
            ? await Composed(entity, language, cancellationToken)
            : null;

    private async Task<RecordDocument> Composed(ShownEntity entity, SiteLanguage language, CancellationToken cancellationToken)
    {
        // The record as the reader's script asks for it in this language: the page carries it, and its
        // names and what the person is known for are written from it.
        var detail = await EncyclopediaEndpoints.Detail(
            db, settings, places, entity.Slug, language.Corpus, language.Corpus, cancellationToken);
        // The record's own answer has read its line in this language and its tally already.
        var own = detail is not null && detail.Slug == entity.Slug ? detail : null;
        var localDistinguisher = own is not null
            ? own.LocalDistinguisher
            : (await EntityDistinguishers.Of(db, [entity.Id], language.Corpus, cancellationToken)).GetValueOrDefault(entity.Id);
        // The structured line where it is phrased in the page's language — every language on an English
        // page, Ukrainian, German and Spanish on theirs — and the source's English line on English pages only.
        var descriptor = await RecordDescriptors.Of(db, entity.Slug, language.Corpus, cancellationToken);
        if (descriptor is not null && !language.IsEnglish && !PhrasedLanguages.Contains(descriptor.Language))
        {
            descriptor = null;
        }

        var described = descriptor is null ? "" : string.Concat(descriptor.Parts.Select(part => part.Text)).Trim();
        var english = language.IsEnglish ? entity.Distinguisher : null;
        var line = new[] { localDistinguisher, described, english }
            .FirstOrDefault(said => !string.IsNullOrWhiteSpace(said));
        var references = own?.References ?? (await SharedReads.Once($"tally:{entity.Id}", () => db.Entities
            .Where(e => e.Id == entity.Id)
            .Select(EncyclopediaEndpoints.Tally)
            .SingleAsync(cancellationToken))).References;
        var first = await SharedReads.Once($"first-verses:{entity.Id}", () => db.EntityVerses.Shown()
            .Where(v => v.EntityId == entity.Id)
            .Select(v => new { v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
            .Distinct()
            .OrderBy(v => v.CanonicalBook).ThenBy(v => v.CanonicalChapter).ThenBy(v => v.CanonicalVerse)
            .Take(VersesNamed)
            .ToListAsync(cancellationToken));
        var quoted = await TextsAt(
            language, [.. first.Select(v => Address(v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse))], cancellationToken);
        var coordinates = entity.Kind == EntityKind.Place ? await Coordinates(entity.Id, cancellationToken) : null;
        return new RecordDocument(
            PageDocuments.Version,
            PageDocuments.Build,
            entity.Kind,
            entity.Slug,
            entity.Shown,
            line is null ? null : PageViews.WithoutCitations(line),
            descriptor is null
                ? []
                : [.. descriptor.Parts.Select(part => new LinePart(
                    part.Text,
                    part.Entity is { } target ? PageAddress.TargetPath(target.Kind, target.Slug) : null))],
            references,
            [
                .. first.Select(v => new QuotedVerse(v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse,
                    quoted.GetValueOrDefault(Address(v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse)))),
            ],
            await Picture(entity.Slug, language.Corpus, cancellationToken),
            coordinates?.Latitude,
            coordinates?.Longitude,
            await SameAs(entity.Id, cancellationToken),
            detail is null ? [] : OriginalNames(detail, language),
            detail?.KnownFor is { } known
                ? [.. known.Select(clause => new KnownClause(clause.Text, StructuredData.LanguageTag(clause.Language)))]
                : [],
            await Conditions(entity.Slug, language, cancellationToken))
        {
            Data = detail is null ? null : PageData.Of([(PageData.EntityAddress(entity.Slug, language), PageData.Json(detail))]),
        };
    }

    /// <summary>A record's page written from its document, in this build's wording.</summary>
    private async Task<PageContent> Written(RecordDocument document, SiteLanguage language, PageWording wording, CancellationToken cancellationToken)
    {
        var mentions = new List<VerseMention>(document.Verses.Count);
        foreach (var verse in document.Verses)
        {
            mentions.Add(new VerseMention(
                verse.Book, verse.Chapter, verse.Verse,
                wording.Reference(await BookName(language, wording, verse.Book, cancellationToken), verse.Chapter, verse.Verse))
            {
                Text = verse.Text,
            });
        }

        return PageViews.Record(language, wording, new RecordView(
            document.Kind,
            document.Slug,
            document.Name,
            document.Line,
            document.Descriptor,
            document.References,
            mentions)
        {
            Image = document.Image,
            Coordinates = document.Latitude is { } latitude && document.Longitude is { } longitude ? (latitude, longitude) : null,
            SameAs = document.SameAs,
            Names = document.Names,
            KnownFor = document.KnownFor,
        }) with
        {
            Data = document.Data,
        };
    }

    /// <summary>
    /// The record's names as the Hebrew or the Greek writes them, each once, with what it means in the
    /// page's language where that is said; a name other than the page's heading is named first.
    /// </summary>
    private static IReadOnlyList<OriginalName> OriginalNames(EntityResponse detail, SiteLanguage language)
    {
        var heading = detail.LocalName ?? detail.Name;
        return
        [
            .. detail.Names
                .Where(name => name.Hebrew is not null || name.Greek is not null)
                .Select(name =>
                {
                    var label = name.Printed ?? name.LocalLabel ?? (language.IsEnglish ? name.Label : null);
                    return new OriginalName(
                        label is null || string.Equals(label, heading, StringComparison.OrdinalIgnoreCase) ? null : label,
                        name.Hebrew, name.HebrewTransliterated, [.. name.HebrewStrongNumbers],
                        name.Greek, name.GreekTransliterated, [.. name.GreekStrongNumbers],
                        name.LocalMeaning ?? (language.IsEnglish ? name.Meaning : null));
                })
                .DistinctBy(name => (name.Label, name.Hebrew, name.Greek)),
        ];
    }

    private async Task<(double Latitude, double Longitude)?> Coordinates(int entityId, CancellationToken cancellationToken) =>
        await SharedReads.Once($"coordinates:{entityId}", () => db.PlaceLocations
            .Where(l => l.EntityId == entityId)
            .Select(l => new { l.Latitude, l.Longitude })
            .FirstOrDefaultAsync(cancellationToken)) is { } at
            ? (at.Latitude, at.Longitude)
            : null;

    /// <summary>The record's Wikidata item and its Wikipedia articles in the site's languages.</summary>
    private async Task<IReadOnlyList<string>> SameAs(int entityId, CancellationToken cancellationToken)
    {
        var articles = await SharedReads.Once($"same-as:{entityId}", () => db.EntityWikipedia
            .Where(w => w.EntityId == entityId)
            .OrderBy(w => w.Language)
            .Select(w => new { w.Language, w.Title, w.Qid })
            .ToListAsync(cancellationToken));
        return
        [
            .. articles.Select(a => a.Qid).Where(qid => !string.IsNullOrEmpty(qid)).Distinct()
                .Select(qid => $"https://www.wikidata.org/wiki/{qid}"),
            .. articles.Where(a => SiteLanguage.All.Any(language => language.Code == a.Language))
                .Select(a => WikipediaLinks.Address(a.Language, a.Title)),
        ];
    }

    private async Task<PageContent?> FamilyTree(PageAddress address, PageWording wording, CancellationToken cancellationToken) =>
        await Entity(address.Slug!, address.Language, cancellationToken) is { } entity
            ? PageViews.FamilyTree(
                address.Language, wording, $"/family-tree?person={Uri.EscapeDataString(entity.Slug)}", entity.Shown,
                await Picture(entity.Slug, address.Language.Corpus, cancellationToken))
            : null;

    /// <summary>
    /// A record by any address it has had, with the name a page in this language gives it; where a
    /// kind is given, also by another of the names a record of that kind holds.
    /// </summary>
    private async Task<ShownEntity?> Entity(
        string slug, SiteLanguage language, CancellationToken cancellationToken, EntityKind? kind = null)
    {
        var current = await MergedAddresses.Current(db, slug, cancellationToken);
        var entity = await Shown(current);
        if (entity is null && kind is { } named && await RecordAliases.Find(db, slug, named, cancellationToken) is { } alias)
        {
            entity = await Shown(alias);
        }

        if (entity is null)
        {
            return null;
        }

        var renamed = (await RenamedHeadings.Of(db, [entity.Id], language.Corpus, cancellationToken)).GetValueOrDefault(entity.Id);
        var local = renamed is null
            ? (await EntityNames.Of(db, [entity.Id], language.Corpus, cancellationToken)).GetValueOrDefault(entity.Id)
            : renamed.LocalName;
        var name = renamed?.Name ?? entity.Name;
        return new ShownEntity(entity.Id, entity.Slug, entity.Kind, string.IsNullOrEmpty(local) ? name : local, entity.Distinguisher);

        Task<StoredEntity?> Shown(string address) => SharedReads.Once($"shown:{address}", () => db.Entities
            .Where(e => e.Slug == address)
            .Select(e => new StoredEntity(e.Id, e.Slug, e.Kind, e.Name, e.Distinguisher))
            .FirstOrDefaultAsync(cancellationToken));
    }

    private sealed record StoredEntity(int Id, string Slug, EntityKind Kind, string Name, string? Distinguisher);

    private async Task<string?> Picture(string slug, string language, CancellationToken cancellationToken) =>
        (await ImageEndpoints.Leading(db, [slug], cancellationToken, settings.Is(SiteSettings.GeneratedImages), places, language))
        .GetValueOrDefault(slug)?.Url;

    private sealed record ShownEntity(int Id, string Slug, EntityKind Kind, string Shown, string? Distinguisher);
}
