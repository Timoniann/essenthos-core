using Essenthos.Core.Products;

namespace Essenthos.Core.Build;

/// <param name="Load">The load's name, as its note in <c>build/served</c> is keyed.</param>
/// <param name="Digest">A digest of the load's declared source files, line endings made one.</param>
/// <param name="Tables">The tables the load fills, which <see cref="ApplyReloads"/> maps to the load again; <c>@carried</c> is every carried table.</param>
internal sealed record LoadCodeDigest(string Load, string Digest, string[] Tables);

/// <summary>
/// The code of each load a build fills tables with (declared in Essenthos.Forge.csproj, digested at compile time into
/// <see cref="All"/>). <c>build/served/&lt;db&gt;</c> records each digest; <c>build --apply</c> loads again whole the tables of a
/// load whose code has moved since, as it does for a moved product, so a loader change that moves no product and adds no
/// migration still reaches the database.
/// </summary>
internal static partial class LoadCode
{
    private const string NotePrefix = "code-";

    /// <summary>The notes a served state records the code of every load in.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Notes => All.Select(load => new KeyValuePair<string, string>(NotePrefix + load.Load, load.Digest));

    /// <summary>Whether a served state records the code of its loads at all: one written before it did records none.</summary>
    public static bool Recorded(ProductHeader served) => served.Notes.Any(note => note.Key.StartsWith(NotePrefix, StringComparison.Ordinal));

    /// <summary>The loads whose code differs from what <paramref name="served"/> records; a load it does not name is new, and moved.</summary>
    public static IReadOnlyList<LoadCodeDigest> Moved(ProductHeader served) =>
    [
        .. All.Where(load => served.Notes.FirstOrDefault(note => note.Key == NotePrefix + load.Load).Value != load.Digest),
    ];

    /// <summary>The tables of <paramref name="load"/>, <c>@carried</c> read as every carried table.</summary>
    public static IEnumerable<string> Tables(LoadCodeDigest load) =>
        load.Tables.SelectMany(table => table == "@carried" ? CarriedTables.Tables : [table]);
}
