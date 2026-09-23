namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">The number as the dataset's Greek column writes it, letter and all.</param>
/// <param name="Strong">The Strong number the label's name is, or null where Strong's has none.</param>
/// <param name="Why">What the lexicon and the Greek witnesses say, which is how it was decided.</param>
internal sealed record ExtendedGreekNumber(string Written, string? Strong, string Why);

/// <summary>
/// The Greek numbers BibleData writes with a trailing letter, and the Strong number each one is.
///
/// <para>
/// On the Hebrew side a trailing letter is the lexicon's homograph marker and dropping it reaches
/// the entry. On the Greek side it is not Strong's at all: the dataset extends the numbering for
/// names the concordance lacks or spells otherwise, and the number before the letter is sometimes
/// the name and sometimes only a neighbour of it. <c>G2490a</c> is a spelling of Ἰωαννᾶς, G2490;
/// <c>G2492a</c> is Ἰούδας and not Ἰώβ, which is what G2492 is, so dropping the letter would file every
/// Judas of the dataset under Job's number.
/// </para>
///
/// <para>
/// So each one is read here, against the lexicon's lemma and against the number the Greek witnesses
/// write that name under at the verse the label names, and a lettered number this list does not
/// hold is refused rather than guessed at. These are this project's readings, not the dataset's.
/// </para>
/// </summary>
internal static class BibleDataGreekNumbers
{
    public static readonly IReadOnlyList<ExtendedGreekNumber> All =
    [
        new("G2492a", "G2455",
            "Ἰούδας, Judah and Judas. G2492 is Ἰώβ, Job; every Greek witness writes Ἰούδας under G2455."),
        new("G2492b", "G5601",
            "The Septuagint's Ὠβήδ for Eber of 1 Chronicles 8:12, the spelling the lexicon holds as G5601. " +
            "G2492 is Ἰώβ, Job."),
        new("G2490a", "G2490",
            "Ἰωανάν, which Nestle and Tischendorf write under G2490, Strong's Ἰωαννᾶς."),
        new("G2494a", "G2494",
            "Ἰωνάμ, which Nestle and Tischendorf write under G2494, Strong's Ἰωνάν of the same verse."),
        new("G2501a", "G2501",
            "Ἰωσήχ, which Nestle and Tischendorf write under G2501, the Ἰωσήφ the Textus Receptus prints " +
            "at the same place."),
        new("G2725a", "G2725",
            "κατήγωρ, the form of κατήγορος, G2725, that the critical texts print at Revelation 12:10."),
        new("G4450a", "G4450",
            "Πύρρος, which Strong's does not hold as a name; Nestle and Tischendorf write it under G4450, " +
            "the adjective it is formed from."),
        new("G5529a", "G5529", "Χουζᾶς, which the lexicon holds as G5529."),
        new("G3102a", "G3158",
            "Ματθάτ, G3158, which every Greek witness writes it under. G3102 is μαθήτρια, a female " +
            "disciple."),
        new("G3483a", "G3497",
            "Ναιμάν, the lexicon's Νεεμάν, G3497, which Nestle and Tischendorf write it under. G3483 " +
            "is ναί, yes."),
        new("G3303a", "G3104",
            "Μεννά, which Nestle and Tischendorf write under G3104, the Μαϊνάν the Textus Receptus prints " +
            "at the same place. G3303 is μέν."),
        new("G2580a", "G2581",
            "Καναναῖος, which the Greek witnesses write under G2581, Κανανίτης. G2580 is Κανᾶ, Cana."),
        new("G95a", null,
            "Ἀδμίν, which Strong's does not hold. G95 is ἀδίκως, unjustly, and Nestle's G284 at the " +
            "place is Aminadab's number, the name the Textus Receptus prints there."),
        new("G5101a", null,
            "Τίτιος, which Strong's does not hold. G5101 is τίς, who, and Nestle's G5103 at the place " +
            "is Titus's number."),
    ];

    private static readonly Dictionary<string, ExtendedGreekNumber> ByWritten =
        All.ToDictionary(number => number.Written, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The Strong number a lettered Greek number is, null where it is none, and false where nobody
    /// has read it.
    /// </summary>
    public static bool TryRead(string written, out string? strong)
    {
        if (ByWritten.TryGetValue(written, out var number))
        {
            strong = number.Strong;
            return true;
        }

        strong = null;
        return false;
    }
}
