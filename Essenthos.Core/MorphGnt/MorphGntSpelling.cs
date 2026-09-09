namespace Essenthos.Core.MorphGnt;

/// <summary>
/// The two ways a 1904 editor and a 2010 editor write the same Greek word differently, and no
/// others.
///
/// <para>
/// Nestle 1904 prints Ἰωάνης, Πειλᾶτος, Δαυείδ, Ἡλείας, Ῥαββεί, Ἐλεισάβετ, Σαμαρίας, Νινευείταις;
/// the SBLGNT prints Ἰωάννης, Πιλᾶτος, Δαυίδ, Ἠλίας, Ῥαββί, Ἐλισάβετ, Σαμαρείας, Νινευίταις. In
/// every case it is one of two things: <c>ει</c> written where the other has <c>ι</c>, or a
/// consonant written twice where the other writes it once. Neither says anything about which words
/// stood in the manuscripts; they are a century of convention.
/// </para>
///
/// <para>
/// **This is not itacism, and stopping short of itacism is the point.** By the Roman period
/// <c>ει</c>, <c>η</c>, <c>υ</c>, <c>οι</c> and <c>υι</c> had all come to be said as <c>ι</c>, and a
/// fold that followed the sound would also make ἡμᾶς and ὑμᾶς the same string — *us* and *you* —
/// and ἔχωμεν and ἔχομεν, a subjunctive and an indicative. Those pairs are places the two editions
/// disagree about the text, and folding them together would put the wrong person on a pronoun and
/// the wrong mood on a verb, silently, in the only direction that matters. Measured over the whole
/// New Testament, the wide fold pairs six pronouns and five verbs wrongly; this one pairs none.
/// </para>
///
/// <para>
/// A doubled vowel is left alone for the same reason: <c>αα</c> and <c>ηη</c> occur inside
/// transliterated Hebrew names — Ἀβραάμ, Ἰωσήφ — where the doubling is the name and not a habit.
/// </para>
/// </summary>
internal static class MorphGntSpelling
{
    /// <summary>
    /// The word with the two conventions removed, for comparison and for nothing else. It is not a
    /// searchable form and is never stored: two words that fold together here are still two
    /// different spellings and the corpus holds both.
    /// </summary>
    public static string Folded(string bare)
    {
        Span<char> folded = stackalloc char[bare.Length];
        var length = 0;

        for (var at = 0; at < bare.Length; at++)
        {
            var letter = bare[at];

            if (letter == 'ε' && at + 1 < bare.Length && bare[at + 1] == 'ι')
            {
                letter = 'ι';
                at++;
            }

            if (length > 0 && folded[length - 1] == letter && !IsVowel(letter))
            {
                continue;
            }

            folded[length++] = letter;
        }

        return new string(folded[..length]);
    }

    private static bool IsVowel(char letter) => letter is 'α' or 'ε' or 'η' or 'ι' or 'ο' or 'υ' or 'ω';
}
