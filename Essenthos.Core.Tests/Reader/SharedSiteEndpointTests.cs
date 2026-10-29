using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// Antipatris, Herod's town on the site of Aphek, and Aphek each link to the other, by name in the
/// reader's language.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SharedSiteEndpointTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public SharedSiteEndpointTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _db.Entities.Add(new Entity { Kind = EntityKind.Place, Slug = "aphek-2", Name = "Aphek", SourceId = "test:aphek-2", Source = "test" });
        _db.Entities.Add(new Entity { Kind = EntityKind.Place, Slug = "antipatris", Name = "Antipatris", SourceId = "test:antipatris", Source = "test" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void AntipatrisIsDeclaredBuiltOnApheksSite() =>
        SharedSites.All.Should().ContainSingle(s => s.Later == "antipatris").Which.Earlier.Should().Be("aphek-2");

    [Fact]
    public async Task EachEndLinksToTheOther()
    {
        var site = SharedSites.All.Single(s => s.Later == "antipatris");

        var earlier = await EncyclopediaEndpoints.SharedSite(_db, site.Earlier, "eng", default);
        var later = await EncyclopediaEndpoints.SharedSite(_db, site.Later, "eng", default);

        (earlier!.Slug, earlier.Kind, earlier.Name).Should().Be(("aphek-2", "place", "Aphek"));
        (later!.Slug, later.Kind, later.Name).Should().Be(("antipatris", "place", "Antipatris"));
    }

    [Fact]
    public async Task APlaceOnNoSharedSiteLinksToNothing()
    {
        (await EncyclopediaEndpoints.SharedSite(_db, null, "eng", default)).Should().BeNull();
        (await EncyclopediaEndpoints.SharedSite(_db, "nowhere", "eng", default)).Should().BeNull();
    }
}
