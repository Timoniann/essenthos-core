using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Strong;

/// <summary>
/// Brady Stephenson's <c>HebrewStrongs.csv</c> in BibleData: Strong's Hebrew restructured, one row per
/// number, with what Strong's own file does not carry — a part of speech, a gender, an occurrence
/// count and a first verse — and the roots of each entry listed as numbers.
///
/// <para>
/// The roots are up to three numbers side by side, not a chain: H62 Abel-beth-maachah lists H58,
/// H1004 and H4601, the three words its name is made of, as Strong derives it. They are the
/// compiler's reading of Strong's derivation, kept as <c>root</c> rows under his name beside
/// Strong's own reading, never as Strong's.
/// </para>
/// </summary>
internal static partial class CompiledHebrewStrongs
{
    public const string Source = BibleDataLoader.Source;

    public const string File = "HebrewStrongs.csv";

    private static readonly string[] RootColumns = ["first_root_number", "second_root_number", "third_root_number"];

    public sealed record Compiled(IReadOnlyList<StrongProfile> Profiles, IReadOnlyList<StatedRelation> Roots, int UnreadFirstVerses);

    public static Compiled Read(string path)
    {
        var profiles = new List<StrongProfile>(8_700);
        var roots = new List<StatedRelation>(7_300);
        var unread = 0;

        foreach (var row in Loading.Encyclopedia.Csv.Read(path))
        {
            if (!int.TryParse(row["strongs_number"], out var digits))
            {
                continue;
            }

            var number = $"H{digits}";
            var first = FirstVerse(row["first_occurrence"]);
            if (first is null && row["first_occurrence"].Length > 0)
            {
                unread++;
            }

            profiles.Add(new StrongProfile
            {
                StrongNumber = number,
                Language = row["language"] == "A" ? "arc" : "hbo",
                PartOfSpeech = Stated(row["part_of_speech"]),
                Gender = Stated(row["gender"]),
                Occurrences = int.TryParse(row["occurrences"], out var occurrences) ? occurrences : 0,
                FirstBook = first?.Book,
                FirstChapter = first?.Chapter,
                FirstVerse = first?.Verse,
                Source = Source,
            });

            var statement = Statement(row["gloss"]);
            var position = 0;
            foreach (var column in RootColumns)
            {
                if (int.TryParse(row[column], out var root))
                {
                    roots.Add(new StatedRelation(number, $"H{root}", StrongRelationKinds.Root, false, ++position, statement));
                }
            }
        }

        return new Compiled(profiles, roots, unread);
    }

    /// <summary><c>GEN 2:24</c>, as the compiler writes a verse, at its canonical book.</summary>
    internal static (int Book, int Chapter, int Verse)? FirstVerse(string written)
    {
        if (Verse().Match(written.Trim()) is not { Success: true } match
            || BibleBookAbbreviation.GetAbbreviation(match.Groups["book"].Value) is not { } book)
        {
            return null;
        }

        return (book.Ordinal, int.Parse(match.Groups["chapter"].Value), int.Parse(match.Groups["verse"].Value));
    }

    /// <summary>
    /// The line of the gloss the roots were read from: the bracketed derivation, which is Strong's
    /// sentence as the compiler copied it, or his own list where the entry has none.
    /// </summary>
    private static string Statement(string gloss)
    {
        var lines = gloss.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var derivation = lines.FirstOrDefault(line => line.StartsWith('[') && line.EndsWith(']'));
        return derivation is not null
            ? derivation[1..^1].Trim()
            : lines.FirstOrDefault(line => line.StartsWith("Root(s):", StringComparison.Ordinal)) ?? "Root(s)";
    }

    private static string? Stated(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^(?<book>[1-3]?[A-Z]{2,3})\s+(?<chapter>\d+):(?<verse>\d+)$")]
    private static partial Regex Verse();
}
