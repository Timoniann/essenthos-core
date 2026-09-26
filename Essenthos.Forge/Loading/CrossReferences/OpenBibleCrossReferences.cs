using System.Globalization;

namespace Essenthos.Core.Loading.CrossReferences;

/// <summary>
/// OpenBible.info's cross references: one line per pair, <c>Gen.1.1	Rom.1.19-Rom.1.20	59</c>, in
/// OSIS book names and the English numbering, which is the shared frame's.
///
/// <para>
/// Each verse's references are ranked by their votes, most first; the file's own order breaks ties,
/// so the ranking is the same on every load of the same bytes.
/// </para>
/// </summary>
internal static class OpenBibleCrossReferences
{
    public const string Folder = "OpenBibleCrossReferences";

    public const string FileName = "cross_references.txt";

    private const char Column = '\t';

    private const char Part = '.';

    private const char Through = '-';

    /// <summary>The OSIS names the file writes, in canonical order: the ordinal is the index plus one.</summary>
    private static readonly string[] OsisBooks =
    [
        "Gen", "Exod", "Lev", "Num", "Deut", "Josh", "Judg", "Ruth", "1Sam", "2Sam", "1Kgs", "2Kgs",
        "1Chr", "2Chr", "Ezra", "Neh", "Esth", "Job", "Ps", "Prov", "Eccl", "Song", "Isa", "Jer", "Lam",
        "Ezek", "Dan", "Hos", "Joel", "Amos", "Obad", "Jonah", "Mic", "Nah", "Hab", "Zeph", "Hag", "Zech",
        "Mal", "Matt", "Mark", "Luke", "John", "Acts", "Rom", "1Cor", "2Cor", "Gal", "Eph", "Phil", "Col",
        "1Thess", "2Thess", "1Tim", "2Tim", "Titus", "Phlm", "Heb", "Jas", "1Pet", "2Pet", "1John", "2John",
        "3John", "Jude", "Rev",
    ];

    private static readonly Dictionary<string, int> Ordinals = OsisBooks
        .Select((name, index) => (name, index))
        .ToDictionary(book => book.name, book => book.index + 1, StringComparer.Ordinal);

    /// <summary>Every pair the file states, ranked within its verse; lines that do not read are counted, not guessed at.</summary>
    public static (List<CrossReferenceRow> Rows, int Unread) Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"OpenBible's cross references are read from {path}, which is not there. Run " +
                "scripts/fetch-openbible-cross-references.ps1, or point Dataset:ResourcesPath at a corpus that has them.",
                path);
        }

        var read = new List<CrossReferenceRow>(350_000);
        var unread = 0;
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (Parse(line) is { } row)
            {
                read.Add(row);
            }
            else if (line.Length > 0)
            {
                unread++;
            }
        }

        var ranked = read
            .Select((row, order) => (row, order))
            .GroupBy(entry => entry.row.From)
            .SelectMany(verse => verse
                .OrderByDescending(entry => entry.row.Votes)
                .ThenBy(entry => entry.order)
                .Select((entry, rank) => entry.row with { Rank = rank + 1 }))
            .ToList();
        return (ranked, unread);
    }

    internal static CrossReferenceRow? Parse(string line)
    {
        var columns = line.Split(Column);
        if (columns.Length < 3
            || Verse(columns[0]) is not { } from
            || !int.TryParse(columns[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var votes))
        {
            return null;
        }

        var target = columns[1].Split(Through);
        if (target.Length > 2 || Verse(target[0]) is not { } to)
        {
            return null;
        }

        VerseAddress? end = null;
        if (target.Length == 2)
        {
            if (Verse(target[1]) is not { } last || last.CompareTo(to) < 0)
            {
                return null;
            }

            end = last == to ? null : last;
        }

        return new CrossReferenceRow(from, to, end, 0, votes);
    }

    private static VerseAddress? Verse(string osis)
    {
        var parts = osis.Split(Part);
        return parts.Length == 3
               && Ordinals.TryGetValue(parts[0], out var book)
               && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var chapter)
               && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var verse)
            ? new VerseAddress(book, chapter, verse)
            : null;
    }
}
