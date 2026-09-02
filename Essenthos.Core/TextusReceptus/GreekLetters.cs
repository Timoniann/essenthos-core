namespace Essenthos.Core.TextusReceptus;

/// <summary>
/// A Greek word with its accents and its case set aside, so that two witnesses of the same word
/// can be recognised as the same word.
///
/// Nestle is accented polytonic Greek and the printed Textus Receptus editions carry no accents at
/// all, so comparing them as written reports every word in the New Testament as a different word.
///
/// **This does not use <c>string.Normalize</c>, and that is the whole point of the file.** This
/// project sets <c>InvariantGlobalization</c>, which leaves the runtime without ICU, and under it
/// <c>Normalize(FormD)</c> does not throw — it silently returns the string unchanged. A test of
/// the normalising version passed, because the test project does not set the flag, while the
/// running loader compared accented Greek against unaccented Greek and found 2,964 matches in
/// 134,863 pairs. A table cannot fail that way: it either has a letter or it does not.
///
/// The table is exactly the letters the loaded Greek texts contain — 151 of them, taken from the
/// database rather than from the Unicode charts, so it covers what is here and claims nothing
/// about what is not.
/// </summary>
internal static class GreekLetters
{
    private const string Written =
        "ΑἀἈἄἌᾄἂἆἎἁἉἅἍἃἋάᾴὰᾶᾷᾳΒΓΔΕἐἘἔἜἑἙἕἝἓἛέὲΖΗἠ" +
        "ἨἤἬᾔἢἪἦἮᾖᾐἡἩἥἭἣἧᾗᾑήῄὴῆῇῃΘΙἰἸἴἼἶἱἹἵἽἳἷίὶῖ" +
        "ϊΐῒΚΛΜΝΞΟὀὈὄὌὂὁὉὅὍὃὋόὸΠΡῥῬΣςΤΥὐὔὒὖὑὙὕὝὓὗ" +
        "ὟύὺῦϋΰῢΦΧΨΩὠὤὬὢὦὮᾠὡὩὥὭὧὯᾧώῴὼῶῷῳ";

    private const string Plain =
        "αααααααααααααααααααααβγδεεεεεεεεεεεεεζηη" +
        "ηηηηηηηηηηηηηηηηηηηηηηηηθιιιιιιιιιιιιιιι" +
        "ιιικλμνξοοοοοοοοοοοοοοπρρρσστυυυυυυυυυυυ" +
        "υυυυυυυφχψωωωωωωωωωωωωωωωωωωωωω";

    /// <summary>
    /// The word as the two editions would both have written it. A letter the table does not know
    /// is passed through: a Greek letter this corpus does not contain, or something that is not
    /// Greek at all, and neither is improved by being guessed at.
    /// </summary>
    public static string Bare(string word)
    {
        Span<char> bare = stackalloc char[word.Length];
        for (var i = 0; i < word.Length; i++)
        {
            var at = Written.IndexOf(word[i], StringComparison.Ordinal);
            bare[i] = at < 0 ? word[i] : Plain[at];
        }

        return new string(bare);
    }

    /// <summary>Whether the two witnesses write the same word, accents aside.</summary>
    public static bool Same(string left, string right) =>
        string.Equals(Bare(left), Bare(right), StringComparison.Ordinal);
}
