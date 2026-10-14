using System.Text;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// A Korean word in Latin letters, so that a name's consonants can be set beside the original's.
///
/// <para>
/// A Hangul syllable is a lead consonant, a vowel and an optional final, packed into one code point
/// by arithmetic: (lead × 21 + vowel) × 28 + final above U+AC00. The Korean Bible spells a foreign
/// name sound by sound — 아브라함 is a-beu-ra-ham, 다윗 da-wit — so the leads and finals give the
/// name's consonants back. The vowels are written as plain vowels, which a skeleton drops anyway.
/// </para>
///
/// <para>
/// Hangul writes fewer consonants than Hebrew: one letter for l and r, one each for g and k, d and
/// t, b and p, and a final ㅅ for a closing d or t. <see cref="Coarse"/> folds a skeleton's letters
/// the same way, and both sides of a comparison go through it.
/// </para>
/// </summary>
internal static class HangulLetters
{
    private const char First = '가';
    private const char Last = '힣';
    private const int Vowels = 21;
    private const int Finals = 28;

    private static readonly string[] Leads =
        ["g", "gg", "n", "d", "dd", "r", "m", "b", "bb", "s", "ss", "", "z", "zz", "ts", "k", "t", "p", "h"];

    private static readonly string[] Closings =
    [
        "", "k", "k", "ks", "n", "nz", "n", "t", "l", "lk", "lm", "lb", "ls", "lt", "lp", "l", "m", "p", "ps", "t", "t",
        "", "t", "t", "k", "t", "p", "",
    ];

    /// <summary>
    /// The particles Korean writes on a name that spell a consonant of their own, longest first. 과 is
    /// <em>and</em>, 에게 <em>to</em>, 을 the object's mark: 사밀과 is Shamir and, and its K is no letter
    /// of the name. The particles of a vowel alone (의, 와, 이, 에) add nothing a skeleton reads.
    /// </summary>
    private static readonly string[] Particles =
        ["에게서", "께서는", "에게", "에서", "까지", "부터", "께서", "으로", "이라", "이니", "과", "을", "를", "은", "는", "가", "도", "만", "로", "께"];

    /// <summary>The fewest syllables left for a particle to be taken off: 하만 is Haman, not 하 and <em>only</em>.</summary>
    private const int LeastStem = 2;

    /// <summary>The word without a particle that spells a consonant, where at least <see cref="LeastStem"/> syllables are left.</summary>
    public static string WithoutParticle(string written)
    {
        foreach (var particle in Particles)
        {
            if (written.Length - particle.Length >= LeastStem && written.EndsWith(particle, StringComparison.Ordinal))
            {
                return written[..^particle.Length];
            }
        }

        return written;
    }

    /// <summary>Whether a letter is a Hangul syllable.</summary>
    public static bool IsSyllable(char letter) => letter is >= First and <= Last;

    /// <summary>The word in Latin letters, a vowel standing for each syllable's vowel; anything not a syllable is left out.</summary>
    public static string Romanised(string written)
    {
        var latin = new StringBuilder(written.Length * 3);
        foreach (var letter in written)
        {
            if (!IsSyllable(letter))
            {
                continue;
            }

            var index = letter - First;
            latin.Append(Leads[index / (Vowels * Finals)]).Append('a').Append(Closings[index % Finals]);
        }

        return latin.ToString();
    }

    /// <summary>A skeleton with the consonants Hangul does not tell apart made one: L and R, G and K, D and T, B and P.</summary>
    public static string Coarse(string skeleton)
    {
        var coarse = new StringBuilder(skeleton.Length);
        foreach (var letter in skeleton)
        {
            var merged = letter switch
            {
                'L' => 'R',
                'G' => 'K',
                'D' => 'T',
                'B' => 'P',
                _ => letter,
            };
            if (coarse.Length == 0 || coarse[^1] != merged)
            {
                coarse.Append(merged);
            }
        }

        return coarse.ToString();
    }
}
