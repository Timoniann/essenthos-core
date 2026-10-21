using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// What the own-record tests run against: every word a ruling the corpus ships names, written at the
/// address the ruling names and carrying its Strong number, and the records the rulings point at or
/// name as alternatives.
/// </summary>
internal sealed class OwnRecordWorld : IDisposable
{
    private OwnRecordWorld(AppDbContext db, IReadOnlyList<OwnRecordRuling> rulings, Dictionary<RuledWord, long> words, Text english)
    {
        Db = db;
        Rulings = rulings;
        Words = words;
        English = english;
    }

    public AppDbContext Db { get; }

    public IReadOnlyList<OwnRecordRuling> Rulings { get; }

    public Dictionary<RuledWord, long> Words { get; }

    public Text English { get; }

    /// <summary>What the load <see cref="Loaded"/> made reported.</summary>
    public OwnRecordOutcome? FirstLoad { get; private set; }

    /// <summary>The world, written afresh into the class's database over whatever the last test left.</summary>
    public static OwnRecordWorld Make(WitnessDatabase database)
    {
        var db = database.NewContext();
        db.Database.ExecuteSqlRaw("DELETE FROM text");
        db.Database.ExecuteSqlRaw("DELETE FROM entity");
        IReadOnlyList<OwnRecordRuling> rulings = [.. SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)];

        // The words the rulings are about, at the addresses the rulings name.
        var placed = db.Place(rulings.Select(ruling => ruling.Word));
        foreach (var ruling in rulings)
        {
            placed[ruling.Word].StrongNumber = ruling.StrongNumber;
            placed[ruling.Word].Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "pers"}""");
        }

        var english = Corpus.Add(db, "ENGLISH", TextKind.Translation, "eng", (1, 1, ["Azariah"]));
        db.SaveChanges();
        var words = placed.ToDictionary(word => word.Key, word => word.Value.Id);

        // The records the rulings point at or name as alternatives; a people ruled beside a town is
        // a people, and the town a place.
        var towns = rulings.Where(r => r.Alongside is not null && r.Alongside != "anointed").ToList();
        var places = towns.Select(r => r.Alongside!).ToHashSet(StringComparer.Ordinal);
        var peoples = towns.Select(r => r.Existing!).ToHashSet(StringComparer.Ordinal);
        foreach (var slug in Named(rulings))
        {
            db.Entities.Add(new Entity
            {
                Kind = slug == "anointed" ? EntityKind.Title
                    : places.Contains(slug) ? EntityKind.Place
                    : peoples.Contains(slug) ? EntityKind.People
                    : EntityKind.Person,
                Slug = slug,
                Name = slug,
                SourceId = slug,
                Source = "a test",
            });
        }

        db.SaveChanges();

        // Left tracking the tens of thousands of rows written here, every query and save the loader
        // makes compares them all, and a load that takes seconds took half a minute. The texts,
        // books and chapters stay tracked, which is where a test placing words of its own looks.
        db.ChangeTracker.Clear();
        db.Texts.Load();
        db.Books.Load();
        db.Chapters.Load();
        return new OwnRecordWorld(db, rulings, words, db.Texts.Local.Single(t => t.Id == english.Id));
    }

    /// <summary>
    /// The world with the rulings loaded into it once, for tests that only read the load; what
    /// <paramref name="arrange"/> writes is in the world before the load.
    /// </summary>
    public static async Task<OwnRecordWorld> Loaded(WitnessDatabase database, Func<OwnRecordWorld, Task>? arrange = null)
    {
        var world = Make(database);
        if (arrange is not null)
        {
            await arrange(world);
        }

        world.FirstLoad = await world.Load();
        return world;
    }

    public Task<OwnRecordOutcome> Load() =>
        new OwnRecordLoader(
                Db,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [OwnRecordLoader.BulkConfigurationKey] = "false",
                    })
                    .Build(),
                NullLogger<OwnRecordLoader>.Instance)
            .Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

    /// <summary>The next test empties the database before it writes its own world.</summary>
    public void Dispose() => Db.Dispose();

    /// <summary>Every record a ruling refers to and does not create.</summary>
    private static IEnumerable<string> Named(IReadOnlyList<OwnRecordRuling> rulings) =>
        rulings
            .SelectMany(r => new[] { r.Existing }
                .Concat(r.Alternatives?.Select(a => a.Slug) ?? []))
            .Where(slug => slug is not null)
            .Select(slug => slug!)
            .Where(slug => rulings.All(r => r.Create?.Slug != slug))
            .Distinct();
}
