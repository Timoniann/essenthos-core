namespace Essenthos.Core.Swete;

internal sealed record SweteWord(string Surface, string Trailer);

/// <param name="Label">
/// The letter the edition prints after the number, where it prints one. The Greek numbers material
/// the Hebrew does not have by extending a verse rather than inventing one, and Swete carries 246
/// such verses — Esther's six additions, the long insertions of 3 Kingdoms, the appendices of
/// Proverbs.
/// </param>
internal sealed record SweteVerse(int Number, IReadOnlyList<SweteWord> Words, string Label = "");

internal sealed record SweteChapter(int Number, IReadOnlyList<SweteVerse> Verses);

/// <param name="Number">
/// The TLG work number the file states on every one of its lines, which is what says which book
/// this is. It is not a canonical ordinal and it is not an order: the Twelve stand in the Greek
/// order, so 37 is Amos and 39 is Joel.
/// </param>
internal sealed record SweteBook(int Number, IReadOnlyList<SweteChapter> Chapters);

/// <summary>
/// A chapter's Roman numeral the reader took out of the text: the token as the file has it, and what
/// of it stays — nothing for a numeral standing alone, the word for one run into its front.
/// </summary>
internal sealed record SweteChapterMarker(string Chapter, string Verse, string Token, string Kept);

/// <summary>
/// The one-token-per-line files of the machine-readable Swete: a reference, a space, and a word.
///
/// <code>
/// 10.1.1 ΚΑΙ
/// 10.1.1 ἐγένετο
/// 10.1.2 καὶ
/// </code>
///
/// The reference is the TLG work number, the chapter and the verse, and consecutive lines carrying
/// the same one are one verse. The words arrive with their punctuation attached and it is split off
/// into a trailer here, the way every other text in this corpus is stored: a word carrying a comma
/// cannot be matched against the same word in a witness that does not.
///
/// <para>
/// **The awkward part is what the file says about text that has no number**, and it says it by
/// accident rather than on purpose. The converter that produced these files walks the TEI carrying
/// a current chapter and a current verse, both starting at zero and both changed only when a
/// numbered division opens. So text printed inside a chapter but before its first numbered verse —
/// a psalm's title, the prologue of Lamentations, the heading of Obadiah — comes out under the
/// number of the last verse before it, which is a verse of the previous chapter. The corpus would
/// refuse it twice over: as an address claimed by two verses, and as a title standing at the end of
/// the psalm before its own.
/// </para>
///
/// <para>
/// So a chapter's first block is read as material printed before its first numbered verse when its
/// number is zero, or when the block after it carries a lower number, and it opens the verse that
/// follows it. That is what the printed page shows and it is what the other reader here already
/// does with the same case in USFM. Checked against Brenton, whose edition numbers a psalm's title
/// as verse 1: after the repair the two editions hold the same words at the same address.
/// </para>
///
/// <para>
/// **The chapter numbers are not the text either.** Swete prints a chapter's number in Roman
/// figures in the margin beside its first line, and the transcription let it in — at the end of the
/// chapter before, at the start of the chapter itself, often both (<c>XX</c> closing Exodus 19 and
/// standing alone as 20:1), and sometimes run into the first word (<c>XVαβὰθ</c>). A token that is
/// nothing but a Roman numeral is taken out where it stands in a chapter's last verse and names the
/// chapter after it, or in a chapter's first verse and names that chapter; a numeral run into the
/// front of a chapter's first word, naming the chapter the one before closes with, is taken off the
/// word, which stays. Nothing
/// else is touched: a Latin letter elsewhere is a misreading <see cref="SweteCorrections"/> settles
/// or leaves, or a letter of the margin the page settles (<see cref="SweteSettled"/>), and a numeral
/// that names no chapter beside it is not a chapter's number. A verse whose
/// only token was the numeral stays, empty: the edition numbers it, and the transcription has none
/// of its words.
/// </para>
/// </summary>
internal static class SweteReader
{
    /// <summary>
    /// The chapter the converter writes for a book the edition prints with no chapter division at
    /// all — the Epistle of Jeremiah, which is one chapter of seventy-two verses — and also for the
    /// unnumbered preface Swete prints before Sirach 1. The two are told apart by whether the file
    /// holds any numbered chapter beside it.
    /// </summary>
    private const string Unnumbered = "0";

    /// <summary>
    /// Esther's Addition A, which stands before chapter 1 and which the transcription gives a
    /// chapter of its own because there is no chapter yet to hang it on.
    /// </summary>
    private const string Prologue = "prologue";

    /// <summary>
    /// The letter Esther's additions carry in this file. Chapter 3 runs 13, then 1a to 7a, then 14
    /// — an addition is numbered from one inside the chapter it stands in and marked with an
    /// <c>a</c> — and Addition A is given the same shape, in chapter 1, so that the one addition
    /// the file could not place is held the way the file places the other four.
    /// </summary>
    private const string AdditionLabel = "a";

    /// <summary>Where both kinds of unnumbered chapter belong: at the head of the book.</summary>
    private const int FirstChapter = 1;

    /// <param name="keepChapterMarkers">
    /// True for the edition as a corpus loaded before the chapter numbers were taken out holds it.
    /// </param>
    public static SweteBook Read(IEnumerable<string> lines, bool keepChapterMarkers = false)
    {
        var (book, blocks) = Blocks(lines);
        if (!keepChapterMarkers)
        {
            TakeChapterMarkers(blocks);
        }

        return new SweteBook(book, Chapters(blocks));
    }

    /// <summary>The chapter numbers <see cref="Read"/> takes out of these lines, in file order.</summary>
    public static IReadOnlyList<SweteChapterMarker> ChapterMarkers(IEnumerable<string> lines) =>
        TakeChapterMarkers(Blocks(lines).Blocks);

    private static (int Book, List<(string Chapter, string Verse, List<string> Tokens)> Blocks) Blocks(
        IEnumerable<string> lines)
    {
        var book = 0;
        var blocks = new List<(string Chapter, string Verse, List<string> Tokens)>(4096);

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var space = line.IndexOf(' ');
            if (space < 0)
            {
                throw new InvalidOperationException(
                    $"\"{line}\" is not a reference and a word. Every line of this edition is " +
                    "\"<book>.<chapter>.<verse> <word>\"; a line that is not has to be understood before it " +
                    "is read, because splitting it on whitespace would put the reference into the corpus " +
                    "as a word.");
            }

            var (number, chapter, verse) = Reference(line[..space]);
            var token = line[(space + 1)..].Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (book == 0)
            {
                book = number;
            }
            else if (book != number)
            {
                throw new InvalidOperationException(
                    $"This file names book {book} on one line and book {number} on another. One file is one " +
                    "book of this edition; a file holding two would put half of one book under the other's " +
                    "name.");
            }

            if (blocks.Count == 0 || blocks[^1].Chapter != chapter || blocks[^1].Verse != verse)
            {
                blocks.Add((chapter, verse, []));
            }

            blocks[^1].Tokens.Add(token);
        }

        if (book == 0)
        {
            throw new InvalidOperationException("The file holds no lines, so nothing says which book it is.");
        }

        return (book, blocks);
    }

    /// <summary>
    /// Takes the chapter numbers out of the blocks in place and says what it took. A chapter's first
    /// and last block are read in file order, which is the order the page prints them in. A numeral run
    /// into a word is taken off it only where the chapter before closes with the same numeral standing
    /// alone, which is the transcription's own evidence that it is the number: an <c>X</c> opening a
    /// word is as often the chi it looks like.
    /// </summary>
    private static List<SweteChapterMarker> TakeChapterMarkers(
        List<(string Chapter, string Verse, List<string> Tokens)> blocks)
    {
        var taken = new List<SweteChapterMarker>();
        int? closedWith = null;
        for (var i = 0; i < blocks.Count; i++)
        {
            var (chapter, verse, tokens) = blocks[i];
            var opens = i == 0 || blocks[i - 1].Chapter != chapter;
            var closing = closedWith;
            closedWith = null;
            if (!int.TryParse(chapter, out var number))
            {
                continue;
            }

            var next = i + 1 < blocks.Count && blocks[i + 1].Chapter != chapter
                       && int.TryParse(blocks[i + 1].Chapter, out var following)
                ? following
                : 0;

            var here = new List<SweteChapterMarker>();
            for (var at = tokens.Count - 1; at >= 0; at--)
            {
                var token = tokens[at];
                var letters = 0;
                while (letters < token.Length && RomanFigures.Contains(token[letters]))
                {
                    letters++;
                }

                if (letters == 0 || Roman(token[..letters]) is not { } value)
                {
                    continue;
                }

                if (letters == token.Length)
                {
                    if ((opens && value == number) || (next > 0 && value == next))
                    {
                        here.Add(new SweteChapterMarker(chapter, verse, token, string.Empty));
                        tokens.RemoveAt(at);
                        if (next > 0 && value == next)
                        {
                            closedWith = value;
                        }
                    }
                }
                else if (opens && at == 0 && value == number && closing == number && IsGreekLetter(token[letters]))
                {
                    here.Add(new SweteChapterMarker(chapter, verse, token, token[letters..]));
                    tokens[at] = token[letters..];
                }
            }

            here.Reverse();
            taken.AddRange(here);
        }

        return taken;
    }

    private const string RomanFigures = "IVXLC";

    /// <summary>The value of a Roman numeral written as Swete prints them, or null where it is not one.</summary>
    internal static int? Roman(string figures)
    {
        var value = 0;
        for (var i = 0; i < figures.Length; i++)
        {
            var here = Figure(figures[i]);
            var after = i + 1 < figures.Length ? Figure(figures[i + 1]) : 0;
            value += here < after ? -here : here;
        }

        return value > 0 && value < 400 && Written(value) == figures ? value : null;
    }

    private static int Figure(char figure) => figure switch
    {
        'I' => 1,
        'V' => 5,
        'X' => 10,
        'L' => 50,
        'C' => 100,
        _ => 0,
    };

    private static string Written(int value)
    {
        (int Value, string Figures)[] steps =
            [(100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var written = new System.Text.StringBuilder();
        foreach (var (step, figures) in steps)
        {
            while (value >= step)
            {
                written.Append(figures);
                value -= step;
            }
        }

        return written.ToString();
    }

    private static bool IsGreekLetter(char letter) =>
        char.IsLetter(letter) && letter is (>= 'Ͱ' and <= 'Ͽ') or (>= 'ἀ' and <= '῿');

    private static IReadOnlyList<SweteChapter> Chapters(
        List<(string Chapter, string Verse, List<string> Tokens)> blocks)
    {
        var numbered = blocks.Any(block => block.Chapter != Unnumbered && block.Chapter != Prologue);
        var chapters = new List<(int Number, List<SweteVerse> Verses)>(64);

        foreach (var group in Grouped(blocks))
        {
            if (group.Chapter == Unnumbered && numbered)
            {
                // Sirach's preface: the translator's own words about his grandfather's book, printed
                // before chapter 1 and numbered as neither a chapter nor a verse. There is no
                // address in this corpus for a page that stands outside the book's numbering, and
                // running it into 1:1 would make that verse two hundred words long and say the
                // edition prints it there, which it does not.
                continue;
            }

            var number = group.Chapter is Unnumbered or Prologue
                ? FirstChapter
                : int.Parse(group.Chapter);
            var verses = group.Chapter == Prologue ? Addition(group.Blocks) : Verses(group.Blocks);
            var at = chapters.FindIndex(chapter => chapter.Number == number);
            if (at < 0)
            {
                chapters.Add((number, [.. verses]));
            }
            else
            {
                // Esther's Addition A is the only chapter that lands on one already held, and it is
                // read first because the edition prints it first, so file order is what keeps it at
                // the head of chapter 1.
                chapters[at] = (number, [.. chapters[at].Verses, .. verses]);
            }
        }

        return [.. chapters.Select(chapter => new SweteChapter(chapter.Number, chapter.Verses))];
    }

    private static List<(string Chapter, List<(string Verse, List<string> Tokens)> Blocks)> Grouped(
        List<(string Chapter, string Verse, List<string> Tokens)> blocks)
    {
        var grouped = new List<(string Chapter, List<(string Verse, List<string> Tokens)> Blocks)>(64);
        foreach (var (chapter, verse, tokens) in blocks)
        {
            var at = grouped.FindIndex(group => group.Chapter == chapter);
            if (at < 0)
            {
                grouped.Add((chapter, [(verse, tokens)]));
            }
            else
            {
                grouped[at].Blocks.Add((verse, tokens));
            }
        }

        return grouped;
    }

    /// <summary>
    /// One chapter's blocks as verses: the head folded into the verse it stands before, and blocks
    /// sharing an address joined, which is what the psalms need once their title has been taken out
    /// of the middle of them.
    /// </summary>
    private static List<SweteVerse> Verses(List<(string Verse, List<string> Tokens)> blocks)
    {
        List<string>? head = null;
        if (blocks.Count > 1 && StandsBefore(blocks[0].Verse, blocks[1].Verse))
        {
            head = blocks[0].Tokens;
            blocks = blocks[1..];
        }

        var held = new List<(int Number, string Label, List<string> Tokens)>(blocks.Count);
        foreach (var (verse, tokens) in blocks)
        {
            var (number, label) = Address(verse);
            var at = held.FindIndex(verses => verses.Number == number && verses.Label == label);
            if (at < 0)
            {
                held.Add((number, label, [.. tokens]));
            }
            else
            {
                held[at].Tokens.AddRange(tokens);
            }
        }

        if (head is not null && held.Count > 0)
        {
            held[0] = (held[0].Number, held[0].Label, [.. head, .. held[0].Tokens]);
        }

        return [.. held.Select(verse => new SweteVerse(verse.Number, Words(verse.Tokens), verse.Label))];
    }

    private static List<SweteVerse> Addition(List<(string Verse, List<string> Tokens)> blocks) =>
        [.. blocks.Select(block =>
            new SweteVerse(Address(block.Verse).Number, Words(block.Tokens), AdditionLabel))];

    /// <summary>
    /// Whether the first block of a chapter is material printed before its first numbered verse.
    /// Zero is what the converter writes where no verse has opened yet in the whole book; anything
    /// higher than the block after it is the number carried over from the chapter before.
    /// </summary>
    private static bool StandsBefore(string first, string next)
    {
        var (number, _) = Address(first);
        return number == 0 || number > Address(next).Number;
    }

    private static (int Number, string Label) Address(string verse)
    {
        var digits = 0;
        while (digits < verse.Length && char.IsAsciiDigit(verse[digits]))
        {
            digits++;
        }

        return digits == 0
            ? throw new InvalidOperationException(
                $"\"{verse}\" is not a verse number. A reference here is a number and, where the edition " +
                "extends a verse rather than adding one, a letter after it.")
            : (int.Parse(verse[..digits]), verse[digits..]);
    }

    private static (int Book, string Chapter, string Verse) Reference(string reference)
    {
        var parts = reference.Split('.');
        return parts.Length == 3 && int.TryParse(parts[0], out var book)
            ? (book, parts[1], parts[2])
            : throw new InvalidOperationException(
                $"\"{reference}\" is not a reference of this edition, which writes them as " +
                "\"<book>.<chapter>.<verse>\" with the book as a number.");
    }

    /// <summary>
    /// A verse's tokens, each split into the word and what trails it. Punctuation is a mark on the
    /// sentence rather than a letter of the word, and a token that is nothing but punctuation
    /// belongs to the word before it — the edition sets the Greek question mark off with a space,
    /// and 735 tokens would otherwise stand in the corpus as words with no letters. Eleven of them
    /// are the superscript figures Swete prints in the margin to number a verse, which the
    /// transcription let out into the text; they stay where they fell rather than being deleted,
    /// because a reader who meets one should be able to see that the source has it.
    ///
    /// Split once the verse is whole rather than as each line is read, because a verse can arrive
    /// in more than one block: a psalm's title is one, its first verse is another, and a mark
    /// opening the second belongs to the last word of the first.
    /// </summary>
    internal static List<SweteWord> Words(IReadOnlyList<string> tokens)
    {
        var words = new List<SweteWord>(tokens.Count);
        foreach (var token in tokens)
        {
            var end = token.Length;
            while (end > 0 && !char.IsLetterOrDigit(token[end - 1]) && token[end - 1] != 'ʼ')
            {
                end--;
            }

            if (end == 0)
            {
                if (words.Count > 0)
                {
                    words[^1] = words[^1] with { Trailer = words[^1].Trailer + token + " " };
                }

                continue;
            }

            words.Add(new SweteWord(token[..end], token[end..] + " "));
        }

        return words;
    }
}
