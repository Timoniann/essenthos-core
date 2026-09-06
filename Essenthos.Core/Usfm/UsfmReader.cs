using System.Text.RegularExpressions;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Usfm;

/// <param name="Book">The three-letter code from <c>\id</c> — <c>GEN</c>, <c>TOB</c>, <c>DAG</c>.</param>
/// <param name="Name">
/// What the edition calls this book in its own language, from <c>\toc1</c> where there is one and
/// <c>\h</c> otherwise. Empty where it names it nowhere.
///
/// It is a title and not a verse, so it is not text — but it is the only place the edition says
/// anything in its own words about the book as a whole, and a Ukrainian pane headed "Genesis" is a
/// Ukrainian pane a Ukrainian reader has to translate back.
/// </param>
internal sealed record UsfmBook(string Book, IReadOnlyList<UsfmChapter> Chapters, string Name = "");

internal sealed record UsfmChapter(int Number, IReadOnlyList<UsfmVerse> Verses);

/// <param name="Words">
/// Split on whitespace, with trailing punctuation moved into the word's trailer so that a word is
/// the word and not the word plus a comma. This is what the other Greek texts do, and a corpus
/// where one witness carries its punctuation and another does not is a corpus that cannot compare
/// them.
/// </param>
internal sealed record UsfmVerse(int Number, IReadOnlyList<UsfmWord> Words, string Label = "")
{
    /// <summary>
    /// The addresses the edition prints for this verse in its own numbering, in the order it
    /// prints them, and empty for a text that numbers its verses the way it is stored.
    ///
    /// eBible renumbers a translation to the English scheme and then says so in the text: Luther
    /// writes <c>[30:1]</c> and the Elberfelder <c>(030:2)</c>, which is each edition recording the
    /// numbering its own readers hold. Taking it out of the words without keeping it would leave
    /// the German unable to say a chapter is numbered differently — and leaving it in would put
    /// "30" and "1" into the corpus as scripture.
    /// </summary>
    public IReadOnlyList<UsfmAddress> Stated { get; init; } = [];

    /// <summary>
    /// The verse holds words standing before the address it states for what follows them, so by the
    /// edition's own numbering it opens in an earlier verse than the one it states.
    /// </summary>
    public bool OpensBeforeItsStatedAddress { get; init; }
}

/// <param name="Chapter">The chapter of the edition's own numbering, which need not be the row's.</param>
/// <param name="Number">The verse of it.</param>
internal readonly record struct UsfmAddress(int Chapter, int Number);

internal sealed record UsfmWord(string Surface, string Trailer)
{
    /// <summary>
    /// The Strong number the edition tags this word with, canonical, where it tags exactly one.
    ///
    /// Null is silence twice over: an edition that tags nothing, and a tag naming several numbers
    /// at once. The Spanish writes <c>doce</c> as <c>H8147,H6240</c> — two and ten — and one
    /// number picked out of that pair would be this reader's choice printed as the edition's.
    /// </summary>
    public string? StrongNumber { get; init; }

    /// <summary>
    /// Which of its verse's <c>\add</c> spans this word stands in, counting from one, and null
    /// where the edition marks nothing. The same field the Synodal's square brackets fill.
    /// </summary>
    public int? SuppliedSpan { get; init; }
}

/// <summary>
/// Just enough USFM for the texts eBible publishes in it: Brenton's Septuagint, the Kulish
/// Ukrainian Bible, the German and Spanish translations, and the six English ones.
///
/// The file is markers at the start of a line and running text after them, and these use almost
/// none of the standard: <c>\id</c>, <c>\c</c>, <c>\v</c>, and a handful of paragraph marks that
/// carry no text of their own. Everything else — <c>\h</c>, <c>\toc</c>, <c>\mt</c> — is a title,
/// and titles are not verses.
///
/// This is deliberately not a USFM implementation. It reads the files that are here, and says so
/// loudly when it meets a marker it has not been told about, rather than dropping the text after
/// it and leaving a verse quietly short. That promise now covers markers standing inside a line as
/// well as at the start of one: the Ukrainian carries 204 footnotes and 2,079 spans marked as
/// spoken by Jesus, and a reader that only knew about line-initial markers would have put both
/// kinds of marker into the corpus as words, along with the footnotes' Ukrainian glosses.
///
/// Two inline markers carry a claim rather than only text, and both are kept. <c>\w …|strong="…"\w*</c>
/// is the edition naming the original-language word a word of its translation renders — 365,353 of
/// them in Luther 1912 and 390,758 in the Reina-Valera — and it is the only thing either text says
/// about the originals, so dropping it would leave the aligner as the sole route from German or
/// Spanish to Hebrew. <c>\add</c> is the translator marking a word the base text does not have,
/// which is the same claim the Synodal makes with square brackets and reaches the same column.
/// </summary>
internal static partial class UsfmReader
{
    /// <summary>
    /// Markers that introduce or interrupt a paragraph and carry no verse of their own. Text on
    /// their line belongs to the passage — a Psalm's superscription is <c>\d</c> and is part of the
    /// psalm — so their content is kept and attached to whatever verse is open.
    /// </summary>
    private static readonly HashSet<string> Passage =
    [
        "p", "m", "nb", "b", "q", "q1", "q2", "q3", "qc", "pi", "pi1", "mi", "d", "s", "s1", "s2",
        "ms", "ms1", "sp", "li", "li1",
    ];

    /// <summary>
    /// Markers that are titles, notes or metadata: read and discarded. The two that name the book —
    /// <c>\h</c> and <c>\toc1</c> — are handled rather than discarded and so are not here.
    /// </summary>
    private static readonly HashSet<string> Matter =
        ["toc2", "toc3", "mt", "mt1", "mt2", "mt3", "is", "is1", "ip", "imt", "rem", "cl", "ide"];

    public static UsfmBook Read(string content)
    {
        string? book = null;
        var chapters = new List<UsfmChapter>();
        var verses = new List<UsfmVerse>();
        var words = new List<UsfmWord>();
        var chapter = 0;
        var verse = 0;
        var label = string.Empty;
        var title = string.Empty;
        var running = new Running();

        void CloseVerse()
        {
            // Words collected before any verse opened are not dropped: they are a heading that
            // introduces the passage, and in the Psalms they are the superscription — 150 of them,
            // which the Greek numbers as part of the psalm. They stay in hand and become the head
            // of the verse that opens next. Silently losing them would be the worse answer, and
            // inventing a verse number for them would be worse still.
            if (verse == 0)
            {
                return;
            }

            if (words.Count > 0)
            {
                verses.Add(new UsfmVerse(verse, [.. words], label)
                {
                    Stated = [.. running.Stated],
                    OpensBeforeItsStatedAddress = running.OpensBefore,
                });
            }

            words.Clear();
            verse = 0;
            label = string.Empty;
            running.Close();
        }

        void CloseChapter()
        {
            CloseVerse();
            if (chapter > 0 && verses.Count > 0)
            {
                chapters.Add(new UsfmChapter(chapter, [.. verses]));
            }

            // A heading with no verse after it in the whole chapter has nowhere to belong.
            words.Clear();
            verses.Clear();
        }

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] != '\\')
            {
                // A continuation line: the verse it belongs to is still open.
                Words(trimmed, words, running);
                continue;
            }

            var match = Marker().Match(trimmed);
            if (!match.Success)
            {
                throw new InvalidOperationException($"This is not a USFM marker: \"{trimmed}\"");
            }

            var name = match.Groups["marker"].Value;
            var rest = match.Groups["rest"].Value.Trim();

            switch (name)
            {
                case "id":
                    book = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    break;

                case "c":
                    CloseChapter();
                    chapter = int.Parse(rest.Split(' ')[0]);
                    break;

                case "v":
                    CloseVerse();
                    var space = rest.IndexOf(' ');
                    (verse, label) = Number(space < 0 ? rest : rest[..space]);
                    if (space >= 0)
                    {
                        Words(rest[(space + 1)..], words, running);
                    }

                    break;

                // The running header comes first in the file and the table-of-contents name after
                // it, and the second is the better of the two: \h is set in capitals for the top
                // of a printed page, \toc1 is the book's name as it would be written.
                case "h":
                    title = title.Length == 0 ? rest : title;
                    break;

                case "toc1":
                    title = rest;
                    break;

                default:
                    if (Passage.Contains(name))
                    {
                        Words(rest, words, running);
                    }
                    else if (!Matter.Contains(name))
                    {
                        throw new InvalidOperationException(
                            $"Unknown USFM marker \\{name}. Decide whether it carries text before reading a " +
                            "file that uses it — a marker treated as matter drops whatever follows it.");
                    }

                    break;
            }
        }

        CloseChapter();

        return book is null
            ? throw new InvalidOperationException("The file has no \\id, so nothing says which book it is.")
            : new UsfmBook(book, chapters, title);
    }

    /// <summary>
    /// A verse number, and the letter after it where there is one.
    ///
    /// The Septuagint carries 317 of these — <c>50a</c>, <c>1b</c>, <c>1e</c> — mostly in Greek
    /// Esther, 1 Kings and Proverbs. They are how the Greek numbers material the Hebrew does not
    /// have: Genesis 31 runs 49, 50, 50a, 52, extending a verse rather than inventing one. Dropping
    /// the letter would collide two verses onto one number; dropping the verse would lose the text.
    /// </summary>
    private static (int Number, string Label) Number(string token)
    {
        var digits = 0;
        while (digits < token.Length && char.IsAsciiDigit(token[digits]))
        {
            digits++;
        }

        return digits == 0
            ? throw new InvalidOperationException($"\"{token}\" is not a verse number.")
            : (int.Parse(token[..digits]), token[digits..]);
    }

    /// <summary>
    /// What an annotated edition leaves open across the lines of one verse: the spans it has marked
    /// as supplied, which of them the run being read stands inside, and the addresses it has
    /// printed in its own numbering. A verse is written over as many lines as its paragraphs need,
    /// so none of these can be a local of the method that reads a line.
    /// </summary>
    private sealed class Running
    {
        private readonly List<UsfmAddress> stated = [];

        private int spans;

        public int? Current { get; private set; }

        public bool OpensBefore { get; private set; }

        public IReadOnlyList<UsfmAddress> Stated => stated;

        public void Open() => Current = ++spans;

        public void CloseSpan() => Current = null;

        public void State(UsfmAddress address, bool afterWords)
        {
            stated.Add(address);
            OpensBefore |= afterWords;
        }

        public void Close()
        {
            spans = 0;
            Current = null;
            OpensBefore = false;
            stated.Clear();
        }
    }

    /// <summary>
    /// The words of a run of text. Punctuation that trails a word goes into its trailer: Greek
    /// commas, the ano teleia, the full stop and the closing quote are marks on the sentence, not
    /// letters of the word, and a word that carries one cannot be matched against the same word
    /// in another witness that does not.
    ///
    /// The markers come out before anything is split, and what they claimed is remembered against
    /// the characters they stood over. Splitting each run between two markers on its own would put
    /// a word boundary wherever a marker closed, and the Reina-Valera closes one immediately before
    /// its full stops: <c>\w Erde|strong="H0776"\w*.</c> would be read as the word and then a lone
    /// full stop, which is the shape this reader gives an em-dash standing on its own.
    ///
    /// A tag over a phrase gives every word of the phrase its number — the Spanish tags
    /// <c>EN el principio</c> as one thing — which is the only way a per-word column can carry a
    /// claim made about a span, and is a reading of the edition rather than its own words.
    /// </summary>
    private static void Words(string text, List<UsfmWord> into, Running running)
    {
        var scripture = Marked().Replace(Note().Replace(text, string.Empty), string.Empty);
        var plain = new System.Text.StringBuilder(scripture.Length);
        var numbers = new List<string?>(scripture.Length);
        var spans = new List<int?>(scripture.Length);
        var read = 0;

        void Keep(ReadOnlySpan<char> run, string? number)
        {
            foreach (var character in run)
            {
                plain.Append(character);
                numbers.Add(number);
                spans.Add(running.Current);
            }
        }

        foreach (Match element in Element().Matches(scripture))
        {
            Keep(scripture.AsSpan(read, element.Index - read), null);
            read = element.Index + element.Length;

            if (element.Groups["surface"].Success)
            {
                Keep(element.Groups["surface"].ValueSpan, StrongNumbers.Normalize(element.Groups["strong"].Value));
            }
            else if (element.Groups["chapter"].Success)
            {
                running.State(
                    new UsfmAddress(int.Parse(element.Groups["chapter"].Value), int.Parse(element.Groups["verse"].Value)),
                    into.Count > 0 || plain.ToString().Trim().Length > 0);
            }
            else if (element.Groups["close"].Success)
            {
                running.CloseSpan();
            }
            else
            {
                running.Open();
            }
        }

        Keep(scripture.AsSpan(read), null);
        Split(plain.ToString(), numbers, spans, into);
    }

    private static void Split(string text, List<string?> numbers, List<int?> spans, List<UsfmWord> into)
    {
        if (text.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A USFM marker stands inside this line, and it is neither a note nor a span of the text: " +
                $"\"{text.Trim()}\". Decide which of the two it is and add it to Note(), Marked() or " +
                "Element() before reading a file that uses it — a marker nobody has decided about is split "
                + "on whitespace and put in the corpus as words.");
        }

        var at = 0;
        while (at < text.Length)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }

            var start = at;
            while (at < text.Length && !char.IsWhiteSpace(text[at]))
            {
                at++;
            }

            if (at == start)
            {
                break;
            }

            var token = text[start..at];
            var end = token.Length;
            while (end > 0 && !char.IsLetterOrDigit(token[end - 1]) && token[end - 1] != 'ʼ')
            {
                end--;
            }

            if (end == 0)
            {
                // Punctuation standing alone belongs to the word before it.
                if (into.Count > 0)
                {
                    into[^1] = into[^1] with { Trailer = into[^1].Trailer + token + " " };
                }

                continue;
            }

            into.Add(new UsfmWord(token[..end], token[end..] + " ")
            {
                StrongNumber = Only(numbers, start, start + end),
                SuppliedSpan = Only(spans, start, start + end),
            });
        }
    }

    /// <summary>
    /// The one thing an edition marked the letters of this word with, and null where it marked
    /// nothing or more than one thing.
    ///
    /// A word is not the same span as a tag. The opening quotation mark of <c>„\w Wort|…\w*</c>
    /// stands outside the tag and belongs to the token, so reading the first letter alone would
    /// lose the number on 2,043 German words and 10,121 Spanish ones — and where a token really
    /// does straddle two tags, the edition made two claims about it and neither of them is that
    /// this word renders that one.
    /// </summary>
    private static T Only<T>(List<T> marks, int from, int to)
    {
        var same = EqualityComparer<T>.Default;
        var found = default(T)!;
        var any = false;

        for (var at = from; at < to; at++)
        {
            if (same.Equals(marks[at], default!))
            {
                continue;
            }

            if (any && !same.Equals(found, marks[at]))
            {
                return default!;
            }

            found = marks[at];
            any = true;
        }

        return found;
    }

    [GeneratedRegex(@"^\\(?<marker>[a-z][a-z0-9]*)\*?(?<rest>.*)$")]
    private static partial Regex Marker();

    /// <summary>
    /// An inline marker that carries a claim about the words it encloses, rather than only the
    /// words: the Strong number the edition tags a span with, and the opening and closing of a span
    /// the translator marks as supplied.
    ///
    /// The attributes are matched as a whole and only <c>strong</c> is read out of them, because
    /// USFM allows a list — <c>\w word|lemma="x" strong="H1"\w*</c> — and a reader that assumed the
    /// number stood alone would silently take a lemma for one the first time it met a file that
    /// carried both.
    ///
    /// The leading <c>+</c> is USFM's mark for a character marker standing inside another one, and
    /// the Reina-Valera uses it three times, for a tagged word inside a supplied span. Three
    /// occurrences in 390,758 are exactly the kind of thing a reader meets after it has been
    /// trusted, so they are read rather than left to the refusal.
    ///
    /// The third alternative is not a marker at all: it is a chapter and verse in brackets, written
    /// into the running text, which is how eBible records the numbering an edition follows where it
    /// has renumbered the file to the English one. Luther writes 357 of them as <c>[30:1]</c> and
    /// the Elberfelder 261 as <c>(030:2)</c>. Only the two-part form counts — a bare number in
    /// brackets is a footnote mark or a variant, and neither is an address. Brenton and the Kulish
    /// Bible write neither form anywhere, so nothing they hold changes by this being read.
    /// </summary>
    [GeneratedRegex(
        @"\\\+?w (?<surface>[^|\\]*)\|(?:[^\\]*?strong=""(?<strong>[^""]*)"")?[^\\]*\\\+?w\*"
        + @"|\\\+?add(?<close>\*)?"
        + @"|[\[(](?<chapter>\d{1,3})[:-](?<verse>\d{1,3})[\])]")]
    private static partial Regex Element();

    /// <summary>
    /// A footnote or a cross reference, from its opening marker to its closing one.
    ///
    /// The whole span leaves the text rather than being kept somewhere: it is the editor writing
    /// about the verse, in a language and a register that are not the verse's — Kulish glosses
    /// Едом as *Червоний* and Егова-Нїссі as *Господь-прапор* — and a corpus that tokenised it
    /// would have those standing in Genesis as words nobody wrote there. Nothing here models a
    /// note, and inventing a place for one on the way past would be worse than dropping it: it
    /// would be a claim about the text made by a regex.
    /// </summary>
    [GeneratedRegex(@"\\(?<note>f|x)\s.*?\\\k<note>\*")]
    private static partial Regex Note();

    /// <summary>
    /// A character marker wrapping words of the text rather than words about it. Only the marker
    /// leaves and the words stay.
    ///
    /// <c>\wj</c> is the edition saying that what it encloses is spoken by Jesus, and the Ukrainian
    /// marks 2,079 spans that way; treating it like a note would take most of the Gospels out of
    /// the corpus. Nothing here records who speaks, so what is lost is the claim and not the text,
    /// which is the right way round to lose something.
    ///
    /// <c>\qs</c> is Selah, which the American Standard Version and the World English Bible each
    /// mark 74 times. It is a word of the psalm and not a note about it — BHSA holds it as a word of
    /// the Hebrew — so the marker goes and the word stays. <c>\bk</c> is the title of a book quoted
    /// inside a verse, which the World English Bible uses twice, for the Book of the Wars of the
    /// LORD; the title is part of the sentence that names it.
    /// </summary>
    [GeneratedRegex(@"\\(?:wj|qs|bk)\*?")]
    private static partial Regex Marked();
}
