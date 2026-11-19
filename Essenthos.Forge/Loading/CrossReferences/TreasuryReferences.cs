using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
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

    /// <param name="Entry">The verse whose entry holds the slip.</param>
    /// <param name="Written">The reference as the module writes it, once in that entry.</param>
    /// <param name="Read">The same reference as the verses it can only mean.</param>
    internal sealed record Correction(VerseAddress Entry, string Written, string Read);

    /// <summary>
    /// Slips in the module's references where what they name cannot be a verse and what they mean is plain, kept by hand
    /// as <c>reference</c> rows (own, the entry's verse key; before, the reference as written; after, as read; why).
    /// </summary>
    public const string CorrectionsProduct = "cross-references/tsk-corrections";

    public const string ReferenceKind = "reference";

    /// <summary>The store's corrections of the module's references; none where the store has no such file.</summary>
    public static IReadOnlyList<Correction> Corrections(ProductStore store)
    {
        if (!store.Exists(CorrectionsProduct))
        {
            return [];
        }

        using var reader = store.Open(CorrectionsProduct, TextCorrection.Schema);
        return
        [
            .. reader.ReadAll().Select(row => row.Kind == ReferenceKind && VerseKey.TryParse(row.Own, out var entry)
                && entry.ChapterLabel.Length + entry.VerseLabel.Length == 0 && row.Before is { Length: > 0 } written && row.After is { } read
                ? new Correction(new VerseAddress(entry.Book, entry.Chapter, entry.Verse), written, read)
                : throw new FormatException(
                    $"{CorrectionsProduct}: the row at {row.Own} is not a reference row naming the entry's verse, the reference as written and as read.")),
        ];
    }

    public static (List<CrossReferenceRow> Rows, int Unread) Read(string folder, IReadOnlyList<Correction> corrections)
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
            var (read, missed) = Entry(from, markup, corrections);
            unread += missed;
            rows.AddRange(read.Select((row, index) => row with { Rank = index + 1 }));
        }

        return (rows, unread);
    }

    /// <summary>One verse's entry: its references in printed order, each under its catchword, and the pieces that did not read.</summary>
    internal static (List<CrossReferenceRow> Rows, int Unread) Entry(VerseAddress from, string markup, IReadOnlyList<Correction> corrections)
    {
        var rows = new List<CrossReferenceRow>();
        var unread = 0;
        string? catchword = null;
        VerseAddress? before = null;
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
                    before = null;
                }

                continue;
            }

            foreach (Match reference in references)
            {
                var body = Corrected(from, WebUtility.HtmlDecode(reference.Groups["body"].Value), corrections);
                var (targets, missed) = Targets(from, body);

                // A line the module broke out of the one before it (Acts 7:5's "9:5; 10:11; 11:9; 34:4" after
                // "De 6:10,11") goes on in that line's book, where the verse's own book has no such verse.
                if (before is { } previous && !Book().IsMatch(body.TrimStart())
                    && targets.Any(t => !EnglishVerses.Has(t.To) || t.End is { } end && !EnglishVerses.Has(end))
                    && Targets(previous, body) is ({ Count: > 0 } continued, _) carried
                    && continued.All(t => EnglishVerses.Has(t.To) && (t.End is not { } last || EnglishVerses.Has(last))))
                {
                    (targets, missed) = carried;
                }

                unread += missed;
                if (targets.Count > 0)
                {
                    var (to, end) = targets[^1];
                    before = end ?? to;
                }
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

    private static string Corrected(VerseAddress from, string body, IReadOnlyList<Correction> corrections)
    {
        foreach (var correction in corrections)
        {
            if (correction.Entry == from)
            {
                body = body.Replace(correction.Written, correction.Read, StringComparison.Ordinal);
            }
        }

        return body;
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
