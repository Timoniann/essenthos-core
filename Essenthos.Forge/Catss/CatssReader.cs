using System.Text;

namespace Essenthos.Core.Catss;

/// <summary>What the Hebrew column of a CATSS line holds.</summary>
internal enum CatssHebrew
{
    /// <summary>Words of the Masoretic text.</summary>
    Words,

    /// <summary><c>--+</c>: the Greek of the line has no counterpart in the Masoretic text (a plus of the Greek).</summary>
    Plus,

    /// <summary><c>{...}</c>: the Hebrew the Greek of the line renders stands on another line.</summary>
    Elsewhere,

    /// <summary><c>^^^</c>: the Hebrew stands earlier or later in the verse, named in column b.</summary>
    Transposed,

    /// <summary>Nothing the reader takes for any of the above.</summary>
    None,
}

/// <summary>
/// A Greek word the line names in braces as a counterpart that stands elsewhere: <c>{..^X}</c> a transposition,
/// <c>{..dX}</c> one Greek word rendering several Hebrew words, <c>{...X}</c> a word shared with another line,
/// <c>{..pX}</c> a preposition the Greek adds, matched by its own line whose Hebrew is <c>{...}</c>.
/// </summary>
internal sealed record CatssReference(char Kind, IReadOnlyList<string> Words);

/// <summary>
/// One line of the CATSS parallel text as the Oxford Text Archive's copy (item 1111) prints it, joined with the lines
/// that continue it (<c>#</c>). Hebrew is Michigan-Claremont consonantal betacode with prefixes and suffixes cut off by
/// <c>/</c>; Greek is the TLG's betacode of Rahlfs.
/// </summary>
/// <param name="Line">The file's line number of its first line, from 1.</param>
/// <param name="Hebrew">The Masoretic words of column a in order, as written (ketiv where it differs from the qere).</param>
/// <param name="Retroversion">Column b: the Hebrew the translator presumably read, or a note on translation technique.</param>
/// <param name="Greek">The Greek words of the running text in order; words in braces are not among them.</param>
/// <param name="Minus">The Greek column says <c>---</c>: the Hebrew has no counterpart in the Greek.</param>
/// <param name="Marks">Every other token of either column (<c>{d}</c>, <c>{p}</c>, <c>{t}</c>, <c>^</c>, <c>?</c>, …), the Hebrew side's prefixed by <c>h:</c>.</param>
/// <param name="Verses">The Greek's own verse numbers where they differ from the Masoretic (<c>[6]</c>, <c>[38.16]</c>, <c>[[29.20]]</c>), as printed.</param>
internal sealed record CatssLine(
    int Line,
    CatssHebrew Kind,
    IReadOnlyList<string> Hebrew,
    string? Retroversion,
    IReadOnlyList<string> Greek,
    bool Minus,
    IReadOnlyList<CatssReference> References,
    IReadOnlyList<string> Marks,
    IReadOnlyList<string> Verses)
{
    public bool Marked(string mark) => Marks.Contains(mark);
}

/// <summary>A verse at the Masoretic address CATSS numbers it by (BHS's numbering), with its lines.</summary>
internal sealed record CatssVerse(string Book, int Chapter, int Verse, IReadOnlyList<CatssLine> Lines);

/// <summary>
/// Reads the Oxford Text Archive's copy of the CATSS parallel text (item 1111): fixed columns, Hebrew in the first 30,
/// Greek from the 31st; citations in TLG's betacode (<c>~c"Gen"x0</c> a book, <c>~x</c> the next chapter, <c>~y</c> the
/// next verse, <c>~y12</c> verse 12).
/// </summary>
internal static class CatssReader
{
    public const int GreekColumn = 30;

    /// <summary>The verses of every file in order, each book's lines as the files hold them.</summary>
    public static List<CatssVerse> ReadFiles(IEnumerable<string> paths) => [.. paths.SelectMany(path => Read(File.ReadLines(path, Encoding.Latin1)))];

    public static List<CatssVerse> Read(IEnumerable<string> lines)
    {
        var verses = new List<CatssVerse>();
        string? book = null;
        int chapter = 0, verse = 0, number = 0;
        var current = new List<CatssLine>();
        Raw? open = null;

        void Close()
        {
            if (open is not null)
            {
                current.Add(open.Line());
                open = null;
            }

            if (book is not null && current.Count > 0)
            {
                verses.Add(new CatssVerse(book, chapter, verse, current));
            }

            current = [];
        }

        foreach (var text in lines)
        {
            number++;
            if (text.StartsWith('~'))
            {
                Close();
                Cite(text, ref book, ref chapter, ref verse);
                continue;
            }

            if (book is null || text.Trim().Length == 0)
            {
                continue;
            }

            var hebrew = Tokens(text.Length > GreekColumn ? text[..GreekColumn] : text);
            var greek = Tokens(text.Length > GreekColumn ? text[GreekColumn..] : string.Empty);
            var continues = open is not null && (open.Hanging || hebrew.FirstOrDefault() == "#" || greek.FirstOrDefault() == "#");
            if (!continues && open is not null)
            {
                current.Add(open.Line());
                open = null;
            }

            open ??= new Raw(number);
            open.Add(hebrew, greek);
        }

        Close();
        return verses;
    }

    /// <summary>Applies a citation line: a book (<c>c"Gen"</c>), a chapter (<c>x</c>, <c>x12</c>) or a verse (<c>y</c>, <c>y12</c>).</summary>
    internal static void Cite(string text, ref string? book, ref int chapter, ref int verse)
    {
        var at = 1;
        while (at < text.Length)
        {
            var level = text[at++];
            string? value = null;
            if (at < text.Length && text[at] == '"')
            {
                var end = text.IndexOf('"', at + 1);
                value = text[(at + 1)..end];
                at = end + 1;
            }
            else
            {
                var start = at;
                while (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    at++;
                }

                value = at > start ? text[start..at] : null;
            }

            switch (level)
            {
                case 'c':
                    book = value;
                    chapter = 1;
                    verse = 0;
                    break;
                case 'x':
                    chapter = value is null ? chapter + 1 : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    verse = 1;
                    break;
                case 'y':
                    verse = value is null ? verse + 1 : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
        }
    }

    /// <summary>A column's tokens: whitespace apart, a brace or bracket with whatever it holds one token.</summary>
    internal static List<string> Tokens(string column)
    {
        var tokens = new List<string>();
        var at = 0;
        while (at < column.Length)
        {
            if (char.IsWhiteSpace(column[at]))
            {
                at++;
                continue;
            }

            var start = at;
            if (column[at] is '{' or '[')
            {
                var close = column[at] == '{' ? '}' : ']';
                var depth = 0;
                while (at < column.Length)
                {
                    if (column[at] == column[start]) depth++;
                    else if (column[at] == close && --depth == 0)
                    {
                        at++;
                        break;
                    }

                    at++;
                }

                // A closing brace can carry a letter after it, as {!}nd does.
                while (at < column.Length && !char.IsWhiteSpace(column[at]))
                {
                    at++;
                }
            }
            else
            {
                while (at < column.Length && !char.IsWhiteSpace(column[at]))
                {
                    at++;
                }
            }

            tokens.Add(column[start..at]);
        }

        return tokens;
    }

    /// <summary>Whether a token is a Hebrew word of column a: consonants, the morpheme cut, a ketiv's star.</summary>
    internal static bool IsHebrew(string token) =>
        token.Any(char.IsAsciiLetterUpper) && token.All(c => HebrewBetacode.IsLetter(c) || c is '/' or '*');

    /// <summary>Whether a token is a Greek word: TLG betacode letters, breathings, accents, iota subscript, diaeresis, elision.</summary>
    internal static bool IsGreek(string token) =>
        token.Any(char.IsAsciiLetterUpper) && token.All(c => char.IsAsciiLetterUpper(c) || c is ')' or '(' or '/' or '\\' or '=' or '|' or '+' or '*' or '\'');

    private sealed class Raw(int line)
    {
        private readonly List<string> _hebrew = [];
        private readonly List<string> _greek = [];
        private readonly List<string> _marks = [];
        private readonly List<string> _verses = [];
        private readonly List<CatssReference> _references = [];
        private StringBuilder? _retroversion;
        private bool _plus, _elsewhere, _transposed, _minus;

        /// <summary>The last column read ended in <c>#</c>: the next line carries it on.</summary>
        public bool Hanging { get; private set; }

        public void Add(List<string> hebrew, List<string> greek)
        {
            // A column of "#" alone marks the line as the continuation, not as running over again.
            Hanging = (hebrew.Count > 1 && hebrew[^1] == "#") || (greek.Count > 1 && greek[^1] == "#");
            var inB = false;
            foreach (var token in hebrew.Where(t => t != "#"))
            {
                if (!inB && (token[0] is '=' or ';' || token.StartsWith("[from", StringComparison.Ordinal)))
                {
                    inB = true;
                }

                if (inB)
                {
                    (_retroversion ??= new StringBuilder()).Append(_retroversion.Length > 0 ? " " : string.Empty).Append(token);
                }
                else if (token == "--+") _plus = true;
                else if (token == "{...}") _elsewhere = true;
                else if (token == "^^^") _transposed = true;
                else if (token.StartsWith("**", StringComparison.Ordinal)) _marks.Add("h:" + token);
                else if (IsHebrew(token)) _hebrew.Add(token.Replace("*", string.Empty, StringComparison.Ordinal));
                else _marks.Add("h:" + token);
            }

            foreach (var token in greek.Where(t => t != "#"))
            {
                if (token == "---") _minus = true;
                else if (token.StartsWith("{..", StringComparison.Ordinal) && Reference(token) is { } reference) _references.Add(reference);
                else if (token.StartsWith('[')) _verses.Add(token);
                else if (IsGreek(token)) _greek.Add(token);
                else _marks.Add(token);
            }
        }

        /// <summary><c>{..^X Y}</c>, <c>{..dX}</c>, <c>{...X}</c>, <c>{..pX}</c>, any with a <c>?</c> of doubt; null for <c>{...}</c> alone.</summary>
        private static CatssReference? Reference(string token)
        {
            var inner = token.TrimEnd('}')[3..];
            var kind = inner.Length > 0 && inner[0] is '.' or '^' or 'd' or 'p' or 'r' ? inner[0] : '?';
            var words = Tokens(inner.TrimStart('.', '^', 'd', 'p', 'r', '?')).Where(IsGreek).ToList();
            return words.Count == 0 ? null : new CatssReference(kind, words);
        }

        public CatssLine Line()
        {
            var kind = _hebrew.Count > 0 ? CatssHebrew.Words
                : _plus ? CatssHebrew.Plus
                : _transposed ? CatssHebrew.Transposed
                : _elsewhere ? CatssHebrew.Elsewhere
                : CatssHebrew.None;
            return new CatssLine(line, kind, _hebrew, _retroversion?.ToString(), _greek, _minus && _greek.Count == 0, _references, _marks, _verses);
        }
    }
}

/// <summary>The Michigan-Claremont transcription of the Hebrew consonants CATSS writes.</summary>
internal static class HebrewBetacode
{
    private const string Latin = ")BGDHWZX+YKLMNS(PCQR&$T";
    private const string Hebrew = "אבגדהוזחטיכלמנסעפצקרששת";

    public static bool IsLetter(char c) => Latin.Contains(c);

    /// <summary>The consonants of a word, final forms written as the others (as <c>MaculaHebrewJoin.Letters</c> folds BHSA's).</summary>
    public static string Letters(string word)
    {
        var letters = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            var at = Latin.IndexOf(c);
            if (at >= 0)
            {
                letters.Append(Hebrew[at]);
            }
        }

        return letters.ToString();
    }
}

/// <summary>The TLG's betacode of Greek, read to bare letters as <see cref="Utils.GreekLetters.Bare"/> writes them.</summary>
internal static class GreekBetacode
{
    private const string Latin = "ABGDEZHQIKLMNCOPRSJTUFXYWV";
    private const string Greek = "αβγδεζηθικλμνξοπρσστυφχψωϝ";

    public static string Bare(string word)
    {
        var letters = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            var at = Latin.IndexOf(c);
            if (at >= 0)
            {
                letters.Append(Greek[at]);
            }
        }

        return letters.ToString();
    }
}
