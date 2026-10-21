using Essenthos.Core.Corpus;
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
/// What an entity page shows of the relationships this project holds.
///
/// The cases are the ones the reader can tell apart: a fact stated from both ends, which is one row
/// on each page with the other end folded into it; a fact stated from the other end only, which is
/// that row read from this side; and a pair the two ends answer differently, which stays two rows
/// because the endpoint is not where that is decided.
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

    private const string Owner = EntityDescriptorLoader.SourcePrefix + " the project owner, decided 2026-09-12";

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
    /// One fact, two rows, one line on the page — and nothing thrown away. The descriptor pass
    /// writes both ends, so the table holds Lot son of Haran and Haran father of Lot. Lot's page
    /// says it once, from Lot's side, because that is the page it is, and the other end rides on it.
    /// </summary>
    [Fact]
    public async Task OneFactIsOneRowAndTheOtherEndRidesOnIt()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Read(lot, haran, DescriptorRelations.SonOf, 1, 11, 27);
        Read(haran, lot, DescriptorRelations.FatherOf, 1, 11, 27);

        var row = (await Page(lot)).Should().ContainSingle().Which;
        row.Type.Should().Be(DescriptorRelations.SonOf, "the page is Lot's and the fact is his side");
        row.Inward.Should().BeFalse();
        row.Dataset.Should().Be(Datasets.Own);

        var folded = row.Corroboration.Should().ContainSingle().Which;
        folded.Type.Should().Be(DescriptorRelations.FatherOf);
        folded.Reversed.Should().BeTrue();

        (await Page(haran)).Should().ContainSingle().Which.Type.Should().Be(DescriptorRelations.FatherOf);
    }

    /// <summary>
    /// The folded end says everything a reader would need to weigh it: its own verse, and who said
    /// it. The owner decided Adam is Seth's father from Genesis 4:25 and a model read Seth as his
    /// son at 5:3; Seth's page shows the reading with the decision beside it.
    /// </summary>
    [Fact]
    public async Task TheFoldedEndKeepsItsOwnVerseAndItsOwnCredit()
    {
        var seth = Person("seth");
        var adam = Person("adam");
        Read(seth, adam, DescriptorRelations.SonOf, 1, 5, 3);
        Decided(adam, seth, DescriptorRelations.FatherOf, 1, 4, 25);

        var row = (await Page(seth)).Should().ContainSingle().Which;

        row.Type.Should().Be(DescriptorRelations.SonOf);
        row.Reference!.Chapter.Should().Be(5);
        row.Source.Should().Be(Model);

        var folded = row.Corroboration.Should().ContainSingle().Which;
        folded.Type.Should().Be(DescriptorRelations.FatherOf);
        folded.Reversed.Should().BeTrue();
        (folded.Reference!.Chapter, folded.Reference.Verse).Should().Be((4, 25));
        folded.Method.Should().Be("manual");
        folded.Source.Should().Be(Owner);
        folded.Dataset.Should().Be(Datasets.Own);
    }

    /// <summary>
    /// A row says what its counterpart is, so a page links Eden to a place and not to a person.
    /// </summary>
    [Fact]
    public async Task ARowSaysWhatItsCounterpartIs()
    {
        var euphrates = Person("euphrates");
        var eden = Person("eden-2");
        eden.Kind = EntityKind.Place;
        _db.SaveChanges();
        Read(euphrates, eden, "river-of", 1, 2, 14);

        var row = (await Page(euphrates)).Should().ContainSingle().Which;

        row.Kind.Should().Be("place");
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
    /// Disagreement is not folded and is not decided here. Abiel the father of Kish on one record
    /// and Kish the grandson of Abiel on the other are two statements, and folding them into one
    /// row would pick a winner in the endpoint, which is the one place the corpus has no rule for
    /// doing it.
    /// </summary>
    [Fact]
    public async Task TwoEndsAnsweringOneQuestionDifferentlyStayTwoRows()
    {
        var abiel = Person("abiel");
        var kish = Person("kish");
        Read(abiel, kish, DescriptorRelations.FatherOf, 9, 9, 1);
        Read(kish, abiel, DescriptorRelations.GrandsonOf, 9, 14, 51);

        var page = await Page(abiel);

        page.Select(row => (row.Type, row.Inward)).Should().Equal(
            (DescriptorRelations.FatherOf, false), (DescriptorRelations.GrandsonOf, true));
        page.Should().OnlyContain(row => row.Corroboration.Count == 0);
    }

    /// <summary>
    /// Every response the page returns has to be registered in the source-generated context, or the
    /// first request fails at runtime rather than the build failing here.
    /// </summary>
    [Theory]
    [InlineData(typeof(EntityRelationshipWitnessResponse))]
    [InlineData(typeof(IList<EntityRelationshipWitnessResponse>))]
    public void TheFoldedEndsResponseIsRegistered(Type response) =>
        AppJsonSerializerContext.Default.GetTypeInfo(response).Should().NotBeNull();

    /// <summary>
    /// And it reaches the wire. A folded end a client cannot see is the same page as one that
    /// dropped it, however carefully the row was assembled.
    /// </summary>
    [Fact]
    public async Task TheFoldedEndSurvivesTheSerialiser()
    {
        var lot = Person("lot");
        var haran = Person("haran");
        Read(lot, haran, DescriptorRelations.SonOf, 1, 11, 27);
        Decided(haran, lot, DescriptorRelations.FatherOf, 1, 11, 27);

        var written = JsonSerializer.Serialize(
            (await Page(lot)).Single(),
            AppJsonSerializerContext.Default.EntityRelationshipResponse);

        var read = JsonSerializer.Deserialize(
            written, AppJsonSerializerContext.Default.EntityRelationshipResponse);

        var folded = read!.Corroboration.Should().ContainSingle().Which;
        folded.Type.Should().Be(DescriptorRelations.FatherOf);
        folded.Dataset.Should().Be(Datasets.Own);
    }

    /// <summary>
    /// The counterpart's name in the case the reader's language wants, without which the section is
    /// English on a Ukrainian page. Nothing computes the form: a language the corpus has not
    /// declined a name into gets nothing here and the client falls back to the English name, which
    /// is the same fallback the descriptor line already has.
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

    private void Read(Entity from, Entity to, string relation, int book, int chapter, int verse) =>
        Tie(from, to, relation, book, chapter, verse, LinkMethod.ModelReading, 0.95, Model);

    private void Decided(Entity from, Entity to, string relation, int book, int chapter, int verse) =>
        Tie(from, to, relation, book, chapter, verse, LinkMethod.Manual, null, Owner);

    private void Tie(
        Entity from, Entity to, string relation, int book, int chapter, int verse,
        LinkMethod method, double? confidence, string source)
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
