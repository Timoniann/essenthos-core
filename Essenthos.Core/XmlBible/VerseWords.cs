using System.Text.RegularExpressions;
using Essenthos.Core.Utils;

namespace Essenthos.Core.XmlBible;

/// <summary>
/// Splits a bible4u verse into the words a corpus is stored as. The source carries editorial
/// markup inside the verse text — the Synodal and Ukrainian files write the Hebrew verse number a
/// psalm is numbered differently under as "(22-1)", and mark a superscription with "^^" — and
/// tokenising that text as it stands put "(", "22", "1" and ")" into the corpus as scripture
/// words. The markup is removed here, once, so the loader and the repair that fixes
/// already-loaded rows cannot disagree about what a verse's words are.
/// </summary>
public static partial class VerseWords
{
    /// <summary>
    /// The marker bible4u wraps a psalm superscription in. The superscription itself is text of
    /// the psalm and stays; only the marker goes.
    /// </summary>
    private const string SuperscriptionMarker = "^^";

    /// <summary>
    /// A trailer is whatever the source wrote between one word and the next, so the separation is
    /// the source's and not this tokeniser's — nothing is added and nothing is dropped (/// is a defect of the Zefania parser, which is a different reader).
    /// </summary>
    public static List<(string Word, string Trailer)> Parse(string verseText)
    {
        return Tokenize(StripMarkup(verseText));
    }

    /// <summary>
    /// Removes the editorial markup, leaving the words of the verse and their punctuation. A
    /// parenthesised bare number is always a cross-reference to the other versification or a
    /// footnote marker — no verse of either translation reads a number in brackets as text.
    /// </summary>
    public static string StripMarkup(string verseText)
    {
        var stripped = verseText.Replace(SuperscriptionMarker, string.Empty);
        stripped = CrossNumbering().Replace(stripped, string.Empty);
        return WordSeparation.NormalizeWhitespace(stripped).Trim();
    }

    /// <summary>
    /// A word is a run of letters and digits, and an apostrophe or hyphen standing between two of
    /// them belongs to the word rather than separating two. A token with no word at all is folded
    /// into the word before it, because a verse cannot contain a word that is not there — the one
    /// exception is punctuation that opens the verse, which has no earlier word to belong to and is
    /// a real character of the text.
    /// </summary>
    private static List<(string Word, string Trailer)> Tokenize(string text)
    {
        var result = new List<(string Word, string Trailer)>(20);
        var length = text.Length;
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
                Append(result, text[start..], string.Empty);
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

            Append(result, wordText, text[trailerStart..i]);
            --i;
        }

        return result;
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

    private static void Append(List<(string Word, string Trailer)> result, string word, string trailer)
    {
        if (word.Length == 0 && result.Count > 0)
        {
            var previous = result[^1];
            result[^1] = (previous.Word, previous.Trailer + trailer);
            return;
        }

        result.Add((word, trailer));
    }

    [GeneratedRegex(@"\(\d+(?:[-:]\d+)?\)")]
    private static partial Regex CrossNumbering();
}
