namespace Essenthos.Core.Configuration;

/// <summary>
/// Where a load keeps the review lists it writes. The lists under <c>Resources/Essenthos/review</c>
/// are the owner's: his console reads them and records his answers in them, and they describe the
/// database that console reads. A load into any other database — a copy a change is rehearsed on, a
/// test's — describes that database instead, so it writes its lists into a folder of its own and
/// leaves his as they were. His answers are read from his lists either way.
/// </summary>
/// <param name="ownerDatabase">The database the owner's console reads.</param>
internal sealed class ReviewLists(string ownerDatabase)
{
    public const string ConfigurationKey = "Dataset:OwnerDatabase";

    /// <summary>The owner's list of that name, which his console reads and answers in.</summary>
    public static string Owners(string resources, string file) =>
        Path.Combine(resources, "Essenthos", "review", file);

    /// <summary>
    /// The list a load into <paramref name="database"/> writes: the owner's own when that is his
    /// database, and otherwise one in a folder named after the database, outside the corpus.
    /// </summary>
    public string For(string resources, string file, string database) =>
        IsOwners(database)
            ? Owners(resources, file)
            : Path.Combine(Path.GetTempPath(), "essenthos-review", database, file);

    public bool IsOwners(string database) => string.Equals(database, ownerDatabase, StringComparison.Ordinal);

    public static ReviewLists Read(IConfiguration configuration) =>
        configuration[ConfigurationKey] is { Length: > 0 } database
            ? new ReviewLists(database)
            : throw new InvalidOperationException(
                $"\"{ConfigurationKey}\" is not set, so a load cannot tell whether the review lists under " +
                "Resources/Essenthos/review describe the database it runs against. Set it to the database the " +
                "owner's console reads, as Essenthos.Forge/appsettings.json does.");
}
