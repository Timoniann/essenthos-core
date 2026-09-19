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
/// The register meeting an encyclopedia that already holds people of these names.
///
/// The fixture is the Zimri group as the corpus actually holds it, because every claim worth testing
/// is in it: three men called Zimri, a fourth the dataset files under Zabdi and Chronicles calls
/// Zimri, and Jehu — whom Jezebel calls <em>Zimri</em> once, so forty-eight of his verses are in the
/// group's occurrence list and none of them are about anybody of that name.
///
/// What the design promises a reader is five things. A bearer whose verses meet a held man's keeps
/// that man's address and stops being the dataset's; a bearer nobody holds is added with the verses
/// that print his name; a held namesake nothing reaches is left exactly as it was; a bearer nothing
/// but the enumeration establishes says so on its claim and cites nothing; and a verse that does not
/// print the name is never written as a reference, whatever a dataset attributes it to.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PersonRegisterLoadTests : IDisposable
{
    private const string Dataset =
        "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private const string Ours =
        "Essenthos, from the bearers Strong's Dictionary enumerates under the name";

    private readonly AppDbContext _db;
    private readonly string _folder;

    public PersonRegisterLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _folder = Path.Combine(Path.GetTempPath(), $"persons-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);

        Held("zimri", "Zimri", "Zimri_1", ["Zimri"], [(4, 25, 14)]);
        Held("zimri-2", "Zimri", "Zimri_2", ["Zimri"],
            [(11, 16, 9), (11, 16, 10), (11, 16, 15), (12, 9, 31)]);
        Held("zabdi", "Zabdi", "Zabdi_1", ["Zabdi", "Zimri"],
            [(6, 7, 1), (6, 7, 17), (13, 2, 6)]);
        Held("jehu-2", "Jehu", "Jehu_2", ["Jehu", "Zimri"], [(12, 9, 14), (12, 9, 31)]);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task A_bearer_whose_verses_meet_a_held_man_keeps_his_slug_and_becomes_ours()
    {
        Register(
            Bearer(1, "Son of Salu, the Simeonite chief", ["NUM 25:14"]),
            Bearer(2, "Fifth king of Israel, who killed Elah",
                ["1KI 16:9", "1KI 16:10", "1KI 16:15", "2KI 9:31"]));

        var outcome = await Load();

        outcome.Claimed.Should().Be(2);
        outcome.Added.Should().Be(0);

        var king = await Person("zimri-2");
        king.Should().NotBeNull("the row stays and its address with it");
        king!.Source.Should().Be(Ours);
        king.SourceId.Should().Be("Zimri_2", "a corrected upstream record still has to be findable");
        king.Claims.Should().Contain(claim =>
            claim.Source == Ours && claim.Method == LinkMethod.ModelReading && claim.Confidence != null);
        king.Claims.Should().Contain(claim =>
            claim.Source == Dataset && claim.Method == LinkMethod.StatedBySource,
            "the dataset's testimony moves to a claim rather than being dropped");
    }

    [Fact]
    public async Task A_bearer_the_dataset_files_under_another_name_reaches_that_record()
    {
        // Chronicles calls him Zimri and BibleData files him as Zabdi. He is one man with two
        // spellings, and a register that added a second page for him would be making the namesake
        // problem worse rather than settling it.
        Register(Bearer(3, "Son of Zerah, grandson of Judah", ["1CH 2:6"], ["JOS 7:1", "JOS 7:17"]));

        var outcome = await Load();

        outcome.Claimed.Should().Be(1);
        outcome.Added.Should().Be(0);
        (await Person("zabdi"))!.Source.Should().Be(Ours);
    }

    [Fact]
    public async Task A_held_man_gains_the_verse_this_reading_gives_him_and_keeps_the_ones_he_had()
    {
        // 1 Chronicles 9:37 calls him Zechariah where 1 Chronicles 8:31 calls him Zecher, and the
        // dataset has neither on the king. A pass about which Zechariah a verse is about that left
        // every reference where it found it would have changed nothing a reader can see.
        Register(Bearer(2, "Fifth king of Israel", ["1KI 16:9", "1KI 16:20"]));

        var outcome = await Load();

        outcome.Claimed.Should().Be(1);
        outcome.Referenced.Should().Be(1, "only the verse the record did not already hold");

        var king = await Person("zimri-2");
        king!.Verses.Should().HaveCount(5);
        king.Verses.Should().ContainSingle(verse => verse.CanonicalChapter == 16 && verse.CanonicalVerse == 20)
            .Which.Source.Should().Contain("person split", "ours stands beside theirs and says so");
        king.Verses.Count(verse => verse.Source == Dataset).Should().Be(4,
            "a row a dataset wrote stays where it is");
    }

    [Fact]
    public async Task One_man_two_names_reach_is_one_row_and_one_claim_naming_both()
    {
        // Zimri son of Zerah and Zabdi father of Carmi are one man, and both names are namesake
        // groups of their own. Letting a row be claimed once altogether rather than once per name
        // gives the second group a page of its own for somebody already on one — the namesake
        // problem made worse rather than settled.
        Register(
            Bearer(3, "Son of Zerah, grandson of Judah", ["1CH 2:6"]),
            Bearer(1, "Father of Carmi, whose son Achan took the accursed thing", ["JOS 7:1"],
                group: "Zabdi", name: "Zabdi"));

        var outcome = await Load();

        outcome.Records.Should().Be(2);
        outcome.Claimed.Should().Be(1, "two records, one man, one row");
        outcome.Added.Should().Be(0);

        var claim = (await Person("zabdi"))!.Claims.Should()
            .ContainSingle(claim => claim.Source == Ours).Subject;
        claim.Note.Should().Contain("Zimri #3").And.Contain("Zabdi #1",
            "a reader is owed both names the record was reached by");
    }

    [Fact]
    public async Task A_namesake_nothing_of_ours_reaches_keeps_the_provenance_it_has()
    {
        // Jehu is in this group because Jezebel calls him Zimri once. Two of his verses are in the
        // occurrence list; neither is assigned to anybody of that name.
        Register(Bearer(2, "Fifth king of Israel", ["1KI 16:9", "2KI 9:31"]));

        await Load();

        var jehu = await Person("jehu-2");
        jehu!.Source.Should().Be(Dataset);
        jehu.Claims.Should().BeEmpty("nothing of ours reaches him, so nothing of ours is said of him");
    }

    [Fact]
    public async Task A_record_this_corpus_wrote_for_itself_is_reached_though_it_carries_no_name_row()
    {
        // OwnRecordLoader writes the people a verse names and no dataset holds. A record of that
        // kind reaches this pass before its own name row does — it is written a step later on a cold
        // load — so a group built out of `entity_name` cannot see it, and the register would add a
        // second page for a man it already holds.
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person,
            Slug = "zimri-the-charioteer",
            Name = "Zimri",
            SourceId = "essenthos:zimri-the-charioteer",
            Source = "Essenthos, on the project owner's ruling",
            Verses = [new EntityVerse { CanonicalBook = 11, CanonicalChapter = 16, CanonicalVerse = 20, Source = "Essenthos" }],
        });
        _db.SaveChanges();

        Register(Bearer(2, "Captain of half Elah's chariots", ["1KI 16:20"]));

        var outcome = await Load();

        outcome.Claimed.Should().Be(1);
        outcome.Added.Should().Be(0, "a record with no name row is still a record of this man");
        (await Person("zimri-the-charioteer"))!.Source.Should().Be(Ours);
    }

    [Fact]
    public async Task A_bearer_nobody_holds_is_added_with_the_verses_that_print_his_name()
    {
        Register(Bearer(4, "Son of Jehoadah, a descendant of Saul",
            ["1CH 8:36", "1CH 9:42"], ["1CH 8:35"]));

        var outcome = await Load();

        outcome.Added.Should().Be(1);
        outcome.Claimed.Should().Be(0);
        outcome.Referenced.Should().Be(2);

        var added = await Person("zimri-3");
        added.Should().NotBeNull();
        added!.Source.Should().Be(Ours);
        added.Distinguisher.Should().Be("Son of Jehoadah, a descendant of Saul");
        added.Names.Should().ContainSingle().Which.HebrewStrongNumber.Should().Be("H2174");
        added.Verses.Select(verse => (verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse))
            .Should().BeEquivalentTo([(13, 8, 36), (13, 9, 42)]);
        added.Verses.Should().NotContain(verse => verse.CanonicalVerse == 35,
            "a verse that does not print the name is context, and citing it would show a reader a "
            + "page whose reference does not hold the person");
    }

    [Fact]
    public async Task A_bearer_the_enumeration_alone_establishes_says_so_and_cites_nothing()
    {
        Register(Bearer(5, "An obscure name among the mingled people of Jeremiah", [],
            standing: "lexicon", why: "a second reading upholds the item as a man"));

        var outcome = await Load();

        outcome.Added.Should().Be(1);
        outcome.OnTheLexicon.Should().Be(1);
        outcome.OnAVerse.Should().Be(0);
        outcome.Referenced.Should().Be(0);

        var added = await Person("zimri-3");
        added!.Verses.Should().BeEmpty("no verse of this corpus tells him from his namesakes");
        var claim = added.Claims.Should().ContainSingle().Subject;
        claim.Confidence.Should().BeLessThan(0.9,
            "a split nothing we hold makes is not as well established as one a verse makes");
        claim.Note.Should().Contain("no verse of this corpus tells him from his namesakes");
    }

    [Fact]
    public async Task Two_bearers_cannot_both_be_one_held_man()
    {
        Register(
            Bearer(2, "Fifth king of Israel", ["1KI 16:9", "1KI 16:10", "1KI 16:15"]),
            Bearer(6, "Somebody else the same verse names", ["1KI 16:9"]));

        var outcome = await Load();

        outcome.Claimed.Should().Be(1, "the row goes to whoever shares the most of it");
        outcome.Added.Should().Be(1);
        (await Person("zimri-2"))!.Distinguisher.Should().BeNull("a claimed row is not rewritten");
    }

    [Fact]
    public async Task A_bearer_the_register_refuses_writes_nothing()
    {
        Register(Bearer(5, "The mingled people of Jeremiah 25:25", [],
            kept: false, standing: null, why: "a second reading refuses it: a people, not a man"));

        var outcome = await Load();

        outcome.Entries.Should().Be(1);
        outcome.Records.Should().Be(0);
        outcome.Added.Should().Be(0);
        (await _db.Entities.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task A_verse_the_dataset_files_under_the_wrong_philip_moves_to_the_tetrarch()
    {
        // BibleData holds Herodias's first husband and Philip the tetrarch as one man, with Luke 3:1,
        // the tetrarch's title and a note calling him the tetrarch. The register tells them apart and
        // adds the tetrarch; what the dataset says at Luke 3:1 is about him and goes with him.
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person,
            Slug = "philip-2",
            Name = "Philip",
            Distinguisher = "brother of Herod (MAT 14:3)",
            Notes = "Philip the Tetrarch of Iturea and Trachonitis",
            SourceId = "person:Philip_2",
            Source = Dataset,
            Names =
            [
                new EntityName { Label = "Philip", Kind = "proper name" },
                new EntityName { Label = "Tetrarch of Ituraea and Trachonitis", Kind = "title" },
            ],
            Verses =
            [
                new EntityVerse { CanonicalBook = 40, CanonicalChapter = 14, CanonicalVerse = 3, Label = "Philip", Source = Dataset },
                new EntityVerse { CanonicalBook = 41, CanonicalChapter = 6, CanonicalVerse = 17, Label = "Philip", Source = Dataset },
                new EntityVerse { CanonicalBook = 42, CanonicalChapter = 3, CanonicalVerse = 1, Label = "Philip", Source = Dataset },
                new EntityVerse
                {
                    CanonicalBook = 42, CanonicalChapter = 3, CanonicalVerse = 1,
                    Label = "Tetrarch of Ituraea and Trachonitis", Source = Dataset,
                },
            ],
        });
        await _db.SaveChangesAsync();

        Register(
            Bearer(3, "Son of Herod the Great, tetrarch of Ituraea and Trachonitis", ["LUK 3:1"],
                group: "Philip", name: "Philip"),
            Bearer(4, "Son of Herod the Great, first husband of Herodias", ["MAT 14:3", "MRK 6:17"],
                group: "Philip", name: "Philip"));

        var outcome = await Load();

        outcome.Claimed.Should().Be(1);
        outcome.Added.Should().Be(1);

        var husband = await Person("philip-2");
        husband!.Verses.Should().NotContain(verse => verse.CanonicalBook == 42,
            "Luke 3:1 names the tetrarch, not Herodias's husband");
        husband.Names.Select(name => name.Label).Should().BeEquivalentTo(["Philip"]);
        husband.Notes.Should().BeNull();

        var tetrarch = await _db.Entities
            .Include(e => e.Claims).Include(e => e.Names).Include(e => e.Verses)
            .AsSplitQuery()
            .SingleAsync(e => e.SourceId == "essenthos:philip3");
        tetrarch.Verses.Where(verse => verse.Source == Dataset)
            .Select(verse => verse.Label)
            .Should().BeEquivalentTo(["Philip", "Tetrarch of Ituraea and Trachonitis"]);
        tetrarch.Names.Should().Contain(name => name.Label == "Tetrarch of Ituraea and Trachonitis");
        tetrarch.Notes.Should().Be("Philip the Tetrarch of Iturea and Trachonitis");
        tetrarch.Claims.Should().Contain(claim =>
            claim.Source == Dataset && claim.Method == LinkMethod.StatedBySource
            && claim.Note!.Contains("person:Philip_2"));
    }

    [Fact]
    public async Task Running_it_twice_writes_the_register_once()
    {
        Register(Bearer(4, "Son of Jehoadah", ["1CH 8:36"]));

        await Load();
        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.Entities.CountAsync(e => e.Source == Ours)).Should().Be(1);
    }

    private void Held(
        string slug,
        string name,
        string sourceId,
        string[] labels,
        (int Book, int Chapter, int Verse)[] verses) =>
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person,
            Slug = slug,
            Name = name,
            SourceId = sourceId,
            Source = Dataset,
            Names = labels
                .Select(label => new EntityName { Label = label, Kind = "proper name" })
                .ToList(),
            Verses = verses
                .Select(address => new EntityVerse
                {
                    CanonicalBook = address.Book,
                    CanonicalChapter = address.Chapter,
                    CanonicalVerse = address.Verse,
                    Source = Dataset,
                })
                .ToList(),
        });

    private async Task<Entity?> Person(string slug) =>
        await _db.Entities
            .Include(e => e.Claims)
            .Include(e => e.Names)
            .Include(e => e.Verses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Slug == slug);

    private static object Bearer(
        int id,
        string description,
        string[] references,
        string[]? otherReferences = null,
        bool kept = true,
        string? standing = "verse",
        string? why = null,
        string group = "Zimri",
        string name = "Zimri") =>
        new
        {
            group,
            id,
            key = $"{group}#{id}",
            name,
            description,
            kind = "person",
            kept,
            standing,
            why = why ?? $"{references.Length} verses of this corpus print the name",
            confidence = "high",
            sameAs = (int?)null,
            enumerated = id,
            enumeration = description,
            strongNumbers = new[] { "H2174" },
            references,
            otherReferences = otherReferences ?? [],
            reading = new
            {
                model = "a test", effort = "medium", promptVersion = "persons-2",
                askedAt = "2026-09-09", note = (string?)null,
            },
            check = (object?)null,
            reaches = (object?)null,
        };

    private void Register(params object[] records)
    {
        using var handle = new StreamWriter(Path.Combine(_folder, "register-0000.jsonl"));
        foreach (var record in records)
        {
            handle.WriteLine(JsonSerializer.Serialize(record));
        }
    }

    private Task<PersonRegisterOutcome> Load() =>
        new PersonRegisterLoader(
                _db,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [PersonRegisterFiles.ConfigurationKey] = _folder,
                    })
                    .Build(),
                NullLogger<PersonRegisterLoader>.Instance)
            .Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));
}
