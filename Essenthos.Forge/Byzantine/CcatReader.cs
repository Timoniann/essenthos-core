using System.Text;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Byzantine;

/// <param name="Kind">
/// The letter Robinson opens the note with: <c>B</c> where the Byzantine manuscripts divide between
/// two readings, <c>N</c> where Nestle-Aland 28 reads otherwise, <c>E</c> where the Editio Critica
/// Maior does; <c>C</c>, <c>M</c> and <c>S</c> mark further editions in Acts and the Catholic
/// Letters that the repository does not name.
/// </param>
/// <param name="From">The first of the verse's words the note is about, counted from zero.</param>
/// <param name="Lemma">The words of Robinson's text the note is about, bare.</param>
/// <param name="Reading">The other reading, bare; empty where it has no words there.</param>
/// <param name="Undoubted">
/// The other reading without the words its edition prints in square brackets, as doubtful; the same
/// as <paramref name="Reading"/> where it brackets none.
/// </param>
/// <param name="Conjecture">The other reading is an editor's conjecture rather than a manuscript's.</param>
internal sealed record ApparatusNote(
    char Kind,
    int From,
    IReadOnlyList<string> Lemma,
    IReadOnlyList<string> Reading,
    IReadOnlyList<string> Undoubted,
    bool Conjecture);

/// <param name="Words">Robinson's text of the verse, every word bare: no accents, breathings or case.</param>
/// <param name="Notes">The notes that could be placed on the words they are about.</param>
/// <param name="Unplaced">Notes whose words could not be found in the verse, or that name no words.</param>
internal sealed record CcatVerse(
    int Chapter,
    int Number,
    IReadOnlyList<string> Words,
    IReadOnlyList<ApparatusNote> Notes,
    int Unplaced);

/// <summary>
/// Robinson's accented Byzantine Textform with its apparatus inline, as the repository's
/// <c>source/CCAT</c> files hold it: one verse to a line, TLG beta code with accents and breathings,
/// and every note in braces —
///
/// <code>01:18 ... O( {N O( MONOGENH\S UI(O/S > MONOGENH\S QEO/S } MONOGENH\S UI(O/S, O( W)\N ...</code>
///
/// which says that where Robinson reads ὁ μονογενὴς υἱός, Nestle-Aland reads μονογενὴς θεός.
///
/// <para>
/// **The note stands beside its words, not instead of them.** The verse reads the same with every
/// note taken out, and the note repeats the words it is about. Robinson sets it after the first of
/// them nearly always (6,559 of 6,746 Nestle-Aland notes), but not always: the B notes of Matthew
/// 5:39 stand before their word, and a few stand words away. So a note is placed on the occurrence
/// of its words nearest to where it stands, and a note whose words the verse does not hold is left
/// unplaced and counted.
/// </para>
///
/// <para>
/// **The words are read bare.** The text the corpus loads is Robinson's own unaccented parse
/// (<see cref="Bp5Reader"/>), so the accents are set aside here and a word is compared by its letters.
/// The beta code is the same TLG alphabet the parse is written in, upper case.
/// </para>
/// </summary>
internal static partial class CcatReader
{
    /// <summary>How far from where a note stands its words are looked for.</summary>
    private const int Reach = 12;

    private const string Omitted = "-";
    private const string ConjectureMark = "(cj)";

    public static IReadOnlyList<CcatVerse> Read(string content)
    {
        var verses = new List<CcatVerse>(1_100);
        foreach (var line in content.Split('\n'))
        {
            var verse = VerseLine().Match(line.TrimEnd('\r'));
            if (verse.Success)
            {
                verses.Add(Verse(
                    int.Parse(verse.Groups[1].Value), int.Parse(verse.Groups[2].Value), verse.Groups[3].Value));
            }
        }

        return verses;
    }

    private static CcatVerse Verse(int chapter, int number, string body)
    {
        var words = new List<string>();
        var pending = new List<(int At, string Note)>();
        foreach (var part in Pieces().Split(body))
        {
            if (part.StartsWith('{'))
            {
                pending.Add((words.Count, part));
            }
            else
            {
                words.AddRange(Words(part));
            }
        }

        var notes = new List<ApparatusNote>(pending.Count);
        var unplaced = 0;
        foreach (var (at, text) in pending)
        {
            if (Note(text, at, words) is { } note)
            {
                notes.Add(note);
            }
            else
            {
                unplaced++;
            }
        }

        return new CcatVerse(chapter, number, words, notes, unplaced);
    }

    private static ApparatusNote? Note(string text, int at, List<string> words)
    {
        var note = NoteText().Match(text);
        if (!note.Success)
        {
            return null;
        }

        var lemma = Words(note.Groups[2].Value).ToList();
        var other = note.Groups[3].Value.Trim();
        if (lemma.Count == 0)
        {
            return null;
        }

        var from = Place(words, lemma, at);
        if (from < 0)
        {
            return null;
        }

        var conjecture = other.Contains(ConjectureMark, StringComparison.Ordinal);
        var tokens = other == Omitted ? [] : Tokens(other.Replace(ConjectureMark, string.Empty));
        return new ApparatusNote(
            note.Groups[1].Value[0],
            from,
            lemma,
            [.. tokens.Select(token => token.Word)],
            [.. tokens.Where(token => !token.Bracketed).Select(token => token.Word)],
            conjecture);
    }

    /// <summary>
    /// Where the note's words begin in the verse: the occurrence nearest to the note's own place,
    /// counting the one that ends with the word before the note as nearest of all; -1 where the
    /// verse does not hold them within reach.
    /// </summary>
    private static int Place(List<string> words, List<string> lemma, int at)
    {
        var best = -1;
        var distance = int.MaxValue;
        for (var start = Math.Max(0, at - lemma.Count - Reach); start <= Math.Min(words.Count - lemma.Count, at + Reach); start++)
        {
            if (!words.Skip(start).Take(lemma.Count).SequenceEqual(lemma))
            {
                continue;
            }

            var away = Math.Abs(start + 1 - at);
            if (away < distance)
            {
                (best, distance) = (start, away);
            }
        }

        return best;
    }

    /// <summary>A reading's words, each saying whether it stands inside square brackets.</summary>
    private static List<(string Word, bool Bracketed)> Tokens(string text)
    {
        var tokens = new List<(string, bool)>();
        var depth = 0;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var bracketed = depth > 0 || token.Contains('[');
            depth += token.Count(c => c == '[') - token.Count(c => c == ']');
            if (Bare(token) is { Length: > 0 } word)
            {
                tokens.Add((word, bracketed));
            }
        }

        return tokens;
    }

    private static IEnumerable<string> Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Bare).Where(word => word.Length > 0);

    /// <summary>
    /// One beta-code word, bare: its letters in lower-case Greek, a sigma that ends it final, and
    /// nothing else — no accent, breathing, iota subscript, diaeresis, capital mark, bracket,
    /// elision mark or punctuation.
    /// </summary>
    public static string Bare(string token)
    {
        var letters = new StringBuilder(token.Length);
        foreach (var character in token)
        {
            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                letters.Append(char.ToLowerInvariant(character));
            }
        }

        return letters.Length == 0 ? string.Empty : Bp5BetaCode.ToGreek(letters.ToString());
    }

    [GeneratedRegex(@"^(\d+):(\d+)\s(.*)$")]
    private static partial Regex VerseLine();

    [GeneratedRegex(@"(\{[^}]*\})")]
    private static partial Regex Pieces();

    [GeneratedRegex(@"^\{([A-Z])\s+(.*?)\s*>\s*(.*?)\s*\}$", RegexOptions.Singleline)]
    private static partial Regex NoteText();
}
