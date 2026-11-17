using System.Text.Json;
using Essenthos.Core.Build;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Descriptions;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A chapter's context says each record its lines point at once, in one map; a line that says one otherwise keeps it with
/// it. A title is said of a people in the plural and of a person in the singular, so in a chapter naming both the Anakim
/// and a giant the Anakim stay <em>Giants</em> and the giant <em>Giant</em>, whichever line the map took the title from.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ContextTargetFormTests : IDisposable
{
    private const int Numbers = 4;

    private readonly WitnessDatabase _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public ContextTargetFormTests(WitnessDatabase database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task APeoplesTitleStaysPluralBesideAPersonsSingular()
    {
        await using (var db = _database.NewContext())
        {
            var giant = Record(db, EntityKind.Title, "giant", "Giant");
            var anakim = Record(db, EntityKind.People, "anakim", "Anakim");
            var ahiman = Record(db, EntityKind.Person, "ahiman", "Ahiman", "male");
            var anak = Record(db, EntityKind.Person, "anak", "Anak", "male");
            Says(db, ahiman, DescriptorRelations.BearsTitle, giant, 1);
            Says(db, ahiman, "son-of", anak, 2);
            Says(db, anakim, DescriptorRelations.BearsTitle, giant, 1);
            Says(db, anakim, "descendants-of", anak, 2);
            db.TitleBearers.Add(new TitleBearer
            {
                TitleEntityId = giant.Id, BearerEntityId = anakim.Id,
                CanonicalBook = Numbers, CanonicalChapter = 13, CanonicalVerse = 33, Source = "a test",
                Called = TitleCalled.Write(new Dictionary<string, string> { ["eng"] = "Giants", ["ukr"] = "Велетні" }),
            });
            db.SaveChanges();
        }

        await Build();

        await using (var db = _database.NewContext())
        {
            var weights = await ContextWeights.Count(db, default);
            foreach (var (names, words) in new[] { ("eng", "eng"), ("ukr", "ukr") })
            {
                var stored = await ContextEndpoints.Context(db, Numbers, 13, names, weights, default, prose: words, stored: true);
                var composed = await ContextEndpoints.Context(db, Numbers, 13, names, weights, default, prose: words);
                Json(stored).Should().Be(Json(composed), "what is stored is what is composed");

                var plural = names == "eng" ? "Giants" : "Велетні";
                var entities = stored.Entities.ToDictionary(e => e.Slug);
                Said(stored, entities["anakim"], "giant").Should().Be(plural, names);
                Said(stored, entities["ahiman"], "giant").Should().Be("Giant", names);
                Said(stored, entities["anakim"], "anak").Should().Be("Anak", names);
                stored.Entities.Count(e => e.Records is not null).Should().Be(1, "only the line the map does not say as it does carries its own");
                stored.Entities.Single(e => e.Records is not null).Records!.Keys.Should().Equal("giant");
            }
        }
    }

    /// <summary>The name a line says a target by: its own form where it carries one, the answer's otherwise.</summary>
    private static string Said(ChapterContextResponse context, ContextEntityResponse entity, string target) =>
        (entity.Records is { } own && own.TryGetValue(target, out var said) ? said : context.Records[target]).Name;

    private async Task Build()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.NewContext());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var connection = _database.NewConnection();
        await new ContextBuild(connection.ConnectionString, new ProductStore(_root), provider, NullLogger.Instance).Run(false, default);
    }

    private static string Json(ChapterContextResponse context) =>
        JsonSerializer.Serialize(context, AppJsonSerializerContext.Default.ChapterContextResponse);

    private static Entity Record(AppDbContext db, EntityKind kind, string slug, string name, string? sex = null)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = name, Sex = sex };
        db.Entities.Add(entity);
        db.SaveChanges();
        db.EntityVerses.Add(new EntityVerse { EntityId = entity.Id, CanonicalBook = Numbers, CanonicalChapter = 13, CanonicalVerse = 22, Source = "test" });
        db.SaveChanges();
        return entity;
    }

    private static void Says(AppDbContext db, Entity subject, string relation, Entity target, int ordinal) =>
        db.EntityDescriptors.Add(new EntityDescriptor
        {
            EntityId = subject.Id,
            TargetEntityId = target.Id,
            Relation = relation,
            Ordinal = ordinal,
            CanonicalBook = Numbers,
            CanonicalChapter = 13,
            CanonicalVerse = 22,
            Method = LinkMethod.Manual,
            Source = "a test",
        });
}
