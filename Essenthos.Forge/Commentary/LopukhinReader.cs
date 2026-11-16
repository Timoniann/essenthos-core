using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Essenthos.Core.Corpus;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Commentary;

/// <summary>
/// The Explanatory Bible of Lopukhin and his successors (Saint Petersburg 1904-1913) read from the FictionBook books of the
/// Christian Classics Ethereal Library's Russian collection into comments, each over the verses it was written on.
///
/// <para>
/// **How a book is printed.** Each book opens with an introduction; then, chapter by chapter, an outline of the chapter, and
/// the Synodal's verses printed a few at a time in bold, each run followed by the comment on it. A comment's paragraph
/// opens with the verses it explains ("2–3. Евр. sekenim…"), which need not be all the verses printed above it; a paragraph
/// without them goes on with the comment before it, or, opening the comment on a run, is on the whole run. The printed
/// verses are the Synodal's text, which the corpus holds already, so they are no part of any comment; they are what places
/// the comments (below). The author's notes, printed at the end of a book, stand where the text points at them, and the
/// author's name signed at the end of a book closes its introduction.
/// </para>
///
/// <para>
/// **Where the verses stand.** The books number the verses as the Synodal does, which is not the King James's in the Psalms
/// (the Greek's numbers, the titles counted as verses), at the ends of Joshua 5, 1 Samuel 23, Job 39, Ecclesiastes 4, the Song
/// 6, Hosea 13 and Jonah 1, in Romans 14 and 16 and elsewhere. Nothing is placed by its number: each printed verse is found,
/// by its words, among the verses of the corpus's Synodal (RUSV), which the frame already places, and stands where that
/// verse stands (<see cref="SynodalPlacement"/>).
/// </para>
/// </summary>
internal static partial class LopukhinReader
{
    /// <summary>The Synodal the printed verses are found in.</summary>
    public const string Synodal = Sources.SynodalSlug;

    /// <summary>
    /// The books whose comments are held back, and why: their authors' death years are not known, so whether their parts
    /// are free outside Russia and the United States is not established (FTR-0286, a question to the owner).
    /// </summary>
    internal static readonly Dictionary<int, string> Withheld = new()
    {
        [32] = "Jonah, signed П. Тычинин, whose death year is not known",
        [53] = "2 Thessalonians, signed К. Н. Фаминский, whose death year is not known",
        [69] = "3 Ezra (2 Esdras), signed Н. Аболенский, whose death year is not known",
    };

    /// <summary>
    /// What the reading kept and left out, in words (runs of letters or digits). The books' own words, notes apart, are
    /// <see cref="Read"/> = <see cref="Printed"/> + <see cref="Titles"/> + <see cref="Markers"/> + <see cref="WithheldWords"/>
    /// + <see cref="DroppedWords"/> + (<see cref="Kept"/> − <see cref="NotesWritten"/>).
    /// </summary>
    internal sealed class Tally
    {
        /// <summary>Every word of the books' bodies but their notes.</summary>
        public int Read;

        /// <summary>The words of the comments written, notes included where they stand.</summary>
        public int Kept;

        /// <summary>The Synodal's verses, printed above the comments.</summary>
        public int Printed;

        /// <summary>The title pages, chapter titles and headings with nothing under them.</summary>
        public int Titles;

        /// <summary>The numbers marking where a note stands, which the note's own words replace.</summary>
        public int Markers;

        /// <summary>The words of the books held back (<see cref="LopukhinReader.Withheld"/>), notes apart.</summary>
        public int WithheldWords;

        /// <summary>The words of the books' notes, each counted once.</summary>
        public int Notes;

        /// <summary>The notes' words as written into the comments, each as often as it is pointed at.</summary>
        public int NotesWritten;

        /// <summary>The words of notes nothing points at, or only a book held back does.</summary>
        public int NotesUnused;

        /// <summary>Printed verses found in no verse of the Synodal.</summary>
        public List<string> Unplaced = [];

        /// <summary>Comments whose verses were placed from a printed neighbour, as their own were not printed.</summary>
        public int Inferred;

        /// <summary>Comments over verses none of which could be placed, left out, and their words (notes apart).</summary>
        public int Dropped;

        public int DroppedWords;

        /// <summary>Paragraphs opening with a number that was not taken for verses, as it falls outside the run printed above.</summary>
        public List<string> NumbersNotTaken = [];

        /// <summary>The signatures moved to their book's introduction.</summary>
        public List<string> Signatures = [];
    }

    /// <summary>The archives under the work's folder, in order.</summary>
    public static string[] Archives(string resources, CommentaryWorks.Work work)
    {
        var folder = Path.Combine(resources, CommentaryReader.Folder, work.Module);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"The commentary {work.Id} is read from {folder}, which is not there. Run scripts/fetch-commentaries.ps1, or point " +
                "Dataset:ResourcesPath at a corpus that has it.");
        }

        return [.. Directory.GetFiles(folder, "*.zip").Order(StringComparer.Ordinal)];
    }

    public static List<CommentaryRow> Read(ProductStore store, CommentaryWorks.Work work, Dictionary<string, int>? unread = null, Tally? tally = null) =>
        Read([.. Archives(store.ResourcesPath, work).Select(archive => (Path.GetFileNameWithoutExtension(archive), Load(archive)))],
            ProductText.Read(store, Synodal), work, unread, tally);

    /// <summary>The FictionBook document inside an archive, in the encoding its declaration names (two are windows-1251).</summary>
    public static XDocument Load(string archive)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var zip = ZipFile.OpenRead(archive);
        using var stream = zip.Entries.Single(e => e.Length > 0).Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        using var reader = XmlReader.Create(buffer, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        return XDocument.Load(reader);
    }

    /// <param name="documents">Each document with its archive's name, which says the book of an Old Testament archive.</param>
    internal static List<CommentaryRow> Read(IReadOnlyList<(string Name, XDocument Document)> documents, ProductText synodal,
        CommentaryWorks.Work work, Dictionary<string, int>? unread = null, Tally? tally = null)
    {
        tally ??= new Tally();
        var books = documents.SelectMany(d => Fb2Book.Read(d.Document, Fb2Book.ArchiveBook(d.Name), unread)).ToList();

        // Front matter standing alone (the introduction to Paul's letters, to the catholic letters) goes before the next book's.
        var placedBooks = new List<Fb2Book>();
        List<Fb2Block> carried = [];
        foreach (var book in books)
        {
            tally.Read += book.Words;
            tally.Titles += book.TitleWords;
            tally.Markers += book.MarkerWords;
            tally.Notes += book.NoteWords;
            if (book.Book is null)
            {
                carried.AddRange(book.Introduction);
                continue;
            }

            book.Introduction.InsertRange(0, carried);
            carried = [];
            placedBooks.Add(book);
        }

        if (carried.Count > 0)
        {
            throw new InvalidDataException("A body that prints no verse stands after the last book; nothing follows it to introduce.");
        }

        var placement = SynodalPlacement.Place(placedBooks.SelectMany(b => b.Printed.Select(p => (b.Book!.Value, p))), synodal, tally);
        var rows = new List<CommentaryRow>();
        foreach (var book in placedBooks)
        {
            var ordinal = book.Book!.Value;
            tally.Printed += book.PrintedWords;
            if (Withheld.ContainsKey(ordinal))
            {
                tally.WithheldWords += book.Words - book.TitleWords - book.PrintedWords - book.MarkerWords;
                continue;
            }

            tally.Signatures.AddRange(book.Signatures.Select(s => $"{ordinal}: {s}"));
            tally.NumbersNotTaken.AddRange(book.NumbersNotTaken.Select(n => $"{ordinal}.{n}"));
            Add(ordinal, null, null, null, null, book.Introduction);
            foreach (var chapter in book.Chapters)
            {
                if (chapter.Introduction.Count > 0)
                {
                    var (inBook, at) = placement.Chapter(ordinal, chapter.Number);
                    Add(inBook, at, null, at, null, chapter.Introduction);
                }

                foreach (var comment in chapter.Comments)
                {
                    if (placement.Span(ordinal, chapter.Number, comment.First, comment.Last, out var inferred) is not { } span)
                    {
                        tally.Dropped++;
                        tally.DroppedWords += comment.Blocks.Sum(b => b.Words - b.NoteWords);
                        continue;
                    }

                    tally.Inferred += inferred ? 1 : 0;
                    Add(span.First.Book, span.First.Chapter, span.First.Verse, span.Last.Chapter, span.Last.Verse, comment.Blocks);
                }
            }
        }

        tally.NotesUnused = books.Where(b => b.Book is null || !Withheld.ContainsKey(b.Book.Value)).Sum(b => b.UnusedNoteWords)
                            + books.Where(b => b.Book is { } n && Withheld.ContainsKey(n)).Sum(b => b.NoteWords);
        return rows;

        void Add(int book, int? firstChapter, int? firstVerse, int? lastChapter, int? lastVerse, List<Fb2Block> blocks)
        {
            var comment = new CommentBuilder();
            foreach (var block in blocks)
            {
                comment.Begin(block.Kind);
                foreach (var piece in block.Pieces)
                {
                    var marks = new[] { (piece.Note, CommentBuilder.Mark.Note), (piece.Bold, CommentBuilder.Mark.Bold), (piece.Italic, CommentBuilder.Mark.Italic), (piece.Raised, CommentBuilder.Mark.Raised) }
                        .Where(m => m.Item1).Select(m => m.Item2).ToList();
                    marks.ForEach(comment.Push);
                    comment.Cite(piece.Cites is { } cited ? placement.Reference(cited) : null);
                    comment.Text(piece.Text);
                    comment.Cite(null);
                    marks.ForEach(_ => comment.Pop());
                }

                comment.End();
            }

            if (comment.Json() is not { } body)
            {
                return;
            }

            tally.Kept += blocks.Sum(b => b.Words);
            tally.NotesWritten += blocks.Sum(b => b.NoteWords);
            rows.Add(new CommentaryRow(work.Id, book, firstChapter, firstVerse, lastChapter, lastVerse, rows.Count + 1, body, work.Source));
        }
    }
}

/// <summary>A verse a book prints, by its chapter and verse as the Synodal numbers them, and its words.</summary>
internal sealed record Fb2Verse(int Chapter, int Verse, string Text);

/// <summary>A run of text with its marks; <see cref="Cites"/> is the verse it points at, in the books' own numbering.</summary>
internal sealed record Fb2Piece(string Text, bool Bold = false, bool Italic = false, bool Raised = false, bool Note = false,
    (int Book, int Chapter, int Verse)? Cites = null);

/// <summary>A heading <c>h</c>, a paragraph <c>p</c>, a line of verse <c>l</c> or a quotation <c>q</c>.</summary>
internal sealed record Fb2Block(string Kind, List<Fb2Piece> Pieces)
{
    /// <summary>An outline line or a heading: what introduces the passage after it, not more of the comment before.</summary>
    public bool Introduces => Kind == "h" || (Kind == "l" && Pieces.All(p => p.Italic || string.IsNullOrWhiteSpace(p.Text)));

    /// <summary>The block's words, a word marked in part (a stressed letter in bold) counted once; a note's apart from the text around it.</summary>
    public int Words => Fb2Book.Count(string.Concat(Pieces.Select(p => p.Note ? " " + p.Text + " " : p.Text)));

    public int NoteWords => Pieces.Where(p => p.Note).Sum(p => Fb2Book.Count(p.Text));
}

/// <summary>A comment over verses <see cref="First"/> to <see cref="Last"/> of its chapter, as the Synodal numbers them.</summary>
internal sealed record Fb2Comment(int First, int Last, List<Fb2Block> Blocks);

internal sealed class Fb2Chapter(int number)
{
    /// <summary>The chapter as the Synodal numbers it: its title's number until its first verse says otherwise.</summary>
    public int Number { get; set; } = number;

    public bool HasVerses { get; set; }

    public List<Fb2Block> Introduction { get; } = [];

    public List<Fb2Comment> Comments { get; } = [];
}

/// <summary>
/// One book of a FictionBook document (one <c>body</c>): its introduction, its chapters and the verses it prints, with the
/// words each part holds. A body that prints no verse (the introduction to Paul's letters) has no <see cref="Book"/>.
/// </summary>
internal sealed partial class Fb2Book
{
    private static readonly XNamespace Fb = "http://www.gribuser.ru/xml/fictionbook/2.0";
    private static readonly XNamespace Link = "http://www.w3.org/1999/xlink";

    /// <summary>
    /// The books by the key their verse ids carry (<c>n29-Joel_I_1</c>: <c>29-Joel</c>): the Old Testament as the King James
    /// orders it, then the Synodal's further books; the New Testament as the Synodal orders it, numbered afresh.
    /// </summary>
    internal static readonly Dictionary<string, int> Keys = new(StringComparer.Ordinal)
    {
        ["01-Gen"] = 1, ["02-Ex"] = 2, ["03-Lev"] = 3, ["04-Num"] = 4, ["05-Deu"] = 5, ["06-Jos"] = 6, ["07-Jdg"] = 7, ["08-Rut"] = 8,
        ["09-1King"] = 9, ["10-2King"] = 10, ["11-3King"] = 11, ["12-4King"] = 12, ["13-1Par"] = 13, ["14-2Par"] = 14, ["15-1Ezd"] = 15,
        ["16-Neh"] = 16, ["17-Est"] = 17, ["18-Job"] = 18, ["19-Ps"] = 19, ["20-Prov"] = 20, ["21-Eccl"] = 21, ["22-Song"] = 22,
        ["23-Isa"] = 23, ["24-Jer"] = 24, ["25-Lam"] = 25, ["26-Ese"] = 26, ["27-Dan"] = 27, ["28-Hos"] = 28, ["29-Joel"] = 29,
        ["30-Amos"] = 30, ["31-Obad"] = 31, ["32-Jona"] = 32, ["33-Mic"] = 33, ["34-Nah"] = 34, ["35-Hab"] = 35, ["36-Zeph"] = 36,
        ["37-Agg"] = 37, ["38-Zeh"] = 38, ["39-Mal"] = 39,

        // The Synodal's 2 Ezra is the Greek's 1 Esdras, its 3 Ezra the Latin's 4 Ezra (2 Esdras).
        ["40-2Ezd"] = 68, ["41-Tob"] = 70, ["42-Jud"] = 71, ["43-Wis"] = 75, ["44-Sir"] = 72, ["46-Bar"] = 67,
        ["47-1Mak"] = 73, ["47-1Ma"] = 73, ["48-2Mak"] = 74, ["49-3Mak"] = 80, ["50-3Ezd"] = 69,
        ["01-Mf"] = 40, ["02-Mk"] = 41, ["03-Lk"] = 42, ["04-Jn"] = 43, ["05-Act"] = 44, ["06-Jak"] = 59, ["07-1Pet"] = 60,
        ["08-2Pet"] = 61, ["09-1Jn"] = 62, ["10-2Jn"] = 63, ["11-3Jn"] = 64, ["12-Jud"] = 65, ["13-Rim"] = 45, ["14-1Kor"] = 46,
        ["15-2Kor"] = 47, ["16-Gal"] = 48, ["17-Ef"] = 49, ["18-Phi"] = 50, ["19-Col"] = 51, ["20-1Fes"] = 52, ["21-2Fes"] = 53,
        ["22-1Tim"] = 54, ["23-2Tim"] = 55, ["24-Tit"] = 56, ["25-Phm"] = 57, ["26-Heb"] = 58, ["27-Ap"] = 66,
    };

    /// <summary>The Old Testament archives by their number (<c>Lopuhin_Old_Tastement_45-JerEp</c>), for a book whose verses carry no id.</summary>
    private static readonly Dictionary<int, int> OldTestamentArchives = new()
    {
        [40] = 68, [41] = 70, [42] = 71, [43] = 75, [44] = 72, [45] = 76, [46] = 67, [47] = 73, [48] = 74, [49] = 80, [50] = 69,
    };

    public int? Book { get; private set; }

    public List<Fb2Block> Introduction { get; } = [];

    public List<Fb2Chapter> Chapters { get; } = [];

    public List<Fb2Verse> Printed { get; } = [];

    public List<string> Signatures { get; } = [];

    public List<string> NumbersNotTaken { get; } = [];

    /// <summary>The body's words, its notes apart.</summary>
    public int Words { get; private set; }

    public int TitleWords { get; private set; }

    public int PrintedWords { get; private set; }

    public int MarkerWords { get; private set; }

    /// <summary>The words of the notes this body holds, and of those nothing points at.</summary>
    public int NoteWords { get; private set; }

    public int UnusedNoteWords { get; private set; }

    /// <summary>Words as the tally counts them: runs of letters or digits.</summary>
    public static int Count(string text) => WordRun().Count(text);

    /// <summary>The book an Old Testament archive holds, by its number; null for the others.</summary>
    public static int? ArchiveBook(string name)
    {
        var match = OldTestamentArchive().Match(name);
        if (!match.Success)
        {
            return null;
        }

        var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return number <= 39 ? number : OldTestamentArchives.GetValueOrDefault(number);
    }

    /// <summary>The books of a document, one a body.</summary>
    public static List<Fb2Book> Read(XDocument document, int? archiveBook, Dictionary<string, int>? unread)
    {
        var root = document.Root!;
        // A note is a paragraph of a notes section with an id, and the paragraphs without one that follow it.
        var notes = new Dictionary<string, (string Text, XElement Body)>(StringComparer.Ordinal);
        foreach (var section in root.Descendants(Fb + "section").Where(IsNotes))
        {
            string? id = null;
            foreach (var paragraph in section.Elements(Fb + "p"))
            {
                if (paragraph.Attribute("id")?.Value is { } own)
                {
                    id = own;
                    notes[id] = (Plain(paragraph), section.Ancestors(Fb + "body").First());
                }
                else if (id is not null)
                {
                    notes[id] = (notes[id].Text + " " + Plain(paragraph), notes[id].Body);
                }
            }
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        var bodies = root.Elements(Fb + "body").ToList();
        var books = new List<Fb2Book>();
        foreach (var body in bodies)
        {
            var book = new Fb2Book();
            new Walker(book, notes, used, unread).Body(body);
            if (book.Book is null && book.Printed.Count > 0)
            {
                book.Book = archiveBook ?? throw new InvalidDataException(
                    $"The body \"{body.Attribute("name")?.Value}\" prints verses that carry no id, and its archive names no book.");
            }

            books.Add(book);
        }

        foreach (var (id, note) in notes)
        {
            var holder = books[bodies.IndexOf(note.Body)];
            var words = Count(NoteText(note.Text));
            holder.NoteWords += words;
            if (!used.Contains(id))
            {
                holder.UnusedNoteWords += words;
            }
        }

        return books;
    }

    /// <summary>A section of notes: titled so, or holding nothing but paragraphs with ids (Acts' second, untitled).</summary>
    private static bool IsNotes(XElement section) =>
        section.Element(Fb + "title") is { } title
            ? NotesTitle().IsMatch(Plain(title))
            : section.Elements(Fb + "p").Any() && section.Elements(Fb + "p").All(p => p.Attribute("id") is not null)
              && !section.Elements().Any(e => e.Name != Fb + "p" && e.Name != Fb + "empty-line");

    /// <summary>A note's words without the number it is printed under.</summary>
    private static string NoteText(string text) => NoteMarker().Match(text) is { Success: true } marker ? text[marker.Length..] : text;

    /// <summary>An element's text, its blocks (paragraphs, lines, a title's lines) apart and its inline marks run together.</summary>
    internal static string Plain(XElement element)
    {
        var text = new StringBuilder();
        Append(element);
        return Whitespace().Replace(text.ToString(), " ").Trim();

        void Append(XElement at)
        {
            foreach (var node in at.Nodes())
            {
                if (node is XText run)
                {
                    text.Append(run.Value);
                }
                else if (node is XElement child)
                {
                    var block = child.Name.LocalName is "p" or "v" or "title" or "subtitle" or "stanza" or "poem" or "cite" or "section"
                        or "epigraph" or "text-author" or "empty-line";
                    text.Append(block ? " " : string.Empty);
                    Append(child);
                    text.Append(block ? " " : string.Empty);
                }
            }
        }
    }

    /// <summary>
    /// The verse an id names: <c>n29-Joel_I_1</c> (a chapter in Roman figures), <c>n19-Ps_50_1</c> (in Arabic), <c>n25-Phm_1</c>
    /// (a book of one chapter).
    /// </summary>
    internal static (int Book, int Chapter, int Verse)? VerseOf(string id)
    {
        var match = VerseId().Match(id);
        if (!match.Success || !Keys.TryGetValue(match.Groups[1].Value, out var book))
        {
            return null;
        }

        var chapter = match.Groups[2].Success ? Number(match.Groups[2].Value) : 1;
        return chapter is { } c ? (book, c, int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)) : null;
    }

    /// <summary>A chapter number in Arabic or Roman figures, the Cyrillic letters a scan reads for Roman ones taken as them.</summary>
    internal static int? Number(string figures)
    {
        var latin = figures.Trim().Replace('Х', 'X').Replace('І', 'I').Replace('С', 'C');
        if (int.TryParse(latin, NumberStyles.None, CultureInfo.InvariantCulture, out var arabic))
        {
            return arabic;
        }

        var total = 0;
        for (var at = 0; at < latin.Length; at++)
        {
            var value = Roman(latin[at]);
            if (value == 0)
            {
                return null;
            }

            total += at + 1 < latin.Length && Roman(latin[at + 1]) > value ? -value : value;
        }

        return total > 0 ? total : null;

        static int Roman(char figure) => figure switch
        {
            'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, 'C' => 100, _ => 0,
        };
    }

    /// <summary>The verses a comment's opening names ("2–3.", "15 и 16.", "45, 46."): the first and the last.</summary>
    internal static (int First, int Last)? Opening(string text)
    {
        var match = OpeningVerses().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var numbers = Digits().Matches(match.Groups[1].Value).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        return (numbers.Min(), numbers.Max());
    }

    /// <summary>
    /// Walks a body in order: the introduction until the first chapter, then each chapter's outline, its printed verses and
    /// the comments on them. Section titles other than a chapter's head what follows them; a heading nothing follows is a
    /// title (the book's text's, "КНИГА ПРОРОКА ИОИЛЯ") and is left out.
    /// </summary>
    private sealed class Walker(Fb2Book book, Dictionary<string, (string Text, XElement Body)> notes, HashSet<string> used, Dictionary<string, int>? unread)
    {
        private Fb2Chapter? chapter;
        private readonly List<Fb2Verse> group = [];
        private bool groupOpen;
        private Fb2Comment? comment;
        private readonly List<Fb2Block> pending = [];
        private XElement? lastPrinted;
        private bool appendix;

        public void Body(XElement body)
        {
            lastPrinted = body.Descendants().LastOrDefault(e => !e.Ancestors(Fb + "section").Any(IsNotes) && PrintedVerse(e) is not null);
            foreach (var element in body.Elements())
            {
                Element(element);
            }

            DropHeadings();
            Signature();
            Flush();
        }

        private void Element(XElement element)
        {
            if (element.Name == Fb + "title")
            {
                Title(element, element.Parent!.Name == Fb + "body");
            }
            else if (element.Name == Fb + "section")
            {
                if (IsNotes(element))
                {
                    return;
                }

                if (!appendix && chapter is not null && lastPrinted is not null && element.IsAfter(lastPrinted))
                {
                    // Articles printed after the book's last verse (Daniel's on Antiochus IV) are the book's, not its last comment's.
                    appendix = true;
                    DropHeadings();
                    Signature();
                    Close();
                    chapter = null;
                }

                foreach (var child in element.Elements())
                {
                    Element(child);
                }
            }
            else
            {
                Content(element);
            }
        }

        private void Title(XElement title, bool titlePage)
        {
            var text = Plain(title);
            var markers = Markers(title);
            var words = Count(text) - markers;
            book.MarkerWords += markers;
            book.Words += words + markers;
            if (titlePage)
            {
                book.TitleWords += words;
                return;
            }

            if (ChapterTitle().Match(NoteMarkers().Replace(text, string.Empty).Trim()) is { Success: true } match
                && Number(match.Groups[2].Value) is { } number)
            {
                DropHeadings();
                Close();
                chapter = new Fb2Chapter(number);
                book.Chapters.Add(chapter);
                if (markers > 0)
                {
                    // A note on the chapter ("Глава 13 [212]", on Susanna) stands with its title at the head of the chapter.
                    chapter.Introduction.Add(Block("h", title.Elements(Fb + "p").Any() ? title.Elements(Fb + "p") : [title]));
                }
                else
                {
                    book.TitleWords += words;
                }

                return;
            }

            pending.Add(Block("h", title.Elements(Fb + "p").Any() ? title.Elements(Fb + "p") : [title]));
        }

        private void Content(XElement element)
        {
            switch (element.Name.LocalName)
            {
                case "empty-line":
                case "image":
                    return;
                case "poem":
                case "stanza":
                    foreach (var child in element.Elements())
                    {
                        if (child.Name == Fb + "title")
                        {
                            Title(child, false);
                        }
                        else
                        {
                            Content(child);
                        }
                    }

                    return;
                case "v":
                case "p":
                    if (PrintedVerse(element) is { } verse)
                    {
                        Print(verse, element);
                        return;
                    }

                    Words(element);
                    if (element.Name == Fb + "p")
                    {
                        Paragraph(element);
                    }
                    else
                    {
                        Add(Block("l", [element]));
                    }

                    return;
                case "cite":
                    if (element.Elements().Any(e => PrintedVerse(e) is not null))
                    {
                        foreach (var child in element.Elements())
                        {
                            Content(child);
                        }

                        return;
                    }

                    Words(element);
                    foreach (var child in element.Elements().Where(e => e.Name != Fb + "empty-line"))
                    {
                        Add(Block("q", [child]));
                    }

                    return;
                case "subtitle":
                    Words(element);
                    pending.Add(Block("h", [element]));
                    return;
                case "epigraph":
                    Words(element);
                    foreach (var child in element.Elements().Where(e => e.Name != Fb + "empty-line"))
                    {
                        Add(Block("q", [child]));
                    }

                    return;
                default:
                    Unread(element.Name.LocalName);
                    Words(element);
                    Add(Block("p", [element]));
                    return;
            }
        }

        private void Words(XElement element)
        {
            book.MarkerWords += Markers(element);
            book.Words += Count(Plain(element));
        }

        /// <summary>The words of the note markers inside an element, counted apart: the note's words stand there instead.</summary>
        private int Markers(XElement element) =>
            element.Descendants(Fb + "a")
                .Where(a => notes.ContainsKey(a.Attribute(Link + "href")?.Value.TrimStart('#') ?? string.Empty))
                .Sum(a => Count(a.Value));

        /// <summary>A verse the book prints: an element with a verse's id, or a line holding nothing but a quotation, numbered.</summary>
        private (int Chapter, int Verse, string Key)? PrintedVerse(XElement element)
        {
            if (element.Attribute("id")?.Value is { } id)
            {
                return VerseOf(id) is { } verse ? (verse.Chapter, verse.Verse, VerseId().Match(id).Groups[1].Value) : null;
            }

            if (element.Name == Fb + "v" && element.Elements().Count() == 1 && element.Elements().Single().Name == Fb + "cite"
                && LeadingNumber().Match(Plain(element)) is { Success: true } number)
            {
                return (chapter?.Number ?? 1, int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture), string.Empty);
            }

            return null;
        }

        private void Print((int Chapter, int Verse, string Key) verse, XElement element)
        {
            if (verse.Key.Length > 0 && Keys.TryGetValue(verse.Key, out var ordinal))
            {
                book.Book ??= ordinal;
            }

            if (chapter is null || chapter.Number != verse.Chapter)
            {
                if (chapter is { HasVerses: false } && book.Chapters.All(c => c.Number != verse.Chapter))
                {
                    // The title's number was misread; the verses carry the chapter's.
                    chapter.Number = verse.Chapter;
                }
                else
                {
                    // A chapter the book does not title (a psalm printed under another's title).
                    DropHeadings();
                    Close();
                    chapter = book.Chapters.Find(c => c.Number == verse.Chapter) ?? new Fb2Chapter(verse.Chapter);
                    if (!book.Chapters.Contains(chapter))
                    {
                        book.Chapters.Add(chapter);
                    }
                }
            }

            chapter.HasVerses = true;
            if (!groupOpen)
            {
                group.Clear();
                groupOpen = true;
                comment = null;
            }

            var text = Plain(element);
            var words = Count(text);
            book.Words += words;
            book.PrintedWords += words;
            var printed = new Fb2Verse(verse.Chapter, verse.Verse, text);
            group.Add(printed);
            book.Printed.Add(printed);
        }

        private void Paragraph(XElement element)
        {
            var block = Block("p", [element]);
            if (chapter is null || group.Count == 0)
            {
                Add(block);
                return;
            }

            groupOpen = false;
            var plain = Plain(element);
            var first = group.Min(v => v.Verse);
            var last = group.Max(v => v.Verse);
            if (Opening(plain) is { } opening)
            {
                // The verses it opens with touch the run printed above, or begin right after it: a comment on a whole passage
                // ("13–18.") follows the passage's first verse, and one reaching back ("1–5.") the passage's last.
                var touches = opening.First <= last + 1 && (opening.First >= first || opening.Last >= first);
                if (touches && opening.First >= 1 && opening.Last - opening.First <= 80)
                {
                    Open(opening.First, opening.Last);
                    comment!.Blocks.Add(block);
                    return;
                }

                book.NumbersNotTaken.Add($"{chapter.Number}:{first}-{last} «{plain[..Math.Min(70, plain.Length)]}»");
            }

            if (comment is null)
            {
                Open(first, last);
            }

            comment!.Blocks.AddRange(pending);
            pending.Clear();
            comment.Blocks.Add(block);
        }

        private void Open(int first, int last)
        {
            comment = new Fb2Comment(first, last, [.. pending]);
            pending.Clear();
            chapter!.Comments.Add(comment);
        }

        /// <summary>A block that opens no comment: the introduction's, the chapter outline's, or more of the comment.</summary>
        private void Add(Fb2Block block)
        {
            if (chapter is null)
            {
                book.Introduction.AddRange(pending);
                pending.Clear();
                book.Introduction.Add(block);
            }
            else if (group.Count == 0)
            {
                chapter.Introduction.AddRange(pending);
                pending.Clear();
                chapter.Introduction.Add(block);
            }
            else if (comment is not null && !block.Introduces && pending.Count == 0)
            {
                comment.Blocks.Add(block);
            }
            else
            {
                // An outline line or a heading between runs introduces the passage that follows.
                pending.Add(block);
            }
        }

        /// <summary>Ends the chapter being read; what waited for a passage that never came closes the chapter's last comment.</summary>
        private void Close()
        {
            Flush();
            group.Clear();
            groupOpen = false;
            comment = null;
        }

        private void Flush()
        {
            if (pending.Count == 0)
            {
                return;
            }

            var target = comment?.Blocks ?? (chapter is not null && group.Count == 0 ? chapter.Introduction : chapter is null ? book.Introduction : null);
            if (target is null)
            {
                // Printed verses with no comment yet: what waits is the start of a comment on them.
                if (chapter is not null && group.Count > 0)
                {
                    Open(group.Min(v => v.Verse), group.Max(v => v.Verse));
                }

                return;
            }

            target.AddRange(pending);
            pending.Clear();
        }

        /// <summary>Headings waiting at the end of what they could head are titles, left out.</summary>
        private void DropHeadings()
        {
            while (pending.Count > 0 && pending[^1].Kind == "h")
            {
                book.TitleWords += pending[^1].Pieces.Where(p => !p.Note).Sum(p => Count(p.Text));
                pending.RemoveAt(pending.Count - 1);
            }
        }

        /// <summary>
        /// The author's name at the end of a book ("Н. П. Розанов", "Магистр богословия, священник А. В. Петровский") is the
        /// book's, not the last comment's: it moves to the end of the introduction.
        /// </summary>
        private void Signature()
        {
            if (comment is null || chapter is null)
            {
                return;
            }

            // The name, then up to two lines above it of rank and degree ("Магистр богословия", "Профессор Московской Духовной Академии").
            if (comment.Blocks is not [.., { Kind: "p" } last] || Text(last).Length > 60 || !SignatureName().IsMatch(Text(last)))
            {
                return;
            }

            var lines = 1;
            for (var at = comment.Blocks.Count - 2; at >= 0 && lines < 3; at--)
            {
                var text = Text(comment.Blocks[at]);
                if (comment.Blocks[at].Kind != "p" || text.Length > 60 || !SignatureRank().IsMatch(text))
                {
                    break;
                }

                lines++;
            }

            var signature = comment.Blocks.GetRange(comment.Blocks.Count - lines, lines);
            comment.Blocks.RemoveRange(comment.Blocks.Count - lines, lines);
            book.Introduction.AddRange(signature);
            book.Signatures.Add(string.Join(" / ", signature.Select(Text)));
            if (comment.Blocks.Count == 0)
            {
                chapter.Comments.Remove(comment);
                comment = null;
            }

            static string Text(Fb2Block block) => string.Concat(block.Pieces.Select(p => p.Text)).Trim();
        }

        private void Unread(string name)
        {
            if (unread is not null)
            {
                unread[name] = unread.GetValueOrDefault(name) + 1;
            }
        }

        private Fb2Block Block(string kind, IEnumerable<XElement> elements)
        {
            var pieces = new List<Fb2Piece>();
            foreach (var element in elements)
            {
                if (pieces.Count > 0)
                {
                    pieces.Add(new Fb2Piece(" "));
                }

                Inline(element, pieces, false, false, false);
            }

            Brackets(pieces);
            return new Fb2Block(kind, pieces);
        }

        private void Inline(XElement element, List<Fb2Piece> pieces, bool bold, bool italic, bool raised)
        {
            foreach (var node in element.Nodes())
            {
                if (node is XText text)
                {
                    pieces.Add(new Fb2Piece(text.Value, bold, italic, raised));
                    continue;
                }

                if (node is not XElement child)
                {
                    continue;
                }

                switch (child.Name.LocalName)
                {
                    case "strong":
                        Inline(child, pieces, true, italic, raised);
                        break;
                    case "emphasis":
                        Inline(child, pieces, bold, true, raised);
                        break;
                    case "sup":
                        Inline(child, pieces, bold, italic, true);
                        break;
                    case "a":
                        Anchor(child, pieces, bold, italic, raised);
                        break;
                    case "empty-line":
                        break;
                    case "p":
                    case "v":
                    case "cite":
                    case "stanza":
                    case "poem":
                    case "text-author":
                        pieces.Add(new Fb2Piece(" "));
                        Inline(child, pieces, bold, italic, raised);
                        break;
                    default:
                        Unread(child.Name.LocalName);
                        Inline(child, pieces, bold, italic, raised);
                        break;
                }
            }
        }

        /// <summary>A link: to a note, whose words stand here; to a verse, which it cites; to anything else, its text.</summary>
        private void Anchor(XElement anchor, List<Fb2Piece> pieces, bool bold, bool italic, bool raised)
        {
            var target = anchor.Attribute(Link + "href")?.Value.TrimStart('#') ?? string.Empty;
            if (notes.TryGetValue(target, out var note))
            {
                used.Add(target);
                pieces.Add(new Fb2Piece(NoteText(note.Text), Note: true));
                return;
            }

            pieces.Add(new Fb2Piece(anchor.Value, bold, italic, raised, Cites: VerseOf(target)));
        }

        /// <summary>The brackets a note's marker was printed in, "[31]", go with the marker.</summary>
        private static void Brackets(List<Fb2Piece> pieces)
        {
            for (var at = 1; at + 1 < pieces.Count; at++)
            {
                if (!pieces[at].Note)
                {
                    continue;
                }

                var before = pieces[at - 1].Text;
                var after = pieces[at + 1].Text;
                if (before.TrimEnd().EndsWith('[') && after.TrimStart().StartsWith(']'))
                {
                    pieces[at - 1] = pieces[at - 1] with { Text = before[..before.LastIndexOf('[')] };
                    pieces[at + 1] = pieces[at + 1] with { Text = after[(after.IndexOf(']') + 1)..] };
                }
            }
        }
    }

    [GeneratedRegex(@"^n(\d\d-[A-Za-z0-9]+)_(?:([IVXLCХІ]+|\d+)_)?(\d+)$")]
    private static partial Regex VerseId();

    [GeneratedRegex(@"_Tastement_(\d\d)-")]
    private static partial Regex OldTestamentArchive();

    [GeneratedRegex(@"^(глава|псалом)\s+([IVXLCХІ]+|\d+)(?:\s*[–—-]\s*\d+)?\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterTitle();

    [GeneratedRegex(@"^примечани[ея]$", RegexOptions.IgnoreCase)]
    private static partial Regex NotesTitle();

    [GeneratedRegex(@"^(\d+(?:\s*[–—-]\s*\d+)?(?:\s*(?:,|и)\s*\d+(?:\s*[–—-]\s*\d+)?)*)\s*\.(?=\s|$)")]
    private static partial Regex OpeningVerses();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"^(\d+)\.\s")]
    private static partial Regex LeadingNumber();

    [GeneratedRegex(@"^\d+\.\s*")]
    private static partial Regex NoteMarker();

    [GeneratedRegex(@"\[\d+\*?\]")]
    private static partial Regex NoteMarkers();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRun();

    /// <summary>A line of rank and degree above a signature's name.</summary>
    [GeneratedRegex(@"^(?:(?:магистр|доктор|профессор|священник|протоиерей|богословия|московской|киевской|петербургской|казанской|духовной|академии|акадении),?\s*)+$", RegexOptions.IgnoreCase)]
    private static partial Regex SignatureRank();

    /// <summary>
    /// A signature's name, alone on its line or after a rank: initials and a surname ("Н. П. Розанов", "Магистр богословия,
    /// В. П. Рыбинский"), a full name ("Протоиерей Николай Орлов", "Василий Никанорович Мышцын"), or a bishop's.
    /// </summary>
    [GeneratedRegex(@"^(?:[А-ЯЁа-яё]+,?\s+){0,4}(?:[А-ЯЁ]\.\s?){1,2}[А-ЯЁ][а-яё]+\.?$|^(?:Протоиерей\s+)?[А-ЯЁ][а-яё]+\s[А-ЯЁ][а-яё]+(?:\s[А-ЯЁ][а-яё]+)?\.?$|^Епископ [А-ЯЁ][а-яё]+\.?$")]
    private static partial Regex SignatureName();
}
