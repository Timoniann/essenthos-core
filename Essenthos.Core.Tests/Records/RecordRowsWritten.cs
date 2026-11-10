using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The rows records-derive makes from files, written into a test's database the way the records load writes its files, so
/// a test can hold a page to them: the pictures (<see cref="EntityImageLoader.Pictures"/>) and the places' points and sites
/// (<see cref="OpenBibleLocationLoader.Locate"/>), each replacing what the table held.
/// </summary>
internal static class RecordRowsWritten
{
    public static async Task<EntityImageOutcome> Pictures(AppDbContext db, string resources)
    {
        var entities = await db.Entities
            .Select(e => new PicturedRecord(e.Id, e.Slug, e.Kind, e.SourceId, e.OpenBibleId))
            .ToListAsync();
        var merged = await db.MergedRecords.Select(m => new { m.Slug, m.EntityId }).ToListAsync();
        var (rows, outcome) = EntityImageLoader.Pictures(
            resources, entities, merged.Select(m => (m.Slug, m.EntityId)), NullLogger.Instance);
        await db.EntityImages.ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        db.EntityImages.AddRange(rows);
        await db.SaveChangesAsync();
        return outcome;
    }

    public static async Task Locations(AppDbContext db, string folder)
    {
        var supplement = LocationSupplement.Read();
        var read = supplement.Apply(OpenBibleLocationLoader.Read(Path.Combine(folder, "ancient.jsonl"), Path.Combine(folder, "modern.jsonl")));
        var regions = supplement.Regions.Select(r => r.Slug).ToList();
        var laterTowns = SharedSites.All.Select(s => s.Later).ToList();
        var places = await db.Entities
            .Where(e => e.Kind == EntityKind.Place
                        && (e.OpenBibleId != null || regions.Contains(e.Slug) || laterTowns.Contains(e.Slug)))
            .Select(e => new OpenBibleLocationLoader.Place(e.Id, e.Slug, e.OpenBibleId))
            .ToListAsync();
        var (locations, candidates, _) = OpenBibleLocationLoader.Locate(read, supplement, places);
        await db.PlaceCandidates.ExecuteDeleteAsync();
        await db.PlaceLocations.ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        db.PlaceLocations.AddRange(locations);
        db.PlaceCandidates.AddRange(candidates);
        await db.SaveChangesAsync();
    }
}
