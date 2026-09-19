using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The records that rest on a dataset because nobody ever asked about them, and what it takes to
/// reach one.
///
/// The fixture is the corpus's own shapes: Sheshan, whom one entry heads and one clause numbers;
/// Beracah, whose entry heads a man and the valley named after him; Adam, whose entry numbers a man
/// and a city under one part of speech; Anamim, whom Strong parts an ordinary noun while the
/// encyclopedia files a son of Mizraim; Jebus, a town to Strong and a man to the encyclopedia; and
/// Andrew, whose name the Greek half of the lexicon states no part of speech for.
///
/// What the design promises a reader is four things. A name nobody else carries, headed by an entry
/// that numbers one bearer of the record's kind, becomes this corpus's own and keeps its address;
/// the dataset's testimony moves to a claim beside ours rather than being dropped; everything the
/// derivation cannot establish keeps exactly the provenance it had, with the reason counted; and
/// nothing else on the record moves at all.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SoleBearerLoadTests : IDisposable
{
    private const string Dataset =
        "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private const string Ours = "Essenthos, from the names Strong's Dictionary heads for one bearer";

    private const string ProperName = "proper name";

    private readonly AppDbContext _db;

    public SoleBearerLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry");

        Entry("H8348", "n-pr-m",
            "Sheshan = \"noble\"\n1) a Judaite of the families of Hezron and Jerahmeel,  son of Ishi");
        Entry("H1294", "n-pr-m n-pr-loc",
            "Berachah = \"blessing\"\nn pr m\n1) a Benjamite,  one of David's warriors\n"
            + "n pr loc\n2) a valley in the wilderness near Tekoa");
        Entry("H121", "n-pr-m", "Adam = \"red\"\n1) first man\n2) city in Jordan valley");
        Entry("H6047", "n", "Anamim = \"affliction of the waters\"\n1) a tribe of Egyptians");
        Entry("H2982", "n-pr-loc",
            "Jebus = \"threshing place\"\n1) an early name for Jerusalem,  the city of the Jebusites");
        Entry("H439", "n-pr-loc",
            "Allon Bachuth = \"oak of weeping\"\n1) site of Deborah's grave near Bethel");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry");
        _db.Dispose();
    }

    [Fact]
    public async Task A_name_nobody_else_carries_becomes_ours_and_keeps_its_address()
    {
        Held("sheshan", "Sheshan", "person:Sheshan_1", EntityKind.Person, "H8348", [(13, 2, 31)]);

        var outcome = await Load();

        outcome.Reached.Should().Be(1);
        outcome.Refused.Values.Sum().Should().Be(0);

        var sheshan = await Record("sheshan");
        sheshan!.Slug.Should().Be("sheshan", "the reader's own URL keys on it");
        sheshan.SourceId.Should().Be("person:Sheshan_1", "a corrected upstream record stays findable");
        sheshan.Source.Should().Be(Ours);
    }

    /// <summary>
    /// The claim is the whole of what this pass adds a reader, so it has to carry the evidence:
    /// which entry, what the entry says, and how much of this corpus stands behind it.
    /// </summary>
    [Fact]
    public async Task The_claim_names_the_entry_quotes_it_and_counts_the_verses()
    {
        Held("sheshan", "Sheshan", "person:Sheshan_1", EntityKind.Person, "H8348",
            [(13, 2, 31), (13, 2, 34)]);

        await Load();

        var claims = (await Record("sheshan"))!.Claims;
        var ours = claims.Should().ContainSingle(claim => claim.Source == Ours).Subject;
        ours.Method.Should().Be(LinkMethod.Lexical);
        ours.Confidence.Should().Be(0.95);
        ours.Note.Should().Contain("H8348")
            .And.Contain("a Judaite of the families of Hezron")
            .And.Contain("2 verses");

        var theirs = claims.Should().ContainSingle(claim => claim.Source == Dataset).Subject;
        theirs.Method.Should().Be(LinkMethod.StatedBySource);
        theirs.Confidence.Should().BeNull("a dataset states a record; it does not conclude one");
        theirs.Note.Should().Contain("person:Sheshan_1");
    }

    /// <summary>
    /// Nothing on the record moves but who it rests on. Every other table keys on the row, so a
    /// pass that rewrote one would be changing what a page says while claiming to change only
    /// where it came from.
    /// </summary>
    [Fact]
    public async Task Nothing_but_the_source_and_the_claims_changes()
    {
        Held("sheshan", "Sheshan", "person:Sheshan_1", EntityKind.Person, "H8348", [(13, 2, 31)]);

        await Load();

        var sheshan = await Record("sheshan");
        sheshan!.Name.Should().Be("Sheshan");
        sheshan.Distinguisher.Should().Be("son of Ishi");
        sheshan.Names.Should().ContainSingle().Which.HebrewStrongNumber.Should().Be("H8348");
        sheshan.Verses.Should().ContainSingle().Which.Source.Should().Be(Dataset,
            "a verse row a dataset wrote stays that dataset's");
    }

    /// <summary>
    /// One entry heading a man and the valley named after him reaches the man, because Strong wrote
    /// a heading over each clause. The valley is nobody's business here and the count of men is one.
    /// </summary>
    [Fact]
    public async Task An_entry_heading_a_man_and_a_valley_reaches_the_man()
    {
        Held("beracah", "Beracah", "person:Beracah_1", EntityKind.Person, "H1294", [(13, 12, 3)]);

        var outcome = await Load();

        outcome.Reached.Should().Be(1);
        (await Record("beracah"))!.Claims
            .Single(claim => claim.Source == Ours).Note
            .Should().Contain("a Benjamite").And.NotContain("valley");
    }

    [Fact]
    public async Task A_place_is_reached_the_same_way_a_person_is()
    {
        Held("allonbacuth", "Allon-bacuth", "place:Allon_bacuth", EntityKind.Place, "H439",
            [(1, 35, 8)]);

        var outcome = await Load();

        outcome.Reached.Should().Be(1);
        (await Record("allonbacuth"))!.Source.Should().Be(Ours);
    }

    /// <summary>
    /// Which of the men called Zechariah a page is is the registers' question. This pass exists
    /// because nobody had asked about the pages where that question does not arise, and it must not
    /// answer it by accident.
    /// </summary>
    [Fact]
    public async Task A_name_another_record_carries_is_left_to_the_registers()
    {
        Held("sheshan", "Sheshan", "person:Sheshan_1", EntityKind.Person, "H8348", [(13, 2, 31)]);
        Held("sheshan-2", "Sheshan", "person:Sheshan_2", EntityKind.Person, "H8348", [(13, 2, 34)]);

        var outcome = await Load();

        outcome.Reached.Should().Be(0);
        outcome.Refused[SoleBearerRefusal.ANameSomebodyElseCarries].Should().Be(2);
        (await Record("sheshan"))!.Source.Should().Be(Dataset);
    }

    /// <summary>
    /// A record this corpus wrote for itself carries no name row of its own, so an index
    /// built out of <c>entity_name</c> alone would call a name unique that one of them already
    /// bears — and this pass would then say a page is the only Sheshan there is.
    /// </summary>
    [Fact]
    public async Task A_record_with_no_name_row_still_carries_its_name()
    {
        Held("sheshan", "Sheshan", "person:Sheshan_1", EntityKind.Person, "H8348", [(13, 2, 31)]);
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person,
            Slug = "sheshan-the-elder",
            Name = "Sheshan",
            SourceId = "essenthos:sheshan-the-elder",
            Source = "Essenthos, on the project owner's ruling",
        });
        _db.SaveChanges();

        var outcome = await Load();

        outcome.Reached.Should().Be(0);
        outcome.Refused[SoleBearerRefusal.ANameSomebodyElseCarries].Should().Be(1);
    }

    /// <summary>
    /// Four Pharaohs share the word and none of them shares a name. A title is not a headword, and
    /// there is nothing to look up.
    /// </summary>
    [Fact]
    public async Task A_title_is_not_a_name_and_is_left_alone()
    {
        var pharaoh = Held("pharaoh", "Pharaoh", "person:Pharaoh_1", EntityKind.Person, null, [(1, 12, 15)]);
        pharaoh.Names.Add(new EntityName { Label = "Pharaoh", Kind = "title", HebrewStrongNumber = "H6547" });
        _db.SaveChanges();

        var outcome = await Load();

        outcome.Reached.Should().Be(0);
        outcome.Refused[SoleBearerRefusal.NoNameToLookUp].Should().Be(1);
    }

    /// <summary>
    /// Adam the first man and Adam the city, both <c>n-pr-m</c>. One record is held and the entry
    /// numbers two things: which of them the record is is exactly what a register answers, and this
    /// pass has no standing to.
    /// </summary>
    [Fact]
    public async Task An_entry_numbering_two_bearers_of_this_kind_is_left_alone()
    {
        Held("adam", "Adam", "person:Adam_1", EntityKind.Person, "H121", [(1, 2, 19)]);

        var outcome = await Load();

        outcome.Reached.Should().Be(0);
        outcome.Refused[SoleBearerRefusal.SeveralBearers].Should().Be(1);
        (await Record("adam"))!.Claims.Should().BeEmpty(
            "nothing of ours reaches him, so nothing of ours is said of him");
    }

    /// <summary>
    /// Strong parts <em>Anamim</em> an ordinary noun; the encyclopedia files a son of Mizraim under
    /// it. His clause reads exactly like a clause about a person and only his tag says otherwise,
    /// which is why the tag is what this pass reads.
    /// </summary>
    [Fact]
    public async Task A_word_Strong_parts_as_a_word_heads_no_name()
    {
        Held("anam", "Anamim", "person:Anamim_1", EntityKind.Person, "H6047", [(1, 10, 13)]);

        var outcome = await Load();

        outcome.Refused[SoleBearerRefusal.NotAName].Should().Be(1);
        (await Record("anam"))!.Source.Should().Be(Dataset);
    }

    /// <summary>
    /// Jebus is a town to Strong and a man to the encyclopedia. The disagreement is somebody's to
    /// settle, and papering over it would put a lexicon's place clause on a person's page as the
    /// reason the person exists.
    /// </summary>
    [Fact]
    public async Task An_entry_knowing_no_bearer_of_this_kind_is_left_alone()
    {
        Held("jebus", "Jebus", "person:Jebus_1", EntityKind.Person, "H2982", [(7, 19, 10)]);

        var outcome = await Load();

        outcome.Refused[SoleBearerRefusal.NoBearerOfThisKind].Should().Be(1);
    }

    /// <summary>
    /// The Greek half of the lexicon states no part of speech and numbers no bearers on any entry
    /// it holds, so a name attested only there has no witness of this kind at all. The refusal says
    /// which of the two silences it is, because <em>no number</em> and <em>a number in a language
    /// this lexicon says nothing about</em> want different answers.
    /// </summary>
    [Fact]
    public async Task A_name_attested_only_in_Greek_has_no_witness_here()
    {
        var andrew = Held("andrew", "Andrew", "person:Andrew_1", EntityKind.Person, null, [(40, 4, 18)]);
        andrew.Names.Add(new EntityName { Label = "Andrew", Kind = ProperName, GreekStrongNumber = "G406" });
        Held("jucam", "Jucam", "person:Jucam_1", EntityKind.Person, null, [(13, 4, 22)])
            .Names.Add(new EntityName { Label = "Jucam", Kind = ProperName });
        _db.SaveChanges();

        var outcome = await Load();

        outcome.Reached.Should().Be(0);
        outcome.Refused[SoleBearerRefusal.GreekOnly].Should().Be(1);
        outcome.Refused[SoleBearerRefusal.NoStrongNumber].Should().Be(1);
    }

    /// <summary>
    /// A record already this corpus's own is not in this population at all — the registers reached
    /// it and its claims are theirs, and a second claim from here would say a page rests on two
    /// derivations when it rests on one.
    /// </summary>
    [Fact]
    public async Task A_record_already_ours_is_not_considered()
    {
        var mine = Held("sheshan", "Sheshan", "essenthos:sheshan", EntityKind.Person, "H8348",
            [(13, 2, 31)]);
        mine.Source = "Essenthos, from the bearers Strong's Dictionary enumerates under the name";
        _db.SaveChanges();

        var outcome = await Load();

        outcome.Resting.Should().Be(0);
        outcome.Reached.Should().Be(0);
    }

    [Fact]
    public async Task Running_it_twice_writes_the_claims_once()
    {
        Held("sheshan", "Sheshan", "person:Sheshan_1", EntityKind.Person, "H8348", [(13, 2, 31)]);

        await Load();
        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.EntityClaims.CountAsync()).Should().Be(2);
    }

    private Entity Held(
        string slug,
        string name,
        string sourceId,
        EntityKind kind,
        string? hebrewStrongNumber,
        (int Book, int Chapter, int Verse)[] verses)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = name,
            Distinguisher = "son of Ishi",
            SourceId = sourceId,
            Source = Dataset,
            Verses = verses
                .Select(address => new EntityVerse
                {
                    CanonicalBook = address.Book,
                    CanonicalChapter = address.Chapter,
                    CanonicalVerse = address.Verse,
                    Source = Dataset,
                })
                .ToList(),
        };

        if (hebrewStrongNumber is { Length: > 0 })
        {
            entity.Names.Add(new EntityName
            {
                Label = name,
                Kind = ProperName,
                HebrewStrongNumber = hebrewStrongNumber,
            });
        }

        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private void Entry(string number, string morphology, string detailedDefinition) =>
        _db.StrongEntries.Add(new StrongEntry
        {
            StrongNumber = number,
            Morphology = morphology,
            DetailedDefinition = detailedDefinition,
        });

    private async Task<Entity?> Record(string slug) =>
        await _db.Entities
            .Include(e => e.Claims)
            .Include(e => e.Names)
            .Include(e => e.Verses)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Slug == slug);

    private Task<SoleBearerOutcome> Load() =>
        new SoleBearerLoader(_db, NullLogger<SoleBearerLoader>.Instance).Load();
}
