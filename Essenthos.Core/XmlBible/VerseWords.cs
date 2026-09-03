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
    /// the source's and not this tokeniser's: nothing is added and nothing is dropped. Dropping the
    /// punctuation that follows a styled span is a defect of the Zefania parser, which is a
    /// different reader over a different file (PRB-0065), and does not happen here.
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
        stripped = CloseUpPunctuation(stripped);

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

    /// <summary>
    /// Closes up the space the source leaves between a word and the punctuation after it.
    ///
    /// The King James file writes <c>Thus saith the Lord , Behold</c> — 2,879 times, in 2,632
    /// verses — because whoever flattened the small-caps LORD markup left the space that had
    /// separated the styled name from what followed. It shows on every page where God speaks,
    /// which in the prophets is most of them, and the King James is the text every other text is
    /// compared against in the split view. PRB-0151.
    ///
    /// <para>
    /// **Only closing punctuation, and that is the whole of the care needed here.** Measured over
    /// the three files, every character that stands after a space is one of these:
    /// </para>
    ///
    /// <code>
    /// KJV    ,1446  .692  ;390  :292  (151  '108  ?47  !11  )1
    /// RUSV   [4089  -979  '875  (131  ^112  |1
    /// UKR    (75  „15  |1  .1
    /// </code>
    ///
    /// <para>
    /// A space before <c>(</c> or <c>„</c> is how a parenthesis and a quotation open, a space
    /// before <c>-</c> is a dash, and <c>'</c> is an apostrophe; all of those are the edition
    /// writing what it meant. So the rule is the closing marks only, and the corpus loses one
    /// stray Ukrainian full stop along with the King James's. The single <c>)</c> is the same
    /// fault as the commas — <em>(I am the Lord ) instead of</em> — and is included for that
    /// reason rather than by symmetry with <c>(</c>.
    /// </para>
    ///
    /// <para>
    /// This is a normalisation and not a repair of the source. <c>essenthos-api</c> owns
    /// <c>Resources/</c> and is frozen (NOT-0013), so editing the file there would be a change to
    /// a frozen repository that this project reads. Instead the correction is named, applied in
    /// <see cref="Separate"/> before anything measures offsets, and included in
    /// <see cref="StripMarkup"/> — which is what <c>EveryBible4uVerseRebuildsItsStrippedSource</c>
    /// compares the rebuilt verse against, so the round trip DOC-0007 asks for still holds
    /// exactly, against the normalised form this reader declares rather than against the bytes.
    /// </para>
    /// </summary>
    private static string CloseUpPunctuation(string text) =>
        SpaceBeforePunctuation().Replace(text, "$1");

    /// <summary>
    /// A run of spaces between a word character and a closing mark. Anchoring on the word
    /// character keeps it away from punctuation that legitimately follows other punctuation, and
    /// the marks are listed rather than taken from a Unicode category because the category holds
    /// the opening brackets and quotation marks too.
    /// </summary>
    [GeneratedRegex(@"(?<=[\p{L}\p{N}]) +([,.;:?!)])")]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"\(\d+(?:[-:]\d+)?\)")]
    private static partial Regex CrossNumbering();
}
