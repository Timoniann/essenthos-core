using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>One Targum as a text: its definition and the Sefaria versions it is read from, one per book.</summary>
internal sealed record TargumText(TextDefinition Definition, IReadOnlyList<(int Book, string Path)> Books)
{
    /// <summary>Joined to BHSA verse by verse through the frame: every Targum numbers its verses as the Hebrew does.</summary>
    public DeclaredVersePair VersePair => new(Definition.Slug, BhsaTextSource.Slug, new HashSet<int>());
}

/// <summary>
/// The Aramaic Targums from Sefaria's export (<c>Resources/Targums</c>, fetched by <c>scripts/fetch-sefaria-targums.ps1</c>),
/// each version taken only where its own <c>license</c> field says Public Domain: Onkelos on the Torah, Jonathan on the
/// Prophets, and the Targums of the Writings the Rabbinic Bible prints. Three works, three texts, each named for what it
/// is; Sefaria's "Targum Jonathan" on the Torah is Pseudo-Jonathan and is not among them.
///
/// <para>
/// A version is a JSON file whose <c>text</c> is an array of chapters of verse strings, numbered as the Hebrew Bible is.
/// The strings carry a little markup: the tags are taken out and what they hold is kept, because the one tag in use,
/// <c>&lt;small&gt;</c>, marks words the Rabbinic Bible prints, ת״א ("another Targum") before a second rendering in Job
/// and Esther and תוספתא before an addition. The sof pasuq, written as a colon, goes to the trailer of the word before.
/// </para>
/// </summary>
internal static partial class TargumTextSource
{
    public const string Folder = "Targums";

    public const string Allowed = "Public Domain";

    private static readonly string[] Torah = ["Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy"];

    private static readonly (int Book, string Name)[] Prophets =
    [
        (6, "Joshua"), (7, "Judges"), (9, "1Samuel"), (10, "2Samuel"), (11, "1Kings"), (12, "2Kings"), (23, "Isaiah"),
        (24, "Jeremiah"), (26, "Ezekiel"), (28, "Hosea"), (29, "Joel"), (30, "Amos"), (31, "Obadiah"), (32, "Jonah"),
        (33, "Micah"), (34, "Nahum"), (35, "Habakkuk"), (36, "Zephaniah"), (37, "Haggai"), (38, "Zechariah"), (39, "Malachi"),
    ];

    private static readonly (int Book, string Name)[] Writings =
    [
        (17, "Esther"), (18, "Job"), (19, "Psalms"), (20, "Proverbs"), (21, "Ecclesiastes"), (22, "SongofSongs"),
        (8, "Ruth"), (25, "Lamentations"),
    ];

    private static TextDefinition Defined(string slug, string name, string nameNative, string sourceUrl) => new(
        Slug: slug,
        Name: name,
        NameNative: nameNative,
        Kind: TextKind.Translation,
        Language: "arc",
        Direction: TextDirection.RightToLeft,
        Versification: Versification.Original,
        PublishedYear: null,
        SourceUrl: sourceUrl,
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: null,
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: null)
    {
        Script = "Hebr",
    };

    public static TargumText Onkelos { get; } = new(
        Defined("ONKELOS", "Targum Onkelos", "תרגום אונקלוס",
            "https://storage.googleapis.com/sefaria-export/json/Tanakh/Targum/Onkelos/Torah") with
        {
            Translators = "Ascribed by the Babylonian Talmud to Onkelos the proselyte",
            Edition = "As Sefaria received it from Torat Emet (toratemetfreeware.com), vocalised; Sefaria's export of 2026-11-01",
            About = "The Aramaic translation of the Torah that the Jews of Babylonia made their own and read in the "
                    + "synagogue verse by verse beside the Hebrew, from the first centuries of our era on. It follows the "
                    + "Hebrew closely, word by word for the most part, and turns what speaks of God in human terms into "
                    + "reverent paraphrase: God is not seen but his glory is revealed, and his Word acts. Because it was "
                    + "made from a Hebrew text before the Masoretes fixed theirs, it is a witness to that text as well as a "
                    + "translation of it. This is the vocalised text Sefaria publishes from Torat Emet.",
            RightsNote = "Sefaria states the licence of each version it publishes in the version itself, and the licence of "
                         + "every book of this text is Public Domain. A second vocalisation Sefaria also publishes, after "
                         + "the Yemenite tradition, is under Creative Commons Attribution-ShareAlike and is not used.",
            Citation = "Targum Onkelos, from Sefaria (www.sefaria.org) as received from Torat Emet, Public Domain.",
        },
        [.. Torah.Select((book, i) => (i + 1, $"onkelos/{book}.json"))]);

    public static TargumText Jonathan { get; } = new(
        Defined("TGJONATHAN", "Targum Jonathan on the Prophets", "תרגום יונתן",
            "https://storage.googleapis.com/sefaria-export/json/Tanakh/Targum/Targum Jonathan/Prophets") with
        {
            Translators = "Ascribed by the Babylonian Talmud to Jonathan ben Uzziel, a disciple of Hillel",
            Edition = "As the Rabbinic Bible (Mikraot Gedolot) prints it, transcribed by Sefaria; Sefaria's export of 2026-11-01",
            About = "The Aramaic translation of the Prophets, Joshua to Malachi, read in the synagogue beside the Hebrew "
                    + "of the prophetic reading and shaped in Babylonia alongside Targum Onkelos. In the books of history "
                    + "it keeps close to the Hebrew; in the poetry of the prophets it often explains as it translates, "
                    + "naming the people a figure stands for and reading the promises of the prophets as promises of the "
                    + "Messiah. It is the text of the Rabbinic Bible as Sefaria transcribed it. Its name is not that of "
                    + "the translation of the Torah also called Targum Jonathan, which is a different and later work.",
            RightsNote = "Sefaria states the licence of each version it publishes in the version itself, and the licence of "
                         + "every book of this text is Public Domain.",
            Citation = "Targum Jonathan on the Prophets, Mikraot Gedolot, from Sefaria (www.sefaria.org), Public Domain.",
        },
        [.. Prophets.Select(book => (book.Book, $"jonathan/{book.Name}.json"))]);

    public static TargumText OfTheWritings { get; } = new(
        Defined("TGWRITINGS", "The Targums of the Writings", "תרגום כתובים",
            "https://storage.googleapis.com/sefaria-export/json/Tanakh/Targum/Aramaic Targum/Writings") with
        {
            Translators = "Unknown; the Talmud names no translator of the Writings, and the Targums of the several books "
                          + "are the work of different hands and ages",
            Editors = "Sefaria, transcribing the Rabbinic Bible (Mikraot Gedolot)",
            Edition = "As the Rabbinic Bible (Mikraot Gedolot) prints them, transcribed by Sefaria; Sefaria's export of 2026-11-01",
            About = "The Aramaic translations of the Psalms, Proverbs, Job and the five Scrolls (the Song of Songs, Ruth, "
                    + "Lamentations, Ecclesiastes and Esther), as the Rabbinic Bible prints them. They are several works "
                    + "from different places and times, most of them later than Onkelos and Jonathan, and they differ in "
                    + "kind: Proverbs keeps close to the Hebrew, Job and the Psalms sometimes give a second rendering of a "
                    + "verse after the words \"another Targum\", and the Song of Songs, Ecclesiastes and Esther are retold "
                    + "at length, the Song as the history of Israel from the Exodus to the Messiah. The Targums of "
                    + "Chronicles, Ezra, Nehemiah and Daniel are not included.",
            RightsNote = "Sefaria states the licence of each version it publishes in the version itself, and the licence of "
                         + "every book of this text is Public Domain.",
            Citation = "The Targums of the Writings, Mikraot Gedolot, from Sefaria (www.sefaria.org), Public Domain.",
        },
        [.. Writings.Select(book => (book.Book, $"writings/{book.Name}.json"))]);

    public static IReadOnlyList<TargumText> All { get; } = [Onkelos, Jonathan, OfTheWritings];

    public static IReadOnlyList<TextDefinition> Definitions => [.. All.Select(text => text.Definition)];

    public static IReadOnlyList<DeclaredVersePair> VersePairs => [.. All.Select(text => text.VersePair)];

    /// <param name="folder">The folder of the export's files, <c>Resources/Targums</c>.</param>
    public static TextSource Read(string folder, TargumText text)
    {
        var books = new List<BookDraft>(text.Books.Count);
        foreach (var (ordinal, relative) in text.Books.OrderBy(book => book.Book))
        {
            var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{text.Definition.Slug} is missing {relative}. Run scripts/fetch-sefaria-targums.ps1 rather than loading part of the Targum.");
            }

            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var licence = json.RootElement.GetProperty("license").GetString();
            if (licence != Allowed)
            {
                throw new InvalidDataException($"{relative} says its licence is \"{licence}\", not \"{Allowed}\"; it is not read.");
            }

            var chapters = new List<ChapterDraft>();
            var number = 0;
            foreach (var chapter in json.RootElement.GetProperty("text").EnumerateArray())
            {
                number++;
                var verses = new List<VerseDraft>();
                var verse = 0;
                foreach (var line in chapter.EnumerateArray())
                {
                    verse++;
                    var words = Words(line.GetString() ?? string.Empty);
                    if (words.Count > 0)
                    {
                        verses.Add(new VerseDraft(verse, words));
                    }
                }

                if (verses.Count > 0)
                {
                    chapters.Add(new ChapterDraft(number, verses));
                }
            }

            var canonical = BibleBookAbbreviation.GetByOrdinal(ordinal)
                            ?? throw new InvalidOperationException($"No canonical book {ordinal}.");
            books.Add(new BookDraft(
                CanonicalOrdinal: canonical.Ordinal,
                Position: books.Count + 1,
                Name: canonical.FullName.Full,
                Slug: Slugs.Of(canonical.StandardAbbreviation.Full),
                Chapters: chapters,
                Abbreviation: canonical.StandardAbbreviation.Full));
        }

        return new TextSource(text.Definition, books);
    }

    /// <summary>
    /// A verse string as words: tags out and their content kept, the left-to-right marks out, split at white space; a
    /// colon or full stop that ends a word, or stands alone, goes to the trailer of the word it follows.
    /// </summary>
    internal static List<WordDraft> Words(string verse)
    {
        var text = Tag().Replace(verse, string.Empty).Replace("‎", string.Empty, StringComparison.Ordinal);
        var words = new List<WordDraft>();
        foreach (var token in text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var end = token.Length;
            while (end > 0 && token[end - 1] is ':' or '.')
            {
                end--;
            }

            var surface = token[..end];
            var trailing = token[end..];
            if (surface.Length == 0)
            {
                if (words.Count > 0)
                {
                    words[^1] = words[^1] with { Trailer = words[^1].Trailer.TrimEnd() + trailing + " " };
                }

                continue;
            }

            words.Add(new WordDraft(Surface: surface, Trailer: trailing + " "));
        }

        if (words.Count > 0)
        {
            words[^1] = words[^1] with { Trailer = words[^1].Trailer.TrimEnd() };
        }

        return words;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();
}
