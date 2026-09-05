using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Strong;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the load writes, asked of Postgres because the thing under test is a row and a foreign key
/// rather than a calculation.
///
/// The Moab case is the whole feature in four rows: one people, one man, one land, and a dictionary
/// clause that says which of the two the people descend from.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongGentilicLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly StrongGentilicLoader _loader;

    public StrongGentilicLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _loader = new StrongGentilicLoader(_db, NullLogger<StrongGentilicLoader>.Instance);
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AMoabiteReachesTheManAndNotTheLand()
    {
        Entry("H4125", "patronymical from מוֹאָב (H4124);");
        Entry("H4124", "from a prefix and אָב;");
        var man = Named("Moab", EntityKind.Person, "H4124");
        Named("Moab", EntityKind.Place, "H4124");

        var outcome = await _loader.Load();

        outcome.Claims.Should().Be(1);
        outcome.Resolved.Should().Be(1);

        var stated = await _db.StrongGentilics.SingleAsync();
        stated.OriginNumber.Should().Be("H4124");
        stated.Kind.Should().Be(GentilicKinds.Patronymic);
        stated.OriginEntityId.Should().Be(man.Id);
        stated.Statement.Should().Be("patronymical from מוֹאָב (H4124)");
        stated.Source.Should().Contain("Strong");
    }

    /// <summary>
    /// Seven men in the encyclopedia answer to H2226, six of them called Zerah, and the derivation
    /// names none of them in particular. The claim is still true and still written; only the page is
    /// withheld, which is the difference between saying less and saying something wrong.
    /// </summary>
    [Fact]
    public async Task AnAmbiguousOriginIsWrittenWithoutAPage()
    {
        Entry("H2227", "patronymically from זֶרַח (H2226);");
        Entry("H2226", "the same as זֶרַח;");
        Named("Zerah", EntityKind.Person, "H2226");
        Named("Zerah", EntityKind.Person, "H2226");

        var outcome = await _loader.Load();

        outcome.Claims.Should().Be(1);
        outcome.Resolved.Should().Be(0);
        (await _db.StrongGentilics.SingleAsync()).OriginEntityId.Should().BeNull();
    }

    /// <summary>
    /// The entry names a gentilic, states no origin, and is counted rather than guessed at. A
    /// refusal that leaves no trace is a refusal nobody can audit.
    /// </summary>
    [Fact]
    public async Task ARefusedEntryIsCountedAndNotWritten()
    {
        Entry("H512", "patrial from a name of uncertain derivation;");

        var outcome = await _loader.Load();

        outcome.Claims.Should().Be(0);
        outcome.Refused.Should().Be(1);
        (await _db.StrongGentilics.AnyAsync()).Should().BeFalse();
    }

    /// <summary>
    /// The startup pipeline runs on every boot, so a second run has to be silent.
    /// </summary>
    [Fact]
    public async Task LoadingTwiceWritesNothingTheSecondTime()
    {
        Entry("H4125", "patronymical from מוֹאָב (H4124);");

        await _loader.Load();
        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.StrongGentilics.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// A name whose Strong column holds a list is a title of several words, and a people is not
    /// named after a title.
    /// </summary>
    [Fact]
    public async Task ATitleIsNotAnOrigin()
    {
        Entry("H4125", "patronymical from מוֹאָב (H4124);");
        Named("King of Moab", EntityKind.Person, "H4428,H4124");

        var outcome = await _loader.Load();

        outcome.Resolved.Should().Be(0);
    }

    private void Entry(string number, string derivation)
    {
        _db.StrongEntries.Add(new StrongEntry
        {
            StrongNumber = number,
            Lemma = number,
            Definition = "a gloss",
            Derivation = derivation,
        });
        _db.SaveChanges();
    }

    private Entity Named(string name, EntityKind kind, string number)
    {
        var entity = new Entity
        {
            Kind = kind,
            Slug = $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}",
            Name = name,
            SourceId = Guid.NewGuid().ToString("N"),
            Source = "a test",
        };

        _db.Entities.Add(entity);
        _db.EntityNames.Add(new EntityName
        {
            Entity = entity,
            Label = name,
            HebrewStrongNumber = number,
        });
        _db.SaveChanges();

        return entity;
    }
}
