using System.Text.Json;
using Essenthos.Core;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What an entity page shows once both witnesses are in the table.
///
/// The cases are the ones the reader can tell apart: a fact both witnesses state, which is one row
/// with two names on it; a fact only BibleData states, which is BibleData's row exactly as it was
/// written; and a pair the two answer differently, which stays two rows because the endpoint is not
/// where that is decided — <see cref="OwnRelationshipLoader"/> settled it at load time and a second
/// rule here would be a second answer to a question that already has one.
///
/// <para>
/// Asked of Postgres, through the query the endpoint runs, because half of what is under test is
/// that the query reaches both directions at once. A page reading only the rows that name its
/// entity first would show Isaac a father and no sons, and no in-memory fixture would notice.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RelationshipMergeTests : IDisposable
{
    private const string Model = EntityDescriptorLoader.SourcePrefix + " a test, asked 2026-09-09";

    private const string Witness = "BibleData by Brady Stephenson, a test";

    private readonly AppDbContext _db;

    public RelationshipMergeTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    /// <summary>
    /// The case the whole thing is for. Both witnesses say Lot is the son of Haran and the reader
    /// meets one statement, in this corpus's words, with BibleData's beside it.
    /// </summary>
    [Fact]
    public async Task WhatBothWitnessesStateIsOneRowCarryingBoth()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Stated(haran, lot, "father", 1, 11, 27);
        Read(lot, haran, DescriptorRelations.SonOf, 1, 11, 27);
        Stated(lot, haran, "son", 1, 11, 27);

        var page = await Page(lot);

        var row = page.Should().ContainSingle().Which;
        row.Type.Should().Be(DescriptorRelations.SonOf);
        row.Dataset.Should().Be(Datasets.Own);
        row.Corroboration.Should().HaveCount(2);
    }

    /// <summary>
    /// The corroboration is the point, so it says everything a reader would need to weigh it: the
    /// dataset's own word for the relation, its own verse, and who it was.
    /// </summary>
    [Fact]
    public async Task TheWitnessKeepsItsOwnWordItsOwnVerseAndItsOwnCredit()
    {
        var seth = Person("seth");
        var adam = Person("adam");
        Read(seth, adam, DescriptorRelations.SonOf, 1, 5, 3);
        Stated(seth, adam, "son", 1, 4, 25);

        var row = (await Page(seth)).Should().ContainSingle().Which;

        row.Type.Should().Be(DescriptorRelations.SonOf);
        row.Reference!.Chapter.Should().Be(5);

        var witness = row.Corroboration.Should().ContainSingle().Which;
        witness.Type.Should().Be("son");
        witness.Reversed.Should().BeFalse();
        witness.Reference!.Chapter.Should().Be(4);
        witness.Reference.Verse.Should().Be(25);
        witness.Dataset.Should().Be("bibledata");
    }

    /// <summary>
    /// The same fact from the other end. BibleData states <em>Bani is the ancestor of Adaiah</em>
    /// and the encyclopedia answers on Adaiah, so one-directional pairing would show a reader two
    /// rows and no sign that they were one thing.
    /// </summary>
    [Fact]
    public async Task AWitnessStatingThePairBackwardsCorroboratesRatherThanRepeats()
    {
        var adaiah = Person("adaiah-6");
        var bani = Person("bani-4");
        Read(adaiah, bani, DescriptorRelations.DescendantOf, 13, 9, 12);
        Stated(bani, adaiah, "ancestor", 13, 9, 12);

        var row = (await Page(adaiah)).Should().ContainSingle().Which;

        row.Type.Should().Be(DescriptorRelations.DescendantOf);
        var witness = row.Corroboration.Should().ContainSingle().Which;
        witness.Type.Should().Be("ancestor");
        witness.Reversed.Should().BeTrue();
    }

    /// <summary>
    /// The witness states the wider tie and our reading the closer one. The loader wrote ours because
    /// the two agree, so the page says <em>son of Haran</em> once, with BibleData's looser word beside
    /// it — from either end the witness wrote it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWitnessStatingTheWiderTieRidesOnTheCloserReading(bool fromTheOtherEnd)
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Read(lot, haran, DescriptorRelations.SonOf, 1, 11, 27);
        if (fromTheOtherEnd)
        {
            Stated(haran, lot, "ancestor", 1, 11, 27);
        }
        else
        {
            Stated(lot, haran, "descendant", 1, 11, 27);
        }

        var row = (await Page(lot)).Should().ContainSingle().Which;

        row.Type.Should().Be(DescriptorRelations.SonOf);
        var witness = row.Corroboration.Should().ContainSingle().Which;
        witness.Type.Should().Be(fromTheOtherEnd ? "ancestor" : "descendant");
        witness.Reversed.Should().Be(fromTheOtherEnd);
    }

    /// <summary>
    /// One fact, four rows, one line on the page — and nothing thrown away.
    ///
    /// <para>
    /// Both witnesses write both ends, so the table holds Lot son of Haran and Haran father of Lot
    /// twice over. Lot's page says it once, from Lot's side, because that is the page it is; the
    /// other three ride on it as what they are. Each is still readable, and each still carries the
    /// grading its own witness gave that direction — the reason for carrying them rather than
    /// dropping them is that the two directions are graded separately and often differently.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OneFactIsOneRowAndTheOtherEndRidesOnIt()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Read(lot, haran, DescriptorRelations.SonOf, 1, 11, 27);
        Read(haran, lot, DescriptorRelations.FatherOf, 1, 11, 27);
        Stated(lot, haran, "son", 1, 11, 27);
        Stated(haran, lot, "father", 1, 11, 27);

        var page = await Page(lot);

        var row = page.Should().ContainSingle().Which;
        row.Type.Should().Be(DescriptorRelations.SonOf, "the page is Lot's and the fact is his side");
        row.Inward.Should().BeFalse();

        row.Corroboration.Select(w => w.Type).Should().BeEquivalentTo(
            ["son", DescriptorRelations.FatherOf, "father"]);
        row.Corroboration.Single(w => w.Type == "son").Reversed.Should().BeFalse();
        row.Corroboration.Where(w => w.Type != "son").Should().OnlyContain(w => w.Reversed);
    }

    /// <summary>
    /// What is folded is the repetition and never the fact: where only the other end exists, it is
    /// the row, and Haran's page is where a reader meets it that way round.
    /// </summary>
    [Fact]
    public async Task TheOtherEndStaysWhenItIsTheOnlyEnd()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Read(haran, lot, DescriptorRelations.FatherOf, 1, 11, 27);

        var page = await Page(lot);

        var row = page.Should().ContainSingle().Which;
        row.Type.Should().Be(DescriptorRelations.FatherOf);
        row.Inward.Should().BeTrue();
    }

    /// <summary>
    /// Disagreement is not merged and is not re-decided here. The loader withholds a clause a
    /// witness of higher standing answers differently, so a page holding both is a page where
    /// something else put them there — and folding them into one row would pick a winner in the
    /// endpoint, which is the one place the corpus has no rule for doing it.
    /// </summary>
    [Fact]
    public async Task TwoWitnessesAnsweringOneQuestionDifferentlyStayTwoRows()
    {
        var abiel = Person("abiel");
        var kish = Person("kish");
        Read(abiel, kish, DescriptorRelations.FatherOf, 9, 9, 1);
        Stated(abiel, kish, "grandfather", 9, 9, 1);

        var page = await Page(abiel);

        page.Should().HaveCount(2);
        page.Should().OnlyContain(row => row.Corroboration.Count == 0);
    }

    /// <summary>
    /// What BibleData states and no reading of ours reached stays exactly as it was written. Its
    /// rows reach entities and relations ours does not, and this pass merges agreement rather than
    /// replacing a witness.
    /// </summary>
    [Fact]
    public async Task AWitnessRowNothingOfOursAnswersIsUntouched()
    {
        var lot = Person("lot");
        var moab = Person("moab");
        Stated(lot, moab, "father", 1, 19, 37);

        var row = (await Page(lot)).Should().ContainSingle().Which;

        row.Type.Should().Be("father");
        row.Category.Should().Be(RelationshipCategories.Explicit);
        row.Method.Should().Be(EnumSpelling.Of(LinkMethod.StatedBySource));
        row.Dataset.Should().Be("bibledata");
        row.Corroboration.Should().BeEmpty();
    }

    /// <summary>
    /// A relation the vocabulary has no word for corroborates nothing and contradicts nothing.
    /// Mordecai is Esther's uncle in our reading and her cousin in BibleData's, and the corpus has
    /// no word for the second — so both are shown, which is the honest answer and not a merge.
    /// </summary>
    [Fact]
    public async Task ARelationTheVocabularyCannotExpressIsARowOfItsOwn()
    {
        var mordecai = Person("mordecai");
        var esther = Person("esther");
        Read(mordecai, esther, DescriptorRelations.UncleOf, 17, 2, 7);
        Stated(mordecai, esther, "cousin", 17, 2, 7);

        (await Page(mordecai)).Should().HaveCount(2);
    }

    /// <summary>
    /// Every response the page returns has to be registered in the source-generated context, or the
    /// first request fails at runtime rather than the build failing here (RUL-0003).
    /// </summary>
    [Theory]
    [InlineData(typeof(EntityRelationshipWitnessResponse))]
    [InlineData(typeof(IList<EntityRelationshipWitnessResponse>))]
    public void TheWitnessResponseIsRegistered(Type response) =>
        AppJsonSerializerContext.Default.GetTypeInfo(response).Should().NotBeNull();

    /// <summary>
    /// And it reaches the wire. A corroboration a client cannot see is the same page as one that
    /// dropped the second witness, however carefully the row was assembled.
    /// </summary>
    [Fact]
    public async Task TheCorroborationSurvivesTheSerialiser()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Read(lot, haran, DescriptorRelations.SonOf, 1, 11, 27);
        Stated(lot, haran, "son", 1, 11, 27);

        var written = JsonSerializer.Serialize(
            (await Page(lot)).Single(),
            AppJsonSerializerContext.Default.EntityRelationshipResponse);

        var read = JsonSerializer.Deserialize(
            written, AppJsonSerializerContext.Default.EntityRelationshipResponse);

        var witness = read!.Corroboration.Should().ContainSingle().Which;
        witness.Type.Should().Be("son");
        witness.Dataset.Should().Be("bibledata");
    }

    /// <summary>
    /// The counterpart's name in the case the reader's language wants, without which the section is
    /// English on a Ukrainian page. Nothing computes the form: a language the corpus has not
    /// declined a name into gets nothing here and the client falls back to the English name, which
    /// is the same fallback the descriptor line already has (PRB-0451).
    /// </summary>
    [Fact]
    public async Task ARowCarriesTheCounterpartsNameInTheCaseTheLanguageAsksFor()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Read(lot, haran, "son-of", 1, 11, 27);
        Declined(haran, "ukr", GrammaticalCases.Genitive, "Гарана");
        await _db.SaveChangesAsync();

        var ukrainian = await Relationships.Of(_db, lot.Id, "ukr", CancellationToken.None);
        ukrainian.Should().ContainSingle().Which.Forms
            .Should().ContainKey(GrammaticalCases.Genitive)
            .WhoseValue.Should().Be("Гарана");

        var english = await Relationships.Of(_db, lot.Id, "eng", CancellationToken.None);
        english.Should().ContainSingle().Which.Forms.Should().BeNull(
            "no pass has declined Haran into English, and an invented ending is worse than the name");

        var unasked = await Page(lot);
        unasked.Should().ContainSingle().Which.Forms.Should().BeNull();
    }

    private void Declined(Entity entity, string language, string grammaticalCase, string form)
    {
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = entity.Id,
            Language = language,
            GrammaticalCase = grammaticalCase,
            Form = form,
            Method = LinkMethod.ModelReading,
            Confidence = 1,
            Source = Model,
        });
    }

    private async Task<List<EntityRelationshipResponse>> Page(Entity entity) =>
        await Relationships.Of(_db, entity.Id, null, CancellationToken.None);

    private void Stated(Entity from, Entity to, string type, int book, int chapter, int verse)
    {
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = type,
            Category = RelationshipCategories.Explicit,
            CanonicalBook = book,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Method = LinkMethod.StatedBySource,
            Source = Witness,
        });
        _db.SaveChanges();
    }

    private void Read(Entity from, Entity to, string relation, int book, int chapter, int verse)
    {
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = relation,
            Category = RelationshipCategories.Read,
            CanonicalBook = book,
            CanonicalChapter = chapter,
            CanonicalVerse = verse,
            Method = LinkMethod.ModelReading,
            Confidence = 0.95,
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
