using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <summary>
/// The New World Translation of the Holy Scriptures, 2013 revision, in English — read from the
/// publisher's own EPUB for local measurement and for nothing else.
///
/// It is copyrighted by the Watch Tower Bible and Tract Society of Pennsylvania, and the terms it is
/// downloaded under allow personal, non-commercial use and forbid putting it on a server for a
/// software application. So it is never a text of the corpus: its definition says
/// <see cref="Redistribution.Prohibited"/>, <see cref="CorpusLoader"/> refuses to write such a
/// text, and the only reader of it is a benchmark that reads this file in the process that asked.
/// Everything the corpus holds can be served and is carried by every corpus release; a text kept
/// out of the database cannot leak through an endpoint somebody forgot to filter.
///
/// The EPUB is one XHTML file per chapter, each verse opened by an empty
/// <c>&lt;span id="chapterC_verseV"&gt;</c>. Those files are written out as USFM here, in memory,
/// so the words are cut by the same reader as every other English text rather than by a second
/// tokeniser that could disagree with it about an apostrophe.
///
/// Three things are dropped and one is normalised. The footnotes and their markers, the headings
/// of the acrostic in Psalm 119 and the editorial line closing Malachi are the publisher's
/// apparatus. The stress marks the edition prints inside proper names — Abʹsa·lom — are removed,
/// because a name spelled with them matches no other text's spelling of it and the marks are a
/// reading aid rather than letters. A verse the edition leaves as a dash, where it follows a Greek
/// text without it, is left empty; Mark 16:9-20 and John 7:53-8:11 it does not number at all.
/// </summary>
internal static partial class NewWorldTextSource
{
    public const string Slug = "NWT2013";

    /// <summary>The folder under <c>Resources</c>, and the file in it, as the fetch left them.</summary>
    public const string Folder = "NewWorld2013";

    public const string FileName = "nwt_E.epub";

    public static TextDefinition Definition { get; } = new(
        Slug: Slug,
        Name: "New World Translation (2013 revision)",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "eng",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 2013,
        SourceUrl: "https://b.jw-cdn.org/apis/pub-media/GETPUBMEDIALINKS?pub=nwt&langwritten=E&fileformat=EPUB",
        RightsHolder: "Watch Tower Bible and Tract Society of Pennsylvania",
        Licence: "All rights reserved",
        LicenceUrl: "https://www.jw.org/en/terms-of-use/",
        Redistribution: Redistribution.Prohibited,
        TextualFamily: "Alexandrian")
    {
        Translators = "The New World Bible Translation Committee",
        Edition = "The 2013 revision, English, as the publisher's EPUB nwt_E",
        RightsNote =
            "Copyrighted and downloaded under jw.org's terms of use, which allow personal, "
            + "non-commercial use and forbid posting it online or uploading it to a server for a "
            + "software application. Held on the owner's machine for his own testing of the aligner "
            + "and never loaded, served or published.",
    };

    public static TextSource Read(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{path} is not there. The New World Translation is fetched by hand, from the publisher's " +
                "own download, onto the machine that measures with it; see Resources/NewWorld2013/LICENCE.md.",
                path);
        }

        using var archive = ZipFile.OpenRead(path);
        var chapters = Chapters(archive).ToList();
        var drafts = new List<BookDraft>(EnglishTextSource.Canon.Length);

        for (var ordinal = 1; ordinal <= EnglishTextSource.Canon.Length; ordinal++)
        {
            var own = chapters.Where(chapter => chapter.Book == ordinal).OrderBy(chapter => chapter.Number).ToList();
            if (own.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{path} holds no chapter of book {ordinal}. The EPUB is partial, or the publisher changed " +
                    "its layout; fetch it again before measuring with it.");
            }

            var book = UsfmReader.Read(Usfm(EnglishTextSource.Canon[ordinal - 1], own));
            drafts.Add(new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: ordinal,
                Name: BookReferences.Name(ordinal),
                Slug: BookReferences.Slug(ordinal),
                Chapters: [.. book.Chapters.Select(chapter => EnglishTextSource.Chapter(chapter, tagged: false))],
                NameNative: book.Name,
                Abbreviation: BookReferences.Abbreviation(ordinal)));
        }

        return new TextSource(Definition, drafts);
    }

    /// <summary>One chapter file: the book and chapter its navigation line names, and its body.</summary>
    internal sealed record EpubChapter(int Book, int Number, string BookName, string Body);

    /// <summary>
    /// The chapter files, by the navigation line each opens with. Its verse-list link names book
    /// and chapter as numbers — <c>bibleversenav19_3.xhtml</c> is Psalm 3 — for single-chapter books
    /// too, which have no chapter link.
    /// </summary>
    private static IEnumerable<EpubChapter> Chapters(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xhtml", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var content = reader.ReadToEnd();
            var navigation = Navigation().Match(content);
            if (!navigation.Success)
            {
                continue;
            }

            yield return new EpubChapter(
                int.Parse(navigation.Groups["book"].Value),
                int.Parse(navigation.Groups["chapter"].Value),
                WebUtility.HtmlDecode(navigation.Groups["name"].Value).Trim(),
                content[content.IndexOf("<body", StringComparison.Ordinal)..]);
        }
    }

    /// <summary>A book's chapter files as the USFM <see cref="UsfmReader"/> reads.</summary>
    internal static string Usfm(string code, IReadOnlyList<EpubChapter> chapters)
    {
        var usfm = new StringBuilder()
            .Append($"\\id {code}\n")
            .Append($"\\toc1 {chapters[0].BookName}\n");

        foreach (var chapter in chapters)
        {
            usfm.Append($"\\c {chapter.Number}\n");
            var body = Apparatus().Replace(chapter.Body, string.Empty);
            foreach (Match paragraph in Paragraph().Matches(body))
            {
                var style = paragraph.Groups["class"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (style.Any(Omitted.Contains))
                {
                    continue;
                }

                var text = VerseNumber().Replace(paragraph.Groups["content"].Value, string.Empty);
                text = VerseAnchor().Replace(text, anchor => $"\n\\v {anchor.Groups["verse"].Value} ");
                text = Tag().Replace(text, string.Empty);
                text = Plain(WebUtility.HtmlDecode(text));
                if (text.Trim().Length == 0)
                {
                    continue;
                }

                usfm.Append(style.Contains(Superscription) ? "\\d " : style.Contains(Prose) ? "\\p\n" : "\\q1\n")
                    .Append(text.TrimStart(' '))
                    .Append('\n');
            }
        }

        return usfm.ToString();
    }

    /// <summary>
    /// The text of a verse as the reader should see it: the pronunciation marks out of the names,
    /// the edition's no-break spaces as spaces, and the dash standing for an omitted verse gone.
    /// </summary>
    private static string Plain(string text)
    {
        text = text.Replace(StressMark, string.Empty).Replace(SyllableDot, string.Empty);
        text = text.Replace('\u00a0', ' ').Replace('\u202f', ' ');
        text = OmittedVerse().Replace(text, " ");
        return Spaces().Replace(text, " ");
    }

    private const string StressMark = "\u02b9";

    private const string SyllableDot = "\u00b7";

    /// <summary>The paragraph style of a psalm's superscription.</summary>
    private const string Superscription = "sw";

    /// <summary>The paragraph style of prose; the others are lines of verse.</summary>
    private const string Prose = "sb";

    /// <summary>
    /// The acrostic letters heading Psalm 119's stanzas, and the editorial line after Malachi that
    /// announces the Greek Scriptures.
    /// </summary>
    private static readonly HashSet<string> Omitted = ["ss", "sd"];

    [GeneratedRegex("""w_biblebookname"><a href="biblebooknav\.xhtml">(?<name>[^<]+)</a>.*?bibleversenav(?<book>\d+)_(?<chapter>\d+)\.xhtml""")]
    private static partial Regex Navigation();

    /// <summary>
    /// Everything in a chapter file that is not the text: the navigation line, the book's heading,
    /// the footnotes and the marks that call them.
    /// </summary>
    [GeneratedRegex("""<p class="w_navigation[^"]*">.*?</p>|<header>.*?</header>|<aside\b.*?</aside>|<a epub:type="noteref"[^>]*>.*?</a>|<span class="w_ch">.*?</span>""", RegexOptions.Singleline)]
    private static partial Regex Apparatus();

    [GeneratedRegex("""<p\b[^>]*?class="(?<class>[^"]*)"[^>]*>(?<content>.*?)</p>""", RegexOptions.Singleline)]
    private static partial Regex Paragraph();

    [GeneratedRegex("""<strong><sup>\d+</sup></strong>""")]
    private static partial Regex VerseNumber();

    [GeneratedRegex("""<span id="chapter\d+_verse(?<verse>\d+)"></span>""")]
    private static partial Regex VerseAnchor();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex("(?<=^|\\s)——(?=\\s|$)")]
    private static partial Regex OmittedVerse();

    [GeneratedRegex("[ \\t]+")]
    private static partial Regex Spaces();
}
