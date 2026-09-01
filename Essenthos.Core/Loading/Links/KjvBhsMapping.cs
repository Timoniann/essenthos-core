using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Links;

/// <param name="Clause">
/// The clause the file puts this word in. It is what lets a conjunction be matched across the
/// disagreement in word order: Hebrew hangs its <em>and</em> on the verb, English puts it first, and
/// the clause is the span inside which they are the same <em>and</em>.
/// </param>
/// <param name="Gloss">
/// The English gloss the file gives this Hebrew word. It is what makes the join checkable: BHSA
/// carries the same glosses, so a verse whose gloss sequence agrees is a verse whose words are
/// lined up, and one that disagrees is reported rather than guessed at.
/// </param>
internal sealed record HebrewEntry(string Strong, string Clause, int Position, string Gloss);

/// <param name="Words">
/// The English words this segment carries, which may be none: where two Hebrew words are rendered
/// by one English phrase, the file gives the phrase to the first and leaves the second with an
/// empty segment. That is not a defect in the file — it is a linear format saying a thing that is
/// not linear — and it is exactly the two-to-one shape a link is for.
/// </param>
internal sealed record EnglishSegment(IReadOnlyList<string> Words, HebrewEntry? RendersHebrew);

internal sealed record MappingRecord(
    int Book,
    int Chapter,
    int Verse,
    IReadOnlyList<HebrewEntry> Hebrew,
    IReadOnlyList<EnglishSegment> English);

/// <summary>
/// The King James to BHS mapping file: for each verse, the Hebrew words in order and the English
/// text with a marker after each phrase naming the Hebrew word it renders.
///
/// The correspondences are **stated by this file**, so the links it produces carry
/// <c>stated-by-source</c> and no confidence. That is the strongest evidence in the project and it
/// should look different from everything else.
/// </summary>
internal static partial class KjvBhsMapping
{
    /// <summary>The Hebrew side has two columns; the English side has five.</summary>
    private const int HebrewColumns = 2;

    private const int EnglishColumns = 5;

    public static IReadOnlyList<MappingRecord> Read(string path)
    {
        var hebrew = new Dictionary<string, string>(24_000);
        var english = new Dictionary<string, string[]>(24_000);

        foreach (var line in File.ReadLines(path))
        {
            var columns = line.Split('\t');
            switch (columns.Length)
            {
                case HebrewColumns:
                    hebrew[columns[0]] = columns[1];
                    break;
                case EnglishColumns:
                    english[columns[0]] = columns;
                    break;
            }
        }

        var records = new List<MappingRecord>(english.Count);
        foreach (var (id, columns) in english)
        {
            if (!hebrew.TryGetValue(id, out var hebrewLine))
            {
                continue;
            }

            var entries = HebrewEntries(hebrewLine);
            records.Add(new MappingRecord(
                int.Parse(columns[1]),
                int.Parse(columns[2]),
                int.Parse(columns[3]),
                entries,
                EnglishSegments(columns[4], entries)));
        }

        return records;
    }

    private static List<HebrewEntry> HebrewEntries(string line)
    {
        var entries = new List<HebrewEntry>(24);
        foreach (Match match in HebrewWord().Matches(line))
        {
            var parts = match.Groups[1].Value.Split('｜');
            if (parts.Length >= 5 && int.TryParse(parts[2], out var position))
            {
                entries.Add(new HebrewEntry(parts[0], parts[1], position, parts[4]));
            }
        }

        return entries;
    }

    /// <summary>
    /// Splits the English text at its markers. Each marker closes the run of text before it, and
    /// names the Hebrew word that run renders; text after the last marker renders nothing named.
    /// </summary>
    private static List<EnglishSegment> EnglishSegments(string text, IReadOnlyList<HebrewEntry> hebrew)
    {
        // A handful of verses list the same position twice. The first occurrence is the one the
        // markers before it refer to, and a marker naming a repeated position is ambiguous — it is
        // resolved to the first and the verse is checked as a whole afterwards.
        var byPosition = new Dictionary<int, HebrewEntry>(hebrew.Count);
        foreach (var entry in hebrew)
        {
            byPosition.TryAdd(entry.Position, entry);
        }
        var segments = new List<EnglishSegment>(24);
        var read = 0;

        foreach (Match match in Marker().Matches(text))
        {
            var before = text[read..match.Index];
            read = match.Index + match.Length;

            var parts = match.Groups[1].Value.Split('｜');
            HebrewEntry? entry = null;
            if (parts.Length >= 2 && int.TryParse(parts[1], out var position))
            {
                byPosition.TryGetValue(position, out entry);
            }

            segments.Add(new EnglishSegment(Words(before), entry));
        }

        if (read < text.Length)
        {
            var tail = Words(text[read..]);
            if (tail.Count > 0)
            {
                segments.Add(new EnglishSegment(tail, null));
            }
        }

        return segments;
    }

    /// <summary>
    /// The words of one English run. The italics the file marks supplied words with are not part of
    /// any word, and an apostrophe or hyphen inside a word is.
    /// </summary>
    public static IReadOnlyList<string> Words(string text) =>
        [.. EnglishWord().Matches(Italics().Replace(text, string.Empty)).Select(m => m.Value)];

    [GeneratedRegex(@"〔([^〕]*)〕")]
    private static partial Regex HebrewWord();

    [GeneratedRegex(@"〈[^＝〉]*＝([^〉]*)〉")]
    private static partial Regex Marker();

    [GeneratedRegex(@"</?i>")]
    private static partial Regex Italics();

    [GeneratedRegex(@"[A-Za-z0-9]+(?:['’-][A-Za-z0-9]+)*")]
    private static partial Regex EnglishWord();
}
