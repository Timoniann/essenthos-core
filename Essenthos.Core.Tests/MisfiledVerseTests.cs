using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Essenthos.Core.Loading.Encyclopedia.MisfiledVerseLoader;

namespace Essenthos.Core.Tests;

/// <summary>
/// Whether a verse the dataset files under a people's ancestor names the man or the people, read off
/// the Hebrew one occurrence of the name at a time.
/// </summary>
public sealed class MisfiledVerseReadingTests
{
    private const string Sons = "H1121";
    private const string House = "H1004";
    private const string Firstborn = "H1060";
    private const string God = "H430";
    private const string Cities = "H5892";

    private static Occurrence At(string? head, bool plural = false, int position = 5) =>
        new(position, head, plural);

    [Fact]
    public void The_children_of_Israel_after_Jacobs_death_are_the_nation() =>
        Read([At(Sons, plural: true)], kinNamed: false, inHisLife: false, peoplesWord: false)
            .Should().Be((Reading.ThePeople, (string?)null));

    [Fact]
    public void The_house_of_Israel_is_the_nation() =>
        Read([At(House)], false, false, false).Reading.Should().Be(Reading.ThePeople);

    [Fact]
    public void While_he_lives_the_sons_of_Israel_may_be_his_own_sons() =>
        Read([At(Sons, plural: true)], false, inHisLife: true, false)
            .Should().Be((Reading.Unsettled, InHisLife));

    [Fact]
    public void The_son_of_Israel_is_one_man_s_son() =>
        Read([At(Sons)], false, false, false).Reading.Should().Be(Reading.TheMan);

    [Fact]
    public void The_firstborn_of_Israel_is_Reuben_the_man_s_son() =>
        Read([At(Firstborn)], false, false, false).Reading.Should().Be(Reading.TheMan);

    [Fact]
    public void A_verse_naming_his_family_is_about_the_man_whatever_the_phrase() =>
        Read([At(Sons, plural: true)], kinNamed: true, inHisLife: false, false)
            .Reading.Should().Be(Reading.TheMan);

    [Fact]
    public void A_name_standing_alone_is_the_man_in_his_life_and_a_question_after_it()
    {
        Read([At(null)], false, inHisLife: true, false).Reading.Should().Be(Reading.TheMan);
        Read([At(null)], false, inHisLife: false, false).Should().Be((Reading.Unsettled, AfterHisLife));
    }

    [Fact]
    public void A_verse_that_names_both_by_their_own_words_has_told_them_apart() =>
        Read([At(null)], false, inHisLife: false, peoplesWord: true).Reading.Should().Be(Reading.TheMan);

    [Fact]
    public void A_verse_that_does_not_print_the_name_is_the_people_only_where_it_prints_their_word()
    {
        Read([], false, false, peoplesWord: true).Reading.Should().Be(Reading.ThePeople);
        Read([], false, false, peoplesWord: false).Should().Be((Reading.Unsettled, NotPrinted));
    }

    [Fact]
    public void A_word_the_corpus_already_annotates_to_the_people_decides_it_even_in_his_life() =>
        Read([new Occurrence(5, null, false, NamesThePeople: true)], false, inHisLife: true, false)
            .Reading.Should().Be(Reading.ThePeople);

    private static readonly Family Jacob = new(new HashSet<int> { Isaac, Reuben, Benjamin, Judah }, new HashSet<int> { Reuben, Benjamin, Judah });
    private const int Isaac = 1, Reuben = 2, Benjamin = 3, Judah = 4;
    private static readonly IReadOnlySet<int> Ancestors = new HashSet<int> { Reuben, Benjamin, Judah };

    [Fact]
    public void The_sons_of_Israel_then_Reuben_is_a_genealogy() =>
        Genealogical([At(Sons, plural: true, position: 2)], [(Reuben, At(null, position: 3))], Jacob, Ancestors)
            .Should().BeTrue();

    [Fact]
    public void Benjamin_their_brother_further_on_is_not()
    {
        // Judges 21:6: the children of Israel repented them for Benjamin their brother.
        Genealogical([At(Sons, plural: true, position: 2)], [(Benjamin, At(null, position: 5))], Jacob, Ancestors)
            .Should().BeFalse();
    }

    [Fact]
    public void Two_sons_who_are_tribes_named_as_persons_are_a_genealogy_and_one_is_not()
    {
        Genealogical([At(null)], [(Judah, At(null, position: 9))], Jacob, Ancestors).Should().BeFalse();
        Genealogical([At(null)], [(Judah, At(null, position: 9)), (Reuben, At(null, position: 12))], Jacob, Ancestors)
            .Should().BeTrue();
    }

    [Fact]
    public void A_son_named_as_his_tribe_s_land_is_not_named_as_a_person() =>
        Genealogical([At(null)], [(Judah, At(Cities)), (Reuben, At(Sons, plural: true))], Jacob, Ancestors)
            .Should().BeFalse();

    [Fact]
    public void The_God_of_Isaac_names_Isaac() =>
        Genealogical([At(God)], [(Isaac, At(God, position: 9))], Jacob, Ancestors).Should().BeTrue();
}

/// <summary>
/// The pass over a database: rows moved to the people, left and listed for the owner, his answers
/// applied, and the single rows the dataset filed under the wrong record put right.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class MisfiledVerseLoadTests : IDisposable
{
    private const string Israel = "H3478";
    private const string JacobsName = "H3290";
    private const string IsaacsName = "H3327";
    private const string ReubensName = "H7205";
    private const string Israelite = "H3481";

    private const int Genesis = 1;
    private const int Exodus = 2;

    private readonly AppDbContext _db;
    private readonly MisfiledVerseLoader _loader;
    private readonly string _resources;
    private readonly Entity _jacob;
    private readonly Entity _israelites;

    public MisfiledVerseLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new MisfiledVerseLoader(_db, NullLogger<MisfiledVerseLoader>.Instance);
        _resources = Path.Combine(Path.GetTempPath(), $"essenthos-misfiled-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_resources, "Essenthos", "review"));

        // Genesis 32 is in Jacob's life; the other chapters are moved to Exodus below, after it.
        var hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (32, 28, ["שמך", "ישראל"]),
            (32, 32, ["בני", "ישראל"]),
            (25, 2, ["דבר", "בני", "ישראל"]),
            (6, 14, ["בכור", "ישראל"]),
            (3, 15, ["אלהי", "יצחק", "בני", "ישראל"]),
            (4, 22, ["בני", "בכרי", "ישראל"]),
            (26, 1, ["המשכן"]),
            (1, 2, ["בני", "ישראל", "ראובן"]),
            (1, 7, ["בני", "ישראלים"]));
        _db.SaveChanges();

        Word(hebrew, 32, 28, 2, Israel);
        Word(hebrew, 32, 32, 1, "H1121", "c", "pl");
        Word(hebrew, 32, 32, 2, Israel);
        Word(hebrew, 25, 2, 2, "H1121", "c", "pl");
        Word(hebrew, 25, 2, 3, Israel);
        Word(hebrew, 6, 14, 1, "H1060", "c", "sg");
        Word(hebrew, 6, 14, 2, Israel);
        Word(hebrew, 3, 15, 1, "H430", "c", "pl");
        Word(hebrew, 3, 15, 2, IsaacsName);
        Word(hebrew, 3, 15, 3, "H1121", "c", "pl");
        Word(hebrew, 3, 15, 4, Israel);
        Word(hebrew, 4, 22, 1, "H1121", "a", "sg");
        Word(hebrew, 4, 22, 2, "H1060", "a", "sg");
        Word(hebrew, 4, 22, 3, Israel);
        Word(hebrew, 1, 2, 1, "H1121", "c", "pl");
        Word(hebrew, 1, 2, 2, Israel);
        Word(hebrew, 1, 2, 3, ReubensName);
        Word(hebrew, 1, 7, 1, "H1121", "c", "pl");
        Word(hebrew, 1, 7, 2, Israelite);
        _db.SaveChanges();
        _db.Database.ExecuteSqlRaw(
            "UPDATE verse_reference SET canonical_book = {0} WHERE canonical_chapter <> 32", Exodus);

        _jacob = Person("jacob", "Jacob", "person:Jacob_1", ("Jacob", JacobsName), ("Israel", Israel));
        var isaac = Person("isaac", "Isaac", "person:Isaac_1", ("Isaac", IsaacsName));
        var reuben = Person("reuben", "Reuben", "person:Reuben_1", ("Reuben", ReubensName));
        _israelites = People("israelites", "Israelites", _jacob, ("Israelites", Israelite));
        People("reubenites", "Reubenites", reuben);
        _db.SaveChanges();

        Tie(isaac, "father", _jacob);
        Tie(_jacob, "father", reuben);

        Cite(_jacob, Genesis, 32, 28);
        Cite(_jacob, Genesis, 32, 32);
        Cite(_jacob, Exodus, 25, 2);
        Cite(_jacob, Exodus, 6, 14);
        Cite(_jacob, Exodus, 3, 15);
        Cite(_jacob, Exodus, 4, 22);
        Cite(_jacob, Exodus, 26, 1);
        Cite(_jacob, Exodus, 1, 2);
        Cite(_jacob, Exodus, 1, 7);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        Directory.Delete(_resources, recursive: true);
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private string Review => Path.Combine(_resources, "Essenthos", "review", ReviewFile);

    [Fact]
    public async Task The_nation_s_verses_move_to_the_people_and_the_man_keeps_his()
    {
        var outcome = await _loader.Load(_resources);

        (await Verses(_jacob)).Should().BeEquivalentTo(
            ["GEN 32:28", "GEN 32:32", "EXO 6:14", "EXO 3:15", "EXO 4:22", "EXO 26:1", "EXO 1:2"]);
        (await Verses(_israelites)).Should().BeEquivalentTo(["EXO 25:2", "EXO 1:7"]);
        outcome.ToThePeople.Should().Be(2);
        outcome.Unsettled.Should().Be(3);

        var moved = await _db.EntityVerses.SingleAsync(v => v.EntityId == _israelites.Id && v.CanonicalChapter == 25);
        moved.Source.Should().Be(BibleDataLoader.Source, "it is still the dataset's testimony that the verse names them");
        moved.Label.Should().Be("Israel");
    }

    [Fact]
    public async Task What_the_rule_cannot_settle_is_listed_for_the_owner_one_entry_per_reason()
    {
        await _loader.Load(_resources);

        var entries = Entries();
        entries.Select(e => (string)e["references"]![0]!).Should().BeEquivalentTo(["GEN 32:32", "EXO 4:22", "EXO 26:1"]);
        entries.Should().OnlyContain(e =>
            (string)e["record"]! == "jacob" && (string)e["label"]! == "Israel" && (string)e["strong"]! == Israel);
        entries.SelectMany(e => e["options"]!.AsArray().Select(o => (string)o!)).Distinct()
            .Should().BeEquivalentTo(["israelites", LeaveIt]);
    }

    [Fact]
    public async Task The_owner_s_answer_is_applied_and_kept_and_a_second_load_changes_nothing()
    {
        await _loader.Load(_resources);
        Answer("EXO 4:22", "israelites");
        Answer("EXO 26:1", LeaveIt);

        var answered = await _loader.Load(_resources);
        answered.Decided.Should().Be(2);
        (await Verses(_israelites)).Should().Contain("EXO 4:22");
        (await Verses(_jacob)).Should().Contain("EXO 26:1");

        var before = File.ReadAllText(Review);
        var again = await _loader.Load(_resources);
        again.ToThePeople.Should().Be(0);
        again.Decided.Should().Be(1, "the verse left with the man is still his to read, and still answered");
        File.ReadAllText(Review).Should().Be(before);
        Entries().Where(e => e["decision"] is not null).Should().HaveCount(2);
        Entries().Where(e => e["decision"] is null).Select(e => (string)e["references"]![0]!)
            .Should().BeEquivalentTo(["GEN 32:32"]);
    }

    [Fact]
    public async Task A_label_filed_a_verse_early_moves_and_a_verse_that_does_not_name_him_goes()
    {
        var satan = Person("satan", "Satan", "person:Satan_1");
        _db.SaveChanges();
        _db.EntityVerses.AddRange(
            new EntityVerse { EntityId = satan.Id, CanonicalBook = 44, CanonicalChapter = 13, CanonicalVerse = 9, Label = "the devil", Source = BibleDataLoader.Source },
            new EntityVerse { EntityId = satan.Id, CanonicalBook = 63, CanonicalChapter = 1, CanonicalVerse = 7, Label = "Deceiver", Source = BibleDataLoader.Source },
            new EntityVerse { EntityId = satan.Id, CanonicalBook = 40, CanonicalChapter = 4, CanonicalVerse = 1, Label = "the devil", Source = BibleDataLoader.Source });
        _db.SaveChanges();

        (await _loader.Load(_resources)).Misfiled.Should().Be(2);
        (await _loader.Load(_resources)).Misfiled.Should().Be(0);

        var left = await _db.EntityVerses.Where(v => v.EntityId == satan.Id)
            .Select(v => new { v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
            .ToListAsync();
        left.Select(v => (v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse))
            .Should().BeEquivalentTo([(44, 13, 10), (40, 4, 1)]);
    }

    [Fact]
    public void Every_misfiled_row_names_two_real_verses_and_says_why()
    {
        foreach (var row in MisfiledVerseLoader.Misfiled)
        {
            TitleLoader.Verse(row.Reference).Should().NotBeNull(row.Reference);
            if (row.MovesTo is { } to)
            {
                TitleLoader.Verse(to).Should().NotBeNull(to);
            }

            row.Why.Should().NotBeNullOrWhiteSpace();
        }
    }

    private List<JsonObject> Entries() =>
        [.. JsonNode.Parse(File.ReadAllText(Review))!["entries"]!.AsArray().OfType<JsonObject>()];

    private void Answer(string reference, string answer)
    {
        var document = JsonNode.Parse(File.ReadAllText(Review))!;
        var entry = document["entries"]!.AsArray().OfType<JsonObject>()
            .Single(e => e["references"]!.AsArray().Any(r => (string)r! == reference));
        entry["decision"] = new JsonObject { ["answer"] = answer, ["note"] = "", ["decidedAt"] = "2026-09-23T12:00:00.000Z" };
        File.WriteAllText(Review, document.ToJsonString());
    }

    private async Task<List<string>> Verses(Entity entity)
    {
        var rows = await _db.EntityVerses.AsNoTracking()
            .Where(v => v.EntityId == entity.Id)
            .Select(v => new { v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
            .ToListAsync();
        return [.. rows.Select(v => $"{(v.CanonicalBook == Genesis ? "GEN" : "EXO")} {v.CanonicalChapter}:{v.CanonicalVerse}")];
    }

    private void Word(Text text, int chapter, int verse, int position, string strong, string? state = null, string? number = null)
    {
        var word = _db.WordAt(text, chapter, verse, position);
        word.StrongNumber = strong;
        if (state is not null)
        {
            word.Morphology = JsonDocument.Parse(JsonSerializer.Serialize(new { state, number }));
        }
    }

    private Entity Person(string slug, string name, string sourceId, params (string Label, string Strong)[] names)
    {
        var person = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = sourceId, Source = BibleDataLoader.Source,
            Names = [.. names.Select(n => new EntityName { Label = n.Label, HebrewStrongNumber = n.Strong })],
        };
        _db.Entities.Add(person);
        return person;
    }

    private Entity People(string slug, string name, Entity origin, params (string Label, string Strong)[] names)
    {
        var people = new Entity
        {
            Kind = EntityKind.People, Slug = slug, Name = name, SourceId = "essenthos:" + slug, Source = "Essenthos",
            Origin = origin,
            Names = [.. names.Select(n => new EntityName { Label = n.Label, HebrewStrongNumber = n.Strong })],
        };
        _db.Entities.Add(people);
        return people;
    }

    private void Tie(Entity from, string type, Entity to) =>
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id, ToEntityId = to.Id, Type = type, Category = "explicit",
            Method = LinkMethod.StatedBySource, Source = BibleDataLoader.Source,
        });

    private void Cite(Entity entity, int book, int chapter, int verse) =>
        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = entity.Id, CanonicalBook = book, CanonicalChapter = chapter, CanonicalVerse = verse,
            Label = "Israel", Source = BibleDataLoader.Source,
        });
}
