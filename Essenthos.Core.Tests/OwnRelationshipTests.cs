using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which relationships an entity page draws once the clauses this corpus wrote for itself reach the
/// table it draws from.
///
/// The cases are the ones where two clauses about one pair come apart: where the owner decided what
/// a model read otherwise, where two readings answer one question two ways, where they are not
/// answering the same question at all, and where a reading is held back by name.
///
/// <para>
/// Asked of Postgres because part of what is under test is what the database will accept: a
/// relationship read here without the verse it was read from must be refused.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRelationshipTests : IDisposable
{
    private const string Model = EntityDescriptorLoader.SourcePrefix + " a test, asked 2026-09-09";

    private const string Owner = EntityDescriptorLoader.SourcePrefix + " the project owner, decided 2026-09-12";

    private readonly AppDbContext _db;
    private readonly OwnRelationshipLoader _loader;

    public OwnRelationshipTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new OwnRelationshipLoader(_db, NullLogger<OwnRelationshipLoader>.Instance);
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    /// <summary>
    /// The plain case, and the reason the table is worth writing into at all: a clause becomes a
    /// relationship, credited to the pass that read it.
    /// </summary>
    [Fact]
    public async Task AClauseBecomesARelationship()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Clause(lot, haran, DescriptorRelations.SonOf, 0.95);

        (await Load()).Written.Should().Be(1);

        var written = await _db.EntityRelationships.SingleAsync();
        written.Type.Should().Be(DescriptorRelations.SonOf);
        written.Category.Should().Be(RelationshipCategories.Read);
        written.Method.Should().Be(LinkMethod.ModelReading);
        written.Confidence.Should().Be(0.95);
        Datasets.Of(written.Source).Should().Be(Datasets.Own);
    }

    /// <summary>
    /// A decision outranks a reading, so where the two answer one question two ways the decision
    /// stands and the reading is withheld: the page never shows Reuben as Joseph's brother and his
    /// half-brother at once.
    /// </summary>
    [Fact]
    public async Task AReadingAnsweringTheOwnersDecisionDifferentlyIsWithheld()
    {
        var reuben = Person("reuben");
        var joseph = Person("joseph");
        Decided(reuben, joseph, DescriptorRelations.HalfBrotherOf);
        Clause(reuben, joseph, DescriptorRelations.BrotherOf, 0.85);

        var outcome = await Load();
        outcome.Written.Should().Be(1);
        outcome.Withheld.Should().Be(1);

        var kept = await _db.EntityRelationships.SingleAsync();
        kept.Type.Should().Be(DescriptorRelations.HalfBrotherOf);
        kept.Method.Should().Be(LinkMethod.Manual);
        kept.Source.Should().Be(Owner);
    }

    /// <summary>
    /// A looser word is a different answer where the closer one is settled. The owner decided
    /// <em>son of</em>, and a reading of <em>descendant of</em> says less than he did, so it gives
    /// way as any other disagreement does.
    /// </summary>
    [Fact]
    public async Task AReadingLooserThanTheDecisionGivesWay()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Decided(lot, haran, DescriptorRelations.SonOf);
        Clause(lot, haran, DescriptorRelations.DescendantOf, 0.95);

        var outcome = await Load();
        outcome.Withheld.Should().Be(1);
        (await Relations(lot, haran)).Should().Equal(DescriptorRelations.SonOf);
    }

    /// <summary>
    /// And a closer one is not a different answer. The owner decided only that Lot descends from
    /// Haran, and a verse read as <em>son of</em> says everything he said and more.
    /// </summary>
    [Fact]
    public async Task AReadingCloserThanTheDecisionIsWrittenBesideIt()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Decided(lot, haran, DescriptorRelations.DescendantOf);
        Clause(lot, haran, DescriptorRelations.SonOf, 0.95);

        var outcome = await Load();
        outcome.Withheld.Should().Be(0);
        (await Relations(lot, haran)).Should().Equal(DescriptorRelations.DescendantOf, DescriptorRelations.SonOf);
    }

    /// <summary>
    /// Not everything that differs disagrees. The commander of Judah is also of the tribe of Judah,
    /// and treating a second kind of claim about one pair as a contradiction would withhold a true
    /// clause for saying something the first simply did not say.
    /// </summary>
    [Fact]
    public async Task AClauseAboutSomethingElseEntirelyStandsBesideTheFirst()
    {
        var nahshon = Person("nahshon");
        var judah = Person("judah");
        Clause(nahshon, judah, DescriptorRelations.DescendantOf, 0.95);
        Clause(nahshon, judah, DescriptorRelations.OfTribe, 0.9);

        (await Load()).Written.Should().Be(2);
    }

    /// <summary>
    /// The Zechariah case at the pair. Two claims of equal standing and equal confidence answer one
    /// question two ways, so the corpus says it does not know and writes nothing — which is what a
    /// reader is already shown at a word, and the reason they can trust the line when it appears.
    /// </summary>
    [Fact]
    public async Task NothingIsWrittenWhereTwoEqualClaimsAnswerOneQuestionTwoWays()
    {
        var one = Person("joel-5");
        var two = Person("izrahiah");
        Clause(one, two, DescriptorRelations.SonOf, 0.9);
        Clause(one, two, DescriptorRelations.DescendantOf, 0.9);

        var outcome = await _loader.Load([], default);
        outcome.Written.Should().Be(0);
        outcome.Disputed.Should().Be(1);
    }

    /// <summary>
    /// Marriage is not an answer to the question descent answers. Abram is Sarai's husband and her
    /// half-brother, both what the text says, and reading the two as one contradiction would
    /// withhold one of them.
    /// </summary>
    [Fact]
    public async Task AMarriageAndAKinshipOverOnePairAreTwoFactsAndNotTwoAnswers()
    {
        var abram = Person("abram");
        var sarai = Person("sarai");
        Clause(abram, sarai, DescriptorRelations.HusbandOf, 0.9);
        Clause(abram, sarai, DescriptorRelations.HalfBrotherOf, 0.9);

        var outcome = await Load();
        outcome.Written.Should().Be(2);
        outcome.Disputed.Should().Be(0);
    }

    /// <summary>
    /// A pair is read from its own subject's end. Lot is a descendant of Terah on Lot's record and
    /// Terah is Lot's grandfather on Terah's: one pass wrote both, and setting them against each
    /// other would be a witness disputing itself.
    /// </summary>
    [Fact]
    public async Task TheTwoEndsOfAPairAreNotSetAgainstEachOther()
    {
        var lot = Person("lot");
        var terah = Person("terah");
        Clause(lot, terah, DescriptorRelations.DescendantOf, 0.9);
        Clause(terah, lot, DescriptorRelations.GrandfatherOf, 0.95);

        var outcome = await Load();
        outcome.Written.Should().Be(2);
        outcome.Withheld.Should().Be(0);
    }

    /// <summary>
    /// A reading the list names is not written, and what else the record says is. Every clause is
    /// read on every load, and the reading stays unwritten each time.
    /// </summary>
    [Fact]
    public async Task AReadingTheListNamesIsHeldBack()
    {
        var elioenai = Person("elioenai");
        var hodaviah = Person("hodaviah");
        var machir = Person("machir");
        var maacah = Person("maacah-5");
        var gilead = Person("gilead");
        Clause(elioenai, hodaviah, DescriptorRelations.AncestorOf, 0.85);
        Clause(machir, maacah, DescriptorRelations.WifeOf, 0.85);
        Clause(machir, gilead, DescriptorRelations.FatherOf, 0.9);
        WithheldClause[] held =
        [
            new("elioenai", DescriptorRelations.AncestorOf, "hodaviah", "1CH 3:24", null),
            new("machir", DescriptorRelations.WifeOf, "maacah-5", "1CH 7:15", null),
        ];

        var outcome = await _loader.Load(held, default);

        outcome.Held.Should().Be(2);
        outcome.Written.Should().Be(1);
        (await _db.EntityRelationships.SingleAsync()).Type.Should().Be(DescriptorRelations.FatherOf);

        var again = await _loader.Load(held, default);
        again.AlreadyLoaded.Should().BeTrue();
        again.Held.Should().Be(2, "both clauses are read again and held again");
        (await _db.EntityRelationships.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// A reading held back takes no part in the pick. Keturah is read as Abraham's wife and as his
    /// concubine at one confidence, which would leave the pair disputed and unwritten; with the
    /// second held, the first stands.
    /// </summary>
    [Fact]
    public async Task AReadingHeldBackDoesNotDisputeTheOneBesideIt()
    {
        var keturah = Person("keturah");
        var abram = Person("abram");
        Clause(keturah, abram, DescriptorRelations.WifeOf, 0.85);
        Clause(keturah, abram, DescriptorRelations.ConcubineOf, 0.85);

        var outcome = await _loader.Load(
            [new WithheldClause("keturah", DescriptorRelations.ConcubineOf, "abram", "1CH 1:32", null)], default);

        outcome.Disputed.Should().Be(0);
        (await Relations(keturah, abram)).Should().Equal(DescriptorRelations.WifeOf);
    }

    /// <summary>
    /// The list the loader ships with: every entry names a relation of the vocabulary and says why
    /// it is there, and none is listed twice.
    /// </summary>
    [Fact]
    public void TheListOfReadingsHeldBackIsWellFormed()
    {
        var list = OwnRelationshipLoader.WithheldClauses();
        var vocabulary = typeof(DescriptorRelations).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        list.DecidedBy.Should().NotBeNullOrWhiteSpace();
        list.Policy.Should().NotBeNullOrWhiteSpace();
        list.Clauses.Should().NotBeEmpty();
        list.Clauses.Should().OnlyContain(clause =>
            clause.Entity.Length > 0 && clause.Target.Length > 0 && vocabulary.Contains(clause.Relation)
            && !string.IsNullOrWhiteSpace(clause.Reference) && !string.IsNullOrWhiteSpace(clause.Why));
        list.Clauses.Select(clause => (clause.Entity, clause.Relation, clause.Target)).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// The startup pipeline runs on every boot, and the descriptor passes arrive in batches over
    /// days: a second run must write nothing for an entity already related and must still reach one
    /// that was described afterwards.
    /// </summary>
    [Fact]
    public async Task ASecondPassWritesNothingForAnEntityAlreadyRelated()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Clause(lot, haran, DescriptorRelations.SonOf, 0.95);

        (await Load()).Written.Should().Be(1);

        var again = await _loader.Load([], default);
        again.AlreadyLoaded.Should().BeTrue();
        (await _db.EntityRelationships.CountAsync()).Should().Be(1);

        var abraham = Person("abraham");
        Clause(abraham, haran, DescriptorRelations.BrotherOf, 0.9);
        (await Load()).Written.Should().Be(1);
    }

    /// <summary>
    /// The Amorite was read as Canaan's son, and the record was folded into the people, which takes
    /// the clause away: the relationship read off it goes with it, and one the clause still states
    /// stays the row it was.
    /// </summary>
    [Fact]
    public async Task ARelationshipNoClauseStatesAnyMoreIsWithdrawn()
    {
        var amorites = Person("amorites");
        var canaan = Person("canaan");
        var gilead = Person("gilead");
        Clause(amorites, canaan, DescriptorRelations.SonOf, 0.9);
        Clause(amorites, gilead, DescriptorRelations.LivedIn, 0.65);
        await Load();
        var kept = await _db.EntityRelationships.AsNoTracking().SingleAsync(r => r.ToEntityId == gilead.Id);
        await _db.EntityDescriptors.Where(d => d.TargetEntityId == canaan.Id).ExecuteDeleteAsync();

        var outcome = await Load();

        outcome.Withdrawn.Should().Be(1);
        outcome.Written.Should().Be(0);
        (await _db.EntityRelationships.AsNoTracking().SingleAsync()).Id.Should().Be(kept.Id);
    }

    /// <summary>A relationship standing twice is said once, and the first row is the one that stays.</summary>
    [Fact]
    public async Task ARelationshipWrittenTwiceIsSaidOnce()
    {
        var deborah = Person("deborah");
        var rebekah = Person("rebekah");
        Clause(deborah, rebekah, DescriptorRelations.ServantOf, 0.9);
        await Load();
        var first = await _db.EntityRelationships.AsNoTracking().SingleAsync();
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = first.FromEntityId, ToEntityId = first.ToEntityId, Type = first.Type,
            Category = first.Category, CanonicalBook = first.CanonicalBook,
            CanonicalChapter = first.CanonicalChapter, CanonicalVerse = first.CanonicalVerse,
            Method = first.Method, Confidence = first.Confidence, Source = first.Source,
        });
        await _db.SaveChangesAsync();

        (await Load()).Withdrawn.Should().Be(1);

        (await _db.EntityRelationships.AsNoTracking().SingleAsync()).Id.Should().Be(first.Id);
    }

    /// <summary>
    /// A clause moved to the verse that says it moves the relationship read off it: Seraiah's father
    /// is cited where the clause cites him, not where the row was first read.
    /// </summary>
    [Fact]
    public async Task ARelationshipFollowsItsClauseToAnotherVerse()
    {
        var azariah = Person("azariah");
        var seraiah = Person("seraiah");
        Clause(azariah, seraiah, DescriptorRelations.FatherOf, 0.9);
        await Load();
        await _db.EntityDescriptors.ExecuteUpdateAsync(d => d.SetProperty(x => x.CanonicalBook, 15)
            .SetProperty(x => x.CanonicalChapter, 7).SetProperty(x => x.CanonicalVerse, 1));

        var outcome = await Load();

        (outcome.Written, outcome.Withdrawn).Should().Be((1, 1));
        var row = await _db.EntityRelationships.AsNoTracking().SingleAsync();
        (row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse).Should().Be((15, 7, 1));
        (await _loader.Load([], default)).AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>
    /// A reading with no verse behind it is a claim nobody can check, and this table is where a
    /// reader is most likely to try; the database refuses it rather than a convention.
    /// </summary>
    [Fact]
    public async Task AReadingWithNoVerseIsRefusedByTheDatabase()
    {
        var lot = Person("lot");
        var haran = Person("haran");

        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = lot.Id,
            ToEntityId = haran.Id,
            Type = DescriptorRelations.SonOf,
            Category = RelationshipCategories.Read,
            Method = LinkMethod.ModelReading,
            Confidence = 0.9,
            Source = Model,
        });

        var writing = async () => await _db.SaveChangesAsync();
        await writing.Should().ThrowAsync<DbUpdateException>()
            .WithInnerException<DbUpdateException, PostgresException>();
        _db.ChangeTracker.Clear();
    }

    private async Task<OwnRelationshipOutcome> Load()
    {
        var outcome = await _loader.Load([], default);
        outcome.AlreadyLoaded.Should().BeFalse();
        return outcome;
    }

    private async Task<List<string>> Relations(Entity from, Entity to) =>
        await _db.EntityRelationships
            .Where(r => r.FromEntityId == from.Id && r.ToEntityId == to.Id)
            .OrderBy(r => r.Type)
            .Select(r => r.Type)
            .ToListAsync();

    private void Clause(Entity entity, Entity target, string relation, double confidence) =>
        Claim(entity, target, relation, LinkMethod.ModelReading, confidence, Model);

    private void Decided(Entity entity, Entity target, string relation) =>
        Claim(entity, target, relation, LinkMethod.Manual, null, Owner);

    private void Claim(Entity entity, Entity target, string relation, LinkMethod method, double? confidence, string source)
    {
        _db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = entity.Id,
            Ordinal = _db.EntityDescriptors.Count(d => d.EntityId == entity.Id) + 1,
            Relation = relation,
            TargetEntityId = target.Id,
            CanonicalBook = 1,
            CanonicalChapter = 11,
            CanonicalVerse = 27,
            Method = method,
            Confidence = confidence,
            Source = source,
        });
        _db.SaveChanges();
    }

    private Entity Person(string slug)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        return entity;
    }
}
