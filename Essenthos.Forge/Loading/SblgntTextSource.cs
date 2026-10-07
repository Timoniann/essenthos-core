using System.Text;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading;

/// <summary>
/// The SBL Greek New Testament, Michael W. Holmes's edition of 2010, as the Society of Biblical
/// Literature and Logos publish its text: one file per book, a title line and then one verse per
/// line, <c>Matt 1:1</c>, a tab and the verse.
///
/// <para>
/// The text carries the signs of its apparatus — <c>⸀ ⸁ ⸂ ⸃ ⸄ ⸅</c>, each pointing to a note in
/// a file that is not read — and they are not letters, so they are left out. The edition's own
/// brackets are kept as what they are: a word inside <c>⟦ ⟧</c> stands in a passage Holmes prints
/// and judges no part of the original — the endings of Mark, the woman taken in adultery — and a
/// word inside <c>[ ]</c> is one he doubts. A bracket opened in one verse and closed many verses
/// later marks every word between, as Westcott and Hort's are read.
/// </para>
///
/// <para>
/// Only the text is taken. MorphGNT's parsing of the same edition is ShareAlike and lies over
/// Nestle's words already; nothing of it is read here, so this text has no lemma, no parse and no
/// Strong number, and is joined to the other Greek editions by the letters both print.
/// </para>
/// </summary>
internal static class SblgntTextSource
{
    public const string Slug = Sources.SblgntSlug;

    public const string Folder = "SBLGNT";

    /// <summary>The commit of the Faithlife/SBLGNT repository the files are read at.</summary>
    public const string Commit = "c4d241a9c1c479a55b989ba35a4976c1d0b8052c";

    /// <summary>Each book's file, in the order of the canon from Matthew.</summary>
    public static readonly string[] Files =
    [
        "Matt", "Mark", "Luke", "John", "Acts", "Rom", "1Cor", "2Cor", "Gal", "Eph", "Phil", "Col", "1Thess",
        "2Thess", "1Tim", "2Tim", "Titus", "Phlm", "Heb", "Jas", "1Pet", "2Pet", "1John", "2John", "3John",
        "Jude", "Rev",
    ];

    private const int Matthew = 40;

    /// <summary>The signs that send a reader to the apparatus, with the number a repeated one carries.</summary>
    private const string ApparatusSigns = "⸀⸁⸂⸃⸄⸅";

    private const string Rejected = "rejected";

    private const string Doubtful = "doubtful";

    public static readonly TextDefinition Definition = new(
        Slug: Slug,
        Name: "SBL Greek New Testament",
        NameNative: "Η ΚΑΙΝΗ ΔΙΑΘΗΚΗ",
        Kind: TextKind.CriticalEdition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 2010,
        SourceUrl: $"https://github.com/Faithlife/SBLGNT/tree/{Commit}/data/sblgnt/text",
        RightsHolder: "The Society of Biblical Literature and Logos Bible Software",
        Licence: "CC-BY-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by/4.0/",
        Redistribution: Redistribution.PermittedWithAttribution,
        TextualFamily: "Alexandrian")
    {
        Editors = "Michael W. Holmes",
        Edition = "An independent eclectic text, version 1.2 (2023), which adds John 7:53–8:11; the text "
                  + "without its apparatus",
        About = "A critical text of the Greek New Testament edited by Michael W. Holmes and published by the "
                + "Society of Biblical Literature with Logos Bible Software in 2010. It is not a revision of the "
                + "Nestle line: Holmes compared four editions — Westcott and Hort, Tregelles, Robinson and "
                + "Pierpont's Byzantine text, and the Greek behind the New International Version — and decided "
                + "between them place by place, reaching a text that, in the edition's own words, differs from "
                + "the standard text in more than 540 variation units. Read beside Nestle 1904, it shows where "
                + "two editors given the same manuscripts chose differently. The passages Holmes prints in "
                + "double brackets as no part of the original — the endings of Mark and the woman taken in "
                + "adultery — and the words he brackets as doubtful are marked as his. The apparatus is not "
                + "included.",
        RightsNote = "Copyright 2010 by the Society of Biblical Literature and Logos Bible Software, released "
                     + "under Creative Commons Attribution 4.0 since version 1.1 of 19 December 2022; before that it "
                     + "was under an end-user licence. MorphGNT's parsing of this edition, a separate work under "
                     + "Attribution-ShareAlike, is not part of this text. Modified: the signs pointing to the "
                     + "apparatus are left out.",
        Citation = "Michael W. Holmes (ed.), The Greek New Testament: SBL Edition, Society of Biblical Literature "
                   + "and Logos Bible Software, 2010, CC BY 4.0.",
    };

    public static TextSource Read(string resources)
    {
        var folder = Path.Combine(resources, Folder);
        var books = new List<BookDraft>(Files.Length);
        foreach (var (file, index) in Files.Select((file, index) => (file, index)))
        {
            var path = Path.Combine(folder, "text", file + ".txt");
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{Slug} is missing {path}. Run scripts/fetch-sblgnt.ps1, which fetches the text at commit "
                    + $"{Commit[..7]} after reading its licence.");
            }

            books.Add(Book(Matthew + index, index + 1, File.ReadAllLines(path, Encoding.UTF8)));
        }

        return new TextSource(Definition, books);
    }

    /// <summary>One book's file: its title, then a verse to a line.</summary>
    public static BookDraft Book(int canonical, int position, IReadOnlyList<string> lines)
    {
        var chapters = new SortedDictionary<int, List<(int Number, List<Word> Words)>>();
        var marks = new Marks();
        string? title = null;
        Word? previous = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var tab = line.IndexOf('\t');
            if (tab < 0)
            {
                title ??= line.Trim();
                continue;
            }

            var (chapter, number) = Address(line[..tab]);
            var words = new List<Word>();
            foreach (var token in line[(tab + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = Token(token, marks, previous);
                if (word is null)
                {
                    continue;
                }

                words.Add(word);
                previous = word;
            }

            if (words.Count == 0)
            {
                continue;
            }

            if (!chapters.TryGetValue(chapter, out var verses))
            {
                chapters[chapter] = verses = [];
            }

            verses.Add((number, words));
        }

        if (marks.Rejected != 0 || marks.Doubtful != 0)
        {
            throw new InvalidOperationException(
                $"The SBLGNT's {BookReferences.Name(canonical)} ends with {marks.Rejected} double and {marks.Doubtful} "
                + "single brackets still open, so which words Holmes marked cannot be read from it.");
        }

        return new BookDraft(
            CanonicalOrdinal: canonical,
            Position: position,
            Name: BookReferences.Name(canonical),
            Slug: BookReferences.Slug(canonical),
            Chapters: [.. chapters.Select(chapter => new ChapterDraft(
                chapter.Key, [.. chapter.Value.Select(verse => Verse(verse.Number, verse.Words))]))],
            NameNative: title,
            Abbreviation: BookReferences.Abbreviation(canonical));
    }

    /// <summary>
    /// A verse's words, each followed by its punctuation and a space — except the last, and a word
    /// whose punctuation opens a parenthesis on the word after it.
    /// </summary>
    private static VerseDraft Verse(int number, List<Word> words) =>
        new(number, [.. words.Select((word, at) => new WordDraft(
            word.Surface,
            word.Trailer + (at == words.Count - 1 || word.Trailer.ToString().EndsWith('(') ? string.Empty : " "),
            Morphology: word.Mark is null
                ? null
                : JsonSerializer.Serialize(new Dictionary<string, string> { ["brackets"] = word.Mark })))]);

    private static (int Chapter, int Verse) Address(string reference)
    {
        var space = reference.LastIndexOf(' ');
        var colon = reference.IndexOf(':', space + 1);
        if (space < 0 || colon < 0
            || !int.TryParse(reference[(space + 1)..colon], out var chapter)
            || !int.TryParse(reference[(colon + 1)..], out var verse))
        {
            throw new InvalidOperationException(
                $"The SBLGNT line \"{reference}\" is not the \"Book chapter:verse\" every other line begins with.");
        }

        return (chapter, verse);
    }

    /// <summary>
    /// One printed word: its letters, the punctuation after it, and the bracket it stands inside.
    /// Punctuation before it — a parenthesis opening — belongs between it and the word before, so it
    /// goes on that word's trailer. A token with no letters at all is punctuation of the same kind.
    /// </summary>
    private static Word? Token(string token, Marks marks, Word? previous)
    {
        var text = new StringBuilder(token.Length);
        for (var i = 0; i < token.Length; i++)
        {
            if (ApparatusSigns.Contains(token[i]))
            {
                while (i + 1 < token.Length && char.IsAsciiDigit(token[i + 1]))
                {
                    i++;
                }

                continue;
            }

            text.Append(token[i]);
        }

        var cleaned = text.ToString();
        var first = 0;
        while (first < cleaned.Length && !char.IsLetter(cleaned[first]))
        {
            first++;
        }

        var last = cleaned.Length - 1;
        while (last >= first && !char.IsLetter(cleaned[last]) && cleaned[last] != Elision)
        {
            last--;
        }

        var before = cleaned[..first];
        marks.Open(before);
        if (first > last)
        {
            var bare = Unbracketed(before);
            if (bare.Length > 0 && previous is not null)
            {
                previous.Trailer.Append(' ').Append(bare);
            }

            marks.Close(before);
            return null;
        }

        var after = cleaned[(last + 1)..];
        var opening = Unbracketed(before);
        if (opening.Length > 0 && previous is not null)
        {
            previous.Trailer.Append(' ').Append(opening);
        }

        var word = new Word(cleaned[first..(last + 1)], marks.Current);
        word.Trailer.Append(Unbracketed(after));
        marks.Close(after);
        return word;
    }

    private const char Elision = 'ʼ';

    private static string Unbracketed(string punctuation) =>
        punctuation.Replace("⟦", string.Empty).Replace("⟧", string.Empty).Replace("[", string.Empty).Replace("]", string.Empty);

    private sealed class Marks
    {
        public int Rejected { get; private set; }

        public int Doubtful { get; private set; }

        public string? Current => Rejected > 0 ? SblgntTextSource.Rejected : Doubtful > 0 ? SblgntTextSource.Doubtful : null;

        public void Open(string punctuation)
        {
            Rejected += punctuation.Count(c => c == '⟦');
            Doubtful += punctuation.Count(c => c == '[');
        }

        public void Close(string punctuation)
        {
            Rejected -= punctuation.Count(c => c == '⟧');
            Doubtful -= punctuation.Count(c => c == ']');
        }
    }

    private sealed class Word(string surface, string? mark)
    {
        public string Surface { get; } = surface;

        public string? Mark { get; } = mark;

        public StringBuilder Trailer { get; } = new();
    }
}
