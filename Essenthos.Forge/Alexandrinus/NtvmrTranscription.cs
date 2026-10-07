using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;

namespace Essenthos.Core.Alexandrinus;

/// <param name="File">The file the transcription is kept in, beside its licence.</param>
/// <param name="DocumentId">The Virtual Manuscript Room's number for the manuscript, which its address carries.</param>
/// <param name="Relabelled">
/// The labels the transcription writes in another form than <c>B25K1V1</c>, by the book name and
/// chapter they state, with the book each belongs to. Every one is checked against the verse keys
/// of CNTR's independent transcription of the same manuscript, which agree with the repaired labels
/// verse for verse.
/// </param>
internal sealed record NtvmrManuscript(
    string File,
    int DocumentId,
    IReadOnlyDictionary<(string Book, int Chapter), int> Relabelled)
{
    public string Url =>
        $"https://ntvmr.uni-muenster.de/community/vmr/api/transcript/get/?docID={DocumentId}&pageID=ALL&format=teiraw";
}

/// <summary>
/// A New Testament manuscript as the Institut für Neutestamentliche Textforschung transcribed it
/// for the Virtual Manuscript Room: one TEI file, one <c>&lt;ab&gt;</c> per verse and one
/// <c>&lt;w&gt;</c> per word, spelled as the scribe spelled it, unaccented and with the nomina sacra
/// contracted.
///
/// <para>
/// A word is its letters as the transcriber read them, including the ones marked unclear and the
/// ones the transcriber supplied where the page is damaged. Where the manuscript was corrected, the
/// first hand is the text and each correction is a note on the verse, naming its corrector as the
/// transcription does — except where the first hand was erased past reading, where the earliest
/// correction is all anybody can read and stands in the text. What the manuscript has lost is
/// absent, not filled, and so is a verse the first hand did not write. The titles and colophons of
/// the books are not verses and are not read.
/// </para>
/// </summary>
internal static partial class NtvmrTranscription
{
    /// <summary>
    /// Codex Alexandrinus. <c>Heb.1.*</c> is 1 Timothy, which follows Hebrews in the manuscript and
    /// whose first fifteen verses the transcription labels after the book before it; the unnamed
    /// book is 3 John.
    /// </summary>
    public static readonly NtvmrManuscript Alexandrinus = new("ntvmr-02.xml", 20002, new Dictionary<(string, int), int>
    {
        [("", 1)] = 25,
        [("Jude", 1)] = 26,
        [("Heb", 13)] = 19,
        [("Heb", 1)] = 15,
    });

    /// <summary>
    /// Codex Sinaiticus. Two leaves of 1 Thessalonians and one of Hebrews are labelled by the book's
    /// name, and 3 John by <c>XXX</c>.
    /// </summary>
    public static readonly NtvmrManuscript Sinaiticus = new("ntvmr-01.xml", 20001, new Dictionary<(string, int), int>
    {
        [("1Thess", 2)] = 13,
        [("1Thess", 3)] = 13,
        [("1Thess", 4)] = 13,
        [("Heb", 8)] = 19,
        [("Heb", 9)] = 19,
        [("2John", 1)] = 24,
        [("XXX", 1)] = 25,
        [("Jude", 1)] = 26,
    });

    /// <summary>Codex Vaticanus. A leaf of Matthew, 3 John and Jude are labelled by the book's name.</summary>
    public static readonly NtvmrManuscript Vaticanus = new("ntvmr-03.xml", 20003, new Dictionary<(string, int), int>
    {
        [("Matt", 16)] = 1,
        [("Matt", 17)] = 1,
        [("3John", 1)] = 25,
        [("Jude", 1)] = 26,
    });

    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";

    /// <summary>INTF numbers the books from Matthew as B01, so the shared canon is that plus this.</summary>
    private const int CanonicalOffset = 39;

    private const string FirstHand = "orig";

    private const string Correction = "corr";

    /// <summary>What one block of the transcription writes as a word where the hand wrote none.</summary>
    private const string Omitted = "OM";

    /// <summary>The divisions that hold a book's title and colophon, which are not verses.</summary>
    private static readonly HashSet<string> Titles = ["incipit", "explicit"];

    [GeneratedRegex(@"^B(?<book>\d\d)K(?<chapter>\d+)V(?<verse>\d+)$")]
    private static partial Regex Standard();

    [GeneratedRegex(@"^(?<book>[A-Za-z0-9]*)\.(?<chapter>\d+)\.(?<verse>\d+)$")]
    private static partial Regex Named();

    [GeneratedRegex("inscriptio|subscriptio", RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    /// <summary><c>corrector2a</c>, <c>2</c>, <c>corrector1V</c>: which corrector, and whether the reading is only apparent.</summary>
    [GeneratedRegex(@"^(?:corrector)?(?<hand>\d*[a-z]?)(?<apparent>V*)$")]
    private static partial Regex Hand();

    public static IReadOnlyList<BookDraft> Books(string path, int firstPosition, NtvmrManuscript manuscript)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var books = new List<(int Canonical, SortedDictionary<int, SortedDictionary<int, Verse>> Chapters)>();

        foreach (var block in document.Descendants(Tei + "ab"))
        {
            if (block.Parent is { } division && Titles.Contains((string?)division.Attribute("type") ?? string.Empty))
            {
                continue;
            }

            var label = (string?)block.Attribute("n") ?? string.Empty;
            if (Address(label, manuscript) is not var (canonical, chapter, number))
            {
                continue;
            }

            if (books.Count == 0 || books[^1].Canonical != canonical)
            {
                if (books.Any(book => book.Canonical == canonical))
                {
                    throw new InvalidOperationException(
                        $"{manuscript.File} returns to {BookReferences.Name(canonical)} at \"{label}\" after another book. "
                        + "Find which book the verses there hold and add the label to the manuscript's repairs.");
                }

                books.Add((canonical, new SortedDictionary<int, SortedDictionary<int, Verse>>()));
            }

            var chapters = books[^1].Chapters;
            if (!chapters.TryGetValue(chapter, out var verses))
            {
                chapters[chapter] = verses = new SortedDictionary<int, Verse>();
            }

            // A verse that runs over a page break is written as two blocks with the same label.
            if (!verses.TryGetValue(number, out var verse))
            {
                verses[number] = verse = new Verse();
            }

            Read(block, verse);
        }

        var position = firstPosition;
        return [.. books.Select(book => new BookDraft(
            CanonicalOrdinal: book.Canonical,
            Position: position++,
            Name: BookReferences.Name(book.Canonical),
            Slug: BookReferences.Slug(book.Canonical),
            Abbreviation: BookReferences.Abbreviation(book.Canonical),
            Chapters: [.. book.Chapters.Select(chapter => new ChapterDraft(
                chapter.Key,
                [.. chapter.Value
                    .Where(verse => verse.Value.Words.Count > 0)
                    .Select(verse => new VerseDraft(
                        verse.Key,
                        [.. verse.Value.Words.Select(word => new WordDraft(word.Surface, word.Trailer.ToString()))])
                    {
                        Notes = [.. verse.Value.Notes.Select(note => new VerseNoteDraft(VerseNoteKind.Footnote, note))],
                    })]))]))];
    }

    /// <summary>The verse a block's label names, or null for a book's title or colophon.</summary>
    public static (int Canonical, int Chapter, int Verse)? Address(string label, NtvmrManuscript manuscript)
    {
        if (Standard().Match(label) is { Success: true } standard)
        {
            var verse = int.Parse(standard.Groups["verse"].Value);
            return verse == 0
                ? null
                : (int.Parse(standard.Groups["book"].Value) + CanonicalOffset,
                    int.Parse(standard.Groups["chapter"].Value), verse);
        }

        if (Title().IsMatch(label))
        {
            return null;
        }

        if (Named().Match(label) is { Success: true } named)
        {
            var chapter = int.Parse(named.Groups["chapter"].Value);
            if (manuscript.Relabelled.TryGetValue((named.Groups["book"].Value, chapter), out var book))
            {
                return (book + CanonicalOffset, chapter, int.Parse(named.Groups["verse"].Value));
            }
        }

        throw new InvalidOperationException(
            $"The transcription {manuscript.File} labels a verse \"{label}\", which is neither the B..K..V.. form nor "
            + "one of the labels known to stand for another verse. Find the verse it holds by its words, "
            + "check it against CNTR's transcription of the same manuscript, and add it to the repairs.");
    }

    /// <summary>
    /// How a note names a corrector: <c>corr.</c> alone where the transcription does not tell the
    /// correctors apart, its number and letter where it does, and <c>vid.</c> where the reading is
    /// only apparent.
    /// </summary>
    public static string Corrector(string? hand)
    {
        if (hand is null || Hand().Match(hand) is not { Success: true } match)
        {
            return "corr.";
        }

        var number = match.Groups["hand"].Value;
        return "corr." + number + (match.Groups["apparent"].Length > 0 ? " vid." : string.Empty);
    }

    private static void Read(XElement container, Verse verse)
    {
        foreach (var node in container.Nodes())
        {
            if (node is not XElement element)
            {
                continue;
            }

            switch (element.Name.LocalName)
            {
                case "w":
                    var surface = Letters(element);
                    if (surface.Length > 0)
                    {
                        verse.Words.Add(new Word(surface));
                    }

                    break;
                case "pc":
                    if (verse.Words.Count > 0)
                    {
                        verse.Words[^1].Trailer.Insert(verse.Words[^1].Trailer.Length - 1, element.Value.Trim());
                    }

                    break;
                case "app":
                    Read(Reading(element, verse), verse);
                    break;
                case "note" or "gap" or "lb" or "cb" or "pb":
                    break;
                default:
                    Read(element, verse);
                    break;
            }
        }
    }

    /// <summary>
    /// The first hand's reading of a corrected place, with the corrections noted on the verse the way
    /// an apparatus writes them: the first hand, a bracket, and what each corrector made of it.
    /// </summary>
    private static XElement Reading(XElement app, Verse verse)
    {
        var readings = app.Elements(Tei + "rdg").ToList();
        var first = readings.FirstOrDefault(rdg => (string?)rdg.Attribute("type") == FirstHand);
        var corrections = readings.Where(rdg => (string?)rdg.Attribute("type") == Correction).ToList();
        if (first is null)
        {
            return corrections.FirstOrDefault() ?? app;
        }

        if (corrections.Count > 0)
        {
            var before = Phrase(first);
            verse.Notes.Add($"{(before.Length > 0 ? before : "—")}] " + string.Join("; ", corrections.Select(corrected =>
            {
                var after = Phrase(corrected);
                return $"{(after.Length > 0 ? after : "om.")} {Corrector((string?)corrected.Attribute("hand"))}";
            })));
        }

        return first.Descendants(Tei + "gap").Any() && corrections.Count > 0 ? corrections[0] : first;
    }

    private static string Phrase(XElement reading) =>
        string.Join(' ', reading.Descendants(Tei + "w").Select(Letters).Where(word => word.Length > 0));

    /// <summary>
    /// The letters of a word. Whitespace, the zero-width spaces some blocks leave at a line's end,
    /// the overline a few blocks type over a nomen sacrum where the rest mark it up, and the
    /// placeholder for an omission are not letters.
    /// </summary>
    private static string Letters(XElement word)
    {
        var letters = new StringBuilder();
        foreach (var text in word.DescendantNodes().OfType<XText>())
        {
            if (text.Ancestors(Tei + "note").Any())
            {
                continue;
            }

            foreach (var c in text.Value)
            {
                if (!char.IsWhiteSpace(c) && c is not ZeroWidthSpace and not Overline)
                {
                    letters.Append(c);
                }
            }
        }

        var surface = letters.ToString().Normalize(NormalizationForm.FormC);
        return surface == Omitted ? string.Empty : surface;
    }

    private const char ZeroWidthSpace = '​';

    private const char Overline = '̅';

    private sealed class Verse
    {
        public List<Word> Words { get; } = [];

        public List<string> Notes { get; } = [];
    }

    private sealed class Word(string surface)
    {
        public string Surface { get; } = surface;

        public StringBuilder Trailer { get; } = new(" ");
    }
}
