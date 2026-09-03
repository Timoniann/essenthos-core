using System.Globalization;

namespace Essenthos.Core.Berean;

/// <param name="OriginalOrder">
/// Where this word stands in the original — <c>Heb Sort</c> for a Hebrew or Aramaic row,
/// <c>Greek Sort</c> for a Greek one. It is the file's own running word number over the whole
/// testament, so it orders a verse and nothing more is asked of it.
///
/// <b>It is not an integer.</b> Forty Hebrew rows carry a half — <c>8132.5</c> — which is how the
/// file inserts a word between two it had already numbered. Read as an integer they all become zero
/// and sort to the front of their verse, which puts every link in those verses on the wrong word and
/// looks exactly like a correct load.
/// </param>
/// <param name="EnglishOrder">
/// Where the English that renders it stands in the Berean sentence. It differs from
/// <paramref name="OriginalOrder"/>, which is what makes this a mapping rather than an interlinear:
/// Genesis 1:1 has אֱלֹהִים third in the Hebrew and *God* second in the English.
/// </param>
/// <param name="English">
/// The Berean phrase rendering this word, as the file writes it — with its brackets around supplied
/// words, its <c>-</c> for a word the English does not render, and its <c>. . .</c> where a
/// rendering has moved elsewhere in the verse.
/// </param>
internal readonly record struct BereanRow(
    double OriginalOrder,
    int EnglishOrder,
    int VerseIndex,
    string Language,
    string Original,
    string Sigla,
    string StrongNumber,
    string Reference,
    string English)
{
    public bool IsGreek => Language is "Greek";

    /// <summary>
    /// Whether the word stands in the base text rather than in one edition's apparatus. The file
    /// wraps a variant reading in its edition's siglum — <c>{TR}</c>, <c>⧼RP⧽</c>, <c>(WH)</c>,
    /// <c>〈NE〉</c>, <c>[NA]</c>, <c>‹SBL›</c>, <c>[[ECM]]</c> — and leaves a word every edition
    /// carries unmarked. 934 of the 138,131 Greek words are marked.
    /// </summary>
    public bool InTheBaseText => string.Equals(Original, Sigla, StringComparison.Ordinal);
}

/// <summary>
/// The Berean Standard Bible translation tables: one row per original-language word, with the
/// English that renders it.
///
/// 754,648 rows, 85 MB, and the shape that matters is that a verse is a run of rows whose
/// <c>Verse</c> index is the same — a running number, not a reference. Only a verse's first row
/// carries the reference, so the reference is remembered as the file is walked; and a verse is
/// padded out to a fixed width with empty rows, so Matthew 1:1 is eight words and ten blanks.
///
/// <para>
/// It streams, because nothing here needs two verses at once and holding 754,648 rows to answer a
/// question about one of them is a cost with no reader.
/// </para>
/// </summary>
internal static class BereanTable
{
    private const int OriginalIsHebrew = 0;
    private const int OriginalIsGreek = 1;
    private const int English = 2;
    private const int VerseIndex = 3;
    private const int Language = 4;
    private const int Word = 5;
    private const int WithSigla = 6;
    private const int StrongHebrew = 10;
    private const int StrongGreek = 11;
    private const int Reference = 12;
    private const int Rendering = 18;

    /// <summary>The widest column this reads. A shorter row is truncated and cannot be trusted.</summary>
    private const int Columns = 19;

    /// <summary>Each verse of the table in the order the file gives them, its rows in original order.</summary>
    public static IEnumerable<(string Reference, IReadOnlyList<BereanRow> Rows)> Verses(string path)
    {
        var rows = new List<BereanRow>(64);
        var reference = string.Empty;
        var verse = int.MinValue;

        foreach (var row in Read(path))
        {
            if (row.VerseIndex != verse && rows.Count > 0)
            {
                yield return (reference, Ordered(rows));
                rows = new List<BereanRow>(64);
                reference = string.Empty;
            }

            verse = row.VerseIndex;
            if (row.Reference.Length > 0)
            {
                reference = row.Reference;
            }

            rows.Add(row);
        }

        if (rows.Count > 0)
        {
            yield return (reference, Ordered(rows));
        }
    }

    private static List<BereanRow> Ordered(List<BereanRow> rows)
    {
        rows.Sort((a, b) => a.OriginalOrder.CompareTo(b.OriginalOrder));
        return rows;
    }

    private static IEnumerable<BereanRow> Read(string path)
    {
        using var reader = new StreamReader(path);
        reader.ReadLine();

        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            if (cells.Length < Columns)
            {
                continue;
            }

            var language = cells[Language].Trim();
            var greek = language is "Greek";

            // A padding row has no word of its own and nothing to say about one. Punctuation still
            // reaches the text through the published edition, which is where the text comes from.
            if (cells[Word].Trim().Length == 0)
            {
                continue;
            }

            yield return new BereanRow(
                Order(cells[greek ? OriginalIsGreek : OriginalIsHebrew]),
                Number(cells[English]),
                Number(cells[VerseIndex]),
                language,
                cells[Word].Trim(),
                cells[WithSigla].Trim(),
                Strong(cells[greek ? StrongGreek : StrongHebrew], greek),
                cells[Reference].Trim(),
                cells[Rendering]);
        }
    }

    /// <summary>
    /// The number as this corpus writes it: a language letter and no leading zeros, which is what
    /// <c>strong_entry</c> and every other text use. The table writes it bare and zero-padded.
    /// </summary>
    private static string Strong(string cell, bool greek)
    {
        var digits = cell.Trim().TrimStart('0');
        return digits.Length == 0 ? string.Empty : (greek ? "G" : "H") + digits;
    }

    /// <summary>
    /// A word's place in its verse, which the file writes with a half where it has inserted a word
    /// between two it had already numbered.
    /// </summary>
    private static double Order(string cell) =>
        double.TryParse(cell.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static int Number(string cell) =>
        int.TryParse(cell.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}
