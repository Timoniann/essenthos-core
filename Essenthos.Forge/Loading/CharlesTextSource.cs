using System.Net;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Sword;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading;

/// <summary>
/// R. H. Charles's 1 Enoch and Jubilees, translated from the Ethiopic, as he printed them in The Apocrypha and
/// Pseudepigrapha of the Old Testament in English (Oxford, 1913), from CrossWire's two general-book modules.
///
/// <para>
/// **Only his translation of the Ethiopic is read.** In Enoch, where a Greek or Latin fragment survives, Charles
/// prints his translation of it beside the Ethiopic's, each in parentheses closed by its siglum: <c>( … )E</c> is the
/// Ethiopic and is kept, <c>( … )Gg</c>, <c>Gᵍ</c>, <c>Gr</c> and <c>L</c> are the Akhmim Greek, Syncellus, another Greek
/// fragment and the Latin, and are left out (where the module's editors keyed the Greek or Latin itself, it goes with
/// them). Notes, cross-references, chapter titles and summaries, section headings, Jubilees' prologue and the
/// anno mundi dates Charles sets in square brackets in Jubilees are not verses. Every other mark is his and stays as
/// printed: ┌ ┐ round words the Greek has and the Ethiopic lacks, ╓ ╖ round words the Ethiopic has and the Greek
/// lacks, † † round a corruption, 〈 〉 and &lt; &gt; round a restoration, [ ] round an interpolation, ( ) round
/// words supplied for the sense.
/// </para>
///
/// <para>
/// **Numbered as Charles numbers.** That is GEEZ81's numbering, the standard for both books, but for Enoch 20:8,
/// the seventh archangel, which only the Greek has, and the verses the Ge'ez transcription skips or runs into the
/// one before: Enoch 21:1, 28:3, 98:16 and 100:13, and Jubilees 1:18-19, 4:3-14 and 26:33 stand here and not there.
/// What Charles prints out of order to restore what he took for the original order (Enoch 92 before 91, 91:12-17
/// after 93, 51:5, 60:25, 106:17) stands at its own numbers, and the pieces of a verse he divides by letters (5:6-7, 39:6-7, 51:5, 89:48, 91:14, 97:9) are joined in
/// letter order.
/// </para>
///
/// <para>
/// **Two verse breaks of the Jubilees module are repaired.** Its scan of the margin numbers runs two verses into
/// one twice: its 1:8 is Charles's 1:8 and 1:9, and its 11:3 his 11:3 and 11:4, so the module's later verses of both
/// chapters stand a number low. Checked against the 1913 volume (archive.org, apocryphapseudep02charuoft).
/// </para>
/// </summary>
internal static partial class CharlesTextSource
{
    public const string Slug = Sources.CharlesSlug;

    public const string Folder = "Charles";

    public const int Enoch = 85;

    public const int Jubilees = 86;

    private const string EnochModule = "Enoch";

    private const string JubileesModule = "Jubilees";

    /// <summary>The Ethiopic, the one version Charles prints in parentheses that is read.</summary>
    private const string Ethiopic = "E";

    /// <summary>The module's one stray before Enoch 75:1, which is no word of the book.</summary>
    private const string Stray = "yyy";

    private const int StrayChapter = 75;

    /// <summary>What became of every word the two modules hold, for the test that accounts for each of them.</summary>
    internal sealed class Tally
    {
        /// <summary>Every word of every node, its markup aside.</summary>
        public int Read;

        /// <summary>The words written as verses.</summary>
        public int Placed;

        /// <summary>The nodes that are no chapter: title pages, Charles's introductions, the section headings of Enoch.</summary>
        public int FrontMatter;

        /// <summary>The module's notes and cross-references.</summary>
        public int Notes;

        /// <summary>Chapter titles, Jubilees' chapter summaries and its prologue, which the module keys as a summary.</summary>
        public int Titles;

        /// <summary>The bold headings over a passage of Enoch.</summary>
        public int Headings;

        /// <summary>The verse numbers.</summary>
        public int Markers;

        /// <summary>The words of the Greek and Latin fragments' translations, and their sigla and the Ethiopic's.</summary>
        public int Versions;

        /// <summary>How many fragments were left out.</summary>
        public int VersionBlocks;

        /// <summary>The words of the anno mundi dates in Jubilees.</summary>
        public int Dates;

        /// <summary>How many dates were left out.</summary>
        public int DateCount;

        /// <summary>The module's stray letters.</summary>
        public int Strays;
    }

    /// <param name="Opening">The words the second verse opens with, found once in the module's verse.</param>
    private sealed record VerseSplit(int Chapter, int Number, string Opening);

    private static readonly VerseSplit[] JubileesSplits =
    [
        new(1, 8, "For they will forget all My commandments"),
        new(11, 3, "And they made for themselves molten images"),
    ];

    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "Charles's Enoch and Jubilees",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "eng",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: 1913,
        SourceUrl: "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=Enoch",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/enoch.conf",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: null)
    {
        Translators = "Robert Henry Charles (1855-1931)",
        Editors = "R. H. Charles",
        Edition = "The Apocrypha and Pseudepigrapha of the Old Testament in English, volume II, Pseudepigrapha, "
                  + "Oxford, Clarendon Press, 1913: the Book of Enoch in the translation of his second edition (1912), "
                  + "the Book of Jubilees in his translation of 1902; scanned by Joshua Williams, Northwest Nazarene "
                  + "College (1995), Enoch corrected by Marjan Šavli and Barbara Hojkar Šavli (2023)",
        About = "1 Enoch and Jubilees in English, translated from the Ethiopic, the one language in which either book "
                + "survives whole, by R. H. Charles, who also edited the Ethiopic texts: the translation stands beside "
                + "the Ge'ez of the Ethiopian Church's Bible verse by verse. Where Greek or Latin fragments of Enoch "
                + "survive, Charles printed his translation of them beside the Ethiopic's; only the Ethiopic's is here. "
                + "His marks are kept as he printed them: ┌ ┐ round words the Greek has and the Ethiopic lacks, ╓ ╖ round "
                + "words the Ethiopic has and the Greek lacks, † † round a corrupt passage, 〈 〉 and < > round his "
                + "restorations, square brackets round what he took for an interpolation, round brackets round words "
                + "supplied for the sense. He numbers the verses as the Ge'ez does. Enoch 20:8, the seventh archangel, "
                + "is his translation of the Greek, which alone has it, and he has verses the Ge'ez transcription skips "
                + "or runs into the verse before: Enoch 21:1, 28:3, 98:16 and 100:13, and Jubilees 1:18-19, 4:3-14 and "
                + "26:33. Passages he printed out of order, to restore what he took for the original order, stand at "
                + "their own numbers. His notes, his chapter summaries and headings, the prologue of Jubilees and the "
                + "years from the creation he sets in brackets through Jubilees are not included.",
        RightsNote = "Charles died in 1931 and both translations were published before then: they are in the public "
                     + "domain. The modules state Public Domain; the scan and its corrections type out the printed text "
                     + "and carry no rights of their own.",
        Citation = "R. H. Charles (trans.), in R. H. Charles (ed.), The Apocrypha and Pseudepigrapha of the Old Testament "
                   + "in English, vol. II, Oxford, Clarendon Press, 1913; CrossWire Bible Society, SWORD modules Enoch 2.0 "
                   + "and Jubilees 1.0.",
        PartSources =
        [
            new TextPartSource(
                Name: "The Book of Jubilees, CrossWire SWORD module Jubilees 1.0",
                Author: "R. H. Charles; scanned by Joshua Williams, Northwest Nazarene College",
                Licence: "Public Domain",
                LicenceUrl: "https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d/jubilees.conf",
                Url: "https://www.crosswire.org/sword/modules/ModInfo.jsp?modName=Jubilees",
                Covers: "Jubilees"),
        ],
    };

    public static TextSource Read(string folder) => Read(folder, new Tally());

    public static TextSource Read(string folder, Tally tally)
    {
        var enoch = Nodes(folder, EnochModule);
        var jubilees = Nodes(folder, JubileesModule);
        return new TextSource(Definition,
        [
            Book(Enoch, 1, ReadEnoch(enoch, tally)),
            Book(Jubilees, 2, ReadJubilees(jubilees, tally)),
        ]);
    }

    private static IReadOnlyList<SwordNode> Nodes(string folder, string module)
    {
        var root = Path.Combine(folder, module);
        if (!Directory.Exists(Path.Combine(root, "mods.d")))
        {
            throw new InvalidOperationException(
                $"{Slug} is missing the {module} module at {root}. Run scripts/fetch-charles.ps1, which fetches both "
                + "modules pinned, rather than loading one book of the two as though it were the text.");
        }

        return SwordGeneralBook.Nodes(root);
    }

    private static BookDraft Book(int ordinal, int position, SortedDictionary<int, SortedDictionary<int, string>> chapters) => new(
        CanonicalOrdinal: ordinal,
        Position: position,
        Name: BookReferences.Name(ordinal),
        Slug: BookReferences.Slug(ordinal),
        Chapters: [.. chapters.Select(chapter => new ChapterDraft(chapter.Key,
            [.. chapter.Value.Select(verse => new VerseDraft(verse.Key, Words(verse.Value)))]))],
        Abbreviation: BookReferences.Abbreviation(ordinal));

    private static IReadOnlyList<WordDraft> Words(string verse) =>
        [.. VerseWords.Printed(verse).Select(token => new WordDraft(token.Word, token.Trailer, Elided: token.Word.Length == 0))];

    /// <summary>
    /// Enoch's chapters, one node each, but 91, which Charles prints in two places: 91:1-11 and 18-19 before chapter 93
    /// and 91:12-17 after it. A verse is opened by its number, or by a letter alone where Charles divides it, which
    /// continues the number before; its pieces are joined in letter order.
    /// </summary>
    private static SortedDictionary<int, SortedDictionary<int, string>> ReadEnoch(IReadOnlyList<SwordNode> nodes, Tally tally)
    {
        var pieces = new SortedDictionary<int, SortedDictionary<int, List<(string Letter, string Text)>>>();
        foreach (var node in nodes)
        {
            tally.Read += Count(node.Text);
            var name = EnochChapter().Match(node.Name);
            if (!name.Success)
            {
                tally.FrontMatter += Count(node.Text);
                continue;
            }

            var chapter = int.Parse(name.Groups[1].Value);
            var markup = Remove(Note(), node.Text, ref tally.Notes);
            markup = Remove(Title(), markup, ref tally.Titles);
            markup = Remove(BoldHeading(), markup, ref tally.Headings);
            markup = WithoutOtherVersions(markup, node.Name, tally);

            var parts = EnochVerse().Split(markup);
            var before = Clean(parts[0]);
            if (before.Length > 0)
            {
                if (chapter != StrayChapter || before != Stray)
                {
                    throw new InvalidDataException(
                        $"{EnochModule} \"{node.Name}\" holds \"{before}\" before its first verse number. Read the node: a "
                        + "heading this reader does not know, or text that belongs to a verse.");
                }

                tally.Strays += CountText(before);
            }

            var verses = pieces.TryGetValue(chapter, out var found) ? found : pieces[chapter] = new();
            int? number = null;
            for (var i = 1; i < parts.Length; i += 2)
            {
                tally.Markers += Count(parts[i]);
                var marker = VerseMarker().Match(parts[i]);
                if (marker.Groups[1].Value.Length > 0)
                {
                    number = int.Parse(marker.Groups[1].Value);
                }

                if (number is not { } verse)
                {
                    throw new InvalidDataException($"{EnochModule} \"{node.Name}\" opens with the letter {parts[i]} and no number before it.");
                }

                (verses.TryGetValue(verse, out var list) ? list : verses[verse] = new()).Add((marker.Groups[2].Value, Clean(parts[i + 1])));
            }
        }

        return Chapters(pieces, pieceList => string.Join(' ',
            pieceList.OrderBy(piece => piece.Letter, StringComparer.Ordinal).Select(piece => piece.Text).Where(text => text.Length > 0)), tally);
    }

    /// <summary>
    /// Takes Charles's translations of the Greek and Latin fragments out of a chapter of Enoch and keeps the
    /// Ethiopic's: each is a parenthesis closed by its siglum, and parentheses nest inside them.
    /// </summary>
    private static string WithoutOtherVersions(string markup, string where, Tally tally)
    {
        while (Siglum().Match(markup) is { Success: true } siglum)
        {
            var depth = 0;
            var open = siglum.Index;
            for (; open >= 0; open--)
            {
                if (markup[open] == ')')
                {
                    depth++;
                }
                else if (markup[open] == '(' && --depth == 0)
                {
                    break;
                }
            }

            if (open < 0)
            {
                throw new InvalidDataException(
                    $"{EnochModule} \"{where}\" closes a version with ){siglum.Groups[1].Value} that no parenthesis opens.");
            }

            var inner = markup[(open + 1)..siglum.Index];
            var after = markup[(siglum.Index + siglum.Length)..];
            tally.Versions += Count(siglum.Groups[1].Value);
            if (siglum.Groups[1].Value == Ethiopic)
            {
                markup = markup[..open] + inner + after;
                continue;
            }

            tally.Versions += Count(inner);
            tally.VersionBlocks++;
            markup = markup[..open] + after;
        }

        return markup;
    }

    /// <summary>
    /// Jubilees' chapters, one node each, every verse an item with its number as its label. The prologue and each
    /// chapter's summary are titles; the dates go wherever they stand in a verse.
    /// </summary>
    private static SortedDictionary<int, SortedDictionary<int, string>> ReadJubilees(IReadOnlyList<SwordNode> nodes, Tally tally)
    {
        var chapters = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var node in nodes)
        {
            tally.Read += Count(node.Text);
            var name = JubileesChapter().Match(node.Name);
            if (!name.Success)
            {
                tally.FrontMatter += Count(node.Text);
                continue;
            }

            var chapter = int.Parse(name.Groups[1].Value);
            var markup = Remove(Note(), node.Text, ref tally.Notes);
            markup = Remove(Title(), markup, ref tally.Titles);

            var verses = new List<(int Number, string Text)>();
            foreach (Match item in JubileesVerse().Matches(markup))
            {
                tally.Markers += Count(item.Groups[1].Value);
                var text = Clean(item.Groups[2].Value);
                text = Remove(Date(), text, ref tally.Dates, ref tally.DateCount);
                text = Remove(UnopenedDate(), text, ref tally.Dates, ref tally.DateCount);
                verses.Add((int.Parse(item.Groups[1].Value), text));
            }

            var rest = Clean(JubileesVerse().Replace(markup, " "));
            if (rest.Length > 0)
            {
                throw new InvalidDataException(
                    $"{JubileesModule} \"{node.Name}\" holds \"{rest}\" outside its verses. Read the node: a heading this "
                    + "reader does not know, or text that belongs to a verse.");
            }

            for (var i = 0; i < verses.Count; i++)
            {
                if (verses[i].Number != i + 1)
                {
                    throw new InvalidDataException(
                        $"{JubileesModule} {chapter} numbers its verse {i + 1} as {verses[i].Number}. The module has changed: read "
                        + "the chapter against the 1913 volume before loading it.");
                }
            }

            foreach (var split in JubileesSplits.Where(split => split.Chapter == chapter))
            {
                Split(verses, split);
            }

            chapters[chapter] = new SortedDictionary<int, string>(verses.ToDictionary(verse => verse.Number, verse => verse.Text));
        }

        foreach (var verse in chapters.Values.SelectMany(chapter => chapter.Values))
        {
            tally.Placed += CountText(verse);
        }

        return chapters;
    }

    /// <summary>Divides a verse the module runs into the next where the next opens, and numbers the rest of the chapter on.</summary>
    private static void Split(List<(int Number, string Text)> verses, VerseSplit split)
    {
        var at = verses.FindIndex(verse => verse.Number == split.Number);
        var text = at < 0 ? string.Empty : verses[at].Text;
        var opening = text.IndexOf(split.Opening, StringComparison.Ordinal);
        if (opening <= 0 || text.IndexOf(split.Opening, opening + 1, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidDataException(
                $"{JubileesModule} {split.Chapter}:{split.Number} does not hold \"{split.Opening}\" once after its opening. The "
                + "module has changed: read the chapter against the 1913 volume, and drop this repair if the break is right.");
        }

        verses[at] = (split.Number, text[..opening].TrimEnd());
        verses.Insert(at + 1, (split.Number + 1, text[opening..]));
        for (var i = at + 2; i < verses.Count; i++)
        {
            verses[i] = (verses[i].Number + 1, verses[i].Text);
        }
    }

    private static SortedDictionary<int, SortedDictionary<int, string>> Chapters<T>(
        SortedDictionary<int, SortedDictionary<int, T>> read, Func<T, string> text, Tally tally)
    {
        var chapters = new SortedDictionary<int, SortedDictionary<int, string>>();
        foreach (var (chapter, verses) in read)
        {
            chapters[chapter] = new SortedDictionary<int, string>(verses.ToDictionary(verse => verse.Key, verse => text(verse.Value)));
            foreach (var verse in chapters[chapter].Values)
            {
                if (verse.Length == 0)
                {
                    throw new InvalidDataException($"{EnochModule} {chapter} has a verse number with no words under it.");
                }

                tally.Placed += CountText(verse);
            }
        }

        return chapters;
    }

    private static string Remove(Regex pattern, string text, ref int words)
    {
        var count = 0;
        var result = pattern.Replace(text, match =>
        {
            count += Count(match.Value);
            return " ";
        });
        words += count;
        return result;
    }

    private static string Remove(Regex pattern, string text, ref int words, ref int matches)
    {
        var count = 0;
        var found = 0;
        var result = pattern.Replace(text, match =>
        {
            count += CountText(match.Value);
            found++;
            return string.Empty;
        });
        words += count;
        matches += found;
        return result;
    }

    /// <summary>The words of a piece of markup, counted as the verses' own words are.</summary>
    private static int Count(string markup) => Words(Clean(markup)).Count(word => word.Surface.Length > 0);

    /// <summary>The words of text already taken out of its markup, whose angle brackets are Charles's and no tag.</summary>
    private static int CountText(string text) => Words(text).Count(word => word.Surface.Length > 0);

    /// <summary>The text of a piece of markup with its tags and the scan's line breaks gone.</summary>
    private static string Clean(string markup)
    {
        var text = WebUtility.HtmlDecode(Tag().Replace(markup, " ")).Replace(' ', ' ');
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"<note\b[^>]*>.*?</note>", RegexOptions.Singleline)]
    private static partial Regex Note();

    [GeneratedRegex(@"<title\b[^>]*>.*?</title>", RegexOptions.Singleline)]
    private static partial Regex Title();

    [GeneratedRegex(@"<p>\s*<hi type=""bold"">.*?</hi>\s*</p>", RegexOptions.Singleline)]
    private static partial Regex BoldHeading();

    [GeneratedRegex(@"\)(E|Gg|Gᵍ|Gs|Gr|L)(?![A-Za-z])")]
    private static partial Regex Siglum();

    [GeneratedRegex(@"^Chapter (\d+)a?$")]
    private static partial Regex EnochChapter();

    [GeneratedRegex(@"<hi type=""super"">(\d*[a-z]?)</hi>")]
    private static partial Regex EnochVerse();

    [GeneratedRegex(@"^(\d*)([a-z]?)$")]
    private static partial Regex VerseMarker();

    [GeneratedRegex(@"^Jubilees (\d+)$")]
    private static partial Regex JubileesChapter();

    [GeneratedRegex(@"<item>\s*<label>(\d+)\.</label>(.*?)</item>", RegexOptions.Singleline)]
    private static partial Regex JubileesVerse();

    /// <summary>A year from the creation in square brackets: <c>[1681 A.M.]</c>, <c>[2450 Anno Mundi]</c>, <c>[582-88]</c>.</summary>
    [GeneratedRegex(@"\s*\[\s*\d[\d\s,()?-]*(?:A\.\s?M\.?|Anno Mundi)?\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex Date();

    /// <summary>The one date the scan lost the opening bracket of, in 13:8: <c>1954 A.M.]</c>.</summary>
    [GeneratedRegex(@"\s*\b\d+\s*A\.\s?M\.\]")]
    private static partial Regex UnopenedDate();
}
