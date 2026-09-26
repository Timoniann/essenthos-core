namespace Essenthos.Core.Corpus;

/// <summary>
/// The sets of cross references the corpus holds, which the loader writing a row and the surface
/// reading it have to agree on exactly: a set's id is what a reader chooses by and what every row
/// carries, and its source is the credit the dataset declaration counts the row under.
/// </summary>
public static class CrossReferenceSets
{
    /// <summary>What a set relates: verses a reader might turn to, or passages that tell the same thing in the same words.</summary>
    public enum Kind
    {
        References,
        Parallels,
    }

    /// <param name="Id">The set as a reader's choice and every row name it.</param>
    /// <param name="Dataset">The declared dataset the set is credited under, from <see cref="Datasets.All"/>.</param>
    /// <param name="Source">The credit every row of the set carries, which that declaration claims by prefix.</param>
    /// <param name="Name">What the set is called where that is not its dataset's name.</param>
    public sealed record Set(string Id, Kind What, string Dataset, string Source, string? Name = null);

    public const string OpenBible = "openbible";

    public const string Treasury = "tsk";

    public const string Parallels = "parallels";

    /// <summary>Which set the reader opens on, once it shows cross references at all.</summary>
    public const string Default = OpenBible;

    public const string OpenBibleSource = "OpenBible.info Cross References";

    public const string TreasurySource = "The Treasury of Scripture Knowledge";

    /// <summary>
    /// How a detected parallel is credited: the method, since nobody else states it. The same string
    /// begins the method the project's own dataset declares.
    /// </summary>
    public const string ParallelsSource =
        "passages detected by Essenthos, from long runs of the same dictionary forms in the Hebrew and Greek";

    /// <summary>In the order a reader is offered them, the default first.</summary>
    public static readonly Set[] All =
    [
        new(OpenBible, Kind.References, "openbible-cross-references", OpenBibleSource),
        new(Treasury, Kind.References, "tsk", TreasurySource),
        new(Parallels, Kind.Parallels, Datasets.Own, ParallelsSource, "Parallel passages"),
    ];

    public static Set? Find(string? id) =>
        Array.Find(All, set => string.Equals(set.Id, id, StringComparison.OrdinalIgnoreCase));
}
