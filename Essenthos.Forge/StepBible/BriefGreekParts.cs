using Essenthos.Core.Database.Entities;
using Essenthos.Core.Strong;

namespace Essenthos.Core.StepBible;

/// <summary>
/// The part of speech and gender TBESG gives each Greek lexeme, which Strong's Greek dictionary does
/// not: its sixth column, <c>Language:Type-Gender-Extra</c> — <c>G:N-F</c> for πίστις, <c>G:V</c>
/// for ἀγαπάω, <c>N:N-M-P</c> for a man's name.
///
/// <para>
/// Written as the compiler of the Hebrew profiles writes his (<em>noun</em>, <em>feminine</em>;
/// <em>noun proper</em>; <em>pronoun demonstrative</em>), so the entry page reads both the same
/// way, and under TBESG's name, never Strong's. A number filed under more than one entry — G1 is
/// both Alpha and the interjection <em>ah!</em> — takes its first, which is the one the number was
/// given for. A code this does not know gives no profile rather than a guess.
/// </para>
/// </summary>
internal static class BriefGreekParts
{
    private const int NumberColumn = 0;
    private const int MorphColumn = 5;

    private const string Greek = "grc";

    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        ["N"] = "noun",
        ["V"] = "verb",
        ["A"] = "adjective",
        ["ADV"] = "adverb",
        ["PREP"] = "preposition",
        ["CONJ"] = "conjunction",
        ["COND"] = "conditional",
        ["INJ"] = "injunction",
        ["PRT"] = "particle",
        ["T"] = "article",
        ["D"] = "pronoun demonstrative",
        ["P"] = "personal pronoun",
        ["R"] = "pronoun relative",
        ["F"] = "pronoun reflexive",
        ["S"] = "pronoun possessive",
        ["C"] = "pronoun reciprocal",
        ["K"] = "pronoun correlative",
        ["Q"] = "interrogative",
        ["I"] = "interrogative",
        ["X"] = "pronoun indefinite",
    };

    private static readonly Dictionary<string, string> Genders = new(StringComparer.Ordinal)
    {
        ["M"] = "masculine",
        ["F"] = "feminine",
        ["N"] = "neuter",
    };

    /// <summary>One profile per Greek number the lexicon gives a code it can read.</summary>
    public static IReadOnlyList<StrongProfile> Read(string path, string source)
    {
        var profiles = new Dictionary<string, StrongProfile>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var cells = line.Split('\t');
            if (cells.Length <= MorphColumn
                || !cells[NumberColumn].StartsWith(StrongNumbers.Greek)
                || StrongNumbers.Normalize(cells[NumberColumn]) is not { } number
                || profiles.ContainsKey(number)
                || Of(cells[MorphColumn]) is not { } part)
            {
                continue;
            }

            profiles[number] = new StrongProfile
            {
                StrongNumber = number,
                Language = Greek,
                PartOfSpeech = part.PartOfSpeech,
                Gender = part.Gender,
                Source = source,
            };
        }

        return [.. profiles.Values];
    }

    /// <summary>
    /// <c>G:N-F</c> as <em>noun</em>, <em>feminine</em>. Where the code offers two readings
    /// (<c>G:A / G:ADV</c>, <c>G:PRT-N+G:CONJ</c>) the first is the lexeme's.
    /// </summary>
    internal static (string PartOfSpeech, string? Gender)? Of(string code)
    {
        var first = code.Split(['/', '+'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        var colon = first?.IndexOf(':') ?? -1;
        if (first is null || colon < 0)
        {
            return null;
        }

        var name = first[..colon] == "N";
        var parts = first[(colon + 1)..].Split('-');
        if (!Types.TryGetValue(parts[0], out var type))
        {
            return null;
        }

        var gender = parts[0] is "N" or "A" && parts.Length > 1 ? Genders.GetValueOrDefault(parts[1]) : null;
        var extra = parts.Length > 2 ? parts[2] : string.Empty;
        var part = (parts[0], parts.Length > 1 ? parts[1] : string.Empty, extra) switch
        {
            ("N", _, "P") when name => "noun proper",
            ("N", _, "T") when name => "noun proper",
            ("N", _, "L") when name => "noun location",
            ("N", _, "LG" or "PG") when name => "noun gentilic",
            ("A", _, "LG" or "PG") when name => "adjective gentilic",
            ("N", _, _) when name => "noun proper",
            ("N", "PRI", _) => "noun proper",
            ("N", "LI", _) => "letter",
            ("A", "NUI", _) => "number",
            ("PRT", "N", _) => "particle negative",
            ("PRT", "I", _) or ("ADV", "I", _) => "interrogative",
            _ => type,
        };

        return (part, gender);
    }
}
