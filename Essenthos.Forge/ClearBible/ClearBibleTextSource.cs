using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;

namespace Essenthos.Core.ClearBible;

/// <summary>
/// A translation read from the token files Clear Bible ships beside its alignment, where nothing else
/// the corpus may take publishes the text: Biblica's Open Hausa Contemporary Bible.
///
/// The files hold every token of the text in order, each with the verse its id names, whether it is
/// punctuation and whether a space follows it, so the verses can be written out as the edition prints
/// them. What they do not hold is anything that is not a word of a verse — paragraphs, headings,
/// footnotes — and so the text loaded has none. A psalm's title the files number as a verse 0 is laid
/// at the head of verse 1, where the corpus prints one.
/// </summary>
internal static class ClearBibleTextSource
{
    public const string OpenHausa = "OHCB";

    /// <summary>The folder under <c>Resources/ClearBible</c> the token files are in.</summary>
    public const string OpenHausaFolder = "data/hau/targets/OHCB";

    public static TextDefinition Definition { get; } = new(
        Slug: OpenHausa,
        Name: "Biblica® Open Hausa Contemporary Bible",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "hau",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 2020,
        SourceUrl: "https://github.com/Clear-Bible/Alignments/releases/tag/data-latest",
        RightsHolder: "Biblica, Inc.",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Redistribution: Redistribution.ShareAlike,
        TextualFamily: "Alexandrian")
    {
        Translators = "Biblica's Hausa translation team",
        Edition = "The edition of 2020, as Clear Bible's token files of 2024 hold it",
        About =
            "A translation into present-day Hausa, the language of some eighty million people in Nigeria and "
            + "Niger, made by Biblica and published in 2020 as one of its Open texts. Its Greek is a critical "
            + "text: Acts 8:37, Matthew 17:21 and John 5:4 are not printed, and 1 John 5:7 has no heavenly "
            + "witnesses. It is read here from the token files Clear Bible published beside its alignment, "
            + "which hold the words and punctuation of every verse and no paragraphs, headings or notes. Its "
            + "New Testament is tied word by word to the Greek by BiblioNexus's team, and those ties are "
            + "loaded as the links beside it.",
        RightsNote =
            "Clear Bible's metadata says \"Public domain\" for the text and CC BY 4.0 for its own token files; "
            + "Biblica publishes its Open texts under Creative Commons Attribution-ShareAlike 4.0, as it does the "
            + "Open New Ukrainian Translation this corpus already holds, and the more restrictive statement is "
            + "the one taken. ShareAlike binds an adaptation of this text, such as its searchable form, and not "
            + "the texts it is read beside. The text is served unchanged and under Biblica's name and mark. "
            + "\"Biblica\" is a trademark registered in the United States Patent and Trademark Office by "
            + "Biblica, Inc., used with permission.",
        Citation =
            "Biblica® Open Hausa Contemporary Bible, copyright © 2020 by Biblica, Inc., CC BY-SA 4.0, in the token "
            + "files of Clear Bible's Alignments release data-latest. The original work by Biblica, Inc. is "
            + "available at open.bible.",
    };

    /// <summary>The token files of one translation, Old Testament first.</summary>
    public static TextSource Read(string clearBible) => Read(Definition, [
        Path.Combine(clearBible, OpenHausaFolder, "ot_OHCB.tsv"),
        Path.Combine(clearBible, OpenHausaFolder, "nt_OHCB.tsv"),
    ]);

    internal static TextSource Read(TextDefinition definition, IReadOnlyList<string> files)
    {
        var missing = files.Where(file => !File.Exists(file)).ToList();
        if (missing.Count > 0)
        {
            throw new FileNotFoundException(
                $"{definition.Slug} is read from Clear Bible's token files and {string.Join(", ", missing)} is not "
                + "there. Run scripts/fetch-clearbible.ps1.");
        }

        var books = new SortedDictionary<int, SortedDictionary<int, SortedDictionary<int, List<WordDraft>>>>();
        foreach (var file in files)
        {
            foreach (var (address, token) in Verses(file))
            {
                var (book, chapter, verse) = address;
                if (!books.TryGetValue(book, out var chapters))
                {
                    books[book] = chapters = [];
                }

                if (!chapters.TryGetValue(chapter, out var verses))
                {
                    chapters[chapter] = verses = [];
                }

                verses[verse] = token;
            }
        }

        var drafts = new List<BookDraft>(books.Count);
        foreach (var (ordinal, chapters) in books)
        {
            drafts.Add(new BookDraft(
                CanonicalOrdinal: ordinal,
                Position: drafts.Count + 1,
                Name: BookReferences.Name(ordinal),
                Slug: BookReferences.Slug(ordinal),
                Chapters: [.. chapters.Select(chapter => new ChapterDraft(chapter.Key, Titled(chapter.Value)))],
                Abbreviation: BookReferences.Abbreviation(ordinal)));
        }

        return new TextSource(definition, drafts);
    }

    /// <summary>
    /// A chapter's verses, with a title numbered as verse 0 laid at the head of verse 1 and the verse
    /// marked as holding one.
    /// </summary>
    private static List<VerseDraft> Titled(SortedDictionary<int, List<WordDraft>> verses)
    {
        var drafts = new List<VerseDraft>(verses.Count);
        verses.TryGetValue(0, out var title);
        foreach (var (number, words) in verses)
        {
            if (number == 0)
            {
                continue;
            }

            drafts.Add(number == 1 && title is { Count: > 0 }
                ? new VerseDraft(number, [.. title[..^1], title[^1] with { Trailer = title[^1].Trailer + " " }, .. words])
                {
                    MarksASuperscription = true,
                }
                : new VerseDraft(number, words));
        }

        return drafts;
    }

    /// <summary>
    /// Each verse of a token file as words: a token marked as punctuation hangs on the word before it,
    /// or opens the word after it where it stands first, and a space follows a token unless the file
    /// says it does not.
    /// </summary>
    internal static IEnumerable<((int Book, int Chapter, int Verse) Address, List<WordDraft> Words)> Verses(string file)
    {
        var address = (0, 0, 0);
        var words = new List<WordDraft>();
        var opening = string.Empty;

        using var reader = new StreamReader(file);
        var header = reader.ReadLine()?.Split('\t') ?? [];
        var joined = Array.IndexOf(header, "skip_space_after");

        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            if (cells.Length < 3 || !ClearBibleAlignment.Address(cells[0], out var book, out var chapter, out var verse))
            {
                continue;
            }

            if ((book, chapter, verse) != address)
            {
                if (words.Count > 0)
                {
                    yield return (address, Finished(words));
                }

                address = (book, chapter, verse);
                words = [];
                opening = string.Empty;
            }

            var text = cells[2];
            var space = joined >= 0 && cells.Length > joined && cells[joined].Trim() == "y" ? string.Empty : " ";
            var punctuation = text.All(character => !char.IsLetterOrDigit(character));
            if (!punctuation)
            {
                words.Add(new WordDraft(opening + text, space));
                opening = string.Empty;
            }
            else if (words.Count > 0)
            {
                words[^1] = words[^1] with { Trailer = words[^1].Trailer + text + space };
            }
            else
            {
                opening += text;
            }
        }

        if (words.Count > 0)
        {
            yield return (address, Finished(words));
        }
    }

    /// <summary>
    /// The file numbers a space after a verse's last token as it does after any other; a verse's
    /// trailing space belongs to the reader joining verses, not to the word.
    /// </summary>
    private static List<WordDraft> Finished(List<WordDraft> words)
    {
        words[^1] = words[^1] with { Trailer = words[^1].Trailer.TrimEnd() };
        return words;
    }
}
