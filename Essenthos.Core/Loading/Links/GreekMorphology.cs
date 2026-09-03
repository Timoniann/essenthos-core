using System.Text.Json;

namespace Essenthos.Core.Loading.Links;

internal enum GreekPart
{
    Other,
    Article,
    Noun,
    Adjective,
    Pronoun,
    Verb,
    Preposition,
}

internal enum GreekCase
{
    None,
    Nominative,
    Genitive,
    Dative,
    Accusative,
    Vocative,
}

internal enum GreekMood
{
    None,
    Indicative,
    Subjunctive,
    Optative,
    Imperative,
    Infinitive,
    Participle,
}

internal enum GreekTense
{
    None,
    Present,
    Imperfect,
    Future,
    Aorist,
    Perfect,
    Pluperfect,
}

/// <param name="Number">The source's own letter: <c>S</c> singular, <c>P</c> plural.</param>
/// <param name="Gender">The source's own letter: <c>M</c>, <c>F</c> or <c>N</c>.</param>
/// <param name="Person">1, 2 or 3, and 0 where the form does not carry one.</param>
internal readonly record struct GreekMorphology(
    GreekPart Part,
    GreekCase Case,
    GreekMood Mood,
    GreekTense Tense,
    char Number,
    char Gender,
    int Person)
{
    private const char Unmarked = '\0';

    /// <summary>
    /// Reads the Robinson tag both Greek witnesses carry — <c>T-ASM</c>, <c>N-GSF</c>,
    /// <c>V-2AAI-3S</c>. Nestle stores it under <c>form</c> and the Textus Receptus under
    /// <c>robinson</c>; the alphabet is the same one.
    ///
    /// The parsed <c>case</c> key Nestle also carries is not read: it holds the gender for a fifth
    /// of the corpus and never once says nominative.
    /// </summary>
    public static GreekMorphology Of(JsonDocument? morphology)
    {
        if (morphology is null)
        {
            return default;
        }

        var root = morphology.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        if (root.TryGetProperty("form", out var form) && form.ValueKind == JsonValueKind.String)
        {
            return Parse(form.GetString());
        }

        return root.TryGetProperty("robinson", out var robinson) && robinson.ValueKind == JsonValueKind.String
            ? Parse(robinson.GetString())
            : default;
    }

    public static GreekMorphology Parse(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return default;
        }

        var segments = code.Split('-');
        var part = PartOfSpeech(segments[0]);

        if (part == GreekPart.Verb)
        {
            return Verb(segments);
        }

        var (grammaticalCase, number, gender, person) = Nominal(segments.Length > 1 ? segments[1] : string.Empty);
        return new GreekMorphology(part, grammaticalCase, GreekMood.None, GreekTense.None, number, gender, person);
    }

    /// <summary>
    /// Whether an article could belong to this word: they have to be the same case, number and
    /// gender, and a word that states none of the three agrees with nothing.
    /// </summary>
    public bool Agrees(GreekMorphology other) =>
        Case != GreekCase.None && Case == other.Case
        && Number != Unmarked && Number == other.Number
        && Gender != Unmarked && Gender == other.Gender;

    public bool IsFinite => Mood is GreekMood.Indicative or GreekMood.Subjunctive
        or GreekMood.Optative or GreekMood.Imperative;

    private static GreekPart PartOfSpeech(string code) => code switch
    {
        "T" => GreekPart.Article,
        "N" => GreekPart.Noun,
        "A" => GreekPart.Adjective,
        "V" => GreekPart.Verb,
        "PREP" => GreekPart.Preposition,

        // Every shape of pronoun Robinson distinguishes: personal, demonstrative, relative,
        // reflexive, possessive, interrogative, indefinite, correlative and the two mixed classes.
        "P" or "D" or "R" or "F" or "S" or "I" or "X" or "K" or "C" or "Q" => GreekPart.Pronoun,
        _ => GreekPart.Other,
    };

    /// <summary>
    /// A verb tag is tense, voice and mood, sometimes behind the digit that marks a second aorist,
    /// and then either a person and number or — for a participle — a case, number and gender.
    /// </summary>
    private static GreekMorphology Verb(string[] segments)
    {
        if (segments.Length < 2)
        {
            return new GreekMorphology(GreekPart.Verb, default, default, default, Unmarked, Unmarked, 0);
        }

        var parsing = segments[1].AsSpan().TrimStart("0123456789");
        var tense = parsing.Length > 0 ? ToTense(parsing[0]) : GreekTense.None;
        var mood = parsing.Length > 2 ? ToMood(parsing[2]) : GreekMood.None;

        if (segments.Length < 3)
        {
            return new GreekMorphology(GreekPart.Verb, default, mood, tense, Unmarked, Unmarked, 0);
        }

        if (mood == GreekMood.Participle)
        {
            var (grammaticalCase, number, gender, _) = Nominal(segments[2]);
            return new GreekMorphology(GreekPart.Verb, grammaticalCase, mood, tense, number, gender, 0);
        }

        var ending = segments[2];
        var person = ending.Length > 0 && char.IsAsciiDigit(ending[0]) ? ending[0] - '0' : 0;
        return new GreekMorphology(
            GreekPart.Verb, default, mood, tense, ending.Length > 1 ? ending[1] : Unmarked, Unmarked, person);
    }

    /// <summary>
    /// A nominal tag ends in case, number and gender — <c>GSF</c> — or, where the form has no
    /// gender to state, in case and number alone: the personal pronoun writes <c>1NS</c>, person
    /// first. What is left over is one of the indeclinables, <c>PRI</c>, <c>NUI</c>, <c>OI</c>,
    /// <c>LI</c>, which state nothing to agree with.
    /// </summary>
    private static (GreekCase Case, char Number, char Gender, int Person) Nominal(string code)
    {
        var person = code.Length > 0 && char.IsAsciiDigit(code[0]) ? code[0] - '0' : 0;

        if (code.Length >= 3
            && ToCase(code[^3]) != GreekCase.None && IsNumber(code[^2]) && IsGender(code[^1]))
        {
            return (ToCase(code[^3]), code[^2], code[^1], person);
        }

        if (code.Length >= 2 && ToCase(code[^2]) != GreekCase.None && IsNumber(code[^1]))
        {
            return (ToCase(code[^2]), code[^1], Unmarked, person);
        }

        return (GreekCase.None, Unmarked, Unmarked, person);
    }

    private static bool IsNumber(char value) => value is 'S' or 'P';

    private static bool IsGender(char value) => value is 'M' or 'F' or 'N';

    private static GreekCase ToCase(char value) => value switch
    {
        'N' => GreekCase.Nominative,
        'G' => GreekCase.Genitive,
        'D' => GreekCase.Dative,
        'A' => GreekCase.Accusative,
        'V' => GreekCase.Vocative,
        _ => GreekCase.None,
    };

    private static GreekMood ToMood(char value) => value switch
    {
        'I' => GreekMood.Indicative,
        'S' => GreekMood.Subjunctive,
        'O' => GreekMood.Optative,
        'M' => GreekMood.Imperative,
        'N' => GreekMood.Infinitive,
        'P' => GreekMood.Participle,
        _ => GreekMood.None,
    };

    private static GreekTense ToTense(char value) => value switch
    {
        'P' => GreekTense.Present,
        'I' => GreekTense.Imperfect,
        'F' => GreekTense.Future,
        'A' => GreekTense.Aorist,
        'R' => GreekTense.Perfect,
        'L' => GreekTense.Pluperfect,
        _ => GreekTense.None,
    };
}
