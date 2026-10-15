namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Strong numbers that are one word. Strong gives the name two numbers by its vowels — H3068 where it is
/// pointed to be read <em>Adonai</em>, H3069 where it stands beside אֲדֹנָי and is pointed to be read
/// <em>Elohim</em> — and a third to its short form יָהּ, and a translation renders all three as it renders the
/// name. Learned per number, LORD on H3069 holds a twentieth of LORD's renderings and is refused although it
/// is the only name in its verse; learned per word, it holds its share of the name's.
/// </summary>
internal static class EvidentiaStrongFamilies
{
    private static readonly IReadOnlyDictionary<string, string> Families = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["H3069"] = "H3068",
        ["H3050"] = "H3068",
    };

    public static string None(string strongNumber) => strongNumber;

    public static string Of(string strongNumber) => Families.GetValueOrDefault(strongNumber, strongNumber);
}
