using System.Text;
using System.Text.RegularExpressions;
using Essenthos.Core.Utils;

namespace Essenthos.Core.XmlBible;

/// <param name="SuppliedSpan">
/// Which of the verse's bracketed spans this word stands in, counting from one, and null where the
/// edition prints it plainly. It is a span number rather than a flag because two brackets stand
/// side by side 79 times in the Synodal — "[для] [управления]" — and a flag would report one
/// editorial mark where the edition made two.
/// </param>
public readonly record struct VerseToken(string Word, string Trailer, int? SuppliedSpan);

/// <summary>
/// Splits a bible4u verse into the words a corpus is stored as. The source carries editorial
/// markup inside the verse text — the Synodal and Ukrainian files write the Hebrew verse number a
/// psalm is numbered differently under as "(22-1)", mark a superscription with "^^", and the
/// Synodal brackets the words it supplies — and tokenising that text as it stands put "(", "22",
/// "1" and ")" into the corpus as scripture words and a bare "[" as a word of its own. The markup
/// is removed here, once, so the loader and the repair that fixes already-loaded rows cannot
/// disagree about what a verse's words are.
/// </summary>
public static partial class VerseWords
{
    /// <summary>
    /// The marker bible4u wraps a psalm superscription in. The superscription itself is text of
    /// the psalm and stays; only the marker goes.
    /// </summary>
    private const string SuperscriptionMarker = "^^";

    private const char SuppliedOpen = '[';

    private const char SuppliedClose = ']';

    /// <summary>Where a bracketed span stands in the verse once the brackets are gone.</summary>
    private readonly record struct SuppliedRange(int Start, int End);

    /// <summary>
    /// A trailer is whatever the source wrote between one word and the next, so the separation is
    /// the source's and not this tokeniser's — nothing is added and nothing is dropped (/// is a defect of the Zefania parser, which is a different reader).
    /// </summary>
    public static List<VerseToken> Parse(string verseText)
    {
        var (text, supplied) = Separate(verseText);
        return Tokenize(text, supplied);
    }

    /// <summary>
    /// Removes the editorial markup, leaving the words of the verse and their punctuation. A
    /// parenthesised bare number is always a cross-reference to the other versification or a
    /// footnote marker — no verse of either translation reads a number in brackets as text.
    /// </summary>
    public static string StripMarkup(string verseText) => Separate(verseText).Text;

    /// <summary>
    /// The verse without its markup, and where the square brackets stood in what is left. The
    /// Synodal prints a word it supplies in brackets; that is a statement the edition makes about
    /// its own text and it belongs in structure, so the characters go and the spans are handed to
    /// the caller.
    ///
    /// Measured over the three files: all 4,247 of the Synodal's brackets are balanced and none
    /// nests, none stands inside a word, and removing them leaves no verse with doubled or
    /// stray whitespace. The King James and the Ukrainian carry none at all. A bracket left open
    /// at the end of a verse is closed there rather than dropped, so an edition less tidy than
    /// this one loses the extent of the mark and not the mark.
    /// </summary>
    private static (string Text, List<SuppliedRange> Supplied) Separate(string verseText)
    {
        var stripped = verseText.Replace(SuperscriptionMarker, string.Empty);
        stripped = CrossNumbering().Replace(stripped, string.Empty);
        stripped = WordSeparation.NormalizeWhitespace(stripped).Trim();

        if (!stripped.Contains(SuppliedOpen) && !stripped.Contains(SuppliedClose))
        {
            return (stripped, []);
        }

        var builder = new StringBuilder(stripped.Length);
        var supplied = new List<SuppliedRange>();
        var openedAt = -1;

        foreach (var character in stripped)
        {
            switch (character)
            {
                case SuppliedOpen:
                    openedAt = builder.Length;
                    continue;
                case SuppliedClose when openedAt >= 0:
                    supplied.Add(new SuppliedRange(openedAt, builder.Length));
                    openedAt = -1;
                    continue;
                case SuppliedClose:
                    continue;
                default:
                    builder.Append(character);
                    continue;
            }
        }

        if (openedAt >= 0)
        {
            supplied.Add(new SuppliedRange(openedAt, builder.Length));
        }

        return (builder.ToString(), supplied);
    }

    /// <summary>
    /// A word is a run of letters and digits, and an apostrophe or hyphen standing between two of
    /// them belongs to the word rather than separating two. A token with no word at all is folded
    /// into the word before it, because a verse cannot contain a word that is not there — the one
    /// exception is punctuation that opens the verse, which has no earlier word to belong to and is
    /// a real character of the text.
    /// </summary>
    private static List<VerseToken> Tokenize(string text, List<SuppliedRange> supplied)
    {
        var result = new List<VerseToken>(20);
        var length = text.Length;
        var span = 0;

        for (var i = 0; i < length; i++)
        {
            var start = i;
            for (; i < length; i++)
            {
                if (!IsWordCharacter(text, i))
                {
                    break;
                }
            }

            if (i >= length)
            {
                Append(result, text[start..], string.Empty, SpanAt(supplied, ref span, start, i));
                break;
            }

            var wordText = start == i ? string.Empty : text[start..i];
            var trailerStart = i++;
            for (; i < length; i++)
            {
                if (IsWordCharacter(text, i))
                {
                    break;
                }
            }

            Append(result, wordText, text[trailerStart..i], SpanAt(supplied, ref span, start, trailerStart));
            --i;
        }

        return result;
    }

    /// <summary>
    /// Which bracketed span the word at these offsets stands in. The spans are in text order and
    /// do not overlap, so the search walks forward with the tokeniser rather than starting again
    /// per word. A word with no letters is punctuation and belongs to no span, whatever it sits
    /// between.
    /// </summary>
    private static int? SpanAt(List<SuppliedRange> supplied, ref int from, int start, int end)
    {
        if (start == end)
        {
            return null;
        }

        while (from < supplied.Count && supplied[from].End <= start)
        {
            from++;
        }

        return from < supplied.Count && supplied[from].Start <= start ? from + 1 : null;
    }

    /// <summary>
    /// An apostrophe is a letter's business in some languages and punctuation in others, and the
    /// difference is where it stands. Ukrainian writes it inside a word — сім'я is one word, and
    /// splitting it gives сім and я, which mean other things — while Russian and English use the
    /// same character to open and close a quotation. A hyphen divides the same way: из-за is one
    /// word and a dash between two clauses is not. So the test is what surrounds it.
    /// </summary>
    private const char Apostrophe = '\u0027';
    private const char TypographicApostrophe = '\u2019';
    private const char Hyphen = '-';

    private static bool IsWordCharacter(string text, int index)
    {
        var c = text[index];
        if (char.IsLetter(c) || char.IsDigit(c))
        {
            return true;
        }

        if (c is not (Apostrophe or TypographicApostrophe or Hyphen))
        {
            return false;
        }

        return index > 0
               && index + 1 < text.Length
               && (char.IsLetter(text[index - 1]) || char.IsDigit(text[index - 1]))
               && (char.IsLetter(text[index + 1]) || char.IsDigit(text[index + 1]));
    }

    private static void Append(List<VerseToken> result, string word, string trailer, int? suppliedSpan)
    {
        if (word.Length == 0 && result.Count > 0)
        {
            var previous = result[^1];
            result[^1] = previous with { Trailer = previous.Trailer + trailer };
            return;
        }

        result.Add(new VerseToken(word, trailer, suppliedSpan));
    }

    [GeneratedRegex(@"\(\d+(?:[-:]\d+)?\)")]
    private static partial Regex CrossNumbering();
}
