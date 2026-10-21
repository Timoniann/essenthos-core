using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;
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
/// A note written while the data was settled never reaches a reader.
///
/// Relationship rows, descriptor clauses, record claims and word annotations each carry a note: a
/// model's reasoning, an agent's working note, the review page's own sentence, and the owner's remark
/// to whoever applied his decision — on David's page that was his instruction <em>виправи це</em>
/// under Tamar. None of it was written for a reader, so none of it is served, in any language. The
/// notes below are the kinds the corpus holds, word for word.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WorkingNoteTests : IDisposable
{
    private const string Owner = EntityDescriptorLoader.SourcePrefix + " the project owner, decided 2026-09-13";

    private const string Model = EntityDescriptorLoader.SourcePrefix + " claude-sonnet-5, asked 2026-09-09";

    private static readonly string[] Notes =
    [
        "1CH 3:9 показує, шо вона була дочкою Давида. Хай буде, шо це дочка. Також тут неправильно " +
        "визначено, яка саме Фамарь — це не невістка Юди, сина Якова — виправи це.",
        "or descendant",
        "Can we accept it but make it \"not sure\"? The verse is ambiguous",
        "agent: EZR 8:33 'Noadiah the son of Binnui, Levites'.",
        "owner, in chat 2026-09-27: priests as Aaron's descendants, not certain. NEH 10:8 'these were the priests'.",
        "the verse states it, decided on the review page",
        "Verse 15 ('Ibhar also, and Elishua') continues the list from v14's heading.",
        "The cited verse 12:9 just lists 'Eliab the third'.",
        "the record was corrected to son on his word of 2026-10-08",
    ];

    private readonly AppDbContext _db;

    public WorkingNoteTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Fact]
    public async Task NoNoteOfARelationshipReachesThePage()
    {
        var david = Person("david");
        var tamar = Person("tamar-2");
        var amnon = Person("amnon");
        var children = Notes.Select((_, index) => Person($"child-{index}")).ToList();

        Tie(tamar, david, DescriptorRelations.DaughterOf, LinkMethod.Manual, null, Owner, Notes[0]);
        Tie(david, tamar, DescriptorRelations.FatherOf, LinkMethod.ModelReading, 0.9, Model, Notes[6]);
        Tie(tamar, amnon, DescriptorRelations.SisterOf, LinkMethod.ModelReading, 0.95, Model, Notes[7]);
        foreach (var (child, index) in children.Select((child, index) => (child, index)))
        {
            Tie(child, david, DescriptorRelations.SonOf, LinkMethod.Manual, null, Owner, Notes[index]);
        }

        foreach (var language in new string?[] { null, "ukr", "eng" })
        {
            foreach (var page in new[] { david, tamar })
            {
                var rows = await Relationships.Of(_db, page.Id, language, CancellationToken.None);
                rows.Should().NotBeEmpty();

                var wire = JsonSerializer.Serialize(
                    rows, AppJsonSerializerContext.Default.GetTypeInfo(typeof(IList<EntityRelationshipResponse>))!);

                ContainsNoNote(wire);
                wire.Should().NotContain("\"notes\"").And.NotContain("readerNote");
            }
        }
    }

    [Fact]
    public async Task NoReasonOfADescriptorClauseReachesTheLine()
    {
        var david = Person("david");
        var tamar = Person("tamar-2");
        var ordinal = 0;
        foreach (var note in Notes)
        {
            _db.EntityDescriptors.Add(new EntityDescriptor
            {
                EntityId = tamar.Id,
                TargetEntityId = david.Id,
                Relation = DescriptorRelations.DaughterOf,
                Ordinal = ++ordinal,
                CanonicalBook = 10,
                CanonicalChapter = 13,
                CanonicalVerse = ordinal,
                Method = LinkMethod.Manual,
                Source = Owner,
                Note = note,
            });
        }

        await _db.SaveChangesAsync();

        foreach (var language in new[] { "eng", "ukr" })
        {
            var line = await Descriptors.Of(_db, tamar.Slug, language, CancellationToken.None);
            line!.Claims.Should().NotBeEmpty();

            ContainsNoNote(JsonSerializer.Serialize(
                line, AppJsonSerializerContext.Default.GetTypeInfo(typeof(EntityDescriptorResponse))!));
        }
    }

    /// <summary>
    /// A word's card keeps the route a naming was carried along, which is generated and which the
    /// card reads to say the name came from the Hebrew; the reason a reading or a ruling gave is
    /// dropped.
    /// </summary>
    [Fact]
    public async Task AWordsCardKeepsTheCarryingRouteAndNothingElseOfTheNote()
    {
        var text = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (13, 1, ["Tamar", "Amnon"]));
        await _db.SaveChangesAsync();
        var tamar = Person("tamar-2");
        var amnon = Person("amnon");
        const string route = "through BHSA word 123456, linked by aligner";
        Name(_db.WordAt(text, 13, 1, 1), tamar, LinkMethod.Manual, null,
            "The project owner decided on 2026-09-26 that this is David's daughter; виправи це.");
        Name(_db.WordAt(text, 13, 1, 2), amnon, LinkMethod.Aligner, 0.9, route + "; the owner's review asked for it");
        await _db.SaveChangesAsync();

        var cards = await Annotations.AllOf(
            _db, [_db.WordAt(text, 13, 1, 1).Id, _db.WordAt(text, 13, 1, 2).Id], CancellationToken.None);

        var wire = JsonSerializer.Serialize(
            cards.Values.SelectMany(card => card).ToList(),
            AppJsonSerializerContext.Default.GetTypeInfo(typeof(IReadOnlyList<EntityRefResponse>))!);

        using (var document = JsonDocument.Parse(wire))
        {
            Strings(document.RootElement).Should().NotContain(value =>
                value.Contains("виправи") || value.Contains("decided") || value.Contains("review"));
        }

        cards[_db.WordAt(text, 13, 1, 1).Id].Single().Note.Should().BeNull();
        cards[_db.WordAt(text, 13, 1, 2).Id].Single().Note.Should().Be(route);
    }

    [Theory]
    [InlineData("through BHSA word 17539936, linked by stated-by-source", "through BHSA word 17539936, linked by stated-by-source")]
    [InlineData("through NESTLE1904 word 12, linked by strong-number", "through NESTLE1904 word 12, linked by strong-number")]
    [InlineData("through bhsa word 7", "through bhsa word 7")]
    [InlineData("crossed with word 17539936: Essenthos, on the project owner's decision that the titles", null)]
    [InlineData("H2148, read as zechariah-2 with high confidence: the son of Berechiah", null)]
    [InlineData("The owner ruled a new record with notes on who he might theoretically be.", null)]
    [InlineData(null, null)]
    public void OnlyTheRouteOfAnAnnotationsNoteIsServed(string? note, string? served) =>
        Annotations.Route(note).Should().Be(served);

    /// <summary>
    /// The four responses that carried a settling note carry no field a note could travel in, so a
    /// note cannot come back by someone filling a property that is already there.
    /// </summary>
    [Theory]
    [InlineData(typeof(EntityRelationshipResponse))]
    [InlineData(typeof(EntityRelationshipWitnessResponse))]
    [InlineData(typeof(DescriptorClaimResponse))]
    [InlineData(typeof(EntityClaimResponse))]
    public void AResponseThatCarriedASettlingNoteHasNoFieldForOne(Type response) =>
        response.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Note") || name.Contains("Reason"));

    /// <summary>Every string the response holds, as a client reads it once the JSON is decoded.</summary>
    private static void ContainsNoNote(string wire)
    {
        using var document = JsonDocument.Parse(wire);
        var said = Strings(document.RootElement).ToList();
        said.Should().NotBeEmpty();
        foreach (var note in Notes)
        {
            said.Should().NotContain(value => value.Contains(note, StringComparison.Ordinal));
        }

        said.Should().NotContain(value => value.Contains("виправ") || value.Contains("Фамарь")
            || value.StartsWith("agent") || value.Contains("review page") || value.Contains("in chat")
            || value.Contains("his word") || value.Contains("or descendant"));
    }

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Strings(property.Value)),
        _ => [],
    };

    private void Tie(
        Entity from, Entity to, string relation, LinkMethod method, double? confidence, string source, string note)
    {
        _db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = relation,
            Category = RelationshipCategories.Read,
            CanonicalBook = 10,
            CanonicalChapter = 13,
            CanonicalVerse = 1,
            Method = method,
            Confidence = confidence,
            Source = source,
            Notes = note,
        });
        _db.SaveChanges();
    }

    private void Name(Word word, Entity entity, LinkMethod method, double? confidence, string note) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = method, Confidence = confidence, Source = "a test",
            Note = note,
            Claims = [new WordEntityClaim { Method = method, Confidence = confidence, Source = "a test", Note = note }],
        });

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

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
