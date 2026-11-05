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

    /// <summary>
    /// The place a place is only another name for, with its name in the reader's language. A place of
    /// the same name is no other name: the source's phrase there says only that it may be that place.
    /// </summary>
    internal static async Task<EntityTribeResponse?> AnotherNameFor(
        AppDbContext db, int? target, string name, string? language, CancellationToken cancellationToken)
    {
        if (target is not { } id)
        {
            return null;
        }

        var found = await db.Entities.Where(e => e.Id == id)
            .Select(e => new { e.Slug, e.Kind, e.Name }).FirstOrDefaultAsync(cancellationToken);
        if (found is null || found.Name == name)
        {
            return null;
        }

        var names = await EntityNames.Of(db, [id], language, cancellationToken);
        return new EntityTribeResponse(found.Slug, EnumSpelling.Of(found.Kind), found.Name, names.GetValueOrDefault(id));
    }

    /// <summary>
    /// The place a town was built on the site of, or the town later built on a place's site, with its
    /// name in the reader's language.
    /// </summary>
    internal static async Task<EntityTribeResponse?> SharedSite(
        AppDbContext db, string? slug, string? language, CancellationToken cancellationToken)
    {
        if (slug is null)
        {
            return null;
        }

        var found = await db.Entities.Where(e => e.Slug == slug)
            .Select(e => new { e.Id, e.Kind, e.Name }).FirstOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return null;
        }

        var names = await EntityNames.Of(db, [found.Id], language, cancellationToken);
        return new EntityTribeResponse(slug, EnumSpelling.Of(found.Kind), found.Name, names.GetValueOrDefault(found.Id));
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
                    OpenBibleId = e.ExternalIds
                        .Where(x => x.Dataset == Datasets.OpenBibleId && x.Relation == RecordExternalRelations.SameAs)
                        .Select(x => x.ExternalId)
                        .FirstOrDefault(),
                    e.AnotherNameForEntityId,
                    Origin = e.Origin == null
                        ? null
                        : new EntityOriginResponse(
                            e.Origin.Slug,
                            EnumSpelling.Of(e.Origin.Kind),
                            e.Origin.Name,
                            e.Origin.Distinguisher),
                    Location = e.Location == null
                        ? null
                        : new EntityLocationResponse(
                            e.Location.Longitude,
                            e.Location.Latitude,
                            e.Location.Kind,
                            e.Location.Score / ScoreScale)
                        {
                            CoordinatesSource = e.Location.CoordinatesSource,
                            CoordinatesBy = e.Location.CoordinatesDataset,
                        },
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

            var events = await Aged(db, await InOrder(db.Events.Where(e => e.EntityId == entity.Id))
                .Select(Rows)
                .ToListAsync(cancellationToken), cancellationToken);
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
                .Select(c => new { c.Method, c.Confidence, c.Source })
                .ToListAsync(cancellationToken);

            var bearers = await db.TitleBearers
                .Where(b => b.TitleEntityId == entity.Id)
                .OrderBy(b => b.CanonicalBook).ThenBy(b => b.CanonicalChapter).ThenBy(b => b.CanonicalVerse)
                .Select(b => new
                {
                    b.BearerEntityId, b.Bearer!.Slug, b.Bearer.Kind, b.Bearer.Name, b.Bearer.Distinguisher,
                    b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse, b.Note,
                })
                .ToListAsync(cancellationToken);

            var titles = await db.TitleBearers
                .Where(b => b.BearerEntityId == entity.Id)
                .OrderBy(b => b.Title!.Name)
                .Select(b => new
                {
                    b.TitleEntityId, b.Title!.Slug, b.Title.Kind, b.Title.Name, b.Title.Distinguisher,
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

            var held = await db.EntityAlternatives
                .Where(a => a.EntityId == entity.Id)
                .Select(a => new
                {
                    Slug = a.Alternative == null ? null : a.Alternative.Slug,
                    Name = a.Alternative == null ? null : a.Alternative.Name,
                    Distinguisher = a.Alternative == null ? null : a.Alternative.Distinguisher,
                    a.Describes,
                    a.Reason,
                    a.Source,
                    a.Back,
                })
                .ToListAsync(cancellationToken);
            var reasons = await TextTranslations.Of(db, held.Select(a => a.Reason), words, cancellationToken);
            var alternatives = held
                .Select(a => new EntityAlternativeResponse(
                    a.Slug, a.Name, a.Distinguisher, a.Describes, a.Reason, a.Source, Datasets.Of(a.Source))
                {
                    Back = a.Back,
                    LocalReason = TextTranslations.In(reasons, a.Reason),
                })
                .ToList();

            var sex = entity.Sex;
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

            // The record's own line and the people's it is related to, in one reading, so a row says who
            // each one is as their own page does, in the reader's language. A people's, a place's or a
            // title's line is not read here.
            var described = await Descriptors.Of(
                db,
                [entity.Slug, .. related.Where(r => r.Kind == EnumSpelling.Of(EntityKind.Person)).Select(r => r.Slug)],
                words,
                cancellationToken,
                language);
            related =
            [
                .. related.Select(r => r with
                {
                    LocalDistinguisher = lines.GetValueOrDefault(r.Slug),
                    Line = described.GetValueOrDefault(r.Slug) is { } line
                        ? string.Concat(line.Parts.Select(part => part.Text))
                        : null,
                }),
            ];
            alternatives =
            [
                .. alternatives.Select(a => a with
                {
                    LocalDistinguisher = a.Slug is null ? null : lines.GetValueOrDefault(a.Slug),
                }),
            ];

            var localNames = await EntityNames.Of(db, [entity.Id], language, cancellationToken);
            var counterpartNames = titles.Count + bearers.Count == 0
                ? []
                : await EntityNames.Of(
                    db, [.. titles.Select(t => t.TitleEntityId), .. bearers.Select(b => b.BearerEntityId)], language,
                    cancellationToken);
            var sourced = await TextTranslations.Of(
                db,
                [
                    entity.ModernEquivalent, entity.Notes,
                    .. bearers.Select(b => b.Note), .. titles.Select(t => t.Note),
                ],
                words,
                cancellationToken);
            var renamed = (await RenamedHeadings.Of(db, [entity.Id], language, cancellationToken)).GetValueOrDefault(entity.Id);
            var locations = entity.Kind == EntityKind.Place
                ? await Candidates(db, entity.Id, language, cancellationToken, words)
                : null;
            // A name the page's heading does not already say is given as the reader's texts print it.
            var otherNames = names
                .Where(n => !EntityNames.IsTitle(n.Kind)
                            && !string.Equals(n.Label, renamed?.Name ?? entity.Name, StringComparison.OrdinalIgnoreCase))
                .Select(n => new BorneName(n.Label, NameSpellings.Numbers(n.HebrewStrongNumber, n.GreekStrongNumber)))
                .Where(n => n.Numbers.Count > 0)
                .ToList();
            var spelled = otherNames.Count == 0
                ? []
                : await NameSpellings.Of(
                    db, entity.Id, otherNames, language, localNames.GetValueOrDefault(entity.Id), cancellationToken);
            var lemmas = await HebrewLemmas(db, names.Where(n => n.Hebrew is null).Select(n => n.HebrewStrongNumber), cancellationToken);
            var glosses = await EntityNameTranslations.Of(
                db, names.SelectMany(n => new[] { n.Label, n.Meaning }), words, cancellationToken);

            var ownNotes = await NoteTranslations.Of(db, entity.Id, entity.Notes, words, cancellationToken);
            var machineNotes = ownNotes is null ? TextTranslations.In(sourced, entity.Notes) : null;

            // A record is ours (the record files keep no dataset as its source): it is credited to our own claim that
            // established it, the strongest first; a dataset's statement about it stays among the claims.
            var credit = established
                .OrderByDescending(c => Datasets.Of(c.Source) == Datasets.Own)
                .ThenByDescending(c => ClaimStanding.Of(c.Method))
                .ThenByDescending(c => c.Confidence ?? 1)
                .Select(c => c.Source)
                .FirstOrDefault() ?? OursOnlyRecords.Credit;

            return Results.Ok(new EntityResponse(
                entity.Slug,
                EnumSpelling.Of(entity.Kind),
                renamed?.Name ?? entity.Name,
                entity.Kind == EntityKind.Place ? PlaceToday.Line(entity.Distinguisher, entity.ModernEquivalent, locations)
                    : entity.Distinguisher,
                sex,
                entity.Tribe,
                entity.PlaceKind,
                entity.Kind == EntityKind.Place ? PlaceToday.Of(entity.ModernEquivalent, locations) : entity.ModernEquivalent,
                entity.Notes,
                entity.OpenBibleId,
                entity.Origin,
                credit,
                Datasets.Of(credit) ?? Datasets.Own,
                tally.References,
                tally.Mentions,
                tally.Disputed,
                [
                    .. names.Select(n => new EntityNameResponse(
                        n.Label,
                        n.Hebrew ?? Lemma(lemmas, n.HebrewStrongNumber)?.Lemma,
                        n.Hebrew is null ? Lemma(lemmas, n.HebrewStrongNumber)?.Transliteration : n.HebrewTransliterated,
                        n.Greek, n.GreekTransliterated,
                        n.Meaning, Numbers(n.HebrewStrongNumber), Numbers(n.GreekStrongNumber), n.Kind)
                    {
                        LocalLabel = n.Kind is { } kind && EntityNameTranslations.RenderedKinds.Contains(kind)
                            ? glosses.GetValueOrDefault(n.Label)
                            : null,
                        LocalMeaning = n.Meaning is { } meaning ? glosses.GetValueOrDefault(meaning) : null,
                        Printed = spelled.GetValueOrDefault(n.Label),
                    }),
                ],
                related,
                await InTheReadersLanguage(
                    db, [.. events.Select(one => Event(one, placesNamed))], language, words, cancellationToken),
                [
                    .. stated.Select(row => new EntityReferenceSourceResponse(
                        Datasets.Of(row.Source), row.Source, row.References, row.Mentions,
                        ReferenceMethods.Of(row.Source))),
                ],
                [
                    .. established.Select(c => new EntityClaimResponse(
                        EnumSpelling.Of(c.Method), c.Confidence, c.Source, Datasets.Of(c.Source))),
                ],
                alternatives,
                alternatives.Any(a => !a.Back))
            {
                // A person headed by another name is said by it in the rows of the page: John companion of Barnabas.
                Forms = renamed is null
                    ? own.GetValueOrDefault(entity.Slug)
                    : RenamedHeadings.Forms(renamed, own.GetValueOrDefault(entity.Slug), language ?? string.Empty) is { } headed
                        ? new Dictionary<string, string>(headed, StringComparer.Ordinal)
                        : null,
                TribeRecord = await TribeRecord(
                    db, entity.Tribe, language, cancellationToken, entity.Slug),
                Descriptor = described.GetValueOrDefault(entity.Slug),
                Location = entity.Location,
                Locations = locations,
                AnotherNameFor = await AnotherNameFor(db, entity.AnotherNameForEntityId, entity.Name, language, cancellationToken),
                BuiltOnSiteOf = await SharedSite(
                    db, SharedSites.All.FirstOrDefault(s => s.Later == entity.Slug)?.Earlier, language, cancellationToken),
                BuiltOnItsSite = await SharedSite(
                    db, SharedSites.All.FirstOrDefault(s => s.Earlier == entity.Slug)?.Later, language, cancellationToken),
                Wikipedia = await WikipediaLinks.Of(db, entity.Id, words, cancellationToken),
                LocalName = renamed is null ? localNames.GetValueOrDefault(entity.Id) : renamed.LocalName,
                AlsoCalled = renamed?.AlsoCalled,
                NameFromSource = (await EntityNames.NamedOnlyBySource(
                    db, [entity.Id], language, localNames, cancellationToken)).Contains(entity.Id),
                LocalDistinguisher = (await EntityDistinguishers.Of(db, [entity.Id], words, cancellationToken))
                    .GetValueOrDefault(entity.Id),
                Renderings = await Renderings(db, entity.Id, cancellationToken),
                Images = await ImageEndpoints.Of(
                    db, entity.Id, cancellationToken, settings.Is(SiteSettings.GeneratedImages), language),
                Bearers =
                [
                    .. bearers.Select(b => new EntityTitleResponse(
                        b.Slug, EnumSpelling.Of(b.Kind), b.Name, b.Distinguisher,
                        BookReferences.At(b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse)!, b.Note)
                    {
                        LocalDistinguisher = lines.GetValueOrDefault(b.Slug),
                        LocalName = counterpartNames.GetValueOrDefault(b.BearerEntityId),
                        LocalNote = TextTranslations.In(sourced, b.Note),
                    }),
                ],
                Titles =
                [
                    .. titles.Select(t => new EntityTitleResponse(
                        t.Slug, EnumSpelling.Of(t.Kind), t.Name, t.Distinguisher,
                        BookReferences.At(t.CanonicalBook, t.CanonicalChapter, t.CanonicalVerse)!, t.Note)
                    {
                        LocalDistinguisher = lines.GetValueOrDefault(t.Slug),
                        LocalName = counterpartNames.GetValueOrDefault(t.TitleEntityId),
                        LocalNote = TextTranslations.In(sourced, t.Note),
                    }),
                ],
                Subtype = entity.Subtype,
                LocalNotes = ownNotes ?? machineNotes,
                LocalNotesMachine = ownNotes is null && machineNotes is not null,
                LocalModernEquivalent = entity.Kind == EntityKind.Place
                    ? PlaceToday.Of(entity.ModernEquivalent, locations, english =>
                        TextTranslations.In(sourced, english)
                        ?? locations?.FirstOrDefault(l => l.Site == english)?.LocalSite)
                    : TextTranslations.In(sourced, entity.ModernEquivalent),
                Passages = [.. passages.Select(Passage)],
                Times =
                [
                    .. times.Select(o => new ObservanceTimeResponse(
                        o.Cycle, o.Month, o.Day, o.LastDay,
                        BookReferences.At(o.CanonicalBook, o.CanonicalChapter, o.CanonicalVerse)!, o.Note)),
                ],
                KnownFor = await KnownForClauses.Of(db, entity.Id, words, cancellationToken),
            });
        });
    }

    /// <summary>
    /// The Hebrew of each name that carries one Hebrew Strong number and no Hebrew of its own: the
    /// lexicon's headword for that number, and its transliteration. A name the register made from
    /// the lexicon is that entry's name, so its headword is the name as the Hebrew writes it.
    /// </summary>
    internal static async Task<Dictionary<string, (string Lemma, string? Transliteration)>> HebrewLemmas(
        AppDbContext db, IEnumerable<string?> numbers, CancellationToken cancellationToken)
    {
        var wanted = numbers.OfType<string>().Where(n => Numbers(n).Count == 1).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        return (await db.StrongEntries
                .Where(s => wanted.Contains(s.StrongNumber) && s.Lemma != null)
                .Select(s => new { s.StrongNumber, s.Lemma, s.Transliteration })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.StrongNumber, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().Lemma!, g.First().Transliteration), StringComparer.Ordinal);
    }

    private static (string Lemma, string? Transliteration)? Lemma(
        IReadOnlyDictionary<string, (string Lemma, string? Transliteration)> lemmas, string? number) =>
        number is not null && lemmas.TryGetValue(number, out var lemma) ? lemma : null;

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
