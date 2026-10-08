using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal static partial class EncyclopediaEndpoints
{
    /// <summary>
    /// The record a tribe or people value names, so a page can say it in the reader's language: an
    /// Israelite tribe is its patriarch (<em>Judah</em>), and anything else is a people, written
    /// the way the datasets write it — as one of its members (<em>Edomite</em>), as the people
    /// (<em>Moabites</em>) or as the land or father it is named after (<em>Edom</em>, <em>Ishmael</em>).
    /// Null where nothing answers, more than one record does, or the answer is the record itself.
    /// </summary>
    internal static async Task<EntityTribeResponse?> TribeRecord(
        AppDbContext db, string? tribe, string? language, CancellationToken cancellationToken, string? self = null)
    {
        if (string.IsNullOrWhiteSpace(tribe)) return null;
        var tribalIds = db.EntityDescriptors.Where(d => d.Relation == "of-tribe").Select(d => d.TargetEntityId);
        var patriarchIds = db.EntityDescriptors.Where(d => tribalIds.Contains(d.EntityId)
            && d.Relation == "descendants-of" && d.Target!.Kind == EntityKind.Person).Select(d => d.TargetEntityId);
        var candidates = await db.Entities.Where(e => e.Name == tribe && e.Kind == EntityKind.Person
            && (tribalIds.Contains(e.Id) || patriarchIds.Contains(e.Id)))
            .Select(e => new { e.Id, e.Slug, e.Kind, e.Name }).ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            string[] spellings = [tribe, tribe + "s", tribe + "ites"];
            candidates = await db.Entities
                .Where(e => e.Kind == EntityKind.People
                    && (spellings.Contains(e.Name) || e.Names.Any(n => spellings.Contains(n.Label))))
                .Select(e => new { e.Id, e.Slug, e.Kind, e.Name })
                .ToListAsync(cancellationToken);
        }

        if (candidates.Count != 1 || candidates[0].Slug == self) return null;
        var found = candidates[0];
        var names = await EntityNames.Of(db, [found.Id], language, cancellationToken);
        return new EntityTribeResponse(found.Slug, EnumSpelling.Of(found.Kind), found.Name, names.GetValueOrDefault(found.Id));
    }

    /// <summary>The place a place is only another name for, with its name in the reader's language.</summary>
    internal static async Task<EntityTribeResponse?> AnotherNameFor(
        AppDbContext db, int? target, string? language, CancellationToken cancellationToken)
    {
        if (target is not { } id)
        {
            return null;
        }

        var found = await db.Entities.Where(e => e.Id == id)
            .Select(e => new { e.Slug, e.Kind, e.Name }).FirstOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return null;
        }

        var names = await EntityNames.Of(db, [id], language, cancellationToken);
        return new EntityTribeResponse(found.Slug, EnumSpelling.Of(found.Kind), found.Name, names.GetValueOrDefault(id));
    }

    /// <summary>One entity's page.</summary>
    private static void MapEntity(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/entities/{slug}", async (
            string slug,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
        {
            slug = await MergedAddresses.Current(db, slug, cancellationToken);
            var words = ReaderLanguages.Prose(prose, language);
            var entity = await db.Entities
                .Where(e => e.Slug == slug)
                .Select(e => new
                {
                    e.Id,
                    e.Slug,
                    e.Kind,
                    e.Name,
                    e.Distinguisher,
                    e.Sex,
                    e.Tribe,
                    e.PlaceKind,
                    e.Subtype,
                    e.ModernEquivalent,
                    e.Notes,
                    e.OpenBibleId,
                    e.AnotherNameForEntityId,
                    Origin = e.Origin == null
                        ? null
                        : new EntityOriginResponse(
                            e.Origin.Slug,
                            EnumSpelling.Of(e.Origin.Kind),
                            e.Origin.Name,
                            e.Origin.Distinguisher),
                    e.Source,
                    Location = e.Location == null
                        ? null
                        : new EntityLocationResponse(
                            e.Location.Longitude,
                            e.Location.Latitude,
                            e.Location.Kind,
                            e.Location.Score / ScoreScale),
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (entity is null)
            {
                return ApiResults.NotFound($"There is no person, place or people \"{slug}\".");
            }

            var tally = await db.Entities
                .Where(e => e.Id == entity.Id)
                .Select(Tally)
                .SingleAsync(cancellationToken);

            var names = await db.EntityNames
                .Where(n => n.EntityId == entity.Id)
                .Select(n => new
                {
                    n.Label, n.Hebrew, n.HebrewTransliterated, n.Greek, n.GreekTransliterated,
                    n.Meaning, n.HebrewStrongNumber, n.GreekStrongNumber, n.Kind,
                })
                .ToListAsync(cancellationToken);

            var related = await Relationships.Of(db, entity.Id, language, cancellationToken);
            var own = await Relationships.Forms(db, [entity.Id], language, cancellationToken);

            var events = await InOrder(db.Events.Where(e => e.EntityId == entity.Id))
                .Select(Rows)
                .ToListAsync(cancellationToken);
            var placesNamed = await PlacesNamed(db, events.Select(row => EventLocation.Of(row.Event)), cancellationToken);

            // Who says the text names this entity where it does — which is not always whoever
            // supplied the entity. A place can come from one dataset and be referenced by another,
            // and a page reporting 955 verses under the first one's credit would attribute the
            // second one's work to it.
            var stated = await db.EntityVerses.Shown()
                .Where(v => v.EntityId == entity.Id)
                .GroupBy(v => v.Source)
                .Select(g => new
                {
                    Source = g.Key,
                    Mentions = g.Count(),
                    References = g
                        .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                                     + v.CanonicalVerse)
                        .Distinct()
                        .Count(),
                })
                .OrderByDescending(row => row.Mentions)
                .ToListAsync(cancellationToken);

            // What established the record, and who else it might be. Both are empty for every
            // record a dataset supplied, which is nearly all of them — the cost is two indexed
            // reads that come back with nothing, and the gain is that a record this corpus wrote
            // for itself can never reach a reader looking like one it merely carried.
            var established = await db.EntityClaims
                .Where(c => c.EntityId == entity.Id)
                .Select(c => new { c.Method, c.Confidence, c.Source, c.Note })
                .ToListAsync(cancellationToken);

            var bearers = await db.TitleBearers
                .Where(b => b.TitleEntityId == entity.Id)
                .OrderBy(b => b.CanonicalBook).ThenBy(b => b.CanonicalChapter).ThenBy(b => b.CanonicalVerse)
                .Select(b => new
                {
                    b.Bearer!.Slug, b.Bearer.Kind, b.Bearer.Name, b.Bearer.Distinguisher,
                    b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse, b.Note,
                })
                .ToListAsync(cancellationToken);

            var titles = await db.TitleBearers
                .Where(b => b.BearerEntityId == entity.Id)
                .OrderBy(b => b.Title!.Name)
                .Select(b => new
                {
                    b.Title!.Slug, b.Title.Kind, b.Title.Name, b.Title.Distinguisher,
                    b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse, b.Note,
                })
                .ToListAsync(cancellationToken);

            var passages = await db.EntityPassages
                .Where(p => p.EntityId == entity.Id)
                .OrderBy(p => p.Ordinal)
                .ToListAsync(cancellationToken);

            var times = await db.ObservanceTimes
                .Where(o => o.EntityId == entity.Id)
                .OrderBy(o => o.Id)
                .ToListAsync(cancellationToken);

            var alternatives = (await db.EntityAlternatives
                    .Where(a => a.EntityId == entity.Id)
                    .Select(a => new
                    {
                        Slug = a.Alternative == null ? null : a.Alternative.Slug,
                        Name = a.Alternative == null ? null : a.Alternative.Name,
                        Distinguisher = a.Alternative == null ? null : a.Alternative.Distinguisher,
                        a.Describes,
                        a.Reason,
                        a.Source,
                    })
                    .ToListAsync(cancellationToken))
                .Select(a => new EntityAlternativeResponse(
                    a.Slug, a.Name, a.Distinguisher, a.Describes, a.Reason, a.Source, Datasets.Of(a.Source)))
                .ToList();

            var readByUs = await OursOnlyRecords.Among(
                db,
                [
                    entity.Slug,
                    .. related.Select(r => r.Slug),
                    .. bearers.Select(b => b.Slug),
                    .. titles.Select(t => t.Slug),
                    .. alternatives.Select(a => a.Slug).OfType<string>(),
                ],
                cancellationToken);
            var mine = readByUs.GetValueOrDefault(entity.Slug);
            var sex = mine is not null ? mine.Sex
                : entity.Sex ?? (entity.Kind == EntityKind.Person
                    ? (await OursOnlyRecords.SexesOf(db, [entity.Id], cancellationToken)).GetValueOrDefault(entity.Id)
                    : null);
            var lines = await EntityDistinguishers.OfSlugs(
                db,
                [
                    .. related.Select(r => r.Slug),
                    .. bearers.Select(b => b.Slug),
                    .. titles.Select(t => t.Slug),
                    .. alternatives.Select(a => a.Slug).OfType<string>(),
                ],
                words,
                cancellationToken);
            related =
            [
                .. related.Select(r => r with
                {
                    Distinguisher = OursOnlyRecords.Line(readByUs, r.Slug, r.Distinguisher),
                    LocalDistinguisher = EntityDistinguishers.For(readByUs, lines, r.Slug),
                }),
            ];
            alternatives =
            [
                .. alternatives.Select(a => a with
                {
                    Distinguisher = OursOnlyRecords.Line(readByUs, a.Slug, a.Distinguisher),
                    LocalDistinguisher = EntityDistinguishers.For(readByUs, lines, a.Slug),
                }),
            ];

            return Results.Ok(new EntityResponse(
                entity.Slug,
                EnumSpelling.Of(entity.Kind),
                entity.Name,
                mine is null ? entity.Distinguisher : mine.Line,
                sex,
                mine is null ? entity.Tribe : mine.Tribe,
                entity.PlaceKind,
                entity.ModernEquivalent,
                mine is null ? entity.Notes : null,
                entity.OpenBibleId,
                entity.Origin,
                mine is null ? entity.Source : OursOnlyRecords.Credit,
                Datasets.Of(mine is null ? entity.Source : OursOnlyRecords.Credit),
                tally.References,
                tally.Mentions,
                tally.Disputed,
                [
                    .. names.Select(n => new EntityNameResponse(
                        n.Label, n.Hebrew, n.HebrewTransliterated, n.Greek, n.GreekTransliterated,
                        n.Meaning, Numbers(n.HebrewStrongNumber), Numbers(n.GreekStrongNumber), n.Kind)),
                ],
                related,
                [.. events.Select(one => Event(one, placesNamed))],
                [
                    .. stated.Select(row => new EntityReferenceSourceResponse(
                        Datasets.Of(row.Source), row.Source, row.References, row.Mentions)),
                ],
                [
                    .. established.Select(c => new EntityClaimResponse(
                        EnumSpelling.Of(c.Method), c.Confidence, c.Source, Datasets.Of(c.Source), c.Note)),
                ],
                alternatives,
                alternatives.Count > 0)
            {
                Forms = own.GetValueOrDefault(entity.Slug),
                TribeRecord = await TribeRecord(
                    db, mine is null ? entity.Tribe : mine.Tribe, language, cancellationToken, entity.Slug),
                Descriptor = await Descriptors.Of(
                    db, entity.Slug, words, cancellationToken, language),
                Location = entity.Location,
                AnotherNameFor = await AnotherNameFor(db, entity.AnotherNameForEntityId, language, cancellationToken),
                Wikipedia = await WikipediaLinks.Of(db, entity.Id, words, cancellationToken),
                LocalName = (await EntityNames.Of(db, [entity.Id], language, cancellationToken))
                    .GetValueOrDefault(entity.Id),
                LocalDistinguisher = mine is null || mine.Line is not null
                    ? (await EntityDistinguishers.Of(db, [entity.Id], words, cancellationToken))
                        .GetValueOrDefault(entity.Id)
                    : null,
                Renderings = await Renderings(db, entity.Id, cancellationToken),
                Images = await ImageEndpoints.Of(
                    db, entity.Id, cancellationToken, settings.Is(SiteSettings.GeneratedImages), language),
                Bearers =
                [
                    .. bearers.Select(b => new EntityTitleResponse(
                        b.Slug, EnumSpelling.Of(b.Kind), b.Name, OursOnlyRecords.Line(readByUs, b.Slug, b.Distinguisher),
                        BookReferences.At(b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse)!, b.Note)
                    {
                        LocalDistinguisher = EntityDistinguishers.For(readByUs, lines, b.Slug),
                    }),
                ],
                Titles =
                [
                    .. titles.Select(t => new EntityTitleResponse(
                        t.Slug, EnumSpelling.Of(t.Kind), t.Name, OursOnlyRecords.Line(readByUs, t.Slug, t.Distinguisher),
                        BookReferences.At(t.CanonicalBook, t.CanonicalChapter, t.CanonicalVerse)!, t.Note)
                    {
                        LocalDistinguisher = EntityDistinguishers.For(readByUs, lines, t.Slug),
                    }),
                ],
                Subtype = entity.Subtype,
                LocalNotes = mine is null
                    ? await NoteTranslations.Of(db, entity.Id, entity.Notes, words, cancellationToken)
                    : null,
                Passages = [.. passages.Select(Passage)],
                Times =
                [
                    .. times.Select(o => new ObservanceTimeResponse(
                        o.Cycle, o.Month, o.Day, o.LastDay,
                        BookReferences.At(o.CanonicalBook, o.CanonicalChapter, o.CanonicalVerse)!, o.Note)),
                ],
            });
        });
    }

    /// <summary>
    /// Every text's spellings of one entity, a text's heading first. The texts in the corpus's own
    /// order of languages and then of texts, which is the order the pane list already offers them in.
    /// </summary>
    private static async Task<List<EntityRenderingResponse>> Renderings(
        AppDbContext db,
        int entityId,
        CancellationToken cancellationToken)
    {
        var rows = await db.EntityRenderings
            .Where(r => r.EntityId == entityId)
            .Select(r => new { r.Text!.Slug, r.Text.Language, r.TextId, r.Form, r.Occurrences, r.Heading })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .GroupBy(r => (r.TextId, r.Slug, r.Language))
                .OrderBy(g => g.Key.Language, StringComparer.Ordinal).ThenBy(g => g.Key.TextId)
                .Select(g => new EntityRenderingResponse(
                    g.Key.Slug,
                    g.Key.Language,
                    g.Single(r => r.Heading).Form,
                    g.Sum(r => r.Occurrences),
                    [
                        .. g.OrderByDescending(r => r.Occurrences).ThenBy(r => r.Form, StringComparer.Ordinal)
                            .Select(r => new EntitySpellingResponse(r.Form, r.Occurrences)),
                    ])),
        ];
    }
}
