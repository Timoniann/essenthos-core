using System.Text.Json.Nodes;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
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
/// The cases are the ones where the two witnesses come apart: where they say the same thing, where
/// they say the same thing from opposite ends, where they answer one question two ways, and where
/// they are not answering the same question at all. And the one the whole exercise is measured by —
/// that nothing of BibleData's is touched, whatever we conclude.
///
/// <para>
/// Asked of Postgres because half of what is under test is what the database will accept: a
/// relationship read here without the verse it was read from must be refused, and the same row from
/// a witness that gave none must not be.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRelationshipTests : IDisposable
{
    private const string Model = EntityDescriptorLoader.SourcePrefix + " a test, asked 2026-09-09";

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
    /// The plain case, and the reason the table is worth writing into at all: a clause about a pair
    /// nobody else states becomes a relationship, credited to the pass that read it.
    /// </summary>
    [Fact]
    public async Task AClauseNobodyElseStatesBecomesARelationship()
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
        Datasets.Of(written.Source).Should().Be("essenthos");
    }

    /// <summary>
    /// Agreement is written rather than skipped. It is the evidence the whole layer rests on — a
    /// clause two witnesses reach independently is better evidenced than one only the model
    /// proposed — and it is what has to be there before BibleData's rows can ever be let go.
    /// </summary>
    [Fact]
    public async Task WhatBothWitnessesSayIsWrittenUnderBothCredits()
    {
        var isaac = Person("isaac");
        var abraham = Person("abraham");
        Stated(isaac, abraham, "son");
        Clause(isaac, abraham, DescriptorRelations.SonOf, 0.99);

        (await Load()).Written.Should().Be(1);

        (await Relations(isaac, abraham)).Should().BeEquivalentTo(["son", DescriptorRelations.SonOf]);
    }

    /// <summary>
    /// The same fact from the other end. A clause is written from its own subject's side, so
    /// BibleData's <em>Bani is the ancestor of Adaiah</em> is answered by the claim on Adaiah, and
    /// reading it one-directionally would call that a disagreement.
    /// </summary>
    [Fact]
    public async Task AClauseThatAnswersTheWitnessFromTheOtherEndStands()
    {
        var bani = Person("bani-4");
        var adaiah = Person("adaiah-6");
        Stated(bani, adaiah, "ancestor");
        Clause(adaiah, bani, DescriptorRelations.DescendantOf, 0.9);

        (await Load()).Written.Should().Be(1);

        (await Relations(adaiah, bani)).Should().Equal(DescriptorRelations.DescendantOf);
    }

    /// <summary>
    /// The rule the corpus already had, applied here rather than replaced. Testimony outranks a
    /// reading, so where the two answer one question two ways the witness stands and the clause is
    /// withheld — and the page never shows a man as somebody's father and his grandfather at once.
    /// </summary>
    [Fact]
    public async Task AClauseAnsweringTheSameQuestionDifferentlyIsWithheld()
    {
        var abiel = Person("abiel");
        var kish = Person("kish");
        Stated(abiel, kish, "grandfather");
        Clause(abiel, kish, DescriptorRelations.FatherOf, 0.98);

        var outcome = await Load();
        outcome.Written.Should().Be(0);
        outcome.Withheld.Should().Be(1);

        (await Relations(abiel, kish)).Should().Equal("grandfather");
    }

    /// <summary>
    /// A closer tie is not a different answer. The witness says only that Lot descends from Haran and
    /// the verse was read as <em>son of</em>: the reading says everything the witness says and more,
    /// and withholding it would put the looser line on the page in place of the one the text gives.
    /// </summary>
    [Fact]
    public async Task AReadingCloserThanTheWitnessIsWrittenRatherThanWithheld()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Stated(haran, lot, "ancestor");
        Clause(lot, haran, DescriptorRelations.SonOf, 0.95);

        var outcome = await Load();
        outcome.Written.Should().Be(1);
        outcome.Withheld.Should().Be(0);
    }

    /// <summary>
    /// And only that way round. A reading of <em>descendant of</em> against a witness's <em>son</em>
    /// says less than the witness, so it gives way exactly as any other disagreement does.
    /// </summary>
    [Fact]
    public async Task AReadingLooserThanTheWitnessStillGivesWay()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Stated(lot, haran, "son");
        Clause(lot, haran, DescriptorRelations.DescendantOf, 0.95);

        var outcome = await Load();
        outcome.Written.Should().Be(0);
        outcome.Withheld.Should().Be(1);
    }

    /// <summary>
    /// Son of Haran already says descendant of Haran, so a pair read both ways is one row and not two.
    /// </summary>
    [Fact]
    public async Task TheWiderClauseBesideACloserOneIsNotWrittenAgain()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Stated(lot, haran, "descendant");
        Clause(lot, haran, DescriptorRelations.SonOf, 0.9);
        Clause(lot, haran, DescriptorRelations.DescendantOf, 0.9);

        (await Load()).Written.Should().Be(1);

        (await Relations(lot, haran)).Should().BeEquivalentTo(["descendant", DescriptorRelations.SonOf]);
    }

    /// <summary>
    /// Not everything that differs disagrees. The commander of Judah is also of the tribe of Judah,
    /// and treating a second kind of claim about one pair as a contradiction would withhold a true
    /// clause for saying something the witness simply did not say.
    /// </summary>
    [Fact]
    public async Task AClauseAboutSomethingElseEntirelyStandsBesideTheWitness()
    {
        var nahshon = Person("nahshon");
        var judah = Person("judah");
        Stated(nahshon, judah, "descendant");
        Clause(nahshon, judah, DescriptorRelations.OfTribe, 0.9);

        (await Load()).Written.Should().Be(1);
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

        var outcome = await Load();
        outcome.Written.Should().Be(0);
        outcome.Disputed.Should().Be(1);
    }

    /// <summary>
    /// A relation the vocabulary has no word for cannot corroborate a clause and cannot contradict
    /// one. Counting it as a disagreement would leave the encyclopedia silent about a pair over a
    /// word it does not happen to have.
    /// </summary>
    [Fact]
    public async Task ARelationTheVocabularyCannotExpressSettlesNothing()
    {
        // "victim" is killed-by or raped-by and does not say which, so it can neither settle a pair
        // nor contradict a clause about it. This test used "cousin" until cousin-of was added, and
        // "concubinator" until it was read as a concubine row from the other end.
        var sisera = Person("sisera");
        var jael = Person("jael");
        Stated(sisera, jael, "victim");
        Clause(sisera, jael, DescriptorRelations.KilledBy, 0.8);

        (await Load()).Written.Should().Be(1);
    }

    /// <summary>
    /// A witness's word for the other end of a fact is that fact. BibleData's <em>Abram concubinator
    /// Hagar</em> says what a reading of <em>Hagar, wife of Abram</em> answers differently, so the
    /// reading gives way as it would to the dataset's own <c>concubine</c> row.
    /// </summary>
    [Fact]
    public async Task AWordForTheOtherEndSettlesThePairReadTheOtherWay()
    {
        var abram = Person("abram");
        var hagar = Person("hagar");
        Stated(abram, hagar, "concubinator");
        Clause(hagar, abram, DescriptorRelations.WifeOf, 0.8);

        var outcome = await Load();
        outcome.Written.Should().Be(0);
        outcome.Withheld.Should().Be(1);
    }

    /// <summary>
    /// What a word the vocabulary gains is for. Until cousin-of existed, the dataset's "cousin" on
    /// Mordecai and Esther settled nothing and a reading calling him her uncle was written. The text
    /// says she was <em>his uncle's daughter</em> (EST 2:7): the dataset is right and the reading is
    /// wrong, and now that the two are answers to one question the witness of higher standing wins.
    /// </summary>
    [Fact]
    public async Task AWordTheVocabularyGainsLetsAWitnessCatchAWrongReading()
    {
        var mordecai = Person("mordecai");
        var esther = Person("esther");
        Stated(mordecai, esther, "cousin");
        Clause(mordecai, esther, DescriptorRelations.UncleOf, 0.8);

        var outcome = await Load();
        outcome.Written.Should().Be(0);
        outcome.Withheld.Should().Be(1);
    }

    /// <summary>
    /// Marriage is not an answer to the question descent answers. BibleData says Abram is Sarai's
    /// husband and her half-brother, both from GEN 11:29 and both what the text says, and reading
    /// the two as one contradiction would withhold the very clause the witness agrees with.
    /// </summary>
    [Fact]
    public async Task AMarriageAndAKinshipOverOnePairAreTwoFactsAndNotTwoAnswers()
    {
        var abram = Person("abram");
        var sarai = Person("sarai");
        Stated(abram, sarai, "husband");
        Stated(abram, sarai, "half-brother");
        Clause(abram, sarai, DescriptorRelations.HusbandOf, 0.9);

        var outcome = await Load();
        outcome.Written.Should().Be(1);
        outcome.Disputed.Should().Be(0);
    }

    /// <summary>
    /// Nothing of the witness's is deleted, rewritten or reordered. The corpus lost 14,515 rows to
    /// a pass that believed it could rebuild them, and BibleData's edge list is the
    /// answer key everything here is measured against.
    /// </summary>
    [Fact]
    public async Task TheWitnessKeepsEveryRowItHad()
    {
        var abiel = Person("abiel");
        var kish = Person("kish");
        Stated(abiel, kish, "grandfather");
        Clause(abiel, kish, DescriptorRelations.FatherOf, 0.98);

        await Load();

        var witness = await _db.EntityRelationships.SingleAsync(r => r.Type == "grandfather");
        witness.Method.Should().Be(LinkMethod.StatedBySource);
        witness.Confidence.Should().BeNull();
        witness.Category.Should().Be(RelationshipCategories.Explicit);
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

        var again = await _loader.Load();
        again.AlreadyLoaded.Should().BeTrue();
        (await _db.EntityRelationships.CountAsync()).Should().Be(1);

        var abraham = Person("abraham");
        Clause(abraham, haran, DescriptorRelations.BrotherOf, 0.9);
        (await Load()).Written.Should().Be(1);
    }

    /// <summary>
    /// A reading withheld because a row of the dataset outranked it comes back when the owner
    /// removes that row, although the entity's own clauses did not change and it already has other
    /// rows of ours that would otherwise say it is settled.
    /// </summary>
    [Fact]
    public async Task AReadingWithheldByARowTheOwnerRemovesReturnsWithoutNewClauses()
    {
        var (gershom, manasseh, dan) = (Person("gershom-2"), Person("manasseh-2"), Person("dan"));
        var reversed = new EntityRelationship
        {
            FromEntityId = manasseh.Id,
            ToEntityId = gershom.Id,
            Type = "son",
            Category = RelationshipCategories.Explicit,
            CanonicalBook = 7,
            CanonicalChapter = 18,
            CanonicalVerse = 30,
            Method = LinkMethod.StatedBySource,
            Source = BibleDataLoader.Source,
        };
        _db.EntityRelationships.Add(reversed);
        _db.SaveChanges();
        Clause(gershom, manasseh, DescriptorRelations.SonOf, 0.8);
        Clause(gershom, dan, DescriptorRelations.BrotherOf, 0.9);

        var first = await Load();
        first.Withheld.Should().Be(1);
        (await Relations(gershom, manasseh)).Should().BeEmpty();

        var resources = Path.Combine(Path.GetTempPath(), $"essenthos-own-{Guid.NewGuid():N}");
        var review = Path.Combine(resources, "Essenthos", "review");
        Directory.CreateDirectory(review);
        try
        {
            File.WriteAllText(
                Path.Combine(review, WithdrawnRelationshipLoader.ReviewFile),
                new JsonObject
                {
                    ["decisions"] = new JsonObject
                    {
                        ["x"] = new JsonObject
                        {
                            ["a"] = "gershom-2",
                            ["b"] = "manasseh-2",
                            ["decidedAt"] = "2026-09-29T08:00:00Z",
                            ["decision"] = WithdrawnRelationshipLoader.Remove,
                            ["rows"] = reversed.Id.ToString(),
                        },
                    },
                }.ToJsonString());

            var withdrawn = await new WithdrawnRelationshipLoader(
                _db, NullLogger<WithdrawnRelationshipLoader>.Instance).Load(resources);

            withdrawn.Changed.Should().BeEquivalentTo([gershom.Id, manasseh.Id]);
            (await _loader.Load()).AlreadyLoaded.Should().BeTrue();
            (await Relations(gershom, manasseh)).Should().BeEmpty("nothing told the loader the row is gone");

            var again = await _loader.Load(withdrawn.Changed);

            again.Withheld.Should().Be(0);
            (await Relations(gershom, manasseh)).Should().Equal("son-of");
            (await Relations(gershom, dan)).Should().Equal("brother-of");
            (await _db.EntityRelationships.CountAsync(r => r.Source == Model)).Should().Be(2);
        }
        finally
        {
            Directory.Delete(resources, recursive: true);
        }
    }

    /// <summary>
    /// The asymmetry between the two witnesses, as a constraint rather than a convention. A reading
    /// with no verse behind it is a claim nobody can check, and this table is where a reader is
    /// most likely to try.
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

    /// <summary>
    /// And the other half of the same decision: a witness that gave no reference keeps its row.
    /// Forty of BibleData's 5,448 are like this and fourteen of those it calls explicit, which is a
    /// fact about that dataset — refusing them would delete it, and filling them in would be
    /// writing a citation nobody can follow.
    /// </summary>
    [Fact]
    public async Task AWitnessThatGaveNoVerseKeepsItsRow()
    {
        var one = Person("adam");
        var two = Person("eve");

        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = one.Id,
            ToEntityId = two.Id,
            Type = "husband",
            Category = RelationshipCategories.Inferred,
            Method = LinkMethod.StatedBySource,
            Source = "a witness",
        });

        await _db.SaveChangesAsync();
        (await _db.EntityRelationships.CountAsync()).Should().Be(1);
    }

    private async Task<OwnRelationshipOutcome> Load()
    {
        var outcome = await _loader.Load();
        outcome.AlreadyLoaded.Should().BeFalse();
        return outcome;
    }

    private async Task<List<string>> Relations(Entity from, Entity to) =>
        await _db.EntityRelationships
            .Where(r => r.FromEntityId == from.Id && r.ToEntityId == to.Id)
            .OrderBy(r => r.Type)
            .Select(r => r.Type)
            .ToListAsync();

    private void Stated(Entity from, Entity to, string type)
    {
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = type,
            Category = RelationshipCategories.Explicit,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Method = LinkMethod.StatedBySource,
            Source = "a witness",
        });
        _db.SaveChanges();
    }

    private void Clause(Entity entity, Entity target, string relation, double confidence)
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
            Method = LinkMethod.ModelReading,
            Confidence = confidence,
            Source = Model,
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
