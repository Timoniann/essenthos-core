namespace Essenthos.Core.Utils;

/// <summary>
/// Hebrew vowel points and Greek accents are separate codepoints, so a correctly spelled query
/// answers nothing against text that carries them: the unaccented Greek word is not a substring of
/// the accented one, and the unpointed Hebrew word is not a substring of the pointed one. Folding
/// removes them from both sides of the comparison; the stored text is never changed, only the
/// column that is searched against.
///
/// The folding is done by Postgres on both sides — the migration that fills
/// <c>original_words.normalized_text</c> and the search that folds the caller's term run the same
/// expression over the same pattern. Doing one of the two in C# looked simpler and was wrong: the
/// API is built with <c>InvariantGlobalization</c>, under which <c>String.Normalize</c> is a
/// no-op, so a precomposed accent would have survived on the query side and matched nothing.
/// </summary>
public static class DiacriticFolding
{
    /// <summary>
    /// The combining marks that occur in the loaded corpora, as a Postgres regex character class.
    /// Postgres has no Unicode-property classes, so the range is spelled out.
    ///
    /// Greek uses U+0300-U+036F; Hebrew uses U+0591-U+05BD (accents and points), U+05BF (rafe),
    /// U+05C1 and U+05C2 (shin and sin dots), U+05C4 and U+05C5 (marks) and U+05C7 (qamats
    /// qatan). The gaps matter: U+05BE (maqaf) and U+05C0 (paseq) are punctuation rather than
    /// marks, and stripping them would join words the text separates.
    /// </summary>
    public const string CombiningMarkPattern =
        "[\u0300-\u036F\u0591-\u05BD\u05BF\u05C1\u05C2\u05C4\u05C5\u05C7]";

    /// <summary>
    /// The letters transliteration schemes use for the glottal stop and the pharyngeal \u2014 the
    /// modifier apostrophes that open a transliteration like the one for elohim. They are letters
    /// rather than marks, so decomposition leaves them in place, and nobody searching for the word
    /// types them.
    /// </summary>
    public const string ModifierLetterPattern = "[\u02B9\u02BA\u02BB\u02BC\u02BD\u2018\u2019']";

    /// <summary>
    /// The folding itself, as a Postgres expression over one column or parameter. Kept here so the
    /// migration that fills a folded column and the query that folds the caller's term are the
    /// same text and cannot drift apart.
    /// </summary>
    public static string Expression(string source)
    {
        return $"lower(regexp_replace(regexp_replace(normalize({source}, NFD), " +
               $"'{Quoted(CombiningMarkPattern)}', '', 'g'), '{Quoted(ModifierLetterPattern)}', '', 'g'))";
    }

    /// <summary>
    /// The modifier-letter class contains an apostrophe, which closes the SQL string literal it is
    /// embedded in unless it is doubled.
    /// </summary>
    private static string Quoted(string pattern)
    {
        return pattern.Replace("'", "''");
    }
}
