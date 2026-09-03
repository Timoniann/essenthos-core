namespace Essenthos.Core.Utils;

/// <summary>
/// A Hebrew word reduced to its consonants, so that two editions of the same text can be compared
/// whatever they do with the pointing.
///
/// The Masoretic text is one text and its editions tokenise it differently: BHSA writes the
/// preposition, the article and the noun of לָאוֹר as three words, and the Westminster Leningrad
/// Codex writes one. Neither is wrong, and neither can be joined to the other by word count or by
/// Strong number — the numbering schemes disagree too. What both preserve exactly is the letters,
/// so the letters are what they are matched on: a run of one edition's words whose consonants
/// concatenate to the other's word is that word.
///
/// <para>
/// Final forms fold to their ordinary ones. A letter that ends a word in one edition stands inside
/// a joined word in the other — מַיִם and מַיִם־ do not differ, but the ם of one is the מ of the
/// other's continuation, and a comparison that kept them apart would fail on every word ending in
/// one of the five.
/// </para>
///
/// <para>
/// Everything after the <c>sof pasuq</c> is dropped. The Westminster edition prints its section
/// markers — <c>פ</c> for an open paragraph, <c>ס</c> for a closed one — after the verse-end mark
/// and glues them to the last word, so its Genesis 1:5 ends <em>אחד ׃ פ</em> where BHSA ends
/// <em>אחד</em>. They are both real Hebrew letters, so they cannot be dropped by letter: they are
/// dropped by where they stand. Not doing this cost 13 points of agreement between the two.
/// </para>
/// </summary>
public static class HebrewLetters
{
    /// <summary>The verse-end mark. What follows it is apparatus, not text.</summary>
    private const char SofPasuq = '׃';

    private const char FirstLetter = 'א';
    private const char LastLetter = 'ת';

    /// <summary>Final kaf, mem, nun, pe and tsadi, against the ordinary form of each.</summary>
    private static char Ordinary(char c) => c switch
    {
        'ך' => 'כ',
        'ם' => 'מ',
        'ן' => 'נ',
        'ף' => 'פ',
        'ץ' => 'צ',
        _ => c,
    };

    /// <summary>
    /// The word's consonants, with the pointing, the cantillation, the maqqef and the punctuation
    /// set aside. An empty result is not a failure: BHSA records 6,488 morphemes that print no
    /// letters at all, and they contribute nothing to a comparison rather than breaking it.
    /// </summary>
    public static string Of(string word)
    {
        var end = word.IndexOf(SofPasuq, StringComparison.Ordinal);
        var text = end >= 0 ? word.AsSpan(0, end) : word.AsSpan();

        Span<char> letters = stackalloc char[text.Length];
        var length = 0;
        foreach (var c in text)
        {
            if (c is >= FirstLetter and <= LastLetter)
            {
                letters[length++] = Ordinary(c);
            }
        }

        return new string(letters[..length]);
    }
}
