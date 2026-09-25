using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;

namespace Essenthos.Core.Alexandrinus;

/// <summary>
/// The New Testament of Codex Alexandrinus as the Institut für Neutestamentliche Textforschung
/// transcribed it for the Virtual Manuscript Room: one TEI file, one <c>&lt;ab&gt;</c> per verse and
/// one <c>&lt;w&gt;</c> per word, spelled as the scribe spelled it, unaccented and with the nomina
/// sacra contracted.
///
/// <para>
/// A word is its letters as the transcriber read them, including the ones marked unclear and the
/// ones the transcriber supplied where the page is damaged. Where the manuscript was corrected, the
/// first hand is the text and the correction is a note on the verse — except where the first hand was
/// erased past reading, where the correction is all anybody can read and stands in the text. What the
/// manuscript has lost is absent, not filled: Matthew before 25:6, John 6:50–8:52 and
/// 2 Corinthians 4:13–12:6 have no verses. So is a verse the first hand did not write — eighteen the
/// manuscript never had, and John 5:12 and 8:52, which only a corrector wrote. The titles and
/// colophons of the books are not verses and are not read.
/// </para>
/// </summary>
internal static partial class NtvmrTranscription
{
    public const string File = "ntvmr-02.xml";

    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";

    /// <summary>INTF numbers the books from Matthew as B01, so the shared canon is that plus this.</summary>
    private const int CanonicalOffset = 39;

    private const string FirstHand = "orig";

    private const string Correction = "corr";

    /// <summary>
    /// The labels the transcription writes in another form than <c>B25K1V1</c>, by the book and
    /// chapter they state, with the book they belong to. Every one was checked against the verse keys
    /// of CNTR's independent transcription of the same manuscript, which agree with the repaired
    /// labels verse for verse. <c>Heb.1.*</c> is 1 Timothy, which follows Hebrews in the manuscript
    /// and whose first fifteen verses the transcription labels after the book before it.
    /// </summary>
    private static readonly Dictionary<(string Book, int Chapter), int> Mislabelled = new()
    {
        [("", 1)] = 25,
        [("Jude", 1)] = 26,
        [("Heb", 13)] = 19,
        [("Heb", 1)] = 15,
    };

    /// <summary>The divisions that hold a book's title and colophon, which are not verses.</summary>
    private static readonly HashSet<string> Titles = ["incipit", "explicit"];

    [GeneratedRegex(@"^B(?<book>\d\d)K(?<chapter>\d+)V(?<verse>\d+)$")]
    private static partial Regex Standard();

    [GeneratedRegex(@"^(?<book>[A-Za-z]*)\.(?<chapter>\d+)\.(?<verse>\d+)$")]
    private static partial Regex Named();

    [GeneratedRegex("inscriptio|subscriptio", RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    public static IReadOnlyList<BookDraft> Books(string path, int firstPosition)
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
            if (Address(label) is not var (canonical, chapter, number))
            {
                continue;
            }

            if (books.Count == 0 || books[^1].Canonical != canonical)
            {
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
    public static (int Canonical, int Chapter, int Verse)? Address(string label)
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
            if (Mislabelled.TryGetValue((named.Groups["book"].Value, chapter), out var book))
            {
                return (book + CanonicalOffset, chapter, int.Parse(named.Groups["verse"].Value));
            }
        }

        throw new InvalidOperationException(
            $"The Alexandrinus transcription labels a verse \"{label}\", which is neither the B..K..V.. form nor "
            + "one of the labels known to stand for another verse. Find the verse it holds by its words, "
            + "check it against CNTR's transcription of the same manuscript, and add it to the repairs.");
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
    /// The first hand's reading of a corrected place, with the correction noted on the verse the way
    /// an apparatus writes it: the first hand, a bracket, and what the corrector made of it.
    /// </summary>
    private static XElement Reading(XElement app, Verse verse)
    {
        var readings = app.Elements(Tei + "rdg").ToList();
        var first = readings.FirstOrDefault(rdg => (string?)rdg.Attribute("type") == FirstHand);
        var corrected = readings.FirstOrDefault(rdg => (string?)rdg.Attribute("type") == Correction);
        if (first is null)
        {
            return corrected ?? app;
        }

        if (corrected is not null)
        {
            var before = Phrase(first);
            var after = Phrase(corrected);
            verse.Notes.Add($"{(before.Length > 0 ? before : "—")}] {(after.Length > 0 ? after : "om.")} corr.");
        }

        return first.Descendants(Tei + "gap").Any() && corrected is not null ? corrected : first;
    }

    private static string Phrase(XElement reading) =>
        string.Join(' ', reading.Descendants(Tei + "w").Select(Letters).Where(word => word.Length > 0));

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
                if (!char.IsWhiteSpace(c))
                {
                    letters.Append(c);
                }
            }
        }

        return letters.ToString().Normalize(NormalizationForm.FormC);
    }

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
