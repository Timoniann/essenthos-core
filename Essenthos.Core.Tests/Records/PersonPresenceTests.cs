using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class PersonPresenceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PersonPresenceLoader _loader;
    private readonly Entity _person;
    private readonly Entity _place;

    public PersonPresenceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _person = Record("traveller", EntityKind.Person);
        _place = Record("hebron", EntityKind.Place);
        _db.SaveChanges();
        _loader = new PersonPresenceLoader(_db, new ThingLoader(_db, NullLogger<ThingLoader>.Instance));
    }

    public void Dispose() => _db.Dispose();

    private Entity Record(string slug, EntityKind kind)
    {
        var entity = new Entity { Slug = slug, Name = slug, Kind = kind, Source = "test", SourceId = "test:" + slug };
        _db.Entities.Add(entity);
        return entity;
    }

    private static PresenceRecord Finding(string category = "present", int[]? addresses = null, string place = "hebron") =>
        new("traveller", place, category, addresses ?? [10_005_005],
            JsonSerializer.Deserialize<JsonElement>("[{\"model\":\"claude-sonnet-5-5\",\"reason_uk\":\"working note\",\"evidence\":[{\"verse\":\"KJV:10:5:5\",\"quote\":\"in Hebron\"}]}]"));

    private static PresenceFile File(params PresenceRecord[] records) => new(1, "attributed model reading", records, []);

    private async Task<string[]> Fingerprint() => await _db.PersonPresences.AsNoTracking().OrderBy(p => p.Id)
        .Select(p => p.Id + "|" + p.PersonId + "|" + p.PlaceId + "|" + p.Category + "|" + p.Source + "|" + p.Evidence)
        .ToArrayAsync();

    [Fact]
    public async Task SharedMentionsAreNotVisitsAndContextWitnessesNeedNoSharedNamingVerse()
    {
        var mentioned = Record("future-destination", EntityKind.Place);
        _db.SaveChanges();
        foreach (var entity in new[] { _person, mentioned })
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = entity.Id, CanonicalBook = 1, CanonicalChapter = 12, CanonicalVerse = 1,
                Source = "test", Names = true,
            });
        await _db.SaveChangesAsync();
        (await EncyclopediaEndpoints.NamedPlaces(_db, _person.Id, null, null, 20)).Items.Should().Contain(p => p.Slug == mentioned.Slug);
        (await EncyclopediaEndpoints.StoryPlaces(_db, _person.Id, null, null, 20)).Total.Should().Be(0);
        await _loader.Load(File(Finding()));
        var result = await EncyclopediaEndpoints.StoryPlaces(_db, _person.Id, null, null, 20);
        result.Items.Select(p => p.Slug).Should().Equal("hebron");
        result.Items[0].Verses.Select(v => (v.BookOrdinal, v.Chapter, v.Verse)).Should().Equal((10, 5, 5));
        result.Items[0].Roles.Should().Equal("event");
    }

    [Fact]
    public async Task BurialIsKeptApartFromLivingPresence()
    {
        await _loader.Load(File(Finding("buried")));
        (await EncyclopediaEndpoints.StoryPlaces(_db, _person.Id, null, null, 20)).Items.Single().Roles.Should().Equal("buried");
        await _loader.Load(File(Finding(), Finding("buried")));
        (await EncyclopediaEndpoints.StoryPlaces(_db, _person.Id, null, null, 20)).Items.Single().Roles.Should().Equal("event", "buried");
    }

    [Fact]
    public async Task CitationsAreDistinctOrderedCappedAndMayIncludePsalmTitles()
    {
        await _loader.Load(File(Finding(addresses: [19_056_001, 19_056_000, 19_056_001])));
        var result = await EncyclopediaEndpoints.StoryPlaces(_db, _person.Id, null, null, 1);
        result.Items.Single().SharedVerses.Should().Be(2);
        result.Items.Single().Verses.Single().Verse.Should().Be(0);
        (await EncyclopediaEndpoints.StoryPlaces(_db, _person.Id, null, null, -1)).Items.Single().Verses.Should().BeEmpty();
    }

    [Fact]
    public async Task RepeatingTheFilePreservesRowIdsAndContent()
    {
        await _loader.Load(File(Finding()));
        var before = await Fingerprint();
        var again = await _loader.Load(File(Finding()));
        again.Written.Should().Be(0);
        again.Removed.Should().Be(0);
        (await Fingerprint()).Should().Equal(before);
    }

    [Fact]
    public async Task ADatabaseFailureAfterRemovalRollsBackThePreviousInventory()
    {
        await _loader.Load(File(Finding()));
        var before = await Fingerprint();
        _db.ChangeTracker.Clear();
        await _db.Database.ExecuteSqlRawAsync("CREATE FUNCTION refuse_presence() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'refused'; END $$; CREATE TRIGGER refuse_presence BEFORE INSERT ON person_presence FOR EACH ROW EXECUTE FUNCTION refuse_presence();");
        try
        {
            var act = () => _loader.Load(File(Finding("buried")));
            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_presence ON person_presence; DROP FUNCTION refuse_presence();");
        }
        _db.ChangeTracker.Clear();
        (await Fingerprint()).Should().Equal(before);
    }

    [Fact]
    public async Task UnresolvedVerdictsDuplicatesAndUnknownIdentitiesCannotReplaceData()
    {
        await _loader.Load(File(Finding()));
        var before = await Fingerprint();
        foreach (var refused in new[] { File(Finding("uncertain")), File(Finding(), Finding()), File(Finding(place: "unknown")) })
        {
            var act = () => _loader.Load(refused);
            await act.Should().ThrowAsync<InvalidDataException>();
            (await Fingerprint()).Should().Equal(before);
        }
    }

    [Fact]
    public async Task MissingOptionalDatasetDoesNotEraseLoadedFindings()
    {
        await _loader.Load(File(Finding()));
        var before = await Fingerprint();
        await _loader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), CancellationToken.None);
        (await Fingerprint()).Should().Equal(before);
    }

    [Fact]
    public async Task WorkingNotesAndModelEvidenceDoNotReachTheReaderResponse()
    {
        await _loader.Load(File(Finding()));
        var result = await EncyclopediaEndpoints.StoryPlaces(_db, _person.Id, null, null, 20);
        var json = JsonSerializer.Serialize(result);
        json.Should().NotContain("working note").And.NotContain("reason_uk").And.NotContain("claude-sonnet");
        (await _db.PersonPresences.SingleAsync()).Evidence.Should().Contain("working note");
    }

    [Fact]
    public async Task FoldedPlaceAddressesCombineEvidenceUnderTheKeptRecord()
    {
        _db.MergedRecords.Add(new MergedRecord
        {
            Slug = "old-hebron", EntityId = _place.Id, Name = "Old Hebron",
            RecordSource = "test", RecordSourceId = "test:old-hebron", Source = "test", Reason = "same place",
        });
        await _db.SaveChangesAsync();
        await _loader.Load(File(Finding(), Finding(place: "old-hebron", addresses: [10_005_006])));
        (await _db.PersonPresences.CountAsync()).Should().Be(1);
        (await _db.PersonPresences.SingleAsync()).Addresses.Should().Equal(10_005_005, 10_005_006);
        (await _loader.Load(File(Finding(), Finding(place: "old-hebron", addresses: [10_005_006])))).Written.Should().Be(0);
    }

    [Theory]
    [InlineData("PSA 63:0", true)]
    [InlineData("PSA 63:0-1", true)]
    [InlineData("GEN 1:0", false)]
    [InlineData("PSA 63:-1", false)]
    public void PsalmTitlesRetainTheirCanonicalZeroVerse(string reference, bool valid) =>
        (ScriptureSpan.TryParse(reference) is not null).Should().Be(valid);

    [Fact]
    [Trait("Category", "Corpus")]
    public async Task TheEntireAdmittedFileCreatesItsSitesAndReplaysWithoutRewriting()
    {
        var path = TestResources.Shipped("Essenthos/person-presence.json");
        System.IO.File.Exists(path).Should().BeTrue("the admitted presence dataset must be published before this proof");
        var file = JsonSerializer.Deserialize<PresenceFile>(await System.IO.File.ReadAllTextAsync(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        file.Sites.Single(s => s.Slug == "wilderness-of-judah").Passages!.Select(p => p.Reference).Should().Contain("PSA 63:0");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM entity");
        _db.ChangeTracker.Clear();
        var people = file.Records.Select(r => r.Person).Distinct().ToHashSet();
        var places = file.Records.Select(r => r.Place).Distinct().ToHashSet();
        people.Intersect(places).Should().BeEmpty();
        foreach (var slug in people) Record(slug, EntityKind.Person);
        foreach (var slug in places) Record(slug, EntityKind.Place);
        await _db.SaveChangesAsync();
        var outcome = await _loader.Load(file);
        outcome.Total.Should().Be(1947);
        (await _db.Entities.CountAsync(e => e.SourceId.StartsWith("essenthos:thing:"))).Should().Be(18);
        (await _db.PersonPresences.CountAsync(p => p.Category == "buried")).Should().Be(69);
        var before = await Fingerprint();
        var repeated = await _loader.Load(file);
        repeated.Written.Should().Be(0);
        repeated.Removed.Should().Be(0);
        (await Fingerprint()).Should().Equal(before);
    }
}
