using Xunit;
using Xunit.Abstractions;

[assembly: TestCollectionOrderer(
    "Essenthos.Core.Tests.DatabaseCollectionFirst", "Essenthos.Core.Tests")]

namespace Essenthos.Core.Tests;

/// <summary>
/// Starts the scratch database's collection before every other one. Its classes share one database
/// and so run one after another, which makes it the longest collection in the suite by minutes,
/// while every other collection runs beside it on the remaining threads. The runner starts
/// collections in this order and keeps a thread for each one until it finishes, so the database
/// collection is running from the first second; left where the default order put it, it waited
/// behind a hundred short collections — 80 seconds of a ten-minute run, and the run ended that much
/// later.
/// </summary>
public sealed class DatabaseCollectionFirst : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        testCollections.OrderBy(collection => collection.DisplayName == WitnessDatabaseCollection.Name ? 0 : 1);
}
