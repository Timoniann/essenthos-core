using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A place the gazetteer says is another name for another links to the place it names, and goes on
/// being a page when that place goes.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class AnotherNameForEndpointTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Entity _ramah;
    private readonly Entity _alias;

    public AnotherNameForEndpointTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _ramah = Place("ramah", "Ramah");
        _alias = Place("ramah-4", "Ramah");
        _db.SaveChanges();
        _alias.AnotherNameForEntityId = _ramah.Id;
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    private Entity Place(string slug, string name)
    {
        var place = new Entity { Kind = EntityKind.Place, Slug = slug, Name = name, SourceId = $"test:{slug}", Source = "test" };
        _db.Entities.Add(place);
        return place;
    }

    [Fact]
    public async Task AnAliasLinksToThePlaceItIsAnotherNameFor()
    {
        var named = await EncyclopediaEndpoints.AnotherNameFor(_db, _alias.AnotherNameForEntityId, "eng", default);

        named.Should().NotBeNull();
        (named!.Slug, named.Kind, named.Name).Should().Be(("ramah", "place", "Ramah"));
    }

    [Fact]
    public async Task APlaceThatIsNotAnAliasLinksToNothing()
    {
        (await EncyclopediaEndpoints.AnotherNameFor(_db, null, "eng", default)).Should().BeNull();
    }

    [Fact]
    public async Task AnAliasStaysWhenThePlaceItNamesGoes()
    {
        _db.Entities.Remove(_ramah);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var alias = await _db.Entities.AsNoTracking().SingleAsync(e => e.Slug == "ramah-4");
        alias.AnotherNameForEntityId.Should().BeNull();
    }
}
