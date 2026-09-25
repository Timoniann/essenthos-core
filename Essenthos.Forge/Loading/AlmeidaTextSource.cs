using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Corpus;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <summary>
/// João Ferreira de Almeida's Portuguese Bible in the Lisbon printing of 1911, from Project
/// Gutenberg's transcription of it.
///
/// The file is a book set in type, not a structured text: a verse is a paragraph opening with its
/// number, and the first verse of a chapter opens with the chapter's number instead, as the printed
/// page sets it in a large initial. What else stands between the verses is the edition's apparatus,
/// told apart by its shape:
///
/// <list type="bullet">
/// <item>a paragraph in italics, <c>_A creação do ceu e da terra._</c>, is a heading the editors set
/// over a passage, and is not loaded;</item>
/// <item>a paragraph in square brackets, <c>[Antes de Christo 4004]</c>, is the margin's date;</item>
/// <item>a paragraph of references, <c>Luc. 3.23-38.</c>, points at a parallel passage;</item>
/// <item><c>[1] Pro. 8.23.</c> after a chapter lists the cross references its verses mark with
/// <c>[1]</c>, and <c>[A] ou, estações.</c> at the end of the book is the alternative rendering its
/// verses mark with <c>[A]</c>. Both are read as the notes they are;</item>
/// <item>and a plain paragraph before the first verse of a psalm is its title.</item>
/// </list>
///
/// Inside a verse, italics are words the translator supplied, which is what the underscores of the
/// transcription stand for, and are read as supplied words.
///
/// The text is turned into USFM in memory and read by <see cref="UsfmReader"/>, so a word is divided
/// from its punctuation and a note anchored to the word before it exactly as in every other
/// translation here.
/// </summary>
internal static partial class AlmeidaTextSource
{
    public const string Slug = "ALM1911";

    public const string Folder = "Almeida1911";

    private const string FileName = "pg62383.txt";

    private const int Psalms = 19;

    private const int LastOldTestamentBook = 39;

    public static TextDefinition Definition { get; } = new(
        Slug: Slug,
        Name: "Almeida Bible (1911)",
        NameNative: "Biblia Sagrada, traduzida em portuguez por João Ferreira d'Almeida",
        Kind: TextKind.Translation,
        Language: "por",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 1681,
        SourceUrl: "https://www.gutenberg.org/ebooks/62383",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://www.gutenberg.org/ebooks/62383",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Byzantine")
    {
        Translators =
            "João Ferreira de Almeida (1628-1691), whose Old Testament was finished after his death by "
            + "Jacobus op den Akker",
        Editors = "The revisers of the Edição Revista e Corrigida, first printed in 1900",
        Edition = "The Revista e Corrigida of 1900, in the reprint the Bible depot in Lisbon made in 1911",
        EditionYear = 1911,
        About =
            "The first Bible in Portuguese. Almeida, a Portuguese convert to the Reformed church working "
            + "for the Dutch in Batavia, translated the New Testament from the Greek of the Textus Receptus, "
            + "printed at Amsterdam in 1681, and died in 1691 in the middle of Ezekiel; the Old Testament was "
            + "finished by others and printed whole in 1753. The Almeida every Portuguese reader knows today "
            + "is one of its modern revisions, all held by Bible societies. This is the Revista e Corrigida of "
            + "1900 as reprinted in Lisbon in 1911, in the spelling of its day — Christo, Sancto, Egypto. It "
            + "prints the heavenly witnesses of 1 John 5:7, Acts 8:37 and the doxology of the Lord's Prayer. "
            + "Its numbering is the English one but for five places, where it follows the Hebrew or divides a "
            + "verse its own way: Judges 5:32, 1 Samuel 20:43, 1 Kings 22:44-54, 2 Corinthians 13:13 and "
            + "3 John 1:15 are recorded at the addresses the English gives those words. The printing numbers "
            + "Mark 4:34 as 31, which is read as the misprint it is.",
        RightsNote =
            "Public domain: Almeida died in 1691 and the revisers worked in the nineteenth century. Project "
            + "Gutenberg states \"Public domain in the USA\" on the eBook's page, and its licence at the head "
            + "and foot of the file concerns its own name and trademark, which are not reproduced. The "
            + "transcription is the work of the Online Distributed Proofreading Team (Júlio Reis, abi278 and "
            + "Sergio Queiroz) from page images at Faithofgod.net. The section headings, marginal dates and "
            + "parallel passages the printing sets over the text are its editors' and are not loaded.",
        Citation =
            "A Biblia Sagrada, contendo o Velho e o Novo Testamento, traduzida em portuguez por João Ferreira "
            + "d'Almeida, edição revista e corrigida, Lisboa: Deposito das Escripturas Sagradas, 1911, as "
            + "transcribed by Project Gutenberg, eBook #62383.",
    };

    /// <summary>
    /// The title the printing sets over each book, in canonical order, with any note mark before it
    /// removed. Matched exactly, one after the other, so that a chapter heading set in capitals —
    /// <c>JEHOVAH.</c> stands alone in Jeremiah and Ezekiel — can never start a book.
    /// </summary>
    private static readonly string[] Titles =
    [
        "O PRIMEIRO LIVRO DE MOYSÉS CHAMADO GENESIS.", "O SEGUNDO LIVRO DE MOYSÉS CHAMADO EXODO.",
        "O TERCEIRO LIVRO DE MOYSÉS CHAMADO LEVITICO.", "O QUARTO LIVRO DE MOYSÉS CHAMADO NUMEROS.",
        "O QUINTO LIVRO DE MOYSÉS CHAMADO DEUTERONOMIO.", "O LIVRO DE JOSUÉ.", "O LIVRO DOS JUIZES.",
        "O LIVRO DE RUTH.", "O PRIMEIRO LIVRO DE SAMUEL.", "O SEGUNDO LIVRO DE SAMUEL.",
        "O PRIMEIRO LIVRO DOS REIS.", "O SEGUNDO LIVRO DOS REIS.", "O PRIMEIRO LIVRO DAS CHRONICAS.",
        "O SEGUNDO LIVRO DAS CHRONICAS.", "O LIVRO DE ESDRAS.", "O LIVRO DE NEHEMIAS.", "O LIVRO DE ESTHER.",
        "O LIVRO DE JOB.", "O LIVRO DOS PSALMOS.", "PROVERBIOS DE SALOMÃO.",
        "LIVRO DO ECCLESIASTES, OU PRÉGADOR.", "CANTARES DE SALOMÃO.", "ISAIAS.", "JEREMIAS.",
        "LAMENTAÇÕES DE JEREMIAS.", "EZEQUIEL.", "DANIEL.", "OSEAS.", "JOEL.", "AMÓS.", "OBADIAS.", "JONAS.",
        "MIQUEAS.", "NAHUM.", "HABACUC.", "SOFONIAS.", "AGGEO.", "ZACHARIAS.", "MALACHIAS.",
        "O SANCTO EVANGELHO SEGUNDO S. MATTHEUS.", "O SANCTO EVANGELHO SEGUNDO S. MARCOS.",
        "O SANCTO EVANGELHO SEGUNDO S. LUCAS.", "O SANCTO EVANGELHO SEGUNDO S. JOÃO.", "ACTOS DOS APOSTOLOS.",
        "EPISTOLA DE S. PAULO AOS ROMANOS.", "PRIMEIRA EPISTOLA DE S. PAULO APOSTOLO AOS CORINTHIOS.",
        "SEGUNDA EPISTOLA DE S. PAULO APOSTOLO AOS CORINTHIOS.", "EPISTOLA DE S. PAULO APOSTOLO AOS GALATAS.",
        "EPISTOLA DE S. PAULO APOSTOLO AOS EPHESIOS.", "EPISTOLA DE S. PAULO APOSTOLO AOS PHILIPPENSES.",
        "EPISTOLA DE S. PAULO APOSTOLO AOS COLOSSENSES.",
        "PRIMEIRA EPISTOLA DE S. PAULO APOSTOLO AOS THESSALONICENSES.",
        "SEGUNDA EPISTOLA DE S. PAULO APOSTOLO AOS THESSALONICENSES.",
        "PRIMEIRA EPISTOLA DE S. PAULO APOSTOLO A TIMOTHEO.", "SEGUNDA EPISTOLA DE S. PAULO APOSTOLO A TIMOTHEO.",
        "EPISTOLA DE S. PAULO APOSTOLO A TITO.", "EPISTOLA DE S. PAULO APOSTOLO A PHILEMON.",
        "EPISTOLA DE S. PAULO APOSTOLO AOS HEBREOS.", "EPISTOLA UNIVERSAL DO APOSTOLO S. THIAGO.",
        "PRIMEIRA EPISTOLA UNIVERSAL DO APOSTOLO S. PEDRO.", "SEGUNDA EPISTOLA UNIVERSAL DO APOSTOLO S. PEDRO.",
        "PRIMEIRA EPISTOLA UNIVERSAL DO APOSTOLO S. JOÃO.", "SEGUNDA EPISTOLA DO APOSTOLO S. JOÃO.",
        "TERCEIRA EPISTOLA DO APOSTOLO S. JOÃO.", "EPISTOLA UNIVERSAL DO APOSTOLO S. JUDAS.",
        "APOCALYPSE DO APOSTOLO S. JOÃO.",
    ];

    /// <summary>The heading of the list of alternative renderings at the end of the file.</summary>
    private const string NotesHeading = "NOTAS";

    /// <summary>
    /// Verse numbers the printing sets wrongly, by book, chapter and the verse before them: the one it
    /// prints and the one it means. Mark 4:34 is printed 31, between 33 and 35.
    /// </summary>
    private static readonly Dictionary<(int Book, int Chapter, int After, int Printed), int> Misprinted = new()
    {
        [(41, 4, 33, 31)] = 34,
    };

    /// <summary>
    /// Where the printing's numbering is not the English one: the verse it numbers apart, where the
    /// English prints those words, and whether they stand at the end of that verse. A move into an empty
    /// place renumbers the verse; a move into a verse joins the two. Applied in order, so the verses
    /// after a join in 1 Kings 22 move up one by one.
    /// </summary>
    private static readonly (int Book, int Chapter, int Verse, int IntoVerse)[] Renumbered =
    [
        (7, 5, 32, 31),
        (9, 20, 43, 42),
        (11, 22, 44, 43),
        .. Enumerable.Range(45, 10).Select(verse => (11, 22, verse, verse - 1)),
        (47, 13, 13, 14),
        (64, 1, 15, 14),
    ];

    [GeneratedRegex(@"^(?:[A-Z]{2,}\. )?(?<mark>\[\w+\] )?(?<number>\d+) (?<text>.+)$")]
    private static partial Regex VerseParagraph();

    /// <summary>A verse the transcription ran into the one before it, after the end of a sentence.</summary>
    [GeneratedRegex(@"(?<=[.;:!?)] )(?<number>\d+) (?=[\p{Lu}_\[])")]
    private static partial Regex RunOnVerse();

    [GeneratedRegex(@"^\[(?<number>\d+)\] (?<text>.+)$")]
    private static partial Regex CrossReference();

    [GeneratedRegex(@"^\[(?<label>[A-Z]+)\] (?<text>.+)$")]
    private static partial Regex Rendering();

    [GeneratedRegex(@"^_.*_(?:\[[A-Z]+\])?\.?$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\d+\.\d+|\bcaps?\.")]
    private static partial Regex Passages();

    /// <summary>A letter of the Hebrew alphabet the printing sets in italics before a verse of an acrostic.</summary>
    [GeneratedRegex(@"^_\p{Lu}\p{Ll}+\._ ")]
    private static partial Regex AcrosticLetter();

    /// <summary>A reading the transcribers suggest in brackets beside what the page prints: <c>o[_o_?]</c>.</summary>
    [GeneratedRegex(@"\[_[^\]]*_\?\]")]
    private static partial Regex TranscribersQuery();

    [GeneratedRegex(@"\[(?<mark>\w+)\]")]
    private static partial Regex Mark();

    [GeneratedRegex(@"_(?<words>[^_]+)_")]
    private static partial Regex Italics();

    public static TextSource Read(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The Almeida of 1911 is not at {path}. Run scripts/fetch-gutenberg-almeida.ps1.", path);
        }

        var books = Books(File.ReadAllText(path));
        var drafts = new List<BookDraft>(books.Count);
        foreach (var (ordinal, usfm) in books)
        {
            var book = UsfmReader.Read(usfm);
            var chapters = book.Chapters
                .Select(chapter => EbibleTextSource.Chapter(chapter, tagged: false))
                .Select(chapter => Renumber(ordinal, chapter))
                .ToList();

            drafts.Add(new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: ordinal,
                Name: BookReferences.Name(ordinal),
                Slug: BookReferences.Slug(ordinal),
                Chapters: chapters,
                Abbreviation: BookReferences.Abbreviation(ordinal)));
        }

        return new TextSource(Definition, drafts);
    }

    /// <summary>Each book as USFM, by canonical ordinal.</summary>
    internal static List<(int Ordinal, string Usfm)> Books(string content)
    {
        var paragraphs = Paragraphs(Body(content));
        var renderings = Renderings(paragraphs);
        var books = new List<(int, string)>(Titles.Length);

        StringBuilder? usfm = null;
        var ordinal = 0;
        var chapter = 0;
        var verse = 0;
        var betweenChapters = false;
        var skipping = false;
        string? title = null;
        var verses = new List<(int Chapter, int Verse, string Text)>();
        var references = new Dictionary<int, string>();

        void CloseChapter()
        {
            // A psalm's title is kept as verse 0 until the chapter's references have been read, since
            // they follow the chapter and a title can carry a mark.
            foreach (var (_, number, text) in verses)
            {
                usfm!.Append(number == 0 ? "\\d " : $"\\v {number.ToString(CultureInfo.InvariantCulture)} ")
                    .Append(Usfm(text, references, renderings)).Append('\n');
            }

            verses.Clear();
            references.Clear();
        }

        void CloseBook()
        {
            if (usfm is null)
            {
                return;
            }

            CloseChapter();
            books.Add((ordinal, usfm.ToString()));
            usfm = null;
        }

        foreach (var (before, text) in paragraphs)
        {
            if (text == NotesHeading)
            {
                break;
            }

            var bare = Rendering().Match(text) is { Success: true } marked && before >= 3
                ? marked.Groups["text"].Value
                : text;
            if (ordinal < Titles.Length && bare == Titles[ordinal])
            {
                CloseBook();
                ordinal++;
                usfm = new StringBuilder($"\\id {ordinal:D2}\n");
                chapter = 0;
                verse = 0;
                betweenChapters = true;
                skipping = false;
                continue;
            }

            if (usfm is null || skipping)
            {
                continue;
            }

            if (before >= 3)
            {
                // The end of the Old Testament, the title page of the New and its table of contents
                // stand between Malachi and Matthew in capitals, like a book's title.
                if (ordinal == LastOldTestamentBook && chapter > 0 && IsCapitals(text) && !VerseParagraph().IsMatch(text))
                {
                    skipping = true;
                    continue;
                }

                betweenChapters = true;
            }

            if (VerseParagraph().Match(text) is { Success: true } opening)
            {
                var number = int.Parse(opening.Groups["number"].Value, CultureInfo.InvariantCulture);
                if (betweenChapters && number == chapter + 1)
                {
                    CloseChapter();
                    chapter++;
                    verse = 1;
                    usfm.Append("\\c ").Append(chapter.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    if (title is not null)
                    {
                        verses.Add((chapter, 0, title));
                        title = null;
                    }
                }
                else if (number == verse + 1)
                {
                    verse = number;
                }
                else if (Misprinted.TryGetValue((ordinal, chapter, verse, number), out var meant))
                {
                    verse = meant;
                }
                else
                {
                    throw new InvalidDataException(
                        $"In {BookReferences.Name(ordinal)} {chapter}, a paragraph numbered {number} follows verse "
                        + $"{verse}: \"{Excerpt(text)}\". Either the transcription changed or the printing "
                        + "misnumbers a verse here; read the page and record it in Misprinted.");
                }

                betweenChapters = false;
                var body = opening.Groups["mark"].Value + opening.Groups["text"].Value;
                foreach (var (at, words) in RunOn(body, verse))
                {
                    verse = at;
                    verses.Add((chapter, at, words));
                }

                continue;
            }

            if (CrossReference().Match(text) is { Success: true } reference)
            {
                references[int.Parse(reference.Groups["number"].Value, CultureInfo.InvariantCulture)] =
                    reference.Groups["text"].Value;
                continue;
            }

            if (Heading().IsMatch(text) || (text.StartsWith('[') && text.EndsWith(']')))
            {
                continue;
            }

            if (ordinal == Psalms && betweenChapters)
            {
                title = title is null ? text : $"{title} {text}";
                continue;
            }

            if (Passages().IsMatch(text))
            {
                continue;
            }

            throw new InvalidDataException(
                $"In {BookReferences.Name(ordinal)} {chapter}:{verse} the paragraph \"{Excerpt(text)}\" is none of the "
                + "shapes this reader knows — a verse, a heading, a date, a reference or a psalm's title. Read it "
                + "before loading the text: a paragraph read as the wrong shape is words lost or words invented.");
        }

        CloseBook();
        if (books.Count != Titles.Length)
        {
            throw new InvalidDataException(
                $"The file holds {books.Count} of the {Titles.Length} books' titles in order; the next expected is "
                + $"\"{Titles[books.Count]}\". Either the transcription changed or the fetch was partial.");
        }

        return books;
    }

    /// <summary>
    /// The verses a paragraph holds: itself, and any following verse the transcription ran into it after
    /// the end of a sentence. Hosea 11:5 is the one place it did.
    /// </summary>
    private static IEnumerable<(int Verse, string Text)> RunOn(string text, int verse)
    {
        var from = 0;
        foreach (Match next in RunOnVerse().Matches(text))
        {
            if (int.Parse(next.Groups["number"].Value, CultureInfo.InvariantCulture) != verse + 1)
            {
                continue;
            }

            yield return (verse, text[from..next.Index].TrimEnd());
            verse++;
            from = next.Index + next.Length;
        }

        yield return (verse, text[from..]);
    }

    /// <summary>
    /// A verse's text as USFM: the supplied words marked as supplied, a cross reference and an
    /// alternative rendering as the notes they are, and the transcribers' bracketed queries removed in
    /// favour of what the page prints. A mark whose note the file does not carry is dropped rather than
    /// left in the words.
    /// </summary>
    private static string Usfm(string text, Dictionary<int, string> references, Dictionary<string, string> renderings)
    {
        text = AcrosticLetter().Replace(TranscribersQuery().Replace(text, string.Empty), string.Empty);
        // No space after the opening marker: italics can begin inside a word — deu-_a_ — and the reader
        // takes the marker out and leaves what follows it where it stood.
        text = Italics().Replace(text, match => $"\\add{match.Groups["words"].Value}\\add*");
        return Mark().Replace(text, match =>
        {
            var mark = match.Groups["mark"].Value;
            if (int.TryParse(mark, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return references.TryGetValue(number, out var passages)
                    ? $"\\x + \\xt {passages.Replace("_", string.Empty)}\\x*"
                    : string.Empty;
            }

            return renderings.TryGetValue(mark, out var rendering)
                ? $"\\f + \\ft {rendering.Replace("_", string.Empty)}\\f*"
                : string.Empty;
        });
    }

    /// <summary>
    /// The printing's verses moved to the addresses the English numbering gives their words, each saying
    /// what the printing numbered it.
    /// </summary>
    private static ChapterDraft Renumber(int book, ChapterDraft chapter)
    {
        var moves = Renumbered.Where(move => move.Book == book && move.Chapter == chapter.Number).ToList();
        if (moves.Count == 0)
        {
            return chapter;
        }

        var verses = chapter.Verses.ToDictionary(verse => verse.Number);
        foreach (var (_, chapterNumber, number, into) in moves)
        {
            if (!verses.Remove(number, out var moved))
            {
                throw new InvalidDataException(
                    $"{BookReferences.Name(book)} {chapterNumber}:{number} was expected in the printing and is not there. "
                    + "The transcription changed; read the passage again before moving any verse.");
            }

            var stated = new StatedNumberDraft(chapterNumber, number);
            if (!verses.TryGetValue(into, out var host))
            {
                verses[into] = moved with { Number = into, Stated = [stated] };
                continue;
            }

            verses[into] = host with
            {
                Words = [.. host.Words, .. moved.Words],
                Notes =
                [
                    .. host.Notes,
                    .. moved.Notes.Select(note => note with { AnchorWordPosition = note.AnchorWordPosition + host.Words.Count }),
                ],
                Stated = [.. host.Stated.DefaultIfEmpty(new StatedNumberDraft(chapterNumber, into)), stated],
            };
        }

        return chapter with { Verses = [.. verses.Values.OrderBy(verse => verse.Number)] };
    }

    /// <summary>The alternative renderings the file lists after the last book, by the mark the verses carry.</summary>
    private static Dictionary<string, string> Renderings(List<(int Before, string Text)> paragraphs)
    {
        var start = paragraphs.FindIndex(paragraph => paragraph.Text == NotesHeading);
        if (start < 0)
        {
            throw new InvalidDataException(
                $"The file has no \"{NotesHeading}\" section, so the alternative renderings its verses mark cannot "
                + "be read. The transcription changed.");
        }

        return paragraphs.Skip(start + 1)
            .Select(paragraph => Rendering().Match(paragraph.Text))
            .Where(match => match.Success)
            .ToDictionary(match => match.Groups["label"].Value, match => match.Groups["text"].Value, StringComparer.Ordinal);
    }

    /// <summary>What stands between Project Gutenberg's header and its licence.</summary>
    private static string Body(string content)
    {
        const string start = "*** START OF THE PROJECT GUTENBERG EBOOK";
        const string end = "*** END OF THE PROJECT GUTENBERG EBOOK";
        var from = content.IndexOf(start, StringComparison.Ordinal);
        var to = content.IndexOf(end, StringComparison.Ordinal);
        if (from < 0 || to < from)
        {
            throw new InvalidDataException(
                "The file is not a Project Gutenberg transcription: it has no START and END lines around the text.");
        }

        return content[(content.IndexOf('\n', from) + 1)..to];
    }

    /// <summary>The file's paragraphs, each with the number of blank lines before it.</summary>
    private static List<(int Before, string Text)> Paragraphs(string body)
    {
        var paragraphs = new List<(int, string)>(60_000);
        var lines = new List<string>();
        var blank = 0;
        var before = 0;
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (lines.Count > 0)
                {
                    paragraphs.Add((before, string.Join(' ', lines)));
                    lines.Clear();
                }

                blank++;
                continue;
            }

            if (lines.Count == 0)
            {
                before = blank;
            }

            lines.Add(line);
            blank = 0;
        }

        if (lines.Count > 0)
        {
            paragraphs.Add((before, string.Join(' ', lines)));
        }

        return paragraphs;
    }

    private static bool IsCapitals(string text) =>
        text.Any(char.IsUpper) && text.Where(char.IsLetter).All(char.IsUpper);

    private static string Excerpt(string text) => text.Length <= 80 ? text : text[..80] + "…";
}
