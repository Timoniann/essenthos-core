using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Essenthos.Core.BetaMasaheft;

/// <summary>How a Beta maṣāḥǝft file lays its text out, which decides what counts as a verse.</summary>
internal enum GeezLayout
{
    /// <summary>
    /// Verses are the <c>&lt;l n&gt;</c> of a chapter. An <c>&lt;l&gt;</c> with no number is a section
    /// heading the church's printed Bible sets between verses — <em>concerning the widow's gift</em> —
    /// and is not text of the book.
    /// </summary>
    Verses,

    /// <summary>
    /// Ludolf's Psalter as HaCohen typed it: an unnumbered <c>&lt;l&gt;</c> is the second half of the
    /// verse before it, and the psalm's title stands in a <c>&lt;title&gt;</c> before its first verse.
    /// </summary>
    Psalter,

    /// <summary>
    /// Divided into chapters and nothing finer: each chapter is one block of text with no verse
    /// numbers at all. 4 Baruch is typed this way.
    /// </summary>
    Chapters,
}

/// <summary>One <c>&lt;l&gt;</c> as the file gives it: its number, if it has one, and its text.</summary>
internal sealed record GeezLine(int? Number, string Text);

/// <param name="Number">The chapter as the file numbers it.</param>
/// <param name="Title">A psalm's title, where the Psalter prints one before its first verse.</param>
internal sealed record GeezRawChapter(int Number, IReadOnlyList<GeezLine> Lines, string? Title = null);

/// <summary>
/// Reads the text of one work out of a Beta maṣāḥǝft TEI file, one edition at a time.
///
/// <para>
/// A file describes a work and may carry more than one edition of it: Jubilees holds VanderKam's
/// text and the church's side by side, Ecclesiastes Mercer's and the church's. Which one is read is
/// the caller's choice and is named by the edition's <c>xml:id</c>; a file with several and no choice
/// is refused rather than read at its first, because reading the first is how a copyrighted edition
/// came to be counted as the only one.
/// </para>
///
/// <para>
/// **Only a chapter's numbered lines are verses.** The New Testament files also carry the
/// prefaces, the lists of chapter titles and the subscriptions the manuscripts transmit around a
/// book, some of them with numbered lines of their own; they sit in textparts that are not
/// chapters, and nothing in them is read. Neither is a line with no text: Luke 21 opens with an
/// empty line numbered 21.
/// </para>
/// </summary>
internal static class GeezReader
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";

    private static readonly XNamespace Xml = XNamespace.Xml;

    private const string Division = "div";

    private const string TextPart = "textpart";

    /// <summary>The subtypes a file gives the divisions it numbers verses inside.</summary>
    private static readonly HashSet<string> ChapterLike = ["chapter", "Psalmus", "section"];

    /// <summary>
    /// A chapter the file numbers by its identifier and not by <c>n</c>: Jubilees' church edition
    /// writes <c>Cap1</c> to <c>Cap50</c>.
    /// </summary>
    private static readonly Regex NumberedIdentifier = new(@"^Cap(\d+)$", RegexOptions.CultureInvariant);

    /// <summary>Elements inside a line whose content is not the text of the verse.</summary>
    private static readonly HashSet<string> NotText = ["ref", "note", "label", "title"];

    /// <summary>What a line break inside a line is written as, for the tokeniser to read.</summary>
    public const char LineBreak = '\n';

    /// <param name="edition">
    /// The <c>xml:id</c> of the edition to read, or null for a file that holds one edition only.
    /// </param>
    /// <param name="skipped">The chapters the file holds that have no chapter number, by identifier.</param>
    public static IReadOnlyList<GeezRawChapter> Read(
        string path,
        string? edition,
        GeezLayout layout,
        out IReadOnlyList<string> skipped)
    {
        var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var chosen = Edition(document, edition, path);
        var chapters = chosen.Descendants(Tei + Division).Where(IsChapter).ToList();
        var unnumbered = new List<string>();
        var read = new List<GeezRawChapter>(chapters.Count + 1);

        if (chapters.Count == 0)
        {
            // Obadiah, the short letters and the Letter of Jeremiah are one chapter the file does not
            // divide at all; their verses stand directly in the edition.
            var lines = chosen.Descendants(Tei + "l")
                .Where(line => !line.Ancestors(Tei + Division).Any(IsTextPart))
                .Select(Line)
                .ToList();
            read.Add(new GeezRawChapter(1, lines));
            skipped = unnumbered;
            return read;
        }

        foreach (var chapter in chapters)
        {
            var number = ChapterNumber(chapter);
            if (number is null)
            {
                unnumbered.Add((string?)chapter.Attribute(Xml + "id") ?? "(no identifier)");
                continue;
            }

            if (layout == GeezLayout.Chapters)
            {
                // The chapter's own blocks only: in 4 Baruch the file nests chapter 4 inside chapter 3.
                var text = string.Join(' ', chapter.Elements(Tei + "ab").Select(Text));
                read.Add(new GeezRawChapter(number.Value, [new GeezLine(null, text)]));
                continue;
            }

            var own = chapter.Descendants(Tei + "l")
                .Where(line => line.Ancestors(Tei + Division).First(IsTextPart) == chapter)
                .Select(Line)
                .ToList();
            var title = layout == GeezLayout.Psalter ? PsalmTitle(chapter) : null;
            read.Add(new GeezRawChapter(number.Value, own, title));
        }

        var repeated = read.GroupBy(chapter => chapter.Number).FirstOrDefault(group => group.Count() > 1);
        if (repeated is not null)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} numbers two chapters {repeated.Key}. A chapter number is an address, and " +
                "reading both under one would put two passages at it; say in GeezTextSource which one is which.");
        }

        skipped = unnumbered;
        return read;
    }

    private static XElement Edition(XDocument document, string? edition, string path)
    {
        var editions = document.Descendants(Tei + Division)
            .Where(division => (string?)division.Attribute("type") == "edition")
            .ToList();

        if (edition is not null)
        {
            return editions.SingleOrDefault(division => (string?)division.Attribute(Xml + "id") == edition)
                   ?? throw new InvalidOperationException(
                       $"{Path.GetFileName(path)} has no edition \"{edition}\"; it holds " +
                       $"{string.Join(", ", editions.Select(division => (string?)division.Attribute(Xml + "id") ?? "one with no identifier"))}. " +
                       "The file has changed since the edition was chosen; read what it holds now and choose again.");
        }

        return editions.Count == 1
            ? editions[0]
            : throw new InvalidOperationException(
                $"{Path.GetFileName(path)} holds {editions.Count} editions and none was chosen. Name the one to read " +
                "in GeezTextSource: several files carry a copyrighted edition beside the church's.");
    }

    private static bool IsTextPart(XElement division) => (string?)division.Attribute("type") == TextPart;

    private static bool IsChapter(XElement division) =>
        IsTextPart(division) && ChapterLike.Contains((string?)division.Attribute("subtype") ?? string.Empty);

    private static int? ChapterNumber(XElement chapter)
    {
        if (int.TryParse((string?)chapter.Attribute("n"), out var number))
        {
            return number;
        }

        var match = NumberedIdentifier.Match((string?)chapter.Attribute(Xml + "id") ?? string.Empty);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static GeezLine Line(XElement line) =>
        new(int.TryParse((string?)line.Attribute("n"), out var number) ? number : null, Text(line));

    /// <summary>
    /// The title Ludolf prints above a psalm, which HaCohen typed as a <c>&lt;title&gt;</c> inside the
    /// chapter's block. Other files use the element for the chapter's number, which is not text.
    /// </summary>
    private static string? PsalmTitle(XElement chapter)
    {
        var title = chapter.Descendants(Tei + "title").FirstOrDefault();
        if (title is null)
        {
            return null;
        }

        var text = Text(title);
        return text.Length == 0 || text.All(character => char.IsDigit(character) || char.IsWhiteSpace(character))
            ? null
            : text;
    }

    /// <summary>
    /// The words of an element, with every run of whitespace made one space and every
    /// <c>&lt;lb/&gt;</c> after the first word kept as a line break: in the Psalter it divides a verse
    /// into its two halves.
    /// </summary>
    private static string Text(XElement element)
    {
        var text = new StringBuilder();
        Append(element, text, root: true);
        return Normalised(text.ToString());
    }

    private static void Append(XElement element, StringBuilder text, bool root)
    {
        if (!root && NotText.Contains(element.Name.LocalName))
        {
            return;
        }

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText run:
                    text.Append(run.Value);
                    break;
                case XElement { Name.LocalName: "lb" }:
                    text.Append(LineBreak);
                    break;
                case XElement child:
                    Append(child, text, root: false);
                    break;
            }
        }
    }

    private static string Normalised(string text)
    {
        var result = new StringBuilder(text.Length);
        var pendingSpace = false;
        var pendingBreak = false;

        foreach (var character in text)
        {
            if (character == LineBreak)
            {
                pendingBreak = true;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (result.Length > 0 && (pendingBreak || pendingSpace))
            {
                result.Append(pendingBreak ? LineBreak : ' ');
            }

            pendingSpace = false;
            pendingBreak = false;
            result.Append(character);
        }

        return result.ToString();
    }
}
