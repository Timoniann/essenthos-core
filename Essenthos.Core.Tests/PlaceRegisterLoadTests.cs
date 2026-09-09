using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The register meeting an encyclopedia that already holds places.
///
/// The four claims under test are the whole of what the design promises a reader. A place our own
/// derivation reaches keeps its address and stops being the gazetteer's; one it does not reach is
/// left exactly as it was, which is the honest half; a name the gazetteer never surveyed becomes a
/// record; and the link between the two is made on the name as it is spelled and never on a fold of
/// it, because a fold reaches further and puts <em>Zoan</em>'s coordinates on Sion.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PlaceRegisterLoadTests : IDisposable
{
    private const string Gazetteer =
        "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    private const string Ours = "Essenthos, from the place names Strong's Dictionary heads";

    private readonly AppDbContext _db;
    private readonly string _folder;

    public PlaceRegisterLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _folder = Path.Combine(Path.GetTempPath(), $"places-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);

        // What the gazetteer surveyed: a place the lexicon heads under a second spelling, one it
        // heads under a feature word the lexicon does not use, and one it heads nowhere.
        Held("beth-lehem", "Beth-lehem", "obi-1");
        Held("gilboa", "Mount Gilboa", "obi-2");
        Held("beautiful-gate", "Beautiful Gate", "obi-3");
        Held("zoan", "Zoan", "obi-4");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task A_place_our_derivation_reaches_keeps_its_slug_and_becomes_ours()
    {
        Register(
            Record("H1035", "Bethlehem", ["Bethlehem", "Bêyth Lechem"]),
            Record("H1533", "Gilboa", ["Gilboa"]));

        var outcome = await Load();

        outcome.Claimed.Should().Be(2);
        outcome.Added.Should().Be(0);

        var bethlehem = await Place("beth-lehem");
        bethlehem.Should().NotBeNull("the row stays and its address with it");
        bethlehem!.Name.Should().Be("Beth-lehem", "renaming a page is not what making it ours means");
        bethlehem.Source.Should().Be(Ours);
        bethlehem.OpenBibleId.Should().Be("obi-1", "the coordinates stay reachable and stay theirs");
        bethlehem.Claims.Should().Contain(claim =>
            claim.Source == Ours && claim.Method == LinkMethod.Lexical && claim.Confidence != null);
        bethlehem.Claims.Should().Contain(claim =>
            claim.Source == Gazetteer && claim.Method == LinkMethod.StatedBySource,
            "the credit moves to a claim rather than being dropped");
        bethlehem.Names.Should().Contain(name => name.HebrewStrongNumber == "H1035");

        var gilboa = await Place("gilboa");
        gilboa!.Source.Should().Be(Ours, "the gazetteer's feature word is a naming convention, not a name");
    }

    [Fact]
    public async Task A_place_our_derivation_does_not_reach_keeps_the_provenance_it_has()
    {
        Register(Record("H1035", "Bethlehem", ["Bethlehem"]));

        var outcome = await Load();

        outcome.Untouched.Should().Be(3);

        var gate = await Place("beautiful-gate");
        gate!.Source.Should().Be(Gazetteer);
        gate.Claims.Should().BeEmpty("nothing of ours reaches it, so nothing of ours is said about it");
        gate.Names.Should().BeEmpty();
    }

    [Fact]
    public async Task A_place_the_gazetteer_never_surveyed_is_added()
    {
        Register(Record("H66", "Abel-Maim", ["Abel-Maim", "Abel-maim"]));

        var outcome = await Load();

        outcome.Added.Should().Be(1);
        outcome.Claimed.Should().Be(0);

        var added = await Place("abelmaim");
        added.Should().NotBeNull();
        added!.Source.Should().Be(Ours);
        added.OpenBibleId.Should().BeNull("an unlinked record is an honest gap");
        added.Names.Should().ContainSingle().Which.HebrewStrongNumber.Should().Be("H66");
    }

    [Fact]
    public async Task The_link_is_made_on_the_spelling_and_never_on_a_fold_of_it()
    {
        // Folded to consonants *Sion* and *Zoan* are one word, which is how a gazetteer's
        // coordinates end up on a page about somewhere else.
        Register(Record("G4622", "Sion", ["Sion", "Zion"]));

        var outcome = await Load();

        outcome.Added.Should().Be(1);
        outcome.Linked.Should().Be(0);
        (await Place("zoan"))!.Source.Should().Be(Gazetteer);
        (await Place("sion"))!.Names.Should().ContainSingle()
            .Which.GreekStrongNumber.Should().Be("G4622");
    }

    [Fact]
    public async Task An_entry_the_register_refuses_writes_nothing()
    {
        Register(Record("H5892", "City", ["City"], kept: false));

        var outcome = await Load();

        outcome.Entries.Should().Be(1);
        outcome.Records.Should().Be(0);
        outcome.Added.Should().Be(0);
        (await _db.Entities.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Running_it_twice_writes_the_register_once()
    {
        Register(Record("H66", "Abel-Maim", ["Abel-Maim"]));

        await Load();
        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.Entities.CountAsync(e => e.Source == Ours)).Should().Be(1);
    }

    /// <summary>
    /// The spellings Strong prints and no gazetteer does. They are the only name a great many
    /// entries offer that a held label can be met by, so a fold that leaves any of them standing
    /// loses the match and adds a second page for a place already on one.
    /// </summary>
    [Theory]
    [InlineData("ʻIvvâh", "ivvah")]
    [InlineData("Baʻal Shâlishâh", "baalshalishah")]
    [InlineData("Akeldamá", "akeldama")]
    [InlineData("sýrtis", "syrtis")]
    [InlineData("Cenchreæ", "cenchreae")]
    [InlineData("Beth-lehem", "bethlehem")]
    [InlineData("Yᵉshanah", "yeshanah")]
    [InlineData("Bêyth Lechem", "beythlechem")]
    [InlineData("Ṭôwb", "towb")]
    [InlineData("Ĕdôwm", "edowm")]
    [InlineData("Çûwr", "cuwr")]
    public void A_lexicon_spelling_normalises_to_the_gazetteer_s(string printed, string expected) =>
        PlaceRegisterFiles.Normalise(printed).Should().Be(expected);

    private void Held(string slug, string name, string openBibleId) =>
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Place,
            Slug = slug,
            Name = name,
            SourceId = $"openbible:{openBibleId}",
            Source = Gazetteer,
            OpenBibleId = openBibleId,
        });

    private async Task<Entity?> Place(string slug) =>
        await _db.Entities
            .Include(e => e.Claims)
            .Include(e => e.Names)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Slug == slug);

    private static object Record(
        string number,
        string name,
        string[] names,
        bool kept = true) =>
        new
        {
            number,
            name,
            names,
            transliteration = name,
            definition = $"{name}, a place in Palestine",
            morphology = "n-pr-loc",
            kept,
            why = "the tag and the gloss both say place",
            tier = "agreed",
            witness = "topo alone",
            statedPlaces = (int?)null,
            person = false,
            reading = new { place = true, places = 1, person = false, why = "a place", model = "a test", askedAt = "2026-09-09" },
            check = (object?)null,
        };

    private void Register(params object[] records)
    {
        using var handle = new StreamWriter(Path.Combine(_folder, "register-0000.jsonl"));
        foreach (var record in records)
        {
            handle.WriteLine(JsonSerializer.Serialize(record));
        }
    }

    private Task<PlaceRegisterOutcome> Load() =>
        new PlaceRegisterLoader(
                _db,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [PlaceRegisterFiles.ConfigurationKey] = _folder,
                    })
                    .Build(),
                NullLogger<PlaceRegisterLoader>.Instance)
            .Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));
}
