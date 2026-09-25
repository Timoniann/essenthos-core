using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Corpus;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <summary>
/// The unfoldingWord Literal Text, read from the same aligned USFM its links are drawn from.
///
/// Every English word of the file stands inside the milestone that names the Hebrew or Greek word it
/// renders, one word to a line: <c>\zaln-s |x-strong="H0430" …\*\w God|x-occurrence="1" …\w*\zaln-e\*</c>.
/// The alignment is the interlinear loader's to read. For the text, the milestones come out and each
/// word is left where it stood, so what reaches <see cref="UsfmReader"/> is an ordinary USFM book with
/// its verses, paragraphs, psalm titles and footnotes.
/// </summary>
internal static partial class UnfoldingWordTextSource
{
    public const string Slug = "ULT";

    /// <summary>The folder under <c>Resources/Door43</c> the release is fetched into.</summary>
    public const string Folder = "en_ult";

    public static TextDefinition Definition { get; } = new(
        Slug: Slug,
        Name: "unfoldingWord® Literal Text",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "eng",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 2022,
        SourceUrl: "https://git.door43.org/unfoldingWord/en_ult/src/tag/v90",
        RightsHolder: "unfoldingWord",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Redistribution: Redistribution.ShareAlike,
        TextualFamily: "Alexandrian")
    {
        Translators =
            "unfoldingWord's translation team and the Door43 World Missions Community, revising the American "
            + "Standard Version of 1901",
        Edition = "Release 90 of 2026-08-17: the 56 books unfoldingWord has finished checking",
        EditionYear = 2026,
        About =
            "An open-licensed revision of the American Standard Version made to show a translator the form of "
            + "the Hebrew and Greek: it keeps their word order and structure wherever English will bear it. "
            + "Every one of its words is tied by hand to the word of unfoldingWord's own Hebrew Bible or Greek "
            + "New Testament it renders, and those ties are loaded as the links beside it. The release holds "
            + "56 books; Numbers, 1 and 2 Chronicles, Ecclesiastes, Isaiah, Jeremiah, Ezekiel, Daniel, Amos "
            + "and Zechariah are still being checked and are not in it. Its Greek is a critical text: the "
            + "heavenly witnesses of 1 John 5:7 are absent.",
        RightsNote =
            "Copyright 2022 by unfoldingWord, under Creative Commons Attribution-ShareAlike 4.0, stated in the "
            + "release's LICENSE.md and its manifest. The text is served unchanged and keeps unfoldingWord's name "
            + "and mark, as its terms ask of an unmodified copy; ShareAlike binds an adaptation of it, such as its "
            + "searchable form, and not the texts it is read beside. The original work by unfoldingWord is "
            + "available from unfoldingword.org/ult. The section headings of the release are not loaded.",
        Citation =
            "unfoldingWord® Literal Text, release 90, copyright 2022 by unfoldingWord, CC BY-SA 4.0, "
            + "git.door43.org/unfoldingWord/en_ult.",
    };

    /// <summary>An alignment milestone, opening or closing, or a translator's section mark.</summary>
    [GeneratedRegex(@"\\zaln-s\s*\|[^\\]*\\\*|\\zaln-e\\\*|\\ts\\\*")]
    private static partial Regex Milestone();

    /// <summary>A word with the occurrence attributes the alignment counts it by.</summary>
    [GeneratedRegex(@"\\w (?<word>[^|\\]*)\|[^\\]*\\w\*")]
    private static partial Regex AlignedWord();

    public static TextSource Read(string folder)
    {
        var books = Directory.GetFiles(folder, "*.usfm")
            .Select(path => UsfmReader.Read(Unaligned(File.ReadAllText(path)), editorialHeadings: true))
            .Select(book => (Ordinal: BookReferences.ResolveOrdinal(book.Book) ?? throw new InvalidDataException(
                $"The ULT file for \"{book.Book}\" names no book this corpus knows."), Book: book))
            .OrderBy(book => book.Ordinal)
            .ToList();

        if (books.Count == 0)
        {
            throw new DirectoryNotFoundException(
                $"No ULT book is under {folder}. Run scripts/fetch-door43-ult.ps1.");
        }

        return new TextSource(Definition, [.. books.Select((book, index) => new BookDraft(
            CanonicalOrdinal: book.Ordinal,
            Position: index + 1,
            Name: BookReferences.Name(book.Ordinal),
            Slug: BookReferences.Slug(book.Ordinal),
            Chapters: [.. book.Book.Chapters.Select(chapter => EbibleTextSource.Chapter(chapter, tagged: false))],
            Abbreviation: BookReferences.Abbreviation(book.Ordinal)))]);
    }

    /// <summary>A verse or a psalm's title opening inside a line, after a line of poetry has opened.</summary>
    [GeneratedRegex(@"(?<=\S)[ \t]*(?=\\(?:v|d) )")]
    private static partial Regex InsideALine();

    /// <summary>
    /// A footnote, a Selah or a supplied word left at the head of a line once the milestone before it
    /// is gone.
    /// </summary>
    [GeneratedRegex(@"(?:\r?\n[ \t]*)+(?=\\(?:f|qs\*?)\s|\\add)")]
    private static partial Regex OpeningALine();

    /// <summary>
    /// The book with its alignment taken out: no milestones, and each word bare. A line that held one
    /// aligned word now holds the word, which the reader takes as the verse running on; a verse or a
    /// title the release opens after <c>\q1</c> on the same line is given a line of its own, and a
    /// footnote or a Selah that now opens a line goes back to the words it stands among.
    /// </summary>
    internal static string Unaligned(string content)
    {
        var bare = AlignedWord().Replace(Milestone().Replace(content, string.Empty), match => match.Groups["word"].Value);
        var supplied = Braces().Replace(Supplied().Replace(bare, match => $"\\add{match.Groups["words"].Value}\\add*"), string.Empty);
        return OpeningALine().Replace(InsideALine().Replace(supplied, "\n"), " ");
    }

    /// <summary>
    /// Words the translators supplied, which the release sets in braces: <c>{was}</c>. Read as supplied
    /// where the braces close before any marker; a brace left over is dropped, since it is not a word.
    /// </summary>
    [GeneratedRegex(@"\{(?<words>[^{}\\]*)\}")]
    private static partial Regex Supplied();

    [GeneratedRegex(@"[{}]")]
    private static partial Regex Braces();
}
