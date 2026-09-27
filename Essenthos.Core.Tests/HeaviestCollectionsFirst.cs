using Xunit;
using Xunit.Abstractions;

[assembly: TestCollectionOrderer(
    "Essenthos.Core.Tests.HeaviestCollectionsFirst", "Essenthos.Core.Tests")]

namespace Essenthos.Core.Tests;

/// <summary>
/// Starts the longest collections first, so that the run does not end on one that began late, and
/// alternates the classes that ask Postgres with the rest. The runner starts collections in this
/// order and keeps a thread for each until it finishes, and a database class waiting for one of
/// <see cref="WitnessDatabase"/>'s places holds its thread doing nothing; started one after
/// another, the database classes took every thread while half of them waited. Classes that read the
/// corpus go first on either side.
/// </summary>
public sealed class HeaviestCollectionsFirst : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        var ordered = testCollections.OrderBy(collection => ReadsCorpus(collection) ? 0 : 1).ToList();
        using var database = ordered.Where(UsesWitnessDatabase).GetEnumerator();
        using var others = ordered.Where(collection => !UsesWitnessDatabase(collection)).GetEnumerator();

        var more = true;
        while (more)
        {
            more = false;
            if (database.MoveNext())
            {
                more = true;
                yield return database.Current;
            }

            if (others.MoveNext())
            {
                more = true;
                yield return others.Current;
            }
        }
    }

    private static bool UsesWitnessDatabase(ITestCollection collection) =>
        collection.CollectionDefinition?.Name == typeof(WitnessDatabaseCollection).FullName;

    /// <summary>
    /// Whether the class a collection was made for carries the corpus category. Collections here are
    /// one per class, and both kinds of name end with the class's full name.
    /// </summary>
    private static bool ReadsCorpus(ITestCollection collection)
    {
        var className = collection.DisplayName[(collection.DisplayName.LastIndexOf(' ') + 1)..];
        var testClass = typeof(HeaviestCollectionsFirst).Assembly.GetType(className);
        return testClass?.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType == typeof(TraitAttribute) &&
            attribute.ConstructorArguments is [{ Value: TestCategory.Name }, { Value: TestCategory.Corpus }]) == true;
    }
}
