using System.Text.Json.Nodes;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A BibleData tie the owner removed in his console, as the reader meets it after the next load:
/// nowhere, from either end, while the row itself stays in the table.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WithdrawnRelationshipTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly string _resources;
    private readonly WithdrawnRelationshipLoader _loader;

    public WithdrawnRelationshipTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _resources = Path.Combine(Path.GetTempPath(), $"essenthos-withdrawn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_resources, "Essenthos", "review"));
        _loader = new WithdrawnRelationshipLoader(_db, NullLogger<WithdrawnRelationshipLoader>.Instance);
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        Directory.Delete(_resources, recursive: true);
    }

    /// <summary>
    /// Mary's page, James's page and the tree all stop saying she is his mother, and the tie he has
    /// from the text, brother of Jesus, stays.
    /// </summary>
    [Fact]
    public async Task ARemovedTieReachesNoPageFromEitherEnd()
    {
        var (mary, james, jesus) = (Person("mary", "female"), Person("james-3", "male"), Person("jesus", "male"));
        await _db.SaveChangesAsync();
        var mother = Tie(mary, "mother", james);
        var son = Tie(james, "son", mary);
        Tie(james, "brother", jesus);
        await _db.SaveChangesAsync();
        Decide("remove", "2026-09-28T10:00:00Z", "james-3", "mary", mother, son);

        var outcome = await _loader.Load(_resources);

        outcome.Withdrawn.Should().Be(2);
        (await Relationships.Of(_db, mary.Id, null, default)).Should().BeEmpty();
        (await Relationships.Of(_db, james.Id, null, default)).Select(r => r.Slug).Should().Equal("jesus");
        var family = await FamilyEndpoints.Family(_db, ["mary", "james-3"], null, default);
        family.People.SelectMany(p => p.Ties).Should().OnlyContain(t => t.Slug == "jesus");
        (await _db.EntityRelationships.IgnoreQueryFilters().CountAsync(r => r.Withdrawn)).Should().Be(2);
    }

    /// <summary>
    /// His later word stands: a removal taken back in the console is shown again on the next load,
    /// and a later removal of a fact first confirmed holds it back.
    /// </summary>
    [Fact]
    public async Task TheLatestDecisionOnARowIsTheOneApplied()
    {
        var (mary, judas) = (Person("mary", "female"), Person("judas-2", "male"));
        await _db.SaveChangesAsync();
        var mother = Tie(mary, "mother", judas);
        await _db.SaveChangesAsync();

        Decide("remove", "2026-09-11T10:00:00Z", "judas-2", "mary", mother);
        (await _loader.Load(_resources)).Withdrawn.Should().Be(1);

        Decide("confirm", "2026-09-28T10:00:00Z", "judas-2", "mary", mother);
        var restored = await _loader.Load(_resources);

        restored.Should().Match<WithdrawnRelationshipOutcome>(o => o.Withdrawn == 0 && o.Restored == 1);
        (await Relationships.Of(_db, mary.Id, null, default)).Should().ContainSingle();
    }

    /// <summary>
    /// A removal names rows by the ids of the day it was taken. A row that no longer joins either
    /// person it was decided about is somebody else's after a renumbering, and a row of ours is not
    /// the dataset's to remove: both stay shown.
    /// </summary>
    [Fact]
    public async Task ARowThatNoLongerFitsTheDecisionIsLeftShown()
    {
        var (lot, haran) = (Person("lot", "male"), Person("haran", "male"));
        await _db.SaveChangesAsync();
        var other = Tie(lot, "son", haran);
        var ours = Tie(lot, "son-of", haran, "read from Scripture by the project owner");
        await _db.SaveChangesAsync();
        Decide("remove", "2026-09-28T10:00:00Z", "judas-2", "mary", other);
        Decide("remove", "2026-09-28T10:00:00Z", "lot", "haran", ours);

        var outcome = await _loader.Load(_resources);

        outcome.Should().Match<WithdrawnRelationshipOutcome>(o => o.Withdrawn == 0 && o.Elsewhere == 2);
        (await _db.EntityRelationships.CountAsync(r => r.FromEntityId == lot.Id)).Should().Be(2);
    }

    private void Decide(string decision, string at, string a, string b, params EntityRelationship[] rows)
    {
        var path = Path.Combine(_resources, "Essenthos", "review", WithdrawnRelationshipLoader.ReviewFile);
        var document = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new JsonObject();
        var decisions = document["decisions"] as JsonObject ?? [];
        document["decisions"] = decisions;
        var key = string.Join('-', rows.Select(r => r.Id));
        decisions[key] = new JsonObject
        {
            ["a"] = a,
            ["b"] = b,
            ["decidedAt"] = at,
            ["decision"] = decision,
            ["rows"] = string.Join('|', rows.Select(r => r.Id)),
        };
        File.WriteAllText(path, document.ToJsonString());
    }

    private Entity Person(string slug, string sex)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person,
            Slug = slug,
            Name = char.ToUpperInvariant(slug[0]) + slug[1..],
            Sex = sex,
            SourceId = $"test:{slug}",
            Source = "test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private EntityRelationship Tie(Entity from, string type, Entity to, string source = BibleDataLoader.Source)
    {
        var row = new EntityRelationship
        {
            FromEntityId = from.Id,
            ToEntityId = to.Id,
            Type = type,
            Category = "inferred",
            Method = LinkMethod.StatedBySource,
            Source = source,
            CanonicalBook = 40,
            CanonicalChapter = 13,
            CanonicalVerse = 55,
        };
        _db.EntityRelationships.Add(row);
        return row;
    }
}
