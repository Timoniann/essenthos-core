using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A load that fails while it writes the places' answers leaves the points and the answers it found
/// as they were: the two are replaced in one transaction, committed or not at all. Its own class,
/// because the load commits here as it does on the corpus, and the class's database is its own.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlaceCandidateReplacementTests(WitnessDatabase database) : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"openbible-replacement-{Guid.NewGuid():n}")).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task AFailedReplacementKeepsThePreviousPointsAndAnswers()
    {
        await using var db = database.NewContext();
        db.Entities.Add(new Entity
        {
            Kind = EntityKind.Place,
            Slug = "tekoa",
            Name = "Tekoa",
            OpenBibleId = "a1",
            SourceId = "test:a1",
            Source = "test",
        });
        await db.SaveChangesAsync();
        var loader = new OpenBibleLocationLoader(db, NullLogger<OpenBibleLocationLoader>.Instance);

        Write("m-first", 600, "35.2,31.7");
        (await loader.Load(_folder)).Located.Should().Be(1);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE FUNCTION refuse_candidate() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'refused'; END $$;
            CREATE TRIGGER refuse_candidate BEFORE INSERT ON place_candidate
            FOR EACH ROW EXECUTE FUNCTION refuse_candidate();
            """);
        try
        {
            Write("m-second", 700, "35.3,31.8");
            db.ChangeTracker.Clear();
            var replacing = () => loader.Load(_folder);
            await replacing.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER refuse_candidate ON place_candidate; DROP FUNCTION refuse_candidate();");
        }

        db.ChangeTracker.Clear();
        (await db.PlaceLocations.SingleAsync()).ModernId.Should().Be("m-first");
        (await db.PlaceCandidates.SingleAsync()).ModernId.Should().Be("m-first");
    }

    private void Write(string modern, int score, string lonLat)
    {
        File.WriteAllText(Path.Combine(_folder, "ancient.jsonl"),
            "{\"id\":\"a1\",\"identifications\":[{\"id_source\":\"modern\",\"resolutions\":[{\"lonlat\":\"" + lonLat
            + "\",\"lonlat_type\":\"point\",\"modern_basis_id\":\"" + modern + "\"}]}],\"modern_associations\":{\""
            + modern + "\":{\"identification_ids\":[[0,0]],\"score\":" + score + "}}}");
        File.WriteAllText(Path.Combine(_folder, "modern.jsonl"),
            $$$"""{"id":"{{{modern}}}","coordinates_source":{"type":"wikidata"}}""");
    }
}
