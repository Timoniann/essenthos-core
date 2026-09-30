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

    private static readonly CorrectedNumber Deborah =
        new(["deborah-2", "deborah"], "Deborah", new WrongNumber("H1682", null), "H1683", null, "the bee");

    /// <summary>
    /// The judge numbered by the word for a bee gets the name's number; the nurse, who already has the
    /// name under it, loses the wrong row a fold brought her; and a second run finds nothing.
    /// </summary>
    [Fact]
    public async Task ANumberADatasetWroteForAnotherWordIsCorrectedOnce()
    {
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person, Slug = "deborah-2", Name = "Deborah", SourceId = "test:deborah2", Source = "test",
            Names = [new EntityName { Label = "Deborah", HebrewStrongNumber = "H1682", Kind = "proper name" }],
        });
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person, Slug = "deborah", Name = "Deborah", SourceId = "test:deborah1", Source = "test",
            Names =
            [
                new EntityName { Label = "Deborah", HebrewStrongNumber = "H1683", Kind = "proper name" },
                new EntityName { Label = "Deborah", HebrewStrongNumber = "H1682", Kind = "proper name" },
            ],
        });
        await _db.SaveChangesAsync();
        var loader = new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance);

        (await loader.Correct([Deborah], default)).Should().Be(2);
        (await loader.Correct([Deborah], default)).Should().Be(0);

        var numbers = await _db.EntityNames.Where(n => n.Label == "Deborah")
            .Select(n => new { n.Entity!.Slug, n.HebrewStrongNumber }).ToListAsync();
        numbers.Should().BeEquivalentTo(
        [
            new { Slug = "deborah-2", HebrewStrongNumber = "H1683" },
            new { Slug = "deborah", HebrewStrongNumber = "H1683" },
        ]);
    }

    /// <summary>A row that carries a Greek number the correction does not name is another row and is left.</summary>
    [Fact]
    public async Task OnlyTheRowWithTheWrongNumbersIsTouched()
    {
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person, Slug = "deborah-2", Name = "Deborah", SourceId = "test:deborah2", Source = "test",
            Names = [new EntityName { Label = "Deborah", HebrewStrongNumber = "H1682", GreekStrongNumber = "G1", Kind = "proper name" }],
        });
        await _db.SaveChangesAsync();

        (await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance).Correct([Deborah], default)).Should().Be(0);
    }

    /// <summary>The list ships inside the loader, so a build that dropped it would load nothing silently.</summary>
    [Fact]
    public async Task TheEmbeddedListIsRead()
    {
        var outcome = await new OwnNameLoader(_db, NullLogger<OwnNameLoader>.Instance).Load();

        outcome.Missing.Should().Be(1, "the scratch database does not hold the heaven the list names");
    }
}
