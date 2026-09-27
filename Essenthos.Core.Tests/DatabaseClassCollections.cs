using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: CollectionBehavior(
    "Essenthos.Core.Tests.DatabaseClassCollections", "Essenthos.Core.Tests")]

namespace Essenthos.Core.Tests;

/// <summary>
/// A collection per class, as xUnit does by default, and for each class in the witness-database
/// collection a collection of its own that still takes that collection's fixture. xUnit makes a
/// collection fixture once per collection, so every such class gets its own
/// <see cref="WitnessDatabase"/>, and the classes run side by side instead of in one queue.
/// </summary>
public sealed class DatabaseClassCollections(ITestAssembly testAssembly, IMessageSink diagnosticMessageSink)
    : IXunitTestCollectionFactory
{
    private readonly CollectionPerClassTestCollectionFactory _perClass = new(testAssembly, diagnosticMessageSink);
    private readonly ConcurrentDictionary<string, ITestCollection> _databaseClasses = new();
    private readonly ITypeInfo _definition =
        testAssembly.Assembly.GetType(typeof(WitnessDatabaseCollection).FullName!);

    public string DisplayName => "collection-per-class, a database per class";

    public ITestCollection Get(ITypeInfo testClass) =>
        UsesWitnessDatabase(testClass)
            ? _databaseClasses.GetOrAdd(testClass.Name, name =>
                new TestCollection(testAssembly, _definition, $"{WitnessDatabaseCollection.Name} {name}"))
            : _perClass.Get(testClass);

    private static bool UsesWitnessDatabase(ITypeInfo testClass) =>
        testClass.GetCustomAttributes(typeof(CollectionAttribute)).SingleOrDefault()?
            .GetConstructorArguments().Single() is WitnessDatabaseCollection.Name;
}
