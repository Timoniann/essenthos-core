using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// Westcott and Hort's New Testament in the Original Greek, 1881 — one of the two editions Nestle
/// voted his own text out of.
///
/// The corpus already holds Nestle 1904 and none of its ingredients. Nestle built his text by
/// majority: where two of Tischendorf's eighth edition, Westcott and Hort, and Weiss agreed, that
/// reading went in. So this edition and the Tischendorf loaded beside it are not two more Greek
/// witnesses against Nestle — they are two of the three voters, and holding them lets a reader ask
/// of any place the editions differ which two outvoted which one. No other pair of texts here can
/// answer that. Weiss is the third voter and no free machine-readable edition of him was found, so
/// two thirds of the vote is as far as the corpus can see, and the reader should be told so rather
/// than shown a complete apparatus that is not one.
///
/// It is read by <see cref="UtrReader"/> unchanged. Robinson wrote this file in the notation his
/// Textus Receptus uses, so the parse costs nothing; what differs is what the two sides of a
/// variant group mean, and that is <see cref="Reading"/>'s business rather than the parser's.
/// </summary>
internal static partial class WestcottHortTextSource
{
    public const string Slug = "WESTCOTTHORT1881";

    /// <summary>
    /// Which side of a variant group Westcott and Hort printed. The other side is the reading they
    /// put in the margin, which is a different edition and not this one.
    ///
    /// Established against the repository's own plain transcription of the text rather than
    /// assumed: taking this side reproduces 7,887 of its 7,940 verses word for word, and taking the
    /// other reproduces 6,774. <c>WestcottHortEditionTests</c> is where that is asserted, so a file
    /// whose sides were ever swapped fails rather than loading the margin as the text.
    /// </summary>
    private const Reading Printed = Reading.First;

    /// <summary>The file stem of each book, and its place in the canon.</summary>
    private static readonly (string File, int Canonical)[] Canon =
    [
        ("MT", 40), ("MR", 41), ("LU", 42), ("JOH", 43), ("AC", 44), ("RO", 45), ("1CO", 46), ("2CO", 47),
        ("GA", 48), ("EPH", 49), ("PHP", 50), ("COL", 51), ("1TH", 52), ("2TH", 53), ("1TI", 54), ("2TI", 55),
        ("TIT", 56), ("PHM", 57), ("HEB", 58), ("JAS", 59), ("1PE", 60), ("2PE", 61), ("1JO", 62), ("2JO", 63),
        ("3JO", 64), ("JUDE", 65), ("RE", 66),
    ];

    public static IReadOnlyList<string> Books => [.. Canon.Select(book => book.File)];

    /// <summary>Where a book of this edition stands in the shared canon.</summary>
    public static int Canonical(string book) =>
        Canon.FirstOrDefault(entry => entry.File == book) is { Canonical: > 0 } found
            ? found.Canonical
            : throw new InvalidOperationException($"The Westcott-Hort has no book \"{book}\".");

    /// <summary>
    /// The 1881 edition is long out of copyright, and Robinson's parsing and Strong numbers are
    /// released into the public domain by the repository that holds them — its README says
    /// <c>License? Public Domain. Copy freely.</c> and names Robinson as the primary author and
    /// Sandborg-Petersen as the maintainer, read on 2026-09-06 and kept beside the data. It is the
    /// same one-sentence statement over the same person's work that the Textus Receptus files
    /// already loaded rest on.
    ///
    /// Two other copies of this edition were refused and the reasons are in the licence kept beside
    /// the data, because the next person to want an accented Westcott-Hort will find them first:
    /// CrossWire's WHNU module is CC BY-NC-SA 4.0 and is a hybrid carrying Nestle-Aland 27 and UBS4
    /// readings under a nineteenth-century name, and the accented edition attributed to Joshua
    /// Grauman is reported as CC BY-SA 3.0 US. A re-wrapping more restrictive than the original
    /// cannot bind the original, and the original is what is taken.
    /// </summary>
    public static readonly TextDefinition Definition = new(
        Slug: Slug,
        Name: "Westcott-Hort 1881 New Testament in the Original Greek",
        NameNative: "Η ΚΑΙΝΗ ΔΙΑΘΗΚΗ",
        Kind: TextKind.CriticalEdition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 1881,
        SourceUrl: "https://github.com/byztxt/greektext-westcott-hort",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://github.com/byztxt/greektext-westcott-hort",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Alexandrian")
    {
        Editors = "Brooke Foss Westcott (1825-1901) and Fenton John Anthony Hort (1828-1892)",
        Edition = "The 1881 text, without the marginal readings; Robinson's parsed transcription",
        About = "Twenty-eight years of work, and the edition that ended the Received Text's reign in "
                + "scholarship. Westcott and Hort weighed manuscripts by the families they descend "
                + "from rather than counting them, and concluded that Codex Vaticanus and Codex "
                + "Sinaiticus preserve a text almost free of the later editing they saw everywhere "
                + "else — so this is the B-pole of nineteenth-century criticism, where Tischendorf's "
                + "eighth edition is the Sinaiticus pole. It is also one of the three editions Nestle "
                + "took his majority from, which is why the corpus holds it: at every place Nestle "
                + "1904 differs from it, the reason is that the other two voters agreed against it. "
                + "The transcription read here is Maurice Robinson's, in the alphabet the Online "
                + "Bible's Greek texts have always been distributed in, and it carries no accents or "
                + "breathings — the edition's own accents are not in any copy of it this project "
                + "could load without a share-alike clause.",
        RightsNote = "Westcott and Hort printed a second set of readings in their margin, and this "
                     + "file carries both at 1,653 places. Only the text is loaded. A transcription "
                     + "that silently preferred the other side would be a different edition under the "
                     + "same name, which is what the CrossWire module does with Nestle-Aland readings.",
        Citation = "Westcott, B. F., & Hort, F. J. A. (1881). The New Testament in the Original Greek.",
    };

    public static TextSource Read(string folder)
    {
        var books = new List<BookDraft>(Canon.Length);
        var position = 0;

        foreach (var (file, canonical) in Canon)
        {
            position++;
            var verses = UtrReader.Read(
                Repaired(File.ReadAllText(Path.Combine(folder, "parsed", $"{file}.UWH"))), Printed);

            var name = BibleBookAbbreviation.GetByOrdinal(canonical)?.FullName.Full
                       ?? throw new InvalidOperationException(
                           $"The Westcott-Hort book {file} is canonical number {canonical}, which has no name. " +
                           $"Add it to {nameof(BibleBookAbbreviation)} — until then this text cannot be placed " +
                           "beside any other.");

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical,
                Position: position,
                Name: name,
                Slug: Slugs.Of(name),
                Chapters: [.. WithMarks(file, verses)
                    // Matthew 12:47 is the one address the file writes and the printed text has no
                    // words for: Westcott and Hort left the verse out and put it in their margin,
                    // so taking their text leaves the group empty. A verse row means the edition has
                    // that verse, so this one gets none — the same thing the Nestle load does with
                    // the sixteen verses the critical text omits outright, which this file does not
                    // number at all.
                    .Where(v => v.Verse.Words.Count > 0)
                    .GroupBy(v => v.Chapter)
                    .OrderBy(chapter => chapter.Key)
                    .Select(chapter => new ChapterDraft(
                        chapter.Key,
                        [.. chapter.OrderBy(v => v.Verse.Number).Select(v => Verse(v.Verse, v.Marks))]))]));
        }

        return new TextSource(Definition, books);
    }

    /// <summary>
    /// One misplaced bracket, moved back onto the word it belongs to.
    ///
    /// Romans 10:5 is written <c>| [tou 3588] {T-GSM}</c> where every other bracketed word in the
    /// file closes after the letters. Left alone, <c>3588]</c> is not a number the parser
    /// recognises, so it becomes a word of Romans and shifts the parse of everything after it.
    /// </summary>
    private static string Repaired(string content) => StrayBracket().Replace(content, "[$1] $2");

    /// <param name="Marks">
    /// What Westcott and Hort printed around this word, and the reason to hold their text rather
    /// than a bare transcription of it. Double brackets are the passages they judged no part of the
    /// original and printed anyway — the long ending of Mark, the woman taken in adultery, the
    /// sweat like blood — and single brackets are the words they thought doubtful. A reader asking
    /// why Nestle prints something the modern critical text does not is asking about exactly these.
    /// </param>
    private readonly record struct MarkedVerse(int Chapter, UtrVerse Verse, IReadOnlyList<string?> Marks);

    /// <summary>
    /// The bracket a word stands inside, resolved over the whole book.
    ///
    /// A bracket is opened on one word and closed on another many verses later — Mark 16:9 opens
    /// and Mark 16:20 closes, John 7:53 opens and John 8:11 closes across a chapter boundary — so
    /// the words between carry no bracket character of their own and are inside it all the same.
    /// Reading the characters word by word would mark the two ends of the long ending of Mark and
    /// leave the eleven verses between them unmarked, which says the opposite of what the edition
    /// says.
    /// </summary>
    private static IEnumerable<MarkedVerse> WithMarks(string book, IReadOnlyList<UtrVerse> verses)
    {
        var rejected = 0;
        var doubtful = 0;

        foreach (var verse in verses)
        {
            var marks = new List<string?>(verse.Words.Count);
            foreach (var word in verse.Words)
            {
                var surface = word.Surface;
                rejected += Opens(surface, DoubleBracket);
                doubtful += Opens(surface, Bracket) - Opens(surface, DoubleBracket);

                marks.Add(rejected > 0 ? Rejected : doubtful > 0 ? Doubtful : Suspected(surface));

                rejected -= Closes(surface, DoubleClose);
                doubtful -= Closes(surface, Close) - Closes(surface, DoubleClose);
            }

            yield return new MarkedVerse(verse.Chapter, verse, marks);
        }

        if (rejected != 0 || doubtful != 0)
        {
            throw new InvalidOperationException(
                $"The Westcott-Hort book {book} ends with {rejected} double and {doubtful} single brackets "
                + "still open, so a bracket was opened on one reading and closed on the other. Which words the "
                + "editors marked cannot be recovered from a file like that; read it before loading it.");
        }
    }

    private const string DoubleBracket = "[[";
    private const string Bracket = "[";
    private const string DoubleClose = "]]";
    private const string Close = "]";

    /// <summary>Westcott and Hort's double brackets: printed, and judged no part of the original.</summary>
    private const string Rejected = "rejected";

    /// <summary>Their single brackets: printed, and doubted.</summary>
    private const string Doubtful = "doubtful";

    /// <summary>
    /// The 28 words Robinson encloses in angle brackets. What the sign means is not stated anywhere
    /// in the repository and is not in the plain transcription beside it, so it is recorded as
    /// marked and not as anything in particular. Guessing at it would put a claim about Westcott
    /// and Hort's judgement on Acts 16:12 and 1 Corinthians 2:4 that nobody made.
    /// </summary>
    private const string Marked = "marked";

    private static string? Suspected(string surface) =>
        surface.StartsWith('<') || surface.EndsWith('>') ? Marked : null;

    private static int Opens(string surface, string bracket) =>
        surface.StartsWith(bracket, StringComparison.Ordinal) ? 1 : 0;

    private static int Closes(string surface, string bracket) =>
        surface.EndsWith(bracket, StringComparison.Ordinal) ? 1 : 0;

    private static VerseDraft Verse(UtrVerse verse, IReadOnlyList<string?> marks) =>
        new(verse.Number, [.. verse.Words.Select((word, at) => new WordDraft(
            Surface: BetaCode.ToGreek(word.Surface.Trim('[', ']', '<', '>')),
            Trailer: at == verse.Words.Count - 1 ? string.Empty : " ",
            StrongNumber: word.Strong is null ? null : $"G{word.Strong}",
            Morphology: Morphology(word, marks[at])))]);

    /// <summary>
    /// Robinson's parse code, the inflection code a verb carries beside its Strong number, and the
    /// editors' own mark. The first two are kept as they were written rather than expanded, which
    /// is what the Textus Receptus and the Byzantine loads do with the same codes.
    /// </summary>
    private static string? Morphology(UtrWord word, string? mark)
    {
        if (word.Morphology is null && word.Inflection is null && mark is null)
        {
            return null;
        }

        var features = new Dictionary<string, string>(3);
        if (word.Morphology is { } parse)
        {
            features["robinson"] = parse;
        }

        if (word.Inflection is { } inflection)
        {
            features["inflection"] = inflection;
        }

        if (mark is not null)
        {
            features["brackets"] = mark;
        }

        return JsonSerializer.Serialize(features);
    }

    [GeneratedRegex(@"\[([^\s\[\]]+) (\d+)\]")]
    private static partial Regex StrayBracket();
}
