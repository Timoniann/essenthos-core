using Essenthos.Core.Corpus;
﻿using Essenthos.Core.Loading;
using System.Data.Common;
using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The descriptions this corpus writes for itself, from the file a generation pass produces to the
/// line a reader is shown.
///
/// The fixtures are written here rather than taken from the corpus, and deliberately: the real
/// files are the output of a model run and are not on most disks, and a loader that cannot be
/// exercised without a gigabyte of sources is a loader nobody exercises. What they stand in for is
/// the shape of the descriptor file — one newline-delimited JSON object per entity, carrying its
/// claims and its name forms — which is the thing three agents are building against.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DescriptorTests : IDisposable
{
    private const string Model = "claude-sonnet-5";

    private const string AskedAt = "2026-09-06";

    /// <summary>What BibleData says about Hobab, which nothing here may touch.</summary>
    private const string ImportedSentence = "the son of Reuel, Moses' father-in-law (NUM 10:29)";

    private readonly AppDbContext _db;

    private readonly ITestOutputHelper _output;

    public DescriptorTests(WitnessDatabase database, ITestOutputHelper output)
    {
        _output = output;
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        Add("hobab-1", EntityKind.Person, "Hobab", ImportedSentence, (4, 10, 29), (7, 4, 11));
        Add("reuel-1", EntityKind.Person, "Reuel", null, (2, 2, 18));
        Add("moses-1", EntityKind.Person, "Moses", null, (2, 2, 10));
        Add("moab-1", EntityKind.Person, "Moab", null, (1, 19, 37));
        Add("moabites", EntityKind.People, "Moabites", null, (1, 19, 37));
        Add("jethro-1", EntityKind.Person, "Jethro", null, (2, 3, 1));
        Add("midianites", EntityKind.People, "Midianites", null, (2, 3, 1));
        Add("bethlehem-1", EntityKind.Place, "Bethlehem", null, (1, 35, 19));
        Add("judah-1", EntityKind.Place, "Judah", null, (1, 35, 19));

        // The place clauses, which are the ones the locative is needed for. Nod is the one no pass
        // produced a locative for, so it is what the fallback is proved on.
        Add("abraham-1", EntityKind.Person, "Abraham", null, (1, 13, 18), (1, 25, 9));
        Add("hebron-1", EntityKind.Place, "Hebron", null, (1, 13, 18), (1, 25, 9));
        Add("nebo-1", EntityKind.Place, "Nebo", null, (5, 34, 1));
        Add("moab-region-1", EntityKind.Place, "Moab", null, (5, 34, 1));
        Add("cain-1", EntityKind.Person, "Cain", null, (1, 4, 16));
        Add("nod-1", EntityKind.Place, "Nod", null, (1, 4, 16));

        // The clauses the vocabulary could not hold before it was widened. Haran is the one no pass
        // produced a Ukrainian genitive for, so he is what the fallback is proved on here.
        Add("lot-1", EntityKind.Person, "Lot", null, (1, 11, 27), (1, 12, 5), (1, 13, 12));
        Add("abram-1", EntityKind.Person, "Abram", null, (1, 12, 5));
        Add("haran-1", EntityKind.Person, "Haran", null, (1, 11, 27));
        Add("terah-1", EntityKind.Person, "Terah", null, (1, 11, 27));
        Add("jordanvalley-1", EntityKind.Place, "Valley of the Jordan", null, (1, 13, 12));
        Add("azrikam-4", EntityKind.Person, "Azrikam", null, (14, 28, 7));
        Add("zichri-1", EntityKind.Person, "Zichri", null, (14, 28, 7));
        Add("jerusalem-1", EntityKind.Place, "Jerusalem", null, (16, 3, 29));
        Add("eastgate-1", EntityKind.Place, "East Gate", null, (16, 3, 29));

        // The claims no single verse holds: Hosea is named in 1:2 and his son in 1:4, and Athaliah is
        // named in 2 Kings 8:26 while 8:18 calls her only Jehoram's wife.
        Add("hosea-1", EntityKind.Person, "Hosea", null, (28, 1, 1), (28, 1, 2));
        Add("jezreel-1", EntityKind.Person, "Jezreel", null, (28, 1, 4));
        Add("athaliah-1", EntityKind.Person, "Athaliah", null, (12, 8, 26));
        Add("jehoram-1", EntityKind.Person, "Jehoram", null, (12, 8, 16), (12, 8, 25));

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private void Add(
        string slug,
        EntityKind kind,
        string name,
        string? distinguisher,
        params (int Book, int Chapter, int Verse)[] verses)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = name,
            Distinguisher = distinguisher,
            SourceId = slug,
            Source = "a test",
        };

        foreach (var (book, chapter, verse) in verses)
        {
            entity.Verses.Add(new EntityVerse
            {
                CanonicalBook = book,
                CanonicalChapter = chapter,
                CanonicalVerse = verse,
                Source = "a test",
            });
        }

        _db.Entities.Add(entity);
    }

    /// <summary>The fixtures, which live in the test project's own output rather than in the corpus.</summary>
    private static string Fixtures(string folder) =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "descriptors", folder);

    private EntityDescriptorLoader Loader(string folder) =>
        new(
            _db,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DescriptorFiles.ConfigurationKey] = Fixtures(folder),
                })
                .Build(),
            NullLogger<EntityDescriptorLoader>.Instance);

    private Task<DescriptorOutcome> Load(string folder) =>
        Loader(folder).Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

    private static string Line(EntityDescriptorResponse? description) =>
        string.Concat(description!.Parts.Select(p => p.Text));

    private Task<EntityDescriptorResponse?> Read(string slug, string? language) =>
        Descriptors.Of(_db, slug, language, default);

    /// <summary>
    /// The re-ask, which is what a widened vocabulary produces: the same entity answered again, in
    /// a file published after the first. The file set already settled this by taking the file that
    /// sorts last; the loader used to leave an entity it had already described exactly as it was,
    /// so the later answer loaded on a fresh corpus and never reached one that already held the
    /// first.
    ///
    /// <para>
    /// The two records carry the same model and the same date on purpose, because the re-ask that
    /// found this ran hours after the batch it corrects. The credit a reader sees is one string on
    /// both, so the file is the only thing that tells them apart.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ALaterPassAboutAnEntityAlreadyDescribedReplacesWhatIsLoaded()
    {
        var first = await Load("reasked-first");
        first.Described.Should().Be(1);
        first.Clauses.Should().Be(1);

        var again = await Load("reasked-again");

        again.Superseded.Should().Be(1, "Hobab was described by the earlier pass");
        again.Forgotten.Should().Be(1, "and his one clause made room for the two");
        again.Skipped.Should().Be(0);
        again.Clauses.Should().Be(2);

        var clauses = await _db.EntityDescriptors
            .Include(d => d.Target)
            .Where(d => d.Entity!.Slug == "hobab-1")
            .OrderBy(d => d.Ordinal)
            .ToListAsync();

        clauses.Select(c => c.Relation).Should().Equal("son-of", "father-in-law-of");
        clauses.Should().OnlyContain(c => c.Run!.StartsWith("reask-0001.jsonl#"),
            "nothing of the superseded pass is left beside the new answer, and the two are told "
            + "apart by their files because their credits are the same string");
    }

    /// <summary>
    /// A clause a person decided against the verse is that person's judgement and not a reading:
    /// stored as manual, with no confidence, credited to whoever decided it. The credit still begins
    /// the way this loader's rows begin, so the guards that find its rows find this one too.
    /// </summary>
    [Fact]
    public async Task AClauseAPersonDecidedIsStoredAsTheirJudgement()
    {
        var outcome = await Load("decided");

        outcome.Clauses.Should().Be(1, "a decided clause needs no confidence to be kept");

        var clause = await _db.EntityDescriptors.SingleAsync(d => d.Entity!.Slug == "hobab-1");
        clause.Method.Should().Be(LinkMethod.Manual);
        clause.Confidence.Should().BeNull();
        clause.Source.Should().Be(EntityDescriptorLoader.SourcePrefix + " the project owner, decided 2026-09-11");
    }

    /// <summary>
    /// A decision the owner adds later is written into the same file under the same name, because the
    /// command that writes his decisions rewrites its files in place. A load over a corpus that
    /// already holds the first version has to see that the record changed, not only which file it is in.
    /// </summary>
    [Fact]
    public async Task ADecisionAddedToARecordInTheSameFileReachesACorpusAlreadyLoaded()
    {
        (await Load("decided")).Clauses.Should().Be(1);

        var again = await Load("decided-again");

        again.Superseded.Should().Be(1, "the record in decided.jsonl is not the one loaded");
        again.Skipped.Should().Be(0);
        var clauses = await _db.EntityDescriptors
            .Where(d => d.Entity!.Slug == "hobab-1")
            .OrderBy(d => d.Ordinal)
            .ToListAsync();
        clauses.Select(c => c.Relation).Should().Equal("son-of", "father-in-law-of");
        clauses.Should().OnlyContain(c => c.Method == LinkMethod.Manual && c.Run!.StartsWith("decided.jsonl#"));

        (await Load("decided-again")).Skipped.Should().Be(1, "loading the same record twice writes it once");
    }

    /// <summary>
    /// The owner sets an agent to decide some facts for him. Its decision is its reading of the verse,
    /// and the credit says so: an agent on his instruction, never the owner himself.
    /// </summary>
    [Fact]
    public async Task AClauseAnAgentDecidedIsCreditedToTheAgentAndNotToTheOwner()
    {
        (await Load("decided-by-agent")).Clauses.Should().Be(1);

        var clause = await _db.EntityDescriptors.SingleAsync(d => d.Entity!.Slug == "hobab-1");
        clause.Method.Should().Be(LinkMethod.Manual);
        clause.Source.Should().Be(EntityDescriptorLoader.SourcePrefix +
                                  " an agent reading NUM 10:29 on the project owner's instruction, decided 2026-09-26");
        clause.Source.Should().NotContain("the project owner, decided");
    }

    /// <summary>
    /// The owner can accept a fact while saying the verse does not settle it. That is still their
    /// judgement, so it stays manual, but the doubt is kept as the confidence they gave.
    /// </summary>
    [Fact]
    public async Task AClauseAPersonAcceptedAsUnsureKeepsTheirDoubt()
    {
        var outcome = await Load("decided-unsure");

        outcome.Clauses.Should().Be(1);

        var clause = await _db.EntityDescriptors.SingleAsync(d => d.Entity!.Slug == "hobab-1");
        clause.Method.Should().Be(LinkMethod.Manual);
        clause.Confidence.Should().Be(0.5);
    }

    /// <summary>
    /// A passage of up to three verses is one statement when it names both people: the clause is
    /// addressed at its first verse and keeps the whole citation, and so does the relationship read
    /// off it, which is what lets the page show the passage rather than a verse naming neither.
    /// </summary>
    [Fact]
    public async Task APassageNamingBothPeopleIsCitedWhole()
    {
        (await Load("cited")).Clauses.Should().Be(2);

        var clause = await _db.EntityDescriptors.SingleAsync(d => d.Entity!.Slug == "jezreel-1");
        (clause.CanonicalBook, clause.CanonicalChapter, clause.CanonicalVerse).Should().Be((28, 1, 2));
        clause.Citation.Should().Be("HOS 1:2-4");
        clause.Confidence.Should().BeNull("a passage that states the tie is a statement");

        await new OwnRelationshipLoader(_db, NullLogger<OwnRelationshipLoader>.Instance).Load();
        var jezreel = await _db.Entities.SingleAsync(e => e.Slug == "jezreel-1");
        var row = (await Relationships.Of(_db, jezreel.Id, null, default)).Single(r => r.Slug == "hosea-1");
        row.Reference!.Verse.Should().Be(2);
        row.Verses!.Select(v => (v.Book, v.Chapter, v.Verse)).Should().Equal(
            ("Hosea", 1, 2), ("Hosea", 1, 3), ("Hosea", 1, 4));

        var described = await Read("jezreel-1", null);
        described!.Claims.Single().Verses!.Select(v => v.Verse).Should().Equal(2, 3, 4);
    }

    /// <summary>
    /// Two verses each stating a part are cited together, and what a reader joins is never stored as
    /// though a verse said it: the owner's decision keeps his credit and carries the composed confidence.
    /// </summary>
    [Fact]
    public async Task TwoVersesComposedAreCitedTogetherBelowAStatement()
    {
        (await Load("cited")).Clauses.Should().Be(2);

        var clause = await _db.EntityDescriptors.SingleAsync(d => d.Entity!.Slug == "athaliah-1");
        clause.Method.Should().Be(LinkMethod.Manual);
        clause.Confidence.Should().Be(Citation.ComposedConfidence);
        (clause.CanonicalBook, clause.CanonicalChapter, clause.CanonicalVerse).Should().Be((12, 8, 18));
        clause.Citation.Should().Be("2KI 8:18; 2KI 8:26");

        await new OwnRelationshipLoader(_db, NullLogger<OwnRelationshipLoader>.Instance).Load();
        var athaliah = await _db.Entities.SingleAsync(e => e.Slug == "athaliah-1");
        var row = (await Relationships.Of(_db, athaliah.Id, null, default)).Single(r => r.Slug == "jehoram-1");
        row.Confidence.Should().Be(Citation.ComposedConfidence);
        row.Verses!.Select(v => v.Verse).Should().Equal(18, 26);
    }

    /// <summary>
    /// What the owner allowed and no more: a passage naming only one of the two, one longer than three
    /// verses, two verses neither of which names the subject, and three verses joined are refused.
    /// </summary>
    [Fact]
    public async Task ACitationBeyondWhatTheOwnerAllowedIsRefused()
    {
        var outcome = await Load("cited-refused");

        outcome.Refused.UnmatchedReference.Should().Be(4);
        outcome.Clauses.Should().Be(0);
    }

    [Theory]
    [InlineData("NUM 10:29", false, 1)]
    [InlineData("HOS 1:2-4", false, 3)]
    [InlineData("2KI 8:18; 2KI 8:26", true, 2)]
    [InlineData("NEH 11:15; 1CH 9:14", true, 2)]
    [InlineData("PSA 7:0", false, 1)]
    public void ACitationIsOneVerseAPassageOrTwoVerses(string text, bool composed, int verses)
    {
        var citation = Citation.Parse(text);

        citation.Should().NotBeNull();
        citation!.Composed.Should().Be(composed);
        citation.Verses.Should().HaveCount(verses);
    }

    [Theory]
    [InlineData("HOS 1:1-4")]
    [InlineData("HOS 1:4-2")]
    [InlineData("HOS 1:2-2:1")]
    [InlineData("2KI 8:18; 2KI 8:18")]
    [InlineData("2KI 8:18-19; 2KI 8:26")]
    [InlineData("2KI 8:16; 2KI 8:18; 2KI 8:26")]
    [InlineData("XYZ 1:1")]
    [InlineData("")]
    public void WhatIsNotACitationIsRefused(string text) =>
        Citation.Parse(text).Should().BeNull();

    /// <summary>
    /// The other loader writes this table too, and a name has one row per language and case. Until
    /// a re-ask existed the whole entity was skipped and the two could not meet; now they can, and
    /// the form already standing is the one that stays — this pass produced its forms in passing
    /// while the other was asked for the name alone.
    /// </summary>
    [Fact]
    public async Task AReAskLeavesAFormAnotherPassAlreadyHolds()
    {
        await Load("reasked-first");

        var hobab = await _db.Entities.SingleAsync(e => e.Slug == "hobab-1");
        _db.EntityNameForms.RemoveRange(
            _db.EntityNameForms.Where(f => f.EntityId == hobab.Id && f.Language == "ukr"));
        await _db.SaveChangesAsync();
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = hobab.Id,
            Language = "ukr",
            GrammaticalCase = GrammaticalCases.Genitive,
            Form = "Ховава з іншого проходу",
            Method = LinkMethod.ModelReading,
            Confidence = 1,
            Source = "declined on this machine by a local model",
        });
        await _db.SaveChangesAsync();

        var again = await Load("reasked-again");

        again.Superseded.Should().Be(1);

        var genitive = await _db.EntityNameForms.SingleAsync(f =>
            f.EntityId == hobab.Id && f.Language == "ukr"
            && f.GrammaticalCase == GrammaticalCases.Genitive);

        genitive.Form.Should().Be("Ховава з іншого проходу");
        genitive.Source.Should().StartWith("declined on this machine");
    }

    /// <summary>
    /// And the ordinary restart is still a no-op. The guard is on what the pass said and when, not
    /// on the entity, so the same files loaded twice leave the corpus exactly as it was.
    /// </summary>
    [Fact]
    public async Task TheSamePassLoadedTwiceChangesNothing()
    {
        await Load("reasked-first");
        var again = await Load("reasked-first");

        again.Skipped.Should().Be(1);
        again.Superseded.Should().Be(0);
        again.Forgotten.Should().Be(0);
        again.Clauses.Should().Be(0);
        again.AlreadyLoaded.Should().BeTrue();

        (await _db.EntityDescriptors.CountAsync(d => d.Entity!.Slug == "hobab-1")).Should().Be(1);
    }

    /// <summary>
    /// The pass read Luke 3:1 while the dataset still filed it under Herodias's husband, so its clauses
    /// there name him. The verse is the tetrarch's now: a clause about the husband at it cites a verse
    /// he is not named in, and a clause pointing at him from it points at the tetrarch.
    /// </summary>
    [Fact]
    public async Task AClauseAtAVerseMovedToANamesakeFollowsTheVerse()
    {
        Add("herod-2", EntityKind.Person, "Herod", null, (40, 14, 3), (42, 3, 1));
        Add("ituraea", EntityKind.Place, "Ituraea", null, (42, 3, 1));
        Add("philip-2", EntityKind.Person, "Philip", null, (40, 14, 3));
        Add("philip-4", EntityKind.Person, "Philip", null, (42, 3, 1));
        await _db.SaveChangesAsync();
        await _db.Entities.Where(e => e.Slug == "philip-2")
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.SourceId, "person:Philip_2"));
        await _db.Entities.Where(e => e.Slug == "philip-4")
            .ExecuteUpdateAsync(set => set.SetProperty(e => e.SourceId, "essenthos:philip3"));

        var outcome = await Load("refiled");

        outcome.Refused.UnmatchedReference.Should().Be(1, "the husband is not named at Luke 3:1");
        var brothers = await _db.EntityDescriptors
            .Where(d => d.Entity!.Slug == "herod-2")
            .Select(d => new { d.CanonicalBook, Target = d.Target!.Slug })
            .ToListAsync();
        brothers.Should().BeEquivalentTo([
            new { CanonicalBook = 40, Target = "philip-2" },
            new { CanonicalBook = 42, Target = "philip-4" },
        ]);
    }

    /// <summary>
    /// The companion rule against real verse text, which is what it never had. Every test before this
    /// one ran with no text loaded, so the only branch ever exercised was "cannot check" — and a rule
    /// looking for the King James under the wrong name passed all of them while refusing nothing on
    /// the corpus. The words are seeded without a normalised form on purpose, so the test also
    /// holds the rule to reading the printed word where there is nothing else.
    /// </summary>
    [Fact]
    public async Task ACompanionTheVerseDoesNotSpeakOfIsRefusedAndOneItDoesIsKept()
    {
        Corpus.Add(_db, Bible4uTextSource.KingJames, TextKind.Translation, "eng",
            (5, 1, ["my", "brother", "and", "companion", "in", "labour"]),
            (5, 2, ["Shallum", "Amariah", "and", "Joseph"]));
        Add("epaphroditus-1", EntityKind.Person, "Epaphroditus", null, (1, 5, 1));
        Add("paul-1", EntityKind.Person, "Paul", null, (1, 5, 1));
        Add("shallum-1", EntityKind.Person, "Shallum", null, (1, 5, 2));
        Add("amariah-1", EntityKind.Person, "Amariah", null, (1, 5, 2));
        await _db.SaveChangesAsync();

        var outcome = await Load("companions");

        outcome.Refused.Unaccompanied.Should().Be(1, "Shallum, Amariah, and Joseph is a list");
        (await _db.EntityDescriptors.CountAsync(d => d.Relation == DescriptorRelations.CompanionOf))
            .Should().Be(1, "my brother and companion in labour speaks of company");
    }

    /// <summary>
    /// A brook is nobody's companion: <em>David ... came to the brook Besor</em> (1SA 30:9) names a
    /// place where he was, and a relation of company is said of someone. The man beside him at the
    /// same verse is kept.
    /// </summary>
    [Fact]
    public async Task ARelationItsSubjectCannotHoldIsRefused()
    {
        Corpus.Add(_db, Bible4uTextSource.KingJames, TextKind.Translation, "eng",
            (5, 1, ["David", "and", "the", "men", "that", "were", "with", "him", "came", "to", "Besor"]));
        Add("besor-1", EntityKind.Place, "Besor", null, (1, 5, 1));
        Add("abiathar-1", EntityKind.Person, "Abiathar", null, (1, 5, 1));
        Add("david-1", EntityKind.Person, "David", null, (1, 5, 1));
        await _db.SaveChangesAsync();

        var outcome = await Load("kinds");

        outcome.Refused.Inadmissible.Should().Be(1, "a brook is not anybody's companion");
        (await _db.EntityDescriptors.Select(d => d.Entity!.Slug).ToListAsync()).Should().Equal("abiathar-1");
    }

    /// <summary>
    /// Every relation of the vocabulary says what it can be said of, once. A relation missing from
    /// the table would be refused of everything, and one in two rows would be read two ways.
    /// </summary>
    [Fact]
    public void EveryRelationNamesTheKindsItCanBeSaidOf()
    {
        foreach (var relation in DescriptorRelations.All)
        {
            DescriptorSubjects.Of(relation).Should().NotBeEmpty($"{relation} has to be said of something");
        }

        DescriptorSubjects.Admits(DescriptorRelations.CompanionOf, EntityKind.Place).Should().BeFalse();
        DescriptorSubjects.Admits(DescriptorRelations.CityIn, EntityKind.Place).Should().BeTrue();
        DescriptorSubjects.Admits(DescriptorRelations.DescendantsOf, EntityKind.Person).Should().BeFalse();
    }

    [Fact]
    public async Task ADescriptionIsTheClausesTheFileStatesInTheOrderItStatesThem()
    {
        var outcome = await Load("described");

        outcome.Described.Should().Be(3);
        outcome.Clauses.Should().Be(4);
        outcome.Unresolved.Should().Be(1, "Hobab's pass could not place the Kenites");

        var clauses = await _db.EntityDescriptors
            .Include(d => d.Entity).Include(d => d.Target)
            .Where(d => d.Entity!.Slug == "hobab-1")
            .OrderBy(d => d.Ordinal)
            .ToListAsync();

        clauses.Select(d => (d.Ordinal, d.Relation, d.Target!.Slug)).Should().Equal(
            (1, DescriptorRelations.SonOf, "reuel-1"),
            (2, DescriptorRelations.FatherInLawOf, "moses-1"));
        clauses.Select(d => (d.CanonicalBook, d.CanonicalChapter, d.CanonicalVerse))
            .Should().Equal((4, 10, 29), (7, 4, 11));
    }

    /// <summary>
    /// Who said it travels onto every row, and onto a claim of its own beside it, so that a second
    /// method agreeing later has somewhere to go instead of overwriting the first.
    /// </summary>
    [Fact]
    public async Task EveryClauseCarriesTheModelAndTheDateAndAClaimOfItsOwn()
    {
        await Load("described");

        var clauses = await _db.EntityDescriptors.Include(d => d.Claims).ToListAsync();

        clauses.Should().OnlyContain(d => d.Method == LinkMethod.ModelReading);
        clauses.Should().OnlyContain(d => d.Confidence != null);
        clauses.Should().OnlyContain(d => d.Source.Contains(Model) && d.Source.Contains(AskedAt));
        clauses.Should().OnlyContain(d => d.Claims.Count == 1);
        clauses.SelectMany(d => d.Claims).Should()
            .OnlyContain(c => c.Method == LinkMethod.ModelReading && c.Confidence != null);
    }

    [Fact]
    public async Task TheEnglishLineNamesEveryTargetAndLinksIt()
    {
        await Load("described");

        var description = await Read("hobab-1", DescriptorPhrasings.English);

        Line(description).Should().Be("son of Reuel, father-in-law of Moses");
        description!.Parts.Where(p => p.Entity is not null).Select(p => p.Entity!.Slug)
            .Should().Equal("reuel-1", "moses-1");
        description.Claims.Select(c => c.Reference.Slug).Should().Equal("numbers", "judges");
    }

    /// <summary>
    /// The case the whole layer was asked for: <em>тесть Мойсея</em> and not <em>тесть Мойсей</em>,
    /// with the name coming from the form the pass produced rather than from a stemmer.
    /// </summary>
    [Fact]
    public async Task UkrainianPutsTheTargetInTheGenitive()
    {
        await Load("described");

        var description = await Read("hobab-1", DescriptorPhrasings.Ukrainian);

        Line(description).Should().Be("син Регуїла, тесть Мойсея");
        description!.Language.Should().Be(DescriptorPhrasings.Ukrainian);
    }

    /// <summary>
    /// The other case the owner asked for by name: hovering <em>моавітяни</em> produces
    /// <em>нащадки Моава</em>, and Moab is a link.
    /// </summary>
    [Fact]
    public async Task APeopleIsTheDescendantsOfItsEponymAndTheEponymIsALink()
    {
        await Load("described");

        var description = await Read("moabites", DescriptorPhrasings.Ukrainian);

        Line(description).Should().Be("нащадки Моава");
        description!.Parts.Should().ContainSingle(p => p.Entity != null)
            .Which.Entity!.Slug.Should().Be("moab-1");
    }

    /// <summary>
    /// The one that produces a visibly wrong sentence if it is got wrong. A client that asked for
    /// Russian, was handed English and was told <em>rus</em> would put another language's grammar
    /// around these names, which is what the fallback exists to prevent.
    /// </summary>
    [Fact]
    public async Task ALanguageTheEncyclopediaDoesNotSpeakIsAnsweredInEnglishAndSaysSo()
    {
        await Load("described");

        var description = await Read("hobab-1", "rus");

        description!.Language.Should().Be(DescriptorPhrasings.English);
        Line(description).Should().Be("son of Reuel, father-in-law of Moses");
        description.Claims.Should().OnlyContain(c => c.Target.Name == c.Target.EnglishName);
    }

    /// <summary>
    /// A target arrives complete: what kind of page it is, what it is called here, and the cases
    /// this language puts it in. A hover card cannot fetch the record of every name in its own line,
    /// so anything the client needs to draw the link has to be in the answer already.
    /// </summary>
    [Fact]
    public async Task EveryTargetCarriesItsKindItsOwnNameAndTheCasesThePassProduced()
    {
        await Load("described");

        var description = await Read("hobab-1", DescriptorPhrasings.Ukrainian);

        var moses = description!.Claims
            .Should().ContainSingle(c => c.Relation == DescriptorRelations.FatherInLawOf)
            .Which.Target;

        moses.Kind.Should().Be("person", "a slug does not say which page to route to");
        moses.Slug.Should().Be("moses-1");
        moses.Name.Should().Be("Мойсей", "the nominative in the language being rendered");
        moses.EnglishName.Should().Be("Moses", "what a case the pass did not produce falls back to");
        moses.Forms.Should().Equal(new Dictionary<string, string>
        {
            [GrammaticalCases.Nominative] = "Мойсей",
            [GrammaticalCases.Genitive] = "Мойсея",
        });
    }

    /// <summary>
    /// The locative gap, end to end: <em>місто в Юдеї</em> and not <em>місто в Юдея</em>. It is here
    /// because the locative is what proves the forms are keyed by case rather than a nominative and
    /// a genitive under other names — a third case cost a phrasing and a generation pass, and
    /// nothing on the wire.
    /// </summary>
    [Fact]
    public async Task APlaceIsPutInTheLocativeAndTheFormsArriveKeyedByCase()
    {
        await Load("described");

        var description = await Read("bethlehem-1", DescriptorPhrasings.Ukrainian);

        Line(description).Should().Be("місто в Юдеї");
        description!.Claims.Should().ContainSingle().Which.Target.Forms!.Keys
            .Should().BeEquivalentTo(GrammaticalCases.All);
    }

    /// <summary>
    /// The four place clauses — <c>lived-in</c>, <c>buried-in</c>, <c>city-in</c> and
    /// <c>mountain-in</c> — which are what a place page is made of: each puts its target in the
    /// locative and none of them can be said with the genitive. <em>жив у Хевроні</em>, not
    /// <em>жив у Хеврона</em>, and not the <em>мешканець Хеврона</em> the phrasing said while
    /// nothing produced a locative — that is an inhabitant rather than someone who lived there.
    /// </summary>
    [Fact]
    public async Task EveryPlaceClauseIsPutInTheLocativeInUkrainian()
    {
        await Load("located");

        Line(await Read("abraham-1", DescriptorPhrasings.Ukrainian))
            .Should().Be("жив у Хевроні, похований у Хевроні");
        Line(await Read("nebo-1", DescriptorPhrasings.Ukrainian)).Should().Be("гора в Моаві");
    }

    /// <summary>
    /// The other path, and the one that matters today: 727 entities' worth of claims have been
    /// generated and none of them carries a locative yet, so this is what almost every place clause
    /// renders as until a second pass over the name forms fills the locative in.
    ///
    /// The assertion that makes it worth writing is the second one. Nod has a genitive, and the
    /// clause does not reach for it — <em>жив у Нода</em> reads as Ukrainian and is not Ukrainian,
    /// while <em>жив у Nod</em> is visibly a gap.
    /// </summary>
    [Fact]
    public async Task APlaceWithNoLocativeFallsBackToTheEnglishNameAndNeverToTheGenitive()
    {
        await Load("located");

        var description = await Read("cain-1", DescriptorPhrasings.Ukrainian);

        Line(description).Should().Be("жив у Nod");
        description!.Claims.Should().ContainSingle().Which.Target.Forms.Should().Equal(
            new Dictionary<string, string>
            {
                [GrammaticalCases.Nominative] = "Нод",
                [GrammaticalCases.Genitive] = "Нода",
            },
            "the genitive is there and is deliberately not what the locative falls back to");
    }

    /// <summary>
    /// The clauses the closed vocabulary could not state until it was widened, rendered rather than
    /// refused. Lot is the page this is measured on: the two things most often said about him are
    /// that he is Haran's son and Abram's nephew, and only the first of them could be written.
    /// </summary>
    [Fact]
    public async Task TheWidenedVocabularyStatesWhatTheNarrowOneRefused()
    {
        var outcome = await Load("widened");

        outcome.Refused.UnknownRelation.Should().Be(0);

        Line(await Read("lot-1", DescriptorPhrasings.English))
            .Should().Be("nephew of Abram, son of Haran");
        Line(await Read("abram-1", DescriptorPhrasings.Ukrainian)).Should().Be("дядько Лота");
        Line(await Read("azrikam-4", DescriptorPhrasings.Ukrainian))
            .Should().Be("намісник Єрусалима, загинув від руки Зіхрі");
        Line(await Read("zichri-1", DescriptorPhrasings.Ukrainian)).Should().Be("вбивця Азрікама");
    }

    /// <summary>
    /// The same clauses in the two languages the encyclopedia gained when it stopped speaking
    /// Russian. German declines the target and Spanish does not, which is the whole difference
    /// between the two tables.
    ///
    /// <para>
    /// The homicide pair is what both languages had to be argued about: <em>Mörder</em> and
    /// <em>asesino</em> would convict, and the relation says only who killed whom, so both
    /// languages say it with a verb and neither asks for a case its pass does not produce.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheWidenedVocabularyIsSaidInGermanAndInSpanish()
    {
        await Load("widened");

        Line(await Read("lot-1", DescriptorPhrasings.German))
            .Should().Be("Neffe Abrams, Sohn Harans");
        Line(await Read("lot-1", DescriptorPhrasings.Spanish))
            .Should().Be("sobrino de Abram, hijo de Harán");

        Line(await Read("azrikam-4", DescriptorPhrasings.German))
            .Should().Be("Statthalter Jerusalems, getötet von Sichri");
        Line(await Read("zichri-1", DescriptorPhrasings.German)).Should().Be("tötete Asrikam");
        Line(await Read("zichri-1", DescriptorPhrasings.Spanish))
            .Should().Be("dio muerte a Azricam");

        Line(await Read("eastgate-1", DescriptorPhrasings.German))
            .Should().Be("ein Tor Jerusalems");
        Line(await Read("eastgate-1", DescriptorPhrasings.Spanish))
            .Should().Be("una puerta de Jerusalén");
    }

    /// <summary>
    /// The doubled preposition, on the page it was reported from. Lot's three clauses are what the
    /// corpus holds for him today, and the third of them read <em>жив у в околиці Йорданській</em>,
    /// because the phrasing supplies <em>у</em> and the form the pass wrote supplied <em>в</em> as
    /// well.
    ///
    /// The preposition comes off the form on the way in rather than off the line at render time:
    /// nothing at render time knows which words of a form are the name, and <em>у Хеврона</em>
    /// cannot be told from <em>у</em> plus <em>Хеврона</em> without knowing the answer already.
    /// </summary>
    [Fact]
    public async Task APlaceWhoseFormCarriesItsOwnPrepositionIsStillSaidWithOne()
    {
        await Load("doubled");

        Line(await Read("lot-1", DescriptorPhrasings.Ukrainian))
            .Should().Be("син Гарана, нащадок Тераха, жив у околиці Йорданській");
    }

    /// <summary>
    /// A gate belongs to a city rather than standing in one, so it takes the genitive and not the
    /// locative the four clauses a place page is otherwise made of take.
    /// </summary>
    [Fact]
    public async Task AGateIsSaidToBeItsCitysAndNotToBeInIt()
    {
        await Load("widened");

        Line(await Read("eastgate-1", DescriptorPhrasings.Ukrainian))
            .Should().Be("брама Єрусалима");
        Line(await Read("eastgate-1", DescriptorPhrasings.English))
            .Should().Be("a gate of Jerusalem");
    }

    /// <summary>
    /// The other path, and the one every new relation is on until a pass produces forms for its
    /// targets: a target with no genitive renders as the English name. <em>син Харана</em> would
    /// read as Ukrainian and be a guess; <em>син Haran</em> is visibly a gap.
    /// </summary>
    [Fact]
    public async Task ANewRelationWithNoFormForItsTargetFallsBackToTheEnglishName()
    {
        await Load("widened");

        Line(await Read("lot-1", DescriptorPhrasings.Ukrainian))
            .Should().Be("племінник Аврама, син Haran");
    }

    /// <summary>
    /// A verse reference the client can link. <c>GEN 35:19</c> is a corpus code and no client holds
    /// a map from it, so the address arrives resolved the way every other reference in v1 is.
    /// </summary>
    [Fact]
    public async Task AReferenceArrivesAsABookASlugAnOrdinalAChapterAndAVerse()
    {
        await Load("described");

        var description = await Read("bethlehem-1", DescriptorPhrasings.English);

        description!.Claims.Should().ContainSingle().Which.Reference
            .Should().Be(new VerseRefResponse(1, "Genesis", "genesis", 35, 19));
    }

    /// <summary>
    /// A form the pass did not produce is a gap, and the gap shows as the English name. Inflecting
    /// it here would be a guess a reader could not tell from a form somebody wrote.
    /// </summary>
    [Fact]
    public async Task AMissingFormFallsBackToTheEnglishNameRatherThanAnInventedInflection()
    {
        await Load("refused");

        Line(await Read("jethro-1", DescriptorPhrasings.Ukrainian)).Should().Be("тесть Moses");
    }

    /// <summary>
    /// Below the confidence at which the corpus states something plainly, the clause is kept and
    /// marked. Dropping it would make the description read as more certain than it is.
    /// </summary>
    [Fact]
    public async Task AClauseBelowTheFloorIsKeptAndMarked()
    {
        await Load("refused");

        var description = await Read("jethro-1", DescriptorPhrasings.English);

        description!.Claims.Should().ContainSingle()
            .Which.Should().Match<DescriptorClaimResponse>(c => c.Doubtful && c.Confidence == 0.55);
        description.Parts.Should().OnlyContain(p => p.Doubtful);
    }

    /// <summary>
    /// The three refusals the descriptor contract asks for, and the fourth the provenance rules
    /// force: a claim with no confidence cannot be stored as an inference, and storing it as
    /// testimony would be a model's guess wearing a source's name.
    /// </summary>
    [Fact]
    public async Task WhatCannotBeRenderedOrCheckedIsRefusedAndCounted()
    {
        var outcome = await Load("refused");

        outcome.Refused.UnknownRelation.Should().Be(1, "shepherd-of is not in the vocabulary");
        outcome.Refused.UnresolvedTarget.Should().Be(1, "no entity answers to zipporah-9");
        outcome.Refused.UnmatchedReference.Should().Be(1, "Jethro is not named in Genesis 1:1");
        outcome.Refused.WithoutConfidence.Should().Be(1);
        outcome.Refused.UnknownEntity.Should().Be(1, "the encyclopedia holds no such record");
        outcome.Refused.Total.Should().Be(5);

        outcome.Clauses.Should().Be(1);
        var clause = await _db.EntityDescriptors.SingleAsync();
        clause.Relation.Should().Be(DescriptorRelations.FatherInLawOf);
        clause.Ordinal.Should().Be(1, "the ordinals of what survives stay contiguous");
    }

    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        await Load("described");
        var before = await _db.EntityDescriptors.CountAsync();
        var forms = await _db.EntityNameForms.CountAsync();

        var again = await Load("described");

        again.Clauses.Should().Be(0);
        again.Forms.Should().Be(0);
        again.Skipped.Should().Be(7, "every record in the file names an entity already described");
        (await _db.EntityDescriptors.CountAsync()).Should().Be(before);
        (await _db.EntityNameForms.CountAsync()).Should().Be(forms);
    }

    /// <summary>
    /// A second batch, generated later, loads beside the first rather than being turned away by a
    /// guard that asks whether the table holds anything at all. That guard is what left a cold
    /// database with no name resolutions in it.
    /// </summary>
    [Fact]
    public async Task ALaterBatchLoadsBesideTheOneBeforeIt()
    {
        await Load("described");

        var outcome = await Load("refused");

        outcome.Clauses.Should().Be(1);
        (await _db.EntityDescriptors.CountAsync(d => d.Entity!.Slug == "jethro-1")).Should().Be(1);
        (await _db.EntityDescriptors.CountAsync(d => d.Entity!.Slug == "hobab-1")).Should().Be(2);
    }

    /// <summary>
    /// The imported sentence is left exactly as it was. It stops being what a reader is shown and
    /// does not stop being the record BibleData supplied — which is what the generated clauses are
    /// measured against, and the only description an entity nothing has been generated for has.
    /// </summary>
    [Fact]
    public async Task TheImportedSentenceIsNeitherReadNorWritten()
    {
        await Load("described");

        var hobab = await _db.Entities.SingleAsync(e => e.Slug == "hobab-1");
        hobab.Distinguisher.Should().Be(ImportedSentence);
        (await _db.Entities.CountAsync(e => e.Distinguisher != null)).Should().Be(1);
    }

    [Fact]
    public async Task NoDescriptorsOnDiskLoadsNothingAndSaysSo()
    {
        var outcome = await Load(Guid.NewGuid().ToString("N"));

        outcome.NoDescriptors.Should().BeTrue();
        outcome.Clauses.Should().Be(0);
    }

    /// <summary>
    /// Sending each target complete costs two queries for a whole page, not one per name.
    ///
    /// <para>
    /// It is asserted rather than assumed because the shape that reads a target's own forms inside
    /// the clause projection is exactly the shape that once took <c>/v1/corpora</c> from a
    /// millisecond to 46 seconds: a subquery in a projection, evaluated once per joined row.
    /// A count of commands is what tells the two apart, and it is invisible in an assertion about
    /// the answer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AWholePageOfDescriptionsCostsTwoQueriesAndNotOnePerName()
    {
        const int Described = 400;
        const int Page = 40;

        Seed(Described);
        var slugs = Enumerable.Range(0, Page).Select(i => $"cost-{i}").ToList();

        var counted = new CountingCommands();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_db.Database.GetConnectionString())
            .AddInterceptors(counted)
            .Options);

        // The first read of a context builds EF's model, which is tens of milliseconds and is not
        // what is being measured. The second is what a request pays.
        await Descriptors.Of(db, slugs, DescriptorPhrasings.Ukrainian, default);
        counted.Reset();

        var started = Stopwatch.GetTimestamp();
        var descriptions = await Descriptors.Of(
            db, slugs, DescriptorPhrasings.Ukrainian, default);
        var elapsed = Stopwatch.GetElapsedTime(started);

        foreach (var sql in counted.Sql)
        {
            _output.WriteLine(sql);
        }

        _output.WriteLine(await Plan(db, counted.Sql[0], slugs));
        _output.WriteLine(
            $"{Page} descriptions of {Described} entities in {elapsed.TotalMilliseconds:F1} ms, "
            + $"{counted.Commands} commands");

        descriptions.Should().HaveCount(Page);
        descriptions[slugs[0]].Claims.Should()
            .OnlyContain(c => c.Target.Forms!.Count == GrammaticalCases.All.Count);
        counted.Commands.Should().Be(2, "one read for the clauses and one for the name forms");
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// What Postgres does with the query EF actually sent, rather than with one written out again
    /// here — the second would go stale the first time the projection changed, which is the change
    /// this is watching for.
    /// </summary>
    private static async Task<string> Plan(AppDbContext db, string sql, IReadOnlyList<string> slugs)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN (ANALYZE, BUFFERS) {sql}";

        var wanted = command.CreateParameter();
        wanted.ParameterName = "wanted";
        wanted.Value = slugs.ToArray();
        command.Parameters.Add(wanted);

        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, plan);
    }

    /// <summary>
    /// A corpus-sized set of entities, each describing itself in three clauses and carrying its
    /// name in every case. The targets are spread across the whole set so the forms query cannot
    /// accidentally be answered from a handful of rows.
    /// </summary>
    private void Seed(int entities)
    {
        for (var i = 0; i < entities; i++)
        {
            Add($"cost-{i}", EntityKind.Person, $"Name {i}", null, (1, 1, 1));
        }

        _db.SaveChanges();

        var ids = _db.Entities
            .Where(e => e.Slug.StartsWith("cost-"))
            .ToDictionary(e => e.Slug, e => e.Id);

        for (var i = 0; i < entities; i++)
        {
            var id = ids[$"cost-{i}"];

            foreach (var (grammaticalCase, index) in GrammaticalCases.All.Select((c, n) => (c, n)))
            {
                _db.EntityNameForms.Add(new EntityNameForm
                {
                    EntityId = id,
                    Language = DescriptorPhrasings.Ukrainian,
                    GrammaticalCase = grammaticalCase,
                    Form = $"Ім'я {i}-{index}",
                    Method = LinkMethod.ModelReading,
                    Confidence = 0.9,
                    Source = "a test",
                });
            }

            for (var ordinal = 1; ordinal <= 3; ordinal++)
            {
                _db.EntityDescriptors.Add(new EntityDescriptor
                {
                    EntityId = id,
                    Ordinal = ordinal,
                    Relation = DescriptorRelations.SonOf,
                    TargetEntityId = ids[$"cost-{(i + ordinal * 97) % entities}"],
                    CanonicalBook = 1,
                    CanonicalChapter = 1,
                    CanonicalVerse = 1,
                    Method = LinkMethod.ModelReading,
                    Confidence = 0.9,
                    Source = "a test",
                });
            }
        }

        _db.SaveChanges();
    }

    /// <summary>How many round trips a read actually made, which no assertion about its answer says.</summary>
    private sealed class CountingCommands : DbCommandInterceptor
    {
        private int _commands;

        public int Commands => _commands;

        public List<string> Sql { get; } = [];

        public void Reset()
        {
            Interlocked.Exchange(ref _commands, 0);
            Sql.Clear();
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _commands);
            Sql.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
