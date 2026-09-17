namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// The three-letter book codes the versification data is written in, mapped to canonical ordinals.
///
/// They are here rather than in <c>BibleBookAbbreviation</c> because they belong to one source, the
/// way BHSA's Latin book names belong to BHSA's reader. The books beyond the sixty-six are listed
/// and deliberately unmapped: the frame has no place for them yet, and a rule about Tobit should be
/// skipped knowingly rather than fail as an unrecognised code.
///
/// Unmapped is not the same as absent. Brenton holds fourteen of these as books of its own, at
/// canonical ordinals 67 to 81; what they lack is a place in the frame, so nothing reversifies them
/// and nothing can be laid against them.
/// </summary>
internal static class BookCodes
{
    private static readonly Dictionary<string, int> Canonical = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gen"] = 1, ["Exo"] = 2, ["Lev"] = 3, ["Num"] = 4, ["Deu"] = 5,
        ["Jos"] = 6, ["Jdg"] = 7, ["Rut"] = 8, ["1Sa"] = 9, ["2Sa"] = 10,
        ["1Ki"] = 11, ["2Ki"] = 12, ["1Ch"] = 13, ["2Ch"] = 14, ["Ezr"] = 15,
        ["Neh"] = 16, ["Est"] = 17, ["Job"] = 18, ["Psa"] = 19, ["Pro"] = 20,
        ["Ecc"] = 21, ["Sng"] = 22, ["Isa"] = 23, ["Jer"] = 24, ["Lam"] = 25,
        ["Ezk"] = 26, ["Dan"] = 27, ["Hos"] = 28, ["Jol"] = 29, ["Amo"] = 30,
        ["Oba"] = 31, ["Jon"] = 32, ["Mic"] = 33, ["Nam"] = 34, ["Hab"] = 35,
        ["Zep"] = 36, ["Hag"] = 37, ["Zec"] = 38, ["Mal"] = 39,
        ["Mat"] = 40, ["Mrk"] = 41, ["Luk"] = 42, ["Jhn"] = 43, ["Act"] = 44,
        ["Rom"] = 45, ["1Co"] = 46, ["2Co"] = 47, ["Gal"] = 48, ["Eph"] = 49,
        ["Php"] = 50, ["Col"] = 51, ["1Th"] = 52, ["2Th"] = 53, ["1Ti"] = 54,
        ["2Ti"] = 55, ["Tit"] = 56, ["Phm"] = 57, ["Heb"] = 58, ["Jas"] = 59,
        ["1Pe"] = 60, ["2Pe"] = 61, ["1Jn"] = 62, ["2Jn"] = 63, ["3Jn"] = 64,
        ["Jud"] = 65, ["Rev"] = 66,
    };

    /// <summary>
    /// Books the data carries rules for and the frame has no ordinal for. Named so that a rule
    /// about one is skipped on purpose, and an unknown code is still an error worth reporting.
    /// </summary>
    private static readonly HashSet<string> BeyondTheCanon = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ade", "Bar", "Bel", "Es", "Esg", "1Es", "2Es", "Jdt", "Lje", "Ma", "1Ma", "2Ma",
        "Man", "Oda", "Sir", "Sus", "Tob", "Wis",
    };

    public static bool TryGetOrdinal(string code, out int ordinal) => Canonical.TryGetValue(code, out ordinal);

    public static bool IsBeyondTheCanon(string code) => BeyondTheCanon.Contains(code);
}
