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
        (await Ours()).Should().BeEmpty();

        Text("NWT2013", Redistribution.Prohibited);
        Text("XYZ", Redistribution.Unknown);
        (await Ours()).Should().Equal("NWT2013", "XYZ");
    }

    /// <summary>
    /// What the check refuses among this test's own texts: the database is shared, and a class
    /// running beside this one may hold texts of its own for the moment it runs.
    /// </summary>
    private async Task<IReadOnlyList<string>> Ours() =>
        [.. (await Publisher.Unreleasable(_db, CancellationToken.None))
            .Where(slug => slug is "KJV" or "BSB" or "BHSA" or "NWT2013" or "XYZ")];
}
