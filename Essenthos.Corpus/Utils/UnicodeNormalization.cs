using System.Globalization;
using System.Text;

namespace Essenthos.Core.Utils;

/// <summary>
/// Unicode's canonical forms, NFC and NFD, computed here rather than by <see cref="string.Normalize()"/>.
///
/// <para>
/// Every project of this solution, the tests included, is built with <c>InvariantGlobalization</c>,
/// and under it <see cref="string.Normalize()"/> returns its string unchanged and
/// <see cref="string.IsNormalized()"/> says it is normalised: <c>α</c> with a combining smooth
/// breathing and acute stays three characters, where NFC makes it the one <c>ἄ</c>. Nothing throws,
/// so a comparison that relies on it simply finds less. The tables in
/// <c>UnicodeNormalization.Data.cs</c> are written by <c>scripts/unicode-normalization.py</c> from the
/// Unicode version Postgres uses, so a string composed here and one composed by
/// <c>normalize(..., NFC)</c> in SQL are the same string.
/// </para>
/// </summary>
internal static partial class UnicodeNormalization
{
    private const int HangulFirst = 0xAC00;
    private const int LeadFirst = 0x1100;
    private const int VowelFirst = 0x1161;
    private const int TrailFirst = 0x11A7;
    private const int Leads = 19;
    private const int Vowels = 21;
    private const int Trails = 28;
    private const int PerLead = Vowels * Trails;
    private const int Syllables = Leads * PerLead;

    /// <summary>Below this every character is already in both forms (NFD and NFC alike begin to differ at À).</summary>
    private const char FirstDecomposable = 'À';

    /// <summary>Below this every character is already composed: the first combining mark is U+0300.</summary>
    private const char FirstCombining = '̀';

    private static readonly Dictionary<int, int[]> Decompositions = ReadDecompositions();
    private static readonly Dictionary<int, byte> CombiningClasses = ReadCombiningClasses();
    private static readonly Dictionary<long, int> Compositions = ReadCompositions();

    /// <summary>The text in Normalization Form C: canonically decomposed, then composed again.</summary>
    public static string Composed(string text)
    {
        if (Below(text, FirstCombining))
        {
            return text;
        }

        var points = Decompose(text);
        Compose(points);
        return Written(points, text);
    }

    /// <summary>The text in Normalization Form D: every character canonically decomposed, marks in canonical order.</summary>
    public static string Decomposed(string text) =>
        Below(text, FirstDecomposable) ? text : Written(Decompose(text), text);

    /// <summary>Whether the text is already in Normalization Form C.</summary>
    public static bool IsComposed(string text) => ReferenceEquals(Composed(text), text);

    /// <summary>The canonical combining class of a character, 0 for a starter.</summary>
    public static int CombiningClass(int character) => CombiningClasses.GetValueOrDefault(character);

    private static bool Below(string text, char limit)
    {
        foreach (var c in text)
        {
            if (c >= limit)
            {
                return false;
            }
        }

        return true;
    }

    private static List<int> Decompose(string text)
    {
        var points = new List<int>(text.Length + 4);
        foreach (var rune in text.EnumerateRunes())
        {
            var code = rune.Value;
            var syllable = code - HangulFirst;
            if (syllable is >= 0 and < Syllables)
            {
                points.Add(LeadFirst + syllable / PerLead);
                points.Add(VowelFirst + syllable % PerLead / Trails);
                if (syllable % Trails != 0)
                {
                    points.Add(TrailFirst + syllable % Trails);
                }
            }
            else if (Decompositions.TryGetValue(code, out var parts))
            {
                points.AddRange(parts);
            }
            else
            {
                points.Add(code);
            }
        }

        Order(points);
        return points;
    }

    /// <summary>Canonical ordering: within each run of marks, a stable sort by combining class.</summary>
    private static void Order(List<int> points)
    {
        for (var at = 1; at < points.Count; at++)
        {
            var mark = CombiningClass(points[at]);
            if (mark == 0)
            {
                continue;
            }

            var back = at;
            while (back > 0 && CombiningClass(points[back - 1]) > mark)
            {
                (points[back - 1], points[back]) = (points[back], points[back - 1]);
                back--;
            }
        }
    }

    /// <summary>
    /// Canonical composition: each character joins the last starter before it unless a character
    /// between them blocks it, which one of class 0 or of a class at least its own does.
    /// </summary>
    private static void Compose(List<int> points)
    {
        var starter = -1;
        var lastClass = 0;
        var written = 0;
        foreach (var point in points.ToArray())
        {
            var mark = CombiningClass(point);
            var adjacent = starter >= 0 && written - 1 == starter;
            var blocked = !adjacent && (lastClass == 0 || lastClass >= mark);
            if (starter >= 0 && !blocked && Composite(points[starter], point) is { } composite)
            {
                points[starter] = composite;
                continue;
            }

            if (mark == 0)
            {
                starter = written;
            }

            lastClass = mark;
            points[written++] = point;
        }

        points.RemoveRange(written, points.Count - written);
    }

    private static int? Composite(int first, int second)
    {
        var lead = first - LeadFirst;
        if (lead is >= 0 and < Leads)
        {
            var vowel = second - VowelFirst;
            return vowel is >= 0 and < Vowels ? HangulFirst + (lead * Vowels + vowel) * Trails : null;
        }

        var syllable = first - HangulFirst;
        if (syllable is >= 0 and < Syllables && syllable % Trails == 0)
        {
            var trail = second - TrailFirst;
            return trail is > 0 and < Trails ? first + trail : null;
        }

        return Compositions.TryGetValue(Key(first, second), out var composite) ? composite : null;
    }

    private static long Key(int first, int second) => ((long)first << 21) | (uint)second;

    /// <summary>The code points as a string, or the original string itself when they spell it unchanged.</summary>
    private static string Written(List<int> points, string original)
    {
        var builder = new StringBuilder(points.Count + 4);
        foreach (var point in points)
        {
            builder.Append(new Rune(point).ToString());
        }

        var result = builder.ToString();
        return string.Equals(result, original, StringComparison.Ordinal) ? original : result;
    }

    private static IEnumerable<(string Key, string Value)> Entries(string data)
    {
        foreach (var entry in data.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var at = entry.IndexOfAny(['=', ':']);
            yield return (entry[..at], entry[(at + 1)..]);
        }
    }

    private static int Hex(string value) => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static Dictionary<int, int[]> ReadDecompositions() =>
        Entries(DecompositionData).ToDictionary(entry => Hex(entry.Key), entry => entry.Value.Split('.').Select(Hex).ToArray());

    private static Dictionary<int, byte> ReadCombiningClasses()
    {
        var classes = new Dictionary<int, byte>();
        foreach (var (range, value) in Entries(CombiningClassData))
        {
            var dash = range.IndexOf('-', StringComparison.Ordinal);
            var first = Hex(dash < 0 ? range : range[..dash]);
            var last = dash < 0 ? first : Hex(range[(dash + 1)..]);
            for (var code = first; code <= last; code++)
            {
                classes[code] = byte.Parse(value, CultureInfo.InvariantCulture);
            }
        }

        return classes;
    }

    private static Dictionary<long, int> ReadCompositions() =>
        Entries(CompositionData).ToDictionary(
            entry =>
            {
                var dot = entry.Key.IndexOf('.', StringComparison.Ordinal);
                return Key(Hex(entry.Key[..dot]), Hex(entry.Key[(dot + 1)..]));
            },
            entry => Hex(entry.Value));
}
