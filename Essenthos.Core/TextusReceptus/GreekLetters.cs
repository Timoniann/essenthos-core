namespace Essenthos.Core.TextusReceptus;

/// <summary>
/// A Greek word with its accents and its case set aside, so that two witnesses of the same word
/// can be recognised as the same word, and so that a reader who types unaccented Greek finds it.
///
/// Nestle is accented polytonic Greek, the printed Textus Receptus editions carry no accents at
/// all, and a reader's keyboard has none either — so comparing them as written reports every word
/// in the New Testament as a different word, and every search for one as a miss.
///
/// **This does not use <c>string.Normalize</c>, and that is the whole point of the file.** This
/// project sets <c>InvariantGlobalization</c>, which leaves the runtime without ICU, and under it
/// <c>Normalize(FormD)</c> does not throw — it silently returns the string unchanged. A test of
/// the normalising version passed, because the test project does not set the flag, while the
/// running loader compared accented Greek against unaccented Greek and found 2,964 matches in
/// 134,863 pairs.
///
/// <para>
/// It also does not use a table of the letters that happen to be loaded, which is what it did
/// until the Septuagint arrived. That table was taken from the database, honestly, and covered
/// every letter the two Greek New Testaments contained — and then Brenton brought fourteen more.
/// 1,462 Septuagint words kept their accents, so <c>αδου</c> found three verses where <c>ᾅδου</c>
/// found fifty-nine, and <c>ωσηε</c> found none at all where Hosea is named eleven times. A table
/// of what is here claims nothing about what is not, and what is not turned up.
/// </para>
///
/// So this folds by the **structure of the Unicode block** instead. Greek Extended is laid out
/// sixteen cells to a vowel: every character from U+1F00 to U+1F0F is an alpha with some
/// combination of breathing, accent and length, and the same holds for each row after it. That is
/// a property of the encoding rather than of this corpus, so a Greek text nobody has loaded yet
/// folds correctly the day it arrives.
/// </summary>
internal static class GreekLetters
{
    /// <summary>
    /// The base letter each sixteen-cell row of Greek Extended belongs to, indexed by the row.
    /// U+1F00 is row 0. A row with no vowel of its own — the rows of marks — is a space, and its
    /// exceptions are handled below.
    /// </summary>
    private const string Rows =
        // 1F0x 1F1x 1F2x 1F3x 1F4x 1F5x 1F6x 1F7x
        "αεηιουω " +
        // 1F8x 1F9x 1FAx 1FBx 1FCx 1FDx 1FEx 1FFx
        "αηωαηιυω";

    /// <summary>
    /// The cells that do not follow their row.
    ///
    /// U+1F70 to U+1F7D is the row of bare grave and acute accents, two cells per vowel in
    /// alphabetical order rather than one row per vowel. Rho takes a breathing and lives in the
    /// upsilon row. Iota subscript adscript is a letter in the alpha row. And the free-standing
    /// diacritics — the ones that are marks rather than letters — belong nowhere and are dropped.
    ///
    /// <para>
    /// Five cells hold a letter of a vowel their row does not belong to, and every one of them
    /// is a capital: the block gave each vowel a row of its own forms and then put the capitals
    /// carrying a bare accent wherever there was space. Getting one wrong is silent, because the
    /// fold is still a Greek letter and the folded word is still a word — Ῥώμη came out as υωμη, so
    /// Rome could be found only by a reader who typed it that way.
    /// </para>
    /// </summary>
    private static char? Exception(char c) => c switch
    {
        >= 'ὰ' and <= 'ώ' => "αεηιουω"[(c - 'ὰ') / 2],
        'ῤ' or 'ῥ' or 'Ῥ' => 'ρ',   // ῤ ῥ Ῥ, in the upsilon row
        'Ὲ' or 'Έ' => 'ε',          // Ὲ Έ, in the eta row
        'Ὸ' or 'Ό' => 'ο',          // Ὸ Ό, in the omega row
        'ι' => 'ι',                    // ι adscript
        '᾽' or '᾿' or '῀' or '῁' or '῍' or '῎' or '῏'
            or '῝' or '῞' or '῟' or '῭' or '΅' or '`'
            or '´' or '῾' => ' ',  // marks, not letters
        _ => null,
    };

    /// <summary>
    /// The elision mark, however an edition writes it: a letter that is not there rather than
    /// punctuation, so it belongs in no folded form.
    ///
    /// Brenton writes it as U+02BC MODIFIER LETTER APOSTROPHE and uses the same character for a
    /// word-initial breathing. It sits outside every Greek block, so a fold that works by block
    /// structure passed it straight through and 4,832 Septuagint words came out as forms no other
    /// witness contains and no reader types. PRB-0158.
    /// </summary>
    private const string Elision = "ʼʻ’‘'";

    /// <summary>
    /// The accented letters of the monotonic block, which is not laid out in rows and so is
    /// written out. Uppercase and final sigma are handled by the ordinary case fold below.
    /// </summary>
    private const string Monotonic = "ΆΈΉΊΌΎΏΐ"
        + "ΪΫάέήίΰϊϋόύώ";

    private const string MonotonicPlain = "αεηιουωι" + "ιυαεηιυιυουω";

    /// <summary>
    /// The word as every witness would have written it bare: lower case, no breathings, no
    /// accents, no iota subscript, final sigma folded to medial. A character that is not Greek at
    /// all is passed through — punctuation, a digit, a Latin letter in a manuscript note — because
    /// none of those is improved by being guessed at.
    /// </summary>
    public static string Bare(string word)
    {
        Span<char> bare = stackalloc char[word.Length];
        var length = 0;

        foreach (var c in word)
        {
            var folded = Fold(c);
            if (folded != ' ')
            {
                bare[length++] = folded;
            }
        }

        return new string(bare[..length]);
    }

    /// <summary>One character, bare. A space means it was a mark and belongs in no word.</summary>
    public static char Fold(char c)
    {
        if (c is >= 'ἀ' and <= '῿')
        {
            return Exception(c) ?? Rows[(c - 'ἀ') / 16];
        }

        var at = Monotonic.IndexOf(c, StringComparison.Ordinal);
        if (at >= 0)
        {
            return MonotonicPlain[at];
        }

        return c switch
        {
            'ς' => 'σ',                                  // final sigma
            >= 'Α' and <= 'Ω' => (char)(c + 32),          // capitals
            'ͅ' or (>= '̀' and <= 'ͯ') => ' ',       // combining marks
            // The elision mark, which is a letter that is not there rather than punctuation.
            // Brenton writes it as U+02BC and uses the same character for an initial breathing, so
            // it stands between a word and every other witness of it: ἐπʼ folded to επʼ, which no reader
            // types and no other Greek text contains. Dropping it is what the Greek blocks' own
            // free-standing diacritics already do a few lines above; this one only lives outside
            // them. PRB-0158.
            _ when Elision.Contains(c) => ' ',
            _ => c,
        };
    }

    /// <summary>Whether the two witnesses write the same word, accents aside.</summary>
    public static bool Same(string left, string right) =>
        string.Equals(Bare(left), Bare(right), StringComparison.Ordinal);
}
