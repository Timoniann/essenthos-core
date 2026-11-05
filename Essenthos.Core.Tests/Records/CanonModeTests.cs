using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The canon mode (DOC-0224 §7.2, the owner's D4): <c>records/canon</c> loaded into <c>entity_canon</c>, and a mode that hides
/// a record none of the canons it receives attests. A record no canon attests is named in no word and no mode hides it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CanonModeTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public CanonModeTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        foreach (var slug in new[] { "adam", "peter", "tobias", "enoch", "mordecai", "nobody" })
        {
            _db.Entities.Add(new Entity { Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = "a test" });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    [Fact]
    public void EachCanonModeReceivesTheCanonsOfItsBooks()
    {
        CanonAttestation.Received(Canons.Find("protestant")!).Should().Equal(RecordCanonGroups.Hebrew, RecordCanonGroups.GreekNt);
        CanonAttestation.Received(Canons.Find("tanakh")!).Should().Equal(RecordCanonGroups.Hebrew);
        CanonAttestation.Received(Canons.Find("catholic")!)
            .Should().Equal(RecordCanonGroups.Hebrew, RecordCanonGroups.GreekNt, RecordCanonGroups.Deuterocanon);
        CanonAttestation.Received(Canons.Find("ethiopian")!).Should().Equal(RecordCanonGroups.All);
        CanonAttestation.TryMode(null, out var none).Should().BeTrue();
        none.Should().BeNull("no mode shows every record");
        CanonAttestation.TryMode("Catholic", out var catholic).Should().BeTrue();
        catholic!.Slug.Should().Be("catholic");
        CanonAttestation.TryMode("lutheran", out _).Should().BeFalse("a misspelt mode is refused, not ignored");
    }

    [Fact]
    public async Task TheFileLoadsAndAModeHidesWhatOnlyCanonsItDoesNotReceiveAttest()
    {
        Canon(("adam", RecordCanonGroups.Hebrew), ("enoch", RecordCanonGroups.Hebrew), ("enoch", RecordCanonGroups.Ethiopic),
            ("mordecai", RecordCanonGroups.Hebrew), ("mordecai", RecordCanonGroups.Deuterocanon),
            ("peter", RecordCanonGroups.GreekNt), ("tobias", RecordCanonGroups.Deuterocanon));

        (await Load()).Should().Be(7);
        (await _db.EntityCanons.CountAsync()).Should().Be(7);

        (await Shown("protestant")).Should().Equal("adam", "enoch", "mordecai", "nobody", "peter");
        (await Shown("tanakh")).Should().Equal("adam", "enoch", "mordecai", "nobody");
        (await Shown("catholic")).Should().Equal("adam", "enoch", "mordecai", "nobody", "peter", "tobias");
        (await Shown(null)).Should().Equal("adam", "enoch", "mordecai", "nobody", "peter", "tobias");
    }

    [Fact]
    public async Task ASlugThatNamesNoRecordFailsTheLoad()
    {
        Canon(("adam", RecordCanonGroups.Hebrew), ("lamech", RecordCanonGroups.Hebrew));

        await FluentActions.Awaiting(Load).Should().ThrowAsync<InvalidOperationException>().WithMessage("*a slug names no record*");
    }

    [Fact]
    public async Task WithoutTheFileTheTableIsEmptyAndNothingIsHidden()
    {
        (await Load()).Should().Be(-1);
        (await Shown("tanakh")).Should().HaveCount(6);
    }

    private void Canon(params (string Slug, string Canon)[] rows)
    {
        var store = new ProductStore(_resources);
        using var writer = store.Create(RecordCanons.Product, ProductKind.Machine, RecordCanonRow.Schema, "test", []);
        writer.WriteAll(rows.Select(r => new RecordCanonRow(r.Slug, r.Canon, 1, 1, "BHSA 1.1:1.1")));
        writer.Complete();
    }

    private Task<long> Load() =>
        RecordCanons.Load(
            (NpgsqlConnection)_db.Database.GetDbConnection(), (NpgsqlTransaction)_transaction.GetDbTransaction(),
            new ProductStore(_resources), CancellationToken.None);

    private async Task<List<string>> Shown(string? canon)
    {
        CanonAttestation.TryMode(canon, out var mode).Should().BeTrue();
        return await _db.Entities.InCanon(mode).Select(e => e.Slug).OrderBy(slug => slug).ToListAsync();
    }
}
