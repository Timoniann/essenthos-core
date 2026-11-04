using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One canon that attests a record: a word of a held text names it in a verse of that canon (<see cref="RecordCanonGroups"/>).
/// A record named in no word has no row, and a canon mode never hides it.
/// </summary>
[PrimaryKey(nameof(EntityId), nameof(Canon))]
public class EntityCanon
{
    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>One of <see cref="RecordCanonGroups.All"/>.</summary>
    public required string Canon { get; set; }

    public override string ToString() => $"EntityCanon({Canon} attests entity {EntityId})";
}

/// <summary>
/// The canons a record is attested in, as <c>entity_canon.canon</c> holds them, and the books of each: the Hebrew Bible
/// (with Messale and Tägsas, the Ethiopian Bible's two halves of Proverbs), the New Testament, the books a Greek, Latin or
/// Slavonic canon receives beyond those, and the books only the Ethiopian canon receives.
/// </summary>
public static class RecordCanonGroups
{
    public const string Hebrew = "hebrew";

    public const string GreekNt = "greek-nt";

    public const string Deuterocanon = "deuterocanon";

    public const string Ethiopic = "ethiopic";

    public static readonly IReadOnlyList<string> All = [Hebrew, GreekNt, Deuterocanon, Ethiopic];
}
