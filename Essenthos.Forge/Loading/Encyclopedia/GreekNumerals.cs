using System.Globalization;
using System.Text;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The numbers a Greek verse spells out in words.
///
/// The Septuagint writes every age in Genesis as words — <em>ἑπτὰ ἔτη καὶ ἑξήκοντα καὶ ἑκατόν</em>
/// for 167 — in any order, with <em>and</em> and <em>years</em> between the parts, sometimes run
/// together into one word (<em>τριακονταπέντε</em>), and in the one ordinal this reckoning needs
/// (<em>τεσσαρακοστῷ καὶ τετρακοσιοστῷ</em>, the four hundred and fortieth). A verse is read as a
/// list of runs, one per number it states, each the sum of its parts.
///
/// <para>
/// Swete's digitisation is optical, and in Genesis 11 it reads <em>τριόκοντα</em> for
/// <em>τριάκοντα</em> and <em>τριοκόσια</em> for <em>τριακόσια</em>. A word one letter away from
/// exactly one number word is read as that number, and nothing looser: the arithmetic that uses
/// these runs checks each patriarch's ages against his whole life, so a misreading shows up as a
/// sum that does not close rather than as a quietly wrong year.
/// </para>
/// </summary>
internal static class GreekNumerals
{
    private static readonly Dictionary<string, int> Words = Build();

    /// <summary>What may stand between the parts of one number without ending it.</summary>
    private static readonly HashSet<string> Joiners = new(StringComparer.Ordinal)
    {
        "και", "ετη", "ετων", "ετοσ", "ετει", "ετουσ",
    };

    /// <summary>Shorter than this, a word is too near too many others to be corrected.</summary>
    private const int ShortestCorrectable = 6;

    /// <summary>The numbers the verse states, in the order it states them.</summary>
    public static IReadOnlyList<int> In(string verse)
    {
        var runs = new List<int>();
        int? run = null;

        foreach (var token in Tokens(verse))
        {
            if (Value(token) is { } value)
            {
                run = (run ?? 0) + value;
                continue;
            }

            if (Joiners.Contains(token))
            {
                continue;
            }

            if (run is { } finished)
            {
                runs.Add(finished);
                run = null;
            }
        }

        if (run is { } last)
        {
            runs.Add(last);
        }

        return runs;
    }

    /// <summary>A word's value, where it is a number word, several run together, or one letter off one.</summary>
    internal static int? Value(string token)
    {
        if (Words.TryGetValue(token, out var value))
        {
            return value;
        }

        if (Compound(token, 0) is { } compound)
        {
            return compound;
        }

        if (token.Length < ShortestCorrectable)
        {
            return null;
        }

        var near = Words.Where(word => word.Key.Length >= ShortestCorrectable && OneEditApart(word.Key, token))
            .Select(word => word.Value)
            .Distinct()
            .ToList();

        return near.Count == 1 ? near[0] : null;
    }

    /// <summary>Number words written as one — <em>ἑβδομηκονταεννέα</em>, seventy-nine.</summary>
    private static int? Compound(string token, int from)
    {
        if (from == token.Length)
        {
            return 0;
        }

        foreach (var (word, value) in Words.OrderByDescending(word => word.Key.Length))
        {
            if (from + word.Length <= token.Length
                && string.CompareOrdinal(token, from, word, 0, word.Length) == 0
                && Compound(token, from + word.Length) is { } rest
                && (from > 0 || from + word.Length < token.Length))
            {
                return value + rest;
            }
        }

        return null;
    }

    /// <summary>Lower case, no accents or breathings, final sigma as sigma, letters only.</summary>
    internal static IEnumerable<string> Tokens(string verse)
    {
        var word = new StringBuilder();

        foreach (var character in verse)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetter(character))
            {
                var plain = Plain.TryGetValue(character, out var folded) ? folded : char.ToLowerInvariant(character);
                word.Append(plain == 'ς' ? 'σ' : plain);
                continue;
            }

            // An elision mark is part of the word it shortens; everything else ends one.
            if (character is '\'' or '’' or 'ʼ')
            {
                continue;
            }

            if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    /// <summary>
    /// Every accented, breathed or subscripted Greek letter, under the plain lower-case letter it is.
    ///
    /// Written out rather than decomposed, because this assembly is built with
    /// <c>InvariantGlobalization</c> and <c>String.Normalize</c> is a no-op under it: decomposing
    /// <em>ἑκατόν</em> leaves it as it was, and it then matches no number at all. The ranges are the
    /// Greek Extended block and the tonos letters of the basic one, in the block's own order.
    /// </summary>
    private static readonly Dictionary<char, char> Plain = Folded();

    private static Dictionary<char, char> Folded()
    {
        (int First, int Last, char Plain)[] ranges =
        [
            (0x1F00, 0x1F0F, 'α'), (0x1F10, 0x1F1D, 'ε'), (0x1F20, 0x1F2F, 'η'), (0x1F30, 0x1F3F, 'ι'),
            (0x1F40, 0x1F4D, 'ο'), (0x1F50, 0x1F5F, 'υ'), (0x1F60, 0x1F6F, 'ω'),
            (0x1F70, 0x1F71, 'α'), (0x1F72, 0x1F73, 'ε'), (0x1F74, 0x1F75, 'η'), (0x1F76, 0x1F77, 'ι'),
            (0x1F78, 0x1F79, 'ο'), (0x1F7A, 0x1F7B, 'υ'), (0x1F7C, 0x1F7D, 'ω'),
            (0x1F80, 0x1F8F, 'α'), (0x1F90, 0x1F9F, 'η'), (0x1FA0, 0x1FAF, 'ω'),
            (0x1FB0, 0x1FBC, 'α'), (0x1FC2, 0x1FC7, 'η'), (0x1FC8, 0x1FC9, 'ε'), (0x1FCA, 0x1FCC, 'η'),
            (0x1FD0, 0x1FDB, 'ι'), (0x1FE0, 0x1FE3, 'υ'), (0x1FE4, 0x1FE5, 'ρ'), (0x1FE6, 0x1FEB, 'υ'),
            (0x1FEC, 0x1FEC, 'ρ'), (0x1FF2, 0x1FF7, 'ω'), (0x1FF8, 0x1FF9, 'ο'), (0x1FFA, 0x1FFC, 'ω'),
            (0x0386, 0x0386, 'α'), (0x0388, 0x0388, 'ε'), (0x0389, 0x0389, 'η'), (0x038A, 0x038A, 'ι'),
            (0x038C, 0x038C, 'ο'), (0x038E, 0x038E, 'υ'), (0x038F, 0x038F, 'ω'), (0x0390, 0x0390, 'ι'),
            (0x03AA, 0x03AA, 'ι'), (0x03AB, 0x03AB, 'υ'), (0x03AC, 0x03AC, 'α'), (0x03AD, 0x03AD, 'ε'),
            (0x03AE, 0x03AE, 'η'), (0x03AF, 0x03AF, 'ι'), (0x03B0, 0x03B0, 'υ'), (0x03CA, 0x03CA, 'ι'),
            (0x03CB, 0x03CB, 'υ'), (0x03CC, 0x03CC, 'ο'), (0x03CD, 0x03CD, 'υ'), (0x03CE, 0x03CE, 'ω'),
        ];

        var plain = new Dictionary<char, char>();
        foreach (var (first, last, letter) in ranges)
        {
            for (var code = first; code <= last; code++)
            {
                plain[(char)code] = letter;
            }
        }

        return plain;
    }

    private static bool OneEditApart(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1 || a == b)
        {
            return false;
        }

        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
        {
            i++;
        }

        if (a.Length == b.Length)
        {
            return a.AsSpan(i + 1).SequenceEqual(b.AsSpan(i + 1));
        }

        return a.Length > b.Length
            ? a.AsSpan(i + 1).SequenceEqual(b.AsSpan(i))
            : a.AsSpan(i).SequenceEqual(b.AsSpan(i + 1));
    }

    private static Dictionary<string, int> Build()
    {
        var words = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ενι"] = 1,
            ["δυο"] = 2, ["δυσι"] = 2,
            ["τρεισ"] = 3, ["τρια"] = 3, ["τριων"] = 3,
            ["τεσσαρα"] = 4, ["τεσσερα"] = 4, ["τεσσαρεσ"] = 4, ["τεσσαρων"] = 4,
            ["πεντε"] = 5, ["εξ"] = 6, ["επτα"] = 7, ["οκτω"] = 8, ["εννεα"] = 9,
            ["δεκα"] = 10, ["ενδεκα"] = 11, ["δωδεκα"] = 12, ["πεντεκαιδεκα"] = 15,
            ["εικοσι"] = 20, ["τριακοντα"] = 30, ["τεσσαρακοντα"] = 40, ["τεσσερακοντα"] = 40,
            ["πεντηκοντα"] = 50, ["εξηκοντα"] = 60, ["εβδομηκοντα"] = 70, ["ογδοηκοντα"] = 80,
            ["ενενηκοντα"] = 90, ["εννενηκοντα"] = 90,
            ["εκατον"] = 100,
        };

        (string Stem, int Value)[] hundreds =
        [
            ("διακοσι", 200), ("τριακοσι", 300), ("τετρακοσι", 400), ("πεντακοσι", 500),
            ("εξακοσι", 600), ("επτακοσι", 700), ("οκτακοσι", 800), ("εννακοσι", 900), ("ενακοσι", 900),
        ];

        foreach (var (stem, value) in hundreds)
        {
            foreach (var ending in new[] { "α", "ων", "οι", "αι", "ουσ", "ασ", "οισ", "αισ" })
            {
                words[stem + ending] = value;
            }
        }

        // The ordinals the reckoning reads: the second year after the Flood, the six hundred and
        // first of Noah's life, and the four hundred and fortieth from the Exodus.
        (string Stem, int Value)[] ordinals =
        [
            ("δευτερ", 2), ("τεσσαρακοστ", 40), ("τετρακοσιοστ", 400), ("εξακοσιοστ", 600),
        ];

        foreach (var (stem, value) in ordinals)
        {
            foreach (var ending in new[] { "ω", "ου", "ον", "οσ", "η", "ησ", "α" })
            {
                words[stem + ending] = value;
            }
        }

        return words;
    }
}
