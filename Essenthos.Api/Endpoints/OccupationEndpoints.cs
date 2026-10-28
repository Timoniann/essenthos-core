using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal sealed record OccupationSummaryResponse(string Slug, string Category, string Name, string? Description, int PersonCount);
internal sealed record OccupationCategoryResponse(string Category, int OccupationCount, int PersonCount);
internal sealed record OccupationCatalogueResponse(int Total, int Skip, int Take, IList<OccupationSummaryResponse> Items,
    IList<OccupationCategoryResponse> Categories);
internal sealed record OccupationWitnessResponse(VerseRefResponse Reference, string Kind, string Text, string Quote);
internal sealed record OccupationPractitionerResponse(EntitySummaryResponse Person, string RoleName,
    IList<VerseRefResponse> Verses, IList<OccupationWitnessResponse> Witnesses);
internal sealed record OccupationDetailResponse(OccupationSummaryResponse Occupation, int TotalPeople, int Skip, int Take,
    IList<OccupationPractitionerResponse> People);
internal sealed record PersonOccupationResponse(OccupationSummaryResponse Occupation, string RoleName,
    IList<VerseRefResponse> Verses, IList<OccupationWitnessResponse> Witnesses);
internal sealed record PersonOccupationsResponse(string PersonSlug, int Total, IList<PersonOccupationResponse> Items)
{
    /// <summary>Who they are, named as the chosen text names them, for a client that has only the slug.</summary>
    public EntitySummaryResponse? Person { get; init; }
}

internal static class OccupationEndpoints
{
    private static readonly string[] Categories = ["trade", "office", "military_role", "religious_role", "heavenly_role", "designation"];
    private const int MostPerPage = 200;

    public static void MapOccupations(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/occupations", async ([FromQuery] string? language, [FromQuery] string? prose,
            [FromQuery] string? q, [FromQuery] string? category, [FromQuery] int? skip, [FromQuery] int? take,
            AppDbContext db, CancellationToken cancellationToken) =>
        {
            if (category is { Length: > 0 } && !Categories.Contains(category))
                return Results.BadRequest(new ProblemResponse("Unknown occupation category."));
            if (q?.Length > 120)
                return Results.BadRequest(new ProblemResponse("Occupation search is limited to 120 characters."));
            return Results.Ok(await Catalogue(db, language, prose, q, category, skip, take, cancellationToken));
        });
        routes.MapGet("/occupations/{slug}", async (string slug, [FromQuery] string? language,
            [FromQuery] string? prose, [FromQuery] int? skip, [FromQuery] int? take,
            AppDbContext db, CancellationToken cancellationToken) =>
        {
            var result = await Detail(db, slug, language, prose, skip, take, cancellationToken);
            return result is null ? ApiResults.NotFound("This occupation was not found.") : Results.Ok(result);
        });
        routes.MapGet("/entities/{slug}/occupations", async (string slug, [FromQuery] string? language,
            [FromQuery] string? prose, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var result = await Person(db, slug, language, prose, cancellationToken);
            return result is null ? ApiResults.NotFound("This person was not found.") : Results.Ok(result);
        });
    }

    private static string Words(string? language, string? prose) => ReaderLanguages.Prose(prose, language)?.Trim() switch
    {
        "uk" or "ukr" => "ukr", "de" or "deu" => "deu", "es" or "spa" => "spa", _ => "eng",
    };

    private static IQueryable<OccupationLabel> Labels(AppDbContext db, string language) =>
        db.OccupationLabels.AsNoTracking().Where(l => l.Language == language && l.Occupation!.People.Any());

    private static IQueryable<OccupationSummaryResponse> Summaries(IQueryable<OccupationLabel> labels) =>
        labels.Select(l => new OccupationSummaryResponse(l.Occupation!.Slug, l.Occupation.Category,
            l.Name, l.Description, l.Occupation.People.Select(p => p.PersonId).Distinct().Count()));

    internal static async Task<OccupationCatalogueResponse> Catalogue(AppDbContext db, string? language = null,
        string? prose = null, string? q = null, string? category = null, int? skip = null, int? take = null,
        CancellationToken cancellationToken = default)
    {
        var labels = Labels(db, Words(language, prose));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var typed = q.Trim().ToLowerInvariant();
            labels = labels.Where(l => l.Name.ToLower().Contains(typed) || l.Masculine.ToLower().Contains(typed)
                || l.Feminine.ToLower().Contains(typed));
        }
        var categories = await labels.GroupBy(l => l.Occupation!.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Category, cancellationToken);
        var occupationIds = labels.Select(l => l.OccupationId);
        var personCounts = await db.PersonOccupations.Where(p => occupationIds.Contains(p.OccupationId))
            .GroupBy(p => p.Occupation!.Category)
            .Select(g => new { Category = g.Key, Count = g.Select(p => p.PersonId).Distinct().Count() })
            .ToDictionaryAsync(g => g.Category, cancellationToken);
        if (!string.IsNullOrWhiteSpace(category)) labels = labels.Where(l => l.Occupation!.Category == category);
        var offset = Math.Max(0, skip ?? 0);
        var limit = Math.Clamp(take ?? 40, 1, MostPerPage);
        return new(await labels.CountAsync(cancellationToken), offset, limit,
            await Summaries(labels.OrderBy(l => l.Name.ToLower()).ThenBy(l => l.Occupation!.Slug))
                .Skip(offset).Take(limit).ToListAsync(cancellationToken),
            Categories.Select(c => new OccupationCategoryResponse(c, categories.GetValueOrDefault(c)?.Count ?? 0,
                personCounts.GetValueOrDefault(c)?.Count ?? 0)).ToList());
    }

    internal static async Task<OccupationDetailResponse?> Detail(AppDbContext db, string slug, string? language = null,
        string? prose = null, int? skip = null, int? take = null, CancellationToken cancellationToken = default)
    {
        var label = await Labels(db, Words(language, prose)).Include(l => l.Occupation)
            .SingleOrDefaultAsync(l => l.Occupation!.Slug == slug, cancellationToken);
        if (label is null) return null;
        var offset = Math.Max(0, skip ?? 0);
        var limit = Math.Clamp(take ?? 40, 1, MostPerPage);
        var people = db.Entities.Where(e => db.PersonOccupations.Any(p => p.PersonId == e.Id && p.OccupationId == label.OccupationId));
        var named = await EntityNames.Alphabetical(EntityNames.Localised(db, people, language), language)
            .ThenBy(p => p.Entity.Slug).Skip(offset).Take(limit)
            .Select(p => new { p.Entity.Id, p.Entity.Slug, p.Entity.Sex, p.LocalName }).ToListAsync(cancellationToken);
        var ids = named.Select(p => p.Id).ToList();
        var summaries = await db.Entities.Where(e => ids.Contains(e.Id)).Select(EncyclopediaEndpoints.Summary)
            .ToDictionaryAsync(e => e.Slug, cancellationToken);
        var renamed = await RenamedHeadings.Of(db, ids, language, cancellationToken);
        var lines = await Descriptors.Of(db, named.Select(p => p.Slug), ReaderLanguages.Prose(prose, language), cancellationToken, language);
        var sexes = await OursOnlyRecords.SexesOf(db, ids, cancellationToken);
        var assertions = await db.PersonOccupations.AsNoTracking().Where(p => p.OccupationId == label.OccupationId && ids.Contains(p.PersonId))
            .Include(p => p.Witnesses).ToDictionaryAsync(p => p.PersonId, cancellationToken);
        var total = await people.CountAsync(cancellationToken);
        return new(new(label.Occupation!.Slug, label.Occupation.Category, label.Name, label.Description, total), total, offset, limit,
            named.Select(p =>
            {
                var summary = summaries[p.Slug] with { LocalName = p.LocalName, Descriptor = lines.GetValueOrDefault(p.Slug) };
                if (renamed.TryGetValue(p.Id, out var heads))
                    summary = summary with { Name = heads.Name, LocalName = heads.LocalName, AlsoCalled = heads.AlsoCalled };
                var evidence = Witnesses(assertions[p.Id]);
                return new OccupationPractitionerResponse(summary, Role(label, p.Sex ?? sexes.GetValueOrDefault(p.Id)),
                    Verses(evidence), evidence);
            }).ToList());
    }

    internal static async Task<PersonOccupationsResponse?> Person(AppDbContext db, string slug, string? language = null,
        string? prose = null, CancellationToken cancellationToken = default)
    {
        var current = await MergedAddresses.Current(db, slug, cancellationToken);
        var person = await db.Entities.Where(e => e.Slug == current && e.Kind == EntityKind.Person)
            .Select(e => new { e.Id, e.Slug, e.Sex }).SingleOrDefaultAsync(cancellationToken);
        if (person is null) return null;
        var summary = await db.Entities.Where(e => e.Id == person.Id).Select(EncyclopediaEndpoints.Summary).SingleAsync(cancellationToken);
        var local = await EntityNames.Localised(db, db.Entities.Where(e => e.Id == person.Id), language)
            .Select(p => p.LocalName).FirstOrDefaultAsync(cancellationToken);
        summary = summary with { LocalName = local };
        if ((await RenamedHeadings.Of(db, [person.Id], language, cancellationToken)).TryGetValue(person.Id, out var heads))
            summary = summary with { Name = heads.Name, LocalName = heads.LocalName, AlsoCalled = heads.AlsoCalled };
        var sex = person.Sex ?? (await OursOnlyRecords.SexesOf(db, [person.Id], cancellationToken)).GetValueOrDefault(person.Id);
        var words = Words(language, prose);
        var rows = await db.PersonOccupations.AsNoTracking().Where(p => p.PersonId == person.Id)
            .Include(p => p.Witnesses).Include(p => p.Occupation).ThenInclude(o => o!.Labels)
            .OrderBy(p => p.Occupation!.Slug).ToListAsync(cancellationToken);
        var ids = rows.Select(p => p.OccupationId).ToList();
        var totals = await db.PersonOccupations.Where(p => ids.Contains(p.OccupationId)).GroupBy(p => p.OccupationId)
            .Select(g => new { Id = g.Key, Count = g.Select(p => p.PersonId).Distinct().Count() })
            .ToDictionaryAsync(g => g.Id, cancellationToken);
        return new(person.Slug, rows.Count, [.. rows.Select(p =>
        {
            var label = p.Occupation!.Labels.Single(l => l.Language == words);
            var evidence = Witnesses(p);
            return new PersonOccupationResponse(new(p.Occupation.Slug, p.Occupation.Category, label.Name, label.Description,
                totals[p.OccupationId].Count), Role(label, sex), Verses(evidence), evidence);
        }).OrderBy(p => Array.IndexOf(Categories, p.Occupation.Category)).ThenBy(p => p.Occupation.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Occupation.Slug, StringComparer.Ordinal)]) { Person = summary };
    }

    private static string Role(OccupationLabel label, string? sex) =>
        sex?.ToLowerInvariant() is "female" or "f" or "woman" ? label.Feminine : label.Masculine;

    private static List<OccupationWitnessResponse> Witnesses(PersonOccupation assertion) => assertion.Witnesses
        .OrderBy(w => w.CanonicalBook).ThenBy(w => w.CanonicalChapter).ThenBy(w => w.CanonicalVerse)
        .ThenBy(w => w.Kind, StringComparer.Ordinal).ThenBy(w => w.Text, StringComparer.Ordinal).ThenBy(w => w.Quote, StringComparer.Ordinal)
        .Select(w => new OccupationWitnessResponse(BookReferences.At(w.CanonicalBook, w.CanonicalChapter, w.CanonicalVerse)!, w.Kind, w.Text, w.Quote))
        .Distinct().ToList();

    private static List<VerseRefResponse> Verses(IList<OccupationWitnessResponse> evidence) =>
        evidence.Where(w => w.Kind == "profession").Select(w => w.Reference).Distinct().ToList();
}
