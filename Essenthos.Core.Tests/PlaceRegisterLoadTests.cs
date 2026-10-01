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
        _db.Database.ExecuteSqlRaw("DELETE FROM text");

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
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
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
    /// The seventeen: a record whose spelling meets nothing held, for a number the corpus already
    /// reads onto a held place. Written as a second record it makes the number name two of them,
    /// and the annotation pass then refuses both — so the duplicate does not merely add a page, it
    /// takes the annotations off the page that was working.
    /// </summary>
    [Fact]
    public async Task A_variant_spelling_of_a_number_the_corpus_already_reads_joins_the_place_it_reads_it_onto()
    {
        var held = Held("beth-meon", "Beth-meon", "obi-5");
        _db.SaveChanges();
        Reads(held, "H1010", "Bethmeon");

        Register(Record("H1010", "Beth-baal-meon", ["Beth-baal-meon", "Bêyth Baʻal Mᵉʻôwn"]));

        var outcome = await Load();

        outcome.Added.Should().Be(0, "the corpus already reads H1010 onto a place it holds");
        outcome.Claimed.Should().Be(1);

        var place = await Place("beth-meon");
        place!.Name.Should().Be("Beth-meon", "the held page keeps its name and its address");
        place.Names.Should().ContainSingle().Which.HebrewStrongNumber.Should().Be("H1010");
        (await _db.Entities.CountAsync(e => e.Slug == "bethbaalmeon")).Should().Be(0);

        var bearers = await _db.EntityNames.CountAsync(n => n.HebrewStrongNumber == "H1010");
        bearers.Should().Be(1, "a number naming two records is what the resolution refuses");
    }

    /// <summary>
    /// Abez and Ebez: one town, named once at JOS 19:20, which the King James spells one way and the
    /// gazetteer the other, so neither the spelling nor the number read through the King James meets
    /// the surveyed place. Written as a record of its own it was a second page for the same verse.
    /// </summary>
    [Fact]
    public async Task A_town_the_King_James_spells_otherwise_is_the_place_the_gazetteer_surveyed()
    {
        Held("ebez", "Ebez", "acd9b19");
        _db.SaveChanges();

        Register(Record("H77", "Abez", ["Abez", "Ebets", "ʼEbets"]));

        var outcome = await Load();

        outcome.Added.Should().Be(0);
        outcome.Claimed.Should().Be(1);

        var ebez = await Place("ebez");
        ebez!.Name.Should().Be("Ebez");
        ebez.Source.Should().Be(Ours);
        ebez.Names.Should().ContainSingle().Which.Should().Match<EntityName>(
            name => name.Label == "Abez" && name.HebrewStrongNumber == "H77");
        (await _db.Entities.CountAsync(e => e.Slug == "abez")).Should().Be(0);
    }

    /// <summary>
    /// The number is asked only where the spelling answers nothing. Strong heads one Mizpah and the
    /// gazetteer surveys three, so a record that has already met one of them by name must not be
    /// spread over all three merely because the corpus reads the number onto each.
    /// </summary>
    [Fact]
    public async Task The_spelling_decides_where_it_answers_at_all()
    {
        var other = Held("beth-meon", "Beth-meon", "obi-5");
        _db.SaveChanges();
        Reads(other, "H1035", "Bethmeon");

        Register(Record("H1035", "Bethlehem", ["Bethlehem"]));

        await Load();

        (await Place("beth-lehem"))!.Names.Should().ContainSingle()
            .Which.HebrewStrongNumber.Should().Be("H1035");
        (await Place("beth-meon"))!.Names.Should().BeEmpty();
    }

    /// <summary>
    /// A Greek record carries the spelling the annotation pass will check its number against. With
    /// the column null there is nothing to compare and the number is refused, which cost Judaea all
    /// 173 of its occurrences.
    /// </summary>
    [Fact]
    public async Task A_Greek_record_carries_the_spelling_its_number_is_annotated_on()
    {
        Register(
            Record("G2449", "Judaea", ["Judaea"], lemma: "Ἰουδαία"),
            Record("H1035", "Bethlehem", ["Bethlehem"], lemma: "בֵּית לֶחֶם"));

        await Load();

        var judaea = await Place("judaea");
        judaea!.Names.Should().ContainSingle().Which.Greek.Should().Be("Ἰουδαία");

        var bethlehem = await Place("beth-lehem");
        bethlehem!.Names.Should().ContainSingle().Which.Greek.Should()
            .BeNull("a Hebrew number is not checked against a Greek spelling");
    }

    /// <summary>
    /// A record the gazetteer files under the place's name with a feature word in front is that
    /// place seen another way, and the name it takes from the entry is the place's. Left standing
    /// as a rival it makes the number name two records, which is what took Zion's 146 words off its
    /// page the first time the register was loaded.
    /// </summary>
    [Fact]
    public async Task A_record_named_with_the_gazetteer_s_feature_word_bears_the_place_s_name()
    {
        var zion = Held("zion", "Zion", "obi-6");
        Held("mount-zion", "Mount Zion", "obi-7");
        _db.SaveChanges();
        Reads(zion, "H6726", "Zion");

        Register(Record("H6726", "Zion", ["Zion"]));

        await Load();

        var hill = await Place("mount-zion");
        hill!.Names.Should().ContainSingle().Which.AspectOfEntityId.Should().Be(
            zion.Id, "Mount Zion carries the number as Zion's hill and not as a second Zion");
        (await Place("zion"))!.Names.Should().ContainSingle()
            .Which.AspectOfEntityId.Should().BeNull("Zion is the place the name is of");
    }

    /// <summary>
    /// Two entries the gazetteer puts at two sites are two places, and the number resolves to
    /// neither of them however clearly the corpus reads it onto one. Which Jericho a verse means is
    /// the namesake pass's work, and a page saying nothing is the right answer until it runs.
    /// </summary>
    [Fact]
    public async Task Two_records_the_gazetteer_places_at_two_sites_stay_two_places()
    {
        var jericho = Held("jericho", "Jericho", "obi-6", "Tell es Sultan");
        Held("jericho-2", "Jericho", "obi-7", "Tell el Alayiq");
        _db.SaveChanges();
        Reads(jericho, "H3405", "Jericho");

        Register(Record("H3405", "Jericho", ["Jericho"]));

        await Load();

        var bearers = await _db.EntityNames
            .Where(name => name.HebrewStrongNumber == "H3405")
            .ToListAsync();

        bearers.Should().HaveCount(2);
        bearers.Should().OnlyContain(name => name.AspectOfEntityId == null,
            "two towns four kilometres apart are two places and the number names both");
    }

    /// <summary>
    /// A city and the country called after it are one place. The gazetteer files both under the
    /// name and its own catalogue index — <em>Samaria</em>, <em>Samaria 2</em> — which places
    /// neither anywhere and so says nothing about their being two.
    /// </summary>
    [Fact]
    public async Task A_city_and_the_country_called_after_it_are_one_place()
    {
        var city = Held("samaria", "Samaria", "obi-6", "Samaria");
        Held("samaria-2", "Samaria", "obi-7", "Samaria 2");
        _db.SaveChanges();
        Reads(city, "H8111", "Samaria");

        Register(Record("H8111", "Samaria", ["Samaria"]));

        await Load();

        (await Place("samaria-2"))!.Names.Should().ContainSingle()
            .Which.AspectOfEntityId.Should().Be(city.Id);
        (await Place("samaria"))!.Names.Should().ContainSingle()
            .Which.AspectOfEntityId.Should().BeNull();
    }

    /// <summary>
    /// A number can carry both situations at once, and the alias does not settle it. The stone heap
    /// called Mizpah is another name for the first of them and no third place, but the two the
    /// gazetteer does place are two, so the number resolves to none of the three.
    /// </summary>
    [Fact]
    public async Task An_entry_that_is_another_name_for_one_of_two_places_does_not_make_them_one()
    {
        var first = Held("mizpah", "Mizpah", "obi-6", "Jel'ad");
        Held("mizpah-3", "Mizpah", "obi-7", "Tell en Nasbeh");
        Held("mizpah-4", "Mizpah", "obi-8", "another name for Mizpah 1");
        _db.SaveChanges();
        Reads(first, "H4709", "Mizpah");

        Register(Record("H4709", "Mizpah", ["Mizpah"]));

        await Load();

        var bearers = await _db.EntityNames
            .Where(name => name.HebrewStrongNumber == "H4709")
            .ToListAsync();

        bearers.Should().HaveCount(3);
        bearers.Should().OnlyContain(name => name.AspectOfEntityId == null);
    }

    /// <summary>
    /// A place is not an aspect of a person however it came by its name. Strong heads one entry for
    /// the man Jephthah and the town named after him, and which of them a word means is BHSA's
    /// marking to say — or, in the Greek, which of them the witnesses reach. Deferring the town to
    /// the man pre-empts both, and it cost Cos, Ephraim and Judah their words when it did.
    /// </summary>
    [Fact]
    public async Task A_place_named_after_a_man_does_not_bear_the_name_as_his()
    {
        var man = new Entity
        {
            Kind = EntityKind.Person,
            Slug = "jephthah",
            Name = "Jephthah",
            SourceId = "bibledata:Jephthah_1",
            Source = "a witness",
            Names = [new EntityName { Label = "Jephthah", HebrewStrongNumber = "H3316" }],
        };
        _db.Entities.Add(man);
        _db.SaveChanges();

        Register(Record("H3316", "Jephthah", ["Jephthah"]));

        await Load();

        var town = await Place("jephthah-2");
        town!.Names.Should().ContainSingle().Which.AspectOfEntityId.Should().BeNull(
            "the man and the town are two things and the marking on the word tells them apart");
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

    private Entity Held(string slug, string name, string openBibleId, string? identification = null)
    {
        var place = new Entity
        {
            Kind = EntityKind.Place,
            Slug = slug,
            Name = name,
            SourceId = $"openbible:{openBibleId}",
            Source = Gazetteer,
            OpenBibleId = openBibleId,
            ModernEquivalent = identification,
        };
        _db.Entities.Add(place);
        return place;
    }

    /// <summary>
    /// The three statements that make the corpus read a Strong number onto a place the gazetteer
    /// supplied: the gazetteer says the place is named in this verse, the King James prints its name
    /// there at a word, and BHSA gives the word that word renders a number and marks it a name.
    /// </summary>
    private void Reads(Entity place, string number, string spelling)
    {
        var hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["בית"]));
        var english = Corpus.Add(_db, EntityCandidates.Rendering, TextKind.Translation, "eng",
            (1, 1, [spelling]));
        _db.SaveChanges();

        var word = _db.WordAt(hebrew, 1, 1, 1);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "topo"}""");

        var link = new Link
        {
            FromTextId = hebrew.Id,
            ToTextId = english.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = word, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord
        {
            Link = link, Word = _db.WordAt(english, 1, 1, 1), Side = LinkSide.To,
        });

        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = place,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Source = Gazetteer,
        });
        _db.SaveChanges();
    }

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
        bool kept = true,
        string? lemma = null) =>
        new
        {
            number,
            name,
            names,
            lemma,
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
