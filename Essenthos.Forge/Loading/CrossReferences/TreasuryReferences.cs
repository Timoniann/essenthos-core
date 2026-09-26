using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Essenthos.Core.Sword;

namespace Essenthos.Core.Loading.CrossReferences;

/// <summary>
/// The Treasury of Scripture Knowledge, from CrossWire's SWORD commentary module.
///
/// <para>
/// Each verse's entry is a run of lines: a catchword of the verse — <em>beginning.</em> — and then the
/// references filed under it, as ThML <c>scripRef</c> elements written the way the printed Treasury
/// writes them: <c>Pr 8:22-24; 16:4; Mr 13:19; Joh 1:1-3</c>. A reference with no book stays in the book
/// before it, one with no chapter in the chapter before it, and the first of an entry starts from the
/// verse's own book and chapter, which is how <c>10,12,18</c> under Genesis 1:4 means Genesis 1:10, 12
/// and 18. A chapter's first verse opens with a summary of the chapter, each line a
/// <c>scripRef passage="…"</c> into the chapter itself; those are headings, not references.
/// </para>
///
/// <para>
/// Ranked in the order the Treasury prints them, since it ranks them no other way.
/// </para>
/// </summary>
internal static partial class TreasuryReferences
{
    public const string Folder = "TreasuryOfScriptureKnowledge";

    /// <summary>A catchword longer than this is a note the Treasury prints after the references, not a word of the verse.</summary>
    private const int LongestCatchword = 40;

    /// <summary>The abbreviations the Treasury prints, in canonical order: the ordinal is the index plus one.</summary>
    private static readonly string[] Abbreviations =
    [
        "Ge", "Ex", "Le", "Nu", "De", "Jos", "Jud", "Ru", "1Sa", "2Sa", "1Ki", "2Ki", "1Ch", "2Ch", "Ezr",
        "Ne", "Es", "Job", "Ps", "Pr", "Ec", "So", "Isa", "Jer", "La", "Eze", "Da", "Ho", "Joe", "Am", "Ob",
        "Jon", "Mic", "Na", "Hab", "Zep", "Hag", "Zec", "Mal", "Mt", "Mr", "Lu", "Joh", "Ac", "Ro", "1Co",
        "2Co", "Ga", "Eph", "Php", "Col", "1Th", "2Th", "1Ti", "2Ti", "Tit", "Phm", "Heb", "Jas", "1Pe",
        "2Pe", "1Jo", "2Jo", "3Jo", "Jude", "Re",
    ];

    private static readonly Dictionary<string, int> Ordinals = Abbreviations
        .Select((name, index) => (name, index))
        .ToDictionary(book => book.name, book => book.index + 1, StringComparer.Ordinal);

    public static (List<CrossReferenceRow> Rows, int Unread) Read(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"The Treasury of Scripture Knowledge is read from {folder}, which is not there. Run " +
                "scripts/fetch-tsk.ps1, or point Dataset:ResourcesPath at a corpus that has it.");
        }

        var rows = new List<CrossReferenceRow>(600_000);
        var unread = 0;
        foreach (var ((book, chapter, verse), markup) in SwordModule.Verses(folder)
                     .OrderBy(entry => entry.Key))
        {
            var from = new VerseAddress(book, chapter, verse);
            var (read, missed) = Entry(from, markup);
            unread += missed;
            rows.AddRange(read.Select((row, index) => row with { Rank = index + 1 }));
        }

        return (rows, unread);
    }

    /// <summary>One verse's entry: its references in printed order, each under its catchword, and the pieces that did not read.</summary>
    internal static (List<CrossReferenceRow> Rows, int Unread) Entry(VerseAddress from, string markup)
    {
        var rows = new List<CrossReferenceRow>();
        var unread = 0;
        string? catchword = null;
        var seen = new HashSet<(VerseAddress, VerseAddress?)>();
        foreach (var line in LineBreak().Split(markup))
        {
            var references = Reference().Matches(line);
            if (references.Count == 0)
            {
                var words = WebUtility.HtmlDecode(Tag().Replace(line, string.Empty)).Trim();
                if (words.Length > 0 && !line.Contains("passage=", StringComparison.Ordinal))
                {
                    catchword = Catchword(words);
                }

                continue;
            }

            foreach (Match reference in references)
            {
                var (targets, missed) = Targets(from, WebUtility.HtmlDecode(reference.Groups["body"].Value));
                unread += missed;
                foreach (var (to, end) in targets)
                {
                    if (to != from && seen.Add((to, end)))
                    {
                        rows.Add(new CrossReferenceRow(from, to, end, 0, Note: catchword));
                    }
                }
            }
        }

        return (rows, unread);
    }

    /// <summary>The catchword as the verse has it, without the full stop the Treasury closes it with.</summary>
    private static string? Catchword(string words)
    {
        var stop = words.IndexOf('.', StringComparison.Ordinal);
        var word = (stop < 0 ? words : words[..stop]).Trim();
        return word.Length is > 0 and <= LongestCatchword ? word : null;
    }

    /// <summary>
    /// The verses one <c>scripRef</c> names. Pieces are separated by semicolons, and within a piece a
    /// comma adds another verse or run to the chapter before it; the Treasury's own remarks — <c>*marg:</c>,
    /// <c>*Gr:</c> — are not references and are skipped.
    /// </summary>
    internal static (List<(VerseAddress To, VerseAddress? End)> Targets, int Unread) Targets(
        VerseAddress from,
        string body)
    {
        var targets = new List<(VerseAddress, VerseAddress?)>();
        var unread = 0;
        var book = from.Book;
        var chapter = from.Chapter;
        foreach (var raw in body.Split(';'))
        {
            var piece = raw.Trim();
            var remark = piece.IndexOf('*', StringComparison.Ordinal);
            if (remark >= 0)
            {
                piece = piece[..remark].Trim();
            }

            if (piece.Length == 0)
            {
                continue;
            }

            // A comma before a book is the Treasury's slip for a semicolon: "39:2, Jer 25:22".
            foreach (var part in BookStart().Split(piece))
            {
                var text = part.Trim().Trim(',').Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var named = Book().Match(text);
                if (named.Success)
                {
                    if (!Ordinals.TryGetValue(named.Groups["book"].Value, out book))
                    {
                        unread++;
                        book = from.Book;
                        continue;
                    }

                    text = text[named.Length..].Trim();
                }

                foreach (var item in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var verses = Verses().Match(item);
                    if (!verses.Success)
                    {
                        unread++;
                        continue;
                    }

                    if (verses.Groups["chapter"].Success)
                    {
                        chapter = Number(verses.Groups["chapter"]);
                    }

                    var to = new VerseAddress(book, chapter, Number(verses.Groups["verse"]));
                    VerseAddress? end = null;
                    if (verses.Groups["last"].Success)
                    {
                        if (verses.Groups["lastChapter"].Success)
                        {
                            chapter = Number(verses.Groups["lastChapter"]);
                        }

                        var last = new VerseAddress(book, chapter, Number(verses.Groups["last"]));
                        if (last.CompareTo(to) < 0)
                        {
                            unread++;
                            continue;
                        }

                        end = last == to ? null : last;
                    }

                    targets.Add((to, end));
                }
            }
        }

        return (targets, unread);
    }

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"<br\s*/?>")]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<scripRef>(?<body>.*?)</scripRef>", RegexOptions.Singleline)]
    private static partial Regex Reference();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^(?<book>[123]?[A-Z][a-z]*)\s+")]
    private static partial Regex Book();

    [GeneratedRegex(@",\s*(?=[123]?[A-Z][a-z]*\s+\d)")]
    private static partial Regex BookStart();

    [GeneratedRegex(@"^(?:(?<chapter>\d+):)?(?<verse>\d+)(?:-(?:(?<lastChapter>\d+):)?(?<last>\d+))?$")]
    private static partial Regex Verses();
}
