namespace Essenthos.Core.Corpus;

/// <summary>
/// A town built on the site of an earlier one and named anew. Each keeps its own record, its own
/// verses and its own page, and the later stands at the earlier's point on the map: the gazetteer
/// files the later town's verse under the earlier site and gives it no point of its own.
/// </summary>
/// <param name="Later">The town built on the site, by address.</param>
/// <param name="Earlier">The place whose site it was built on, by address; it holds the gazetteer's point.</param>
public sealed record SharedSite(string Later, string Earlier);

public static class SharedSites
{
    public static readonly IReadOnlyList<SharedSite> All =
    [
        // Herod the Great rebuilt Aphek of the Sharon and named it after his father Antipater.
        new("antipatris", "aphek-2"),
    ];
}
