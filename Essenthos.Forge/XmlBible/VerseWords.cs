using System.Globalization;
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
/// An address the edition prints inside the verse, in the numbering the edition itself follows —
/// the "(118-1)" that stands at the head of the Synodal's Psalm 119:1, which is the Synodal saying
/// that in its own pages this verse is 118:1.
/// </summary>
public readonly record struct VerseAddress(int Chapter, int Number);

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

    /// <summary>
    /// What stands between the chapter and the verse of a printed address. The Synodal and the
    /// Ukrainian both write the hyphen and nothing else, and the colon is accepted because the
    /// pattern that finds these markers has always accepted it.
    /// </summary>
    private static readonly char[] AddressSeparators = ['-', ':'];

    /// <summary>Where a bracketed span stands in the verse once the brackets are gone.</summary>
    private readonly record struct SuppliedRange(int Start, int End);

    /// <summary>
    /// A trailer is whatever the source wrote between one word and the next, so the separation is
    /// the source's and not this tokeniser's: nothing is added and nothing is dropped. Dropping the
    /// punctuation that follows a styled span is a defect of the Zefania parser, which is a
    /// different reader over a different file, and does not happen here.
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
    /// The addresses the edition prints for this verse in its own numbering, in the order it prints
    /// them, and empty for a verse where it prints none.
    ///
    /// <para>
    /// bible4u renumbers both Slavic files to the numbering the King James follows, and then says
    /// so in the text: where its number and the edition's disagree it opens the verse with the
    /// edition's own — "(118-1)" at Psalm 119:1 of the Synodal, which prints that psalm as 118. So
    /// a marker is not decoration, it is the one place either file records the numbering its
    /// readers actually hold in their hands, and taking it out of the words without keeping it left
    /// the Synodal unable to say a chapter is numbered differently while Brenton, whose own rows
    /// carry his own numbers, could.
    /// </para>
    ///
    /// <para>
    /// **Only the two-part form is an address, and that distinction is measured rather than
    /// assumed.** Over the three files there are 2,678 marked verses in the Synodal and 1,928 in
    /// Ohienko's Ukrainian, none at all in the King James, and every marker writes a chapter and a
    /// verse — except two, Job 2:9 and Job 9:9 in the Synodal, which write a bare "(1)" standing at
    /// the end of a clause where the edition footnotes a variant reading. A bare number names no
    /// chapter and no verse and is not stored as one; it still leaves the words, because it is no
    /// more scripture than the address is. What is left is 2,676 addressed verses in the Synodal
    /// and 1,928 in the Ukrainian, 2,735 and 1,931 addresses between them.
    /// </para>
    ///
    /// <para>
    /// A verse may print more than one, and 58 of them do: bible4u merges what the edition divides,
    /// so Psalm 12:1 of the Synodal carries "(11-1)" over the superscription and "(11-2)" over the
    /// body, and Revelation 13:1 of the Ukrainian carries "(12-18)" and "(13-1)" across a chapter
    /// boundary. All of them are kept, in order, because one verse of ours genuinely is two of
    /// theirs and reporting the first alone would say the second is not there. Nor is the position
    /// in the verse a condition: 73 verses print their first address after a superscription or in
    /// the middle of the line, addressing the part that follows it, and requiring the head would
    /// drop exactly those.
    /// </para>
    /// </summary>
    public static IReadOnlyList<VerseAddress> StatedAddresses(string verseText)
    {
        var found = new List<VerseAddress>(1);

        foreach (var match in CrossNumbering().EnumerateMatches(verseText))
        {
            var marker = verseText.AsSpan(match.Index, match.Length);
            var separator = marker.IndexOfAny(AddressSeparators);
            if (separator < 0)
            {
                continue;
            }

            found.Add(new VerseAddress(
                int.Parse(marker[1..separator], CultureInfo.InvariantCulture),
                int.Parse(marker[(separator + 1)..^1], CultureInfo.InvariantCulture)));
        }

        return found;
    }

    /// <summary>
    /// Whether the edition wraps a superscription in this verse.
    ///
    /// <para>
    /// The Synodal file marks one with <c>^^</c>, and it does so in exactly 120 places, every one of
    /// them the first verse of a psalm and every one of them a balanced pair. Sixty-three of those
    /// psalms are ones the Hebrew numbers the title as a verse of its own; the other fifty-seven
    /// keep it inside verse one, as the Hebrew does, so there is nothing for them to be placed
    /// against. Ohienko's Ukrainian carries the marker nowhere and is silent here.
    /// </para>
    ///
    /// <para>
    /// This says the verse holds a superscription, not where it begins and ends. The marker is not
    /// reliable as a span — the Synodal's Psalm 51:1 wraps the whole verse, body and all — and a
    /// division the edition did not print is not one to record.
    /// </para>
    /// </summary>
    public static bool MarksASuperscription(string verseText) => verseText.Contains(SuperscriptionMarker);

    /// <summary>
    /// Whether the verse holds words standing before the address it states for what follows them.
    ///
    /// <para>
    /// A marker inside a verse is the edition saying that everything after it is its own verse
    /// <c>k</c>, so text standing before that marker is its verse <c>k - 1</c> or earlier, printed
    /// here because the publisher merged what the edition divides. The Synodal's Psalm 3:1 opens
    /// with the superscription and only then writes "(3-2)": by its own numbering those first words
    /// are its verse 3:1, and it is the Hebrew's title verse that stands there.
    /// </para>
    ///
    /// <para>
    /// The number has to be greater than one for the same reason. A verse whose only marker names
    /// verse one states that it begins where the edition's verse begins, and a verse opening
    /// "(113-9)" — the Synodal's Psalm 115:1, which continues the psalm it numbers 113 — carries
    /// nothing before its marker at all.
    /// </para>
    /// </summary>
    public static bool OpensBeforeItsStatedAddress(string verseText)
    {
        var last = -1;
        var number = 0;

        foreach (var match in CrossNumbering().EnumerateMatches(verseText))
        {
            var marker = verseText.AsSpan(match.Index, match.Length);
            var separator = marker.IndexOfAny(AddressSeparators);
            if (separator < 0)
            {
                continue;
            }

            last = match.Index;
            number = int.Parse(marker[(separator + 1)..^1], CultureInfo.InvariantCulture);
        }

        return last > 0 && number > 1 && StripMarkup(verseText[..last]).Length > 0;
    }

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
    /// compared against in the split view.
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
    /// This is a normalisation and not a repair of the source. The file under <c>Resources/</c> is
    /// the publisher's bytes, fetched and not committed, so a correction written into it would be
    /// indistinguishable from the edition's own text and would vanish on the next fetch. Instead
    /// the correction is named, applied in
    /// <see cref="Separate"/> before anything measures offsets, and included in
    /// <see cref="StripMarkup"/> — which is what <c>EveryBible4uVerseRebuildsItsStrippedSource</c>
    /// compares the rebuilt verse against, so the round trip the corpus requires of every reader
    /// still holds exactly — against the normalised form this one declares, rather than the bytes.
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
