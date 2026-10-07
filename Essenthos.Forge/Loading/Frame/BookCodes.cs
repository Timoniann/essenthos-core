using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// The three-letter book codes the versification data is written in, mapped to canonical ordinals.
///
/// They are here rather than in <c>BibleBookAbbreviation</c> because they belong to one source, the
/// way BHSA's Latin book names belong to BHSA's reader. The books the data names and the corpus
/// has no ordinal for are listed and deliberately unmapped, so that a rule about one is skipped
/// knowingly rather than failing as an unrecognised code.
///
/// The books of the Greek and Latin Bibles beyond the sixty-six are placed by the data's rules, so
/// that the Vulgate and the Douay, the Synodal, the King James's Apocrypha and the World English
/// Bible's stand at the standard's verses. The two Greek editions and the Ge'ez are the exception in
/// Tobit, Judith, Wisdom, Sirach, the two books of Esdras, the Prayer of Manasseh, Susanna and Bel:
/// the data's Greek columns there describe Rahlfs's text, and measured against the King James and
/// against each other they put Brenton's Tobit 13 and Sirach 23 and Swete's 1 Esdras 2 and Judith 9
/// beside other verses, so those editions stand at their own numbers there, as they did, and at the
/// verses <see cref="LetteredEditions"/> reads for them. Baruch, the Letter of Jeremiah and
/// Maccabees are placed for every edition.
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

        // The two books of Maccabees, where the data's only rules are the Latin's and every Greek and
        // English edition stands at its own numbers, so placing them moves nothing but the Vulgate.
        ["1Ma"] = 73, ["2Ma"] = 74,

        // Baruch, and the Letter of Jeremiah the Latin and English print as its sixth chapter. The
        // data numbers the letter as Baruch 6 in its standard column, which the frame places under
        // both names.
        ["Bar"] = 67, ["Lje"] = 76,
        ["Tob"] = 70, ["Jdt"] = 71, ["Wis"] = 75, ["Sir"] = 72, ["1Es"] = 68, ["2Es"] = 69, ["Man"] = 79,
        ["Sus"] = 77, ["Bel"] = 78,
    };

    /// <summary>The books placed by the data's rules for every edition but the Greek ones.</summary>
    private static readonly HashSet<int> OutsideTheGreek = [68, 69, 70, 71, 72, 75, 77, 78, 79];

    /// <summary>
    /// Books the data carries rules for and the frame has no ordinal for. Named so that a rule
    /// about one is skipped on purpose, and an unknown code is still an error worth reporting.
    /// </summary>
    private static readonly HashSet<string> BeyondTheCanon = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ade", "Es", "Esg", "Ma", "Oda",
    };

    public static bool TryGetOrdinal(string code, out int ordinal) => Canonical.TryGetValue(code, out ordinal);

    /// <summary>The code a book is written by in an address, upper case, or null for a book with none.</summary>
    public static string? Code(int ordinal) =>
        Canonical.Where(pair => pair.Value == ordinal).Select(pair => pair.Key.ToUpperInvariant()).FirstOrDefault();

    public static bool IsBeyondTheCanon(string code) => BeyondTheCanon.Contains(code);

    /// <summary>
    /// Whether the frame places this book in every edition. A book it does not stands at its own
    /// numbers in some, and a shared address there is only as good as the two texts' agreement.
    /// </summary>
    public static bool Places(int ordinal) => Placed.Contains(ordinal);

    /// <summary>Whether the data's rules for this book are read for an edition of this tradition.</summary>
    public static bool Places(Versification tradition, int ordinal) =>
        tradition != Versification.Septuagint || !OutsideTheGreek.Contains(ordinal);

    private static readonly HashSet<int> Placed = [.. Canonical.Values.Where(ordinal => !OutsideTheGreek.Contains(ordinal))];
}
