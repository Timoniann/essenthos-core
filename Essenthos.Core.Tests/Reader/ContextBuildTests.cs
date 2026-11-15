using System.Text.Json;
using Essenthos.Core.Build;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The chapter context stored at build (TSK-1669): what a request puts together from the stored chapter and lines is, byte for
/// byte, what it composes from the served tables, for every pair of languages, open texts and canon mode; a pair of languages
/// the build did not store, or an open text bringing a record the chapter does not name, is composed as before; and a current
/// product is copied, not composed again.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ContextBuildTests : IDisposable
{
    private const int Genesis = 1;

    private readonly WitnessDatabase _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public ContextBuildTests(WitnessDatabase database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task TheStoredContextIsTheComposedOneAndACurrentProductIsCopied()
    {
        Text english;
        await using (var db = _database.NewContext())
        {
            var hebrew = Corpus.Add(db, BhsaTextSource.Slug, TextKind.CriticalEdition, "hbo",
                (10, 1, ["אלה", "תולדת", "בני", "נח"]),
                (10, 2, ["בני", "יפת", "גמר"]),
                (10, 3, ["ובני", "גמר"]));
            english = Corpus.Add(db, "test-english", TextKind.Translation, "eng",
                (10, 1, ["These", "are", "the", "sons", "of", "Noah"]),
                (10, 2, ["The", "sons", "of", "Japheth"]),
                (10, 3, ["And", "the", "sons", "of", "him"]));
            var noah = Person(db, "noah", "male");
            var japheth = Person(db, "japheth", "male");
            var gomer = Person(db, "gomer", "male");
            var magog = Person(db, "magog", "male");
            var shinar = new Entity { Kind = EntityKind.Place, Slug = "shinar", Name = "Shinar" };
            db.Entities.Add(shinar);
            db.SaveChanges();

            Names(db, db.WordAt(hebrew, 10, 1, 4), noah, WordEntityOrigins.Settled);
            Names(db, db.WordAt(hebrew, 10, 2, 2), japheth, WordEntityOrigins.Settled);
            Names(db, db.WordAt(hebrew, 10, 2, 3), gomer, WordEntityOrigins.Settled);
            Names(db, db.WordAt(english, 10, 3, 5), gomer, WordEntityOrigins.Added);
            db.EntityVerses.Add(new EntityVerse { Entity = shinar, CanonicalBook = Genesis, CanonicalChapter = 10, CanonicalVerse = 3, Source = "test" });
            db.EntityCanons.Add(new EntityCanon { EntityId = magog.Id, Canon = RecordCanonGroups.Deuterocanon });
            Names(db, db.WordAt(hebrew, 10, 3, 2), magog, WordEntityOrigins.Settled);
            Tie(db, japheth, "son-of", noah);
            Tie(db, gomer, "son-of", japheth);
            Says(db, japheth, "son-of", noah);
            Says(db, gomer, "son-of", japheth);
            db.EntityDistinguishers.Add(new EntityDistinguisher
            {
                Entity = noah, Language = "ukr", Text = "той, хто збудував ковчег", English = "the man who built the ark", Source = "Essenthos, a test",
            });
            noah.Distinguisher = "the man who built the ark";
            db.EntityNameForms.Add(new EntityNameForm
            {
                EntityId = noah.Id, Language = "ukr", GrammaticalCase = GrammaticalCases.Nominative, Form = "Ной",
                Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
            });
            db.Events.Add(new Database.Entities.Event
            {
                Slug = "the-table-of-nations", Name = "The table of nations*", YearFromCreation = 1700,
                CanonicalBook = Genesis, CanonicalChapter = 10, CanonicalVerse = 1, Source = "test",
            });
            db.SaveChanges();
        }

        var store = new ProductStore(_root);
        var first = await Run(store);
        first.Made.Should().Be(1 + FamilyDocuments.ProseLanguages.Length, "the chapters and the lines of every language of words");
        first.Chapters.Should().Be(1);

        var open = new TextEntry(english.Id, english.Slug, [Genesis], true);
        await using (var db = _database.NewContext())
        {
            var weights = await ContextWeights.Count(db, default);
            foreach (var (language, prose, texts, mode) in new (string?, string?, IReadOnlyList<TextEntry>?, string?)[]
                     {
                         ("eng", "eng", null, null),
                         ("ukr", "ukr", null, null),
                         ("ukr", null, null, null),
                         ("rus", "ukr", null, null),
                         ("eng", "eng", [open], "protestant"),
                         ("ukr", "ukr", [open], "catholic"),
                         (null, null, null, null),
                     })
            {
                var canon = mode is null ? null : Canons.Find(mode);
                var composed = await ContextEndpoints.Context(db, Genesis, 10, language, weights, default, prose: prose, open: texts, mode: canon);
                var stored = await ContextEndpoints.Context(db, Genesis, 10, language, weights, default, prose: prose, open: texts, mode: canon, stored: true);
                Json(stored).Should().Be(Json(composed), $"names {language}, words {prose}, texts {texts?.Count}, canon {mode}");
            }

            var family = await ChapterFamilies.Of(db, Genesis, 10, "eng", default, "eng");
            var storedFamily = await ChapterFamilies.Of(db, Genesis, 10, "eng", default, "eng", stored: true);
            JsonSerializer.Serialize(storedFamily, AppJsonSerializerContext.Default.ChapterFamilyResponse)
                .Should().Be(JsonSerializer.Serialize(family, AppJsonSerializerContext.Default.ChapterFamilyResponse));
            family.Trees.Should().ContainSingle();

            (await db.ContextChapters.CountAsync()).Should().Be(1);
            (await db.ContextLines.Select(l => l.Prose).Distinct().CountAsync()).Should().Be(FamilyDocuments.ProseLanguages.Length);
        }

        var again = await Run(store);
        again.Made.Should().Be(0, "nothing it was made from moved");
        again.Rows.Should().Be(0, "the tables already hold the products");
    }

    private async Task<ContextOutcome> Run(ProductStore store)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.NewContext());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var connection = _database.NewConnection();
        return await new ContextBuild(connection.ConnectionString, store, provider, NullLogger.Instance).Run(false, default);
    }

    private static string Json(ChapterContextResponse context) =>
        JsonSerializer.Serialize(context, AppJsonSerializerContext.Default.ChapterContextResponse);

    private static Entity Person(AppDbContext db, string slug, string sex)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = char.ToUpperInvariant(slug[0]) + slug[1..], Sex = sex };
        db.Entities.Add(entity);
        return entity;
    }

    private static void Names(AppDbContext db, Word word, Entity entity, string origin) =>
        db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = entity.Id,
            Method = LinkMethod.Manual,
            Source = $"test:{entity.Slug}",
            Origin = origin,
            Role = WordEntityRoles.Name,
        });

    private static void Tie(AppDbContext db, Entity from, string type, Entity to) =>
        db.EntityRelationships.Add(new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = type,
            Category = RelationshipCategories.Read,
            Method = LinkMethod.Manual,
            Source = "read from Scripture by a test",
            CanonicalBook = Genesis,
            CanonicalChapter = 10,
            CanonicalVerse = 2,
        });

    private static void Says(AppDbContext db, Entity subject, string relation, Entity target) =>
        db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = subject.Id,
            TargetEntityId = target.Id,
            Relation = relation,
            Ordinal = 1,
            CanonicalBook = Genesis,
            CanonicalChapter = 10,
            CanonicalVerse = 2,
            Method = LinkMethod.Manual,
            Source = "a test",
        });
}
