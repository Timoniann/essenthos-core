using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Publishing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A release copies every text in the database to a public server, so the texts that may not travel
/// are looked for in the database itself, not only refused by the loader that is meant to keep them out.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ReleaseRedistributionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public ReleaseRedistributionTests(WitnessDatabase database)
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

    private void Text(string slug, Redistribution redistribution)
    {
        Corpus.Add(_db, slug, TextKind.Translation, "eng", (1, 1, ["In"])).Redistribution = redistribution;
        _db.SaveChanges();
    }

    [Fact]
    public async Task ATextThatMayNotBeRedistributedStopsTheRelease()
    {
        Text("KJV", Redistribution.PublicDomain);
        Text("BSB", Redistribution.ShareAlike);
        Text("BHSA", Redistribution.NonCommercialOnly);
        (await Publisher.Unreleasable(_db, CancellationToken.None)).Should().BeEmpty();

        Text("NWT2013", Redistribution.Prohibited);
        Text("XYZ", Redistribution.Unknown);
        (await Publisher.Unreleasable(_db, CancellationToken.None)).Should().Equal("NWT2013", "XYZ");
    }
}
