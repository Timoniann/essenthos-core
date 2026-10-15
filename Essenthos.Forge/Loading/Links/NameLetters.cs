using System.Text;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The consonants of a translation's word, in every script the corpus holds, as <see cref="NameLists"/>
/// compares them with the original's.
///
/// <para>
/// <see cref="NameLists.Skeleton(string?, string?)"/> reads Latin, Greek, Hebrew, Cyrillic and Ge'ez.
/// Here the rest are given Latin letters first: Korean by its syllables (<see cref="HangulLetters"/>),
/// the Indian scripts by the place each consonant holds in its block — every one of them lays its
/// consonants out in the same order, so क, ক, ਕ, ક, க, క, ಕ and ക are all ka at offset 0x15 — Arabic
/// and Urdu letter by letter, and the letters Church Slavonic and Hausa add to their alphabets.
/// </para>
///
/// <para>
/// A name can come with more than one reading. The Synodal writes the θ of Greek names with
/// ф — Фарра is Terah, Вефиль Bethel — where it writes a Greek φ the same way, so a Cyrillic ф is
/// read both as P and as T. A script that does not tell some consonants apart (Korean, Tamil) is
/// compared with those consonants made one on both sides.
/// </para>
/// </summary>
internal static class NameLetters
{
    /// <summary>The readings of a word's consonants; empty where its script carries none we can read.</summary>
    public static IReadOnlyList<string> Of(string surface, string? language)
    {
        if (string.IsNullOrWhiteSpace(surface))
        {
            return [];
        }

        var lower = surface.ToLowerInvariant();
        if (lower.Any(HangulLetters.IsSyllable))
        {
            var korean = HangulLetters.Coarse(NameLists.Skeleton(HangulLetters.Romanised(HangulLetters.WithoutParticle(lower))));
            return korean.Length == 0 ? [] : [korean];
        }

        if (Indic(lower) is { } indic)
        {
            var latin = Transliterated(lower, IndicLetter);
            var skeleton = NameLists.Skeleton(latin);
            return skeleton.Length == 0 ? [] : [indic == Tamil ? HangulLetters.Coarse(skeleton) : skeleton];
        }

        if (lower.Any(IsArabic))
        {
            var skeleton = NameLists.Skeleton(Transliterated(lower, ArabicLetter));
            return skeleton.Length == 0 ? [] : [skeleton];
        }

        var written = Replaced(lower);
        var stemLanguage = language is "chu" ? "rus" : language;
        if (written.Contains('ф') && stemLanguage is "rus" or "ukr")
        {
            return
            [
                .. new[] { NameLists.Skeleton(written, stemLanguage), NameLists.Skeleton(written.Replace('ф', 'т'), stemLanguage) }
                    .Where(reading => reading.Length > 0)
                    .Distinct(),
            ];
        }

        var plain = NameLists.Skeleton(written, stemLanguage);
        return plain.Length == 0 ? [] : [plain];
    }

    /// <summary>
    /// The original's readings, made comparable with a translation's: where the translation's script
    /// cannot tell consonants apart, the original's are folded the same way.
    /// </summary>
    public static string Comparable(string originalSkeleton, string translationSurface) =>
        translationSurface.Any(HangulLetters.IsSyllable) || Indic(translationSurface) == Tamil
            ? HangulLetters.Coarse(originalSkeleton)
            : originalSkeleton;

    /// <summary>The best likeness between any reading of the translation's word and the original's.</summary>
    public static double Likeness(IReadOnlyList<string> readings, string original, string translationSurface)
    {
        var comparable = Comparable(original, translationSurface);
        var best = 0.0;
        foreach (var reading in readings)
        {
            best = Math.Max(best, NameLists.Alike(reading, comparable));
        }

        return best;
    }

    /// <summary>
    /// The form a text writes a name in, made one across its inflections, which is what a text's own
    /// spelling of an entity is learned as.
    /// </summary>
    public static string Form(string surface, string? language)
    {
        var folded = Encyclopedia.NameConsensus.Fold(surface);
        if (folded.Length == 0)
        {
            return folded;
        }

        return language switch
        {
            "rus" or "ukr" or "chu" => SlavicStemmer.Stem(folded, isName: true),
            "deu" => GermanStemmer.Stem(folded),
            "spa" => SpanishStemmer.Stem(folded),
            "kor" => HangulLetters.WithoutParticle(folded),
            _ => folded,
        };
    }

    private const int Tamil = 0x0B80;

    private static readonly int[] IndicBlocks = [0x0900, 0x0980, 0x0A00, 0x0A80, 0x0B00, 0x0B80, 0x0C00, 0x0C80, 0x0D00];

    private static int? Indic(string written)
    {
        foreach (var letter in written)
        {
            foreach (var block in IndicBlocks)
            {
                if (letter >= block && letter < block + 0x80)
                {
                    return block;
                }
            }
        }

        return null;
    }

    private static string Transliterated(string written, Func<char, string?> letter)
    {
        var latin = new StringBuilder(written.Length * 2);
        foreach (var character in written)
        {
            latin.Append(letter(character) ?? string.Empty);
        }

        return latin.ToString();
    }

    /// <summary>
    /// An Indian script's consonant by its place in the block. The aspirates are their plain letter,
    /// as a skeleton keeps no h; ca and ja are the sibilants Hebrew names are written with in them
    /// (जब्बूलून is Zebulun); a vowel, a vowel sign or a virama is nothing.
    /// </summary>
    private static string? IndicLetter(char character)
    {
        var block = IndicBlocks.FirstOrDefault(start => character >= start && character < start + 0x80, -1);
        if (block < 0)
        {
            return null;
        }

        return (character - block) switch
        {
            0x15 or 0x16 or 0x58 or 0x59 => "k",
            0x17 or 0x18 or 0x5A => "g",
            0x19 or 0x1E or 0x23 or 0x28 or 0x29 => "n",
            0x1A or 0x1B => "s",
            0x1C or 0x1D or 0x5B => "z",
            0x1F or 0x20 or 0x24 or 0x25 => "t",
            0x21 or 0x22 or 0x26 or 0x27 => "d",
            0x2A or 0x2B or 0x5E => "p",
            0x2C or 0x2D => "b",
            0x2E => "m",
            0x30 or 0x31 or 0x5C or 0x5D or 0x5F => "r",
            0x32 or 0x33 or 0x34 => "l",
            0x35 => "v",
            0x36 or 0x37 or 0x38 => "s",
            0xDC or 0xDD => "r",
            _ => null,
        };
    }

    private static bool IsArabic(char letter) => letter is >= '؀' and <= 'ۿ';

    /// <summary>
    /// An Arabic or Urdu consonant. ج is the Hebrew gimel (جاد is Gad), ع, ح, ه and the letters that
    /// stand for vowels are nothing, as their Hebrew counterparts are in a skeleton.
    /// </summary>
    private static string? ArabicLetter(char character) => character switch
    {
        'ب' => "b",
        'پ' => "p",
        'ت' or 'ث' or 'ط' or 'ٹ' or 'ة' => "t",
        'ج' or 'غ' or 'گ' => "g",
        'چ' => "s",
        'خ' or 'ق' or 'ك' or 'ک' => "k",
        'د' or 'ذ' or 'ض' or 'ڈ' => "d",
        'ر' or 'ڑ' => "r",
        'ز' or 'ظ' or 'ژ' => "z",
        'س' or 'ش' or 'ص' => "s",
        'ف' => "f",
        'ل' => "l",
        'م' => "m",
        'ن' or 'ں' => "n",
        _ => null,
    };

    /// <summary>The letters Church Slavonic and Hausa write that the skeleton has no reading for.</summary>
    private static string Replaced(string lower)
    {
        if (!lower.Any(letter => letter is 'ѳ' or 'ѯ' or 'ѱ' or 'ѕ' or 'ɓ' or 'ɗ' or 'ƙ'))
        {
            return lower;
        }

        return lower.Replace("ѳ", "ф").Replace("ѯ", "кс").Replace("ѱ", "пс").Replace('ѕ', 'з')
            .Replace('ɓ', 'b').Replace('ɗ', 'd').Replace('ƙ', 'k');
    }
}
