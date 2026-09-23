using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A name no dataset gives a record, written once and beside the dataset's own.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnNameLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public OwnNameLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    private static readonly OwnName Heavens =
        new(["heaven"], "heavens", "שָׁמַיִם", "shamayim", "H8064", "the heavens", "no singular");

    [Fact]
    public async Task HeavenAnswersToHeavensOnceHoweverOftenItLoads()
    {
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Place,
            Slug = "heaven",
            Name = "heaven",
            SourceId = "test:heaven",
            Source = "test",
            Names = [new EntityName { Label = "heaven", Kind = "name" }],
        });
        await _db.SaveChangesAsync();
        var loader = new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance);

        (await loader.Load([Heavens], default)).Written.Should().Be(1);
        (await loader.Load([Heavens], default)).Written.Should().Be(0);

        var names = await _db.EntityNames.Where(n => n.Entity!.Slug == "heaven").Select(n => n.Label).ToListAsync();
        names.Should().BeEquivalentTo(["heaven", "heavens"]);
    }

    [Fact]
    public async Task ANameForARecordNobodyHoldsIsCountedNotWritten()
    {
        var outcome = await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance)
            .Load([Heavens with { Entities = ["nowhere-at-all"] }], default);

        outcome.Missing.Should().Be(1);
        outcome.Written.Should().Be(0);
    }

    /// <summary>The list ships inside the loader, so a build that dropped it would load nothing silently.</summary>
    [Fact]
    public async Task TheEmbeddedListIsRead()
    {
        var outcome = await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance).Load();

        outcome.Missing.Should().Be(2, "the scratch database holds neither heaven the list names");
    }
}
