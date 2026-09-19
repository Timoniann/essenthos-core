using System.Text.RegularExpressions;
using Essenthos.Core.Strong;

namespace Essenthos.Core.StepBible;

/// <param name="Entry">
/// The lexicon's own identifier, from its second column: <c>G0001G</c> and <c>G0001H</c> are two
/// entries filed under G1, Alpha and the interjection <em>ah!</em>.
/// </param>
/// <param name="StrongNumber">The number the entry is filed under, as the corpus writes one.</param>
/// <param name="Lemmas">
/// Every dictionary form the entry prints. Most print one; 67 print two or three — <c>ἄρρην, ἄρσην</c>,
/// <c>γαμίσκω, γαμίζω</c> — which are one lexeme spelled more than one way.
/// </param>
internal sealed record BriefLexiconEntry(string Entry, string StrongNumber, IReadOnlyList<string> Lemmas, string Gloss);

/// <summary>
/// STEPBible's Translators Brief lexicon of Extended Strongs for Greek — TBESG — which gives each
/// Greek lexeme a gloss in a word or two, written by Tyndale House scholars.
///
/// It is read for that gloss and nothing else. The file also carries Abbott-Smith's full entry for
/// each lexeme, and it is not taken: what a reader hovering a word wants is the short meaning, and
/// Strong's own definition already stands behind every number for anyone who wants more.
///
/// <para>
/// Its reach beyond Strong is the reason it is here. Strong catalogued the New Testament's
/// vocabulary; the Septuagint's is half as large again, and TBESG numbers those words too, from
/// G6000 and in the G20000s, so a Greek Old Testament word Strong never saw still has a meaning.
/// </para>
///
/// <para>
/// The file is a long preamble, then one entry a line in eight tab-separated columns: the number,
/// the entry, the unified number, the Greek, a transliteration, a morphology code, the gloss and
/// the full entry. Only a line whose first column is a Greek number is an entry; the preamble
/// documents the columns in prose and in examples that must not be read as entries.
/// </para>
/// </summary>
internal static partial class BriefGreekLexicon
{
    private const int NumberColumn = 0;
    private const int EntryColumn = 1;
    private const int GreekColumn = 3;
    private const int GlossColumn = 6;

    /// <summary>The narrowest line that holds a gloss.</summary>
    private const int Columns = GlossColumn + 1;

    /// <summary>
    /// What the file leaves at the end of a gloss that is not part of it — the full stop of
    /// <c>brave.</c>, the stray comma of <c>ill-advised,.</c>.
    /// </summary>
    private static readonly char[] Trailing = [',', '.', ';', ' '];

    [GeneratedRegex(@"^G\d+$")]
    private static partial Regex GreekNumber { get; }

    public static IEnumerable<BriefLexiconEntry> Read(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var cells = line.Split('\t');
            if (cells.Length < Columns || !GreekNumber.IsMatch(cells[NumberColumn]))
            {
                continue;
            }

            var number = StrongNumbers.Normalize(cells[NumberColumn])
                         ?? throw new InvalidOperationException(
                             $"TBESG files an entry under \"{cells[NumberColumn]}\", which is not a Strong number. "
                             + "Read the line before loading the file: every entry is found by its number.");

            var entry = cells[EntryColumn].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var lemmas = cells[GreekColumn]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var gloss = cells[GlossColumn].Trim().TrimEnd(Trailing);

            if (entry is null || lemmas.Length == 0 || gloss.Length == 0)
            {
                continue;
            }

            yield return new BriefLexiconEntry(entry, number, lemmas, gloss);
        }
    }
}
