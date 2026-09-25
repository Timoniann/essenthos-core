using System.Text;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// A word of the Ethiopic Bible reduced to what a model can count.
///
/// <para>
/// Ge'ez is written in a syllabary: each letter is a consonant and one of seven vowels, laid out in
/// Unicode as rows of eight, so <c>(letter - U+1200) &gt;&gt; 3</c> is the consonant. The vowels are
/// where the language inflects — the accusative <em>ቃለ</em> against <em>ቃል</em>, the suffix
/// <em>ቃሉ</em> "his word" — so the consonants alone are the word much as BHSA's unpointed
/// consonants are the Hebrew one, and for the same reason: a model that has to learn every vowel
/// of every form separately never sees any of them often enough.
/// </para>
///
/// <para>
/// Four pairs and one triple of letters were once different sounds and had long merged when the
/// manuscripts behind these texts were copied, so the scribes — and the typists of the church's
/// printed Bible after them — write one for the other: ሀ ሐ ኀ, ሰ ሠ, አ ዐ, ጸ ፀ. They are one
/// consonant here, which also absorbs part of the typing slips of the church's text.
/// </para>
///
/// <para>
/// Ge'ez writes onto a word what Greek writes as words of its own: <em>ወ-</em> "and", <em>ለ-</em>
/// "to", <em>በ-</em> "in", <em>ዘ-</em> "which, of", <em>እም-</em> "from", <em>እስከ-</em> "until",
/// in front, and the pronoun of the object or the owner behind. Those come off, so <em>ወለእግዚአብሔር</em>
/// counts as <em>እግዚአብሔር</em> and meets κύριος, and καί and the article are left for the Greek to
/// answer with nothing. It is shallow, like the other stemmers: a prefix is taken only where two
/// letters remain, and one suffix at most.
/// </para>
/// </summary>
internal static class GeezStemmer
{
    private const int FirstSyllable = 0x1200;

    private const int LastSyllable = 0x135A;

    /// <summary>What must be left of a word once a prefix or a suffix is taken off it.</summary>
    private const int Keep = 2;

    /// <summary>The sixth order, the consonant with no vowel or the short ə.</summary>
    private const int Sixth = 5;

    /// <summary>The words Ge'ez writes onto the front of the next, longest first.</summary>
    private static readonly string[] Proclitics = ["እስከ", "እም", "እን", "ወ", "ለ", "በ", "ዘ"];

    /// <summary>
    /// The object and possessive pronouns, longest first. <em>-ya</em> "my" is taken only after a
    /// consonant with no vowel — <em>ቤትየ</em> — because after a vowel it is as often the last
    /// letter of the word: <em>ሰማየ</em> is the accusative of <em>ሰማይ</em> "heaven".
    /// </summary>
    private static readonly string[] Suffixes = ["ሆሙ", "ሆን", "ክሙ", "ክን", "ሁ", "ሃ", "ሙ", "ከ", "ኪ", "ነ", "ኒ", "ዮ"];

    private const string My = "የ";

    /// <summary>
    /// The rows whose sound manuscripts interchange, each moved onto the one it is merged with.
    /// </summary>
    private static readonly Dictionary<int, int> Homophones = new()
    {
        [Row('ሐ')] = Row('ሀ'),
        [Row('ኀ')] = Row('ሀ'),
        [Row('ሠ')] = Row('ሰ'),
        [Row('ዐ')] = Row('አ'),
        [Row('ፀ')] = Row('ጸ'),
    };

    /// <summary>
    /// The labiovelar rows, <em>ቈ</em> <em>ኰ</em> <em>ጐ</em> <em>ኈ</em>, which are the consonant of the
    /// row before them with a rounding the reduced word does not need.
    /// </summary>
    private static readonly Dictionary<int, int> Labiovelars = new()
    {
        [Row('ቈ')] = Row('ቀ'),
        [Row('ቘ')] = Row('ቐ'),
        [Row('ኈ')] = Row('ኀ'),
        [Row('ኰ')] = Row('ከ'),
        [Row('ዀ')] = Row('ኸ'),
        [Row('ጐ')] = Row('ገ'),
    };

    /// <summary>
    /// The consonants as Latin letters, for comparing a name with its Greek: <c>h</c>, the glottal
    /// stop and the pharyngeal are what Greek does not write and <see cref="NameLists"/> drops.
    /// </summary>
    private static readonly Dictionary<int, string> Latin = new()
    {
        [Row('ሀ')] = "h", [Row('ለ')] = "l", [Row('ሐ')] = "h", [Row('መ')] = "m", [Row('ሠ')] = "s",
        [Row('ረ')] = "r", [Row('ሰ')] = "s", [Row('ሸ')] = "sh", [Row('ቀ')] = "q", [Row('ቐ')] = "q",
        [Row('በ')] = "b", [Row('ቨ')] = "v", [Row('ተ')] = "t", [Row('ቸ')] = "ch", [Row('ኀ')] = "h",
        [Row('ነ')] = "n", [Row('ኘ')] = "n", [Row('አ')] = string.Empty, [Row('ከ')] = "k", [Row('ኸ')] = "k",
        [Row('ወ')] = "w", [Row('ዐ')] = string.Empty, [Row('ዘ')] = "z", [Row('ዠ')] = "zh", [Row('የ')] = "y",
        [Row('ደ')] = "d", [Row('ዸ')] = "d", [Row('ጀ')] = "g", [Row('ገ')] = "g", [Row('ጘ')] = "g",
        [Row('ጠ')] = "t", [Row('ጨ')] = "ch", [Row('ጰ')] = "p", [Row('ጸ')] = "s", [Row('ፀ')] = "s",
        [Row('ፈ')] = "f", [Row('ፐ')] = "p",
    };

    /// <summary>The consonants of the word without its clitics, one letter of the first order each.</summary>
    public static string Stem(string word)
    {
        var bare = Bare(word);
        return bare.Length == 0 ? word.ToLowerInvariant() : Consonants(Suffixless(Unprefixed(bare)));
    }

    /// <summary>The word's letters without the clitics on either side.</summary>
    internal static string Unprefixed(string word)
    {
        var stripped = true;
        while (stripped)
        {
            stripped = false;
            foreach (var proclitic in Proclitics)
            {
                if (word.Length - proclitic.Length >= Keep && word.StartsWith(proclitic, StringComparison.Ordinal))
                {
                    word = word[proclitic.Length..];
                    stripped = true;
                    break;
                }
            }
        }

        return word;
    }

    internal static string Suffixless(string word)
    {
        foreach (var suffix in Suffixes)
        {
            if (word.Length - suffix.Length >= Keep && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return word[..^suffix.Length];
            }
        }

        return word.Length - 1 >= Keep && word.EndsWith(My, StringComparison.Ordinal) && Order(word[^2]) == Sixth
            ? word[..^1]
            : word;
    }

    /// <summary>
    /// Each syllable as its consonant, homophones merged; anything that is not a syllable — a
    /// numeral, a stray mark — stays as it is.
    /// </summary>
    internal static string Consonants(string word)
    {
        var consonants = new StringBuilder(word.Length);
        foreach (var letter in word)
        {
            consonants.Append(IsSyllable(letter) ? (char)(FirstSyllable + (Merged(Row(letter)) << 3)) : letter);
        }

        return consonants.ToString();
    }

    /// <summary>
    /// The letter with the homophones merged and the vowel kept, which is how a reader searches: a
    /// word typed with <em>ሀ</em> finds the one printed with <em>ሐ</em>.
    /// </summary>
    public static string Fold(string word)
    {
        var folded = new StringBuilder(word.Length);
        foreach (var letter in word)
        {
            folded.Append(IsSyllable(letter) && Homophones.TryGetValue(Row(letter), out var into)
                ? (char)(FirstSyllable + (into << 3) + Order(letter))
                : letter);
        }

        return folded.ToString();
    }

    /// <summary>The consonants of a word in Latin letters, clitics off, for spelling it against a name.</summary>
    public static string Transliterated(string word)
    {
        var latin = new StringBuilder(word.Length);
        foreach (var letter in Unprefixed(Bare(word)))
        {
            if (IsSyllable(letter) && Latin.TryGetValue(Unrounded(Row(letter)), out var sound))
            {
                latin.Append(sound);
            }
        }

        return latin.ToString();
    }

    /// <summary>The syllables of a word, without whatever else a typist put into it.</summary>
    private static string Bare(string word) => new([.. word.Where(IsSyllable)]);

    private static bool IsSyllable(char letter) => letter is >= (char)FirstSyllable and <= (char)LastSyllable;

    private static int Row(char letter) => (letter - FirstSyllable) >> 3;

    private static int Order(char letter) => (letter - FirstSyllable) & 7;

    private static int Unrounded(int row) => Labiovelars.TryGetValue(row, out var plain) ? plain : row;

    private static int Merged(int row)
    {
        row = Unrounded(row);
        return Homophones.TryGetValue(row, out var into) ? into : row;
    }
}
