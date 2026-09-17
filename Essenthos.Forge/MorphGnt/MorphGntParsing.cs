namespace Essenthos.Core.MorphGnt;

/// <summary>
/// The eight-character parsing code, read into the vocabulary the rest of the corpus already uses
/// for Greek morphology — the words Nestle's own annotation writes, so that the two analyses can be
/// compared without a translation table standing between them.
///
/// <para>
/// Every slot is fixed by position and holds <c>-</c> where the word has no such feature. There is
/// nothing to parse and no group to find, which is the difference between this and the Robinson
/// codes elsewhere here: <c>ADV</c> came back accusative from one of those because a reader counted
/// hyphens, and no equivalent mistake is available in a fixed-width code.
/// </para>
/// </summary>
internal static class MorphGntParsing
{
    /// <summary>Person, tense, voice, mood, case, number, gender, degree.</summary>
    public const int Length = 8;

    private const int PersonSlot = 0;
    private const int TenseSlot = 1;
    private const int VoiceSlot = 2;
    private const int MoodSlot = 3;
    private const int CaseSlot = 4;
    private const int NumberSlot = 5;
    private const int GenderSlot = 6;
    private const int DegreeSlot = 7;

    /// <summary>
    /// The README documents four cases and the files write five. <c>V</c> stands 580 times, and
    /// every one of those words is a vocative in Nestle 1904 as well — so the undocumented letter
    /// is not a defect in the data, it is a gap in the README, and a reader that dropped it would
    /// lose the corroboration of the whole vocative.
    /// </summary>
    public static string? Case(string parse) => At(parse, CaseSlot) switch
    {
        'N' => "nominative",
        'G' => "genitive",
        'D' => "dative",
        'A' => "accusative",
        'V' => "vocative",
        _ => null,
    };

    public static string? Number(string parse) => At(parse, NumberSlot) switch
    {
        'S' => "singular",
        'P' => "plural",
        _ => null,
    };

    public static string? Gender(string parse) => At(parse, GenderSlot) switch
    {
        'M' => "masculine",
        'F' => "feminine",
        'N' => "neuter",
        _ => null,
    };

    public static string? Tense(string parse) => At(parse, TenseSlot) switch
    {
        'P' => "present",
        'I' => "imperfect",
        'F' => "future",
        'A' => "aorist",
        'X' => "perfect",
        'Y' => "pluperfect",
        _ => null,
    };

    /// <summary>
    /// Middle and passive are separate answers here, where Nestle's annotation writes
    /// <c>middlepassive</c> for a form that could be either. That is the largest single thing this
    /// dataset adds: 4,602 Greek words whose voice the corpus could not name.
    /// </summary>
    public static string? Voice(string parse) => At(parse, VoiceSlot) switch
    {
        'A' => "active",
        'M' => "middle",
        'P' => "passive",
        _ => null,
    };

    public static string? Mood(string parse) => At(parse, MoodSlot) switch
    {
        'I' => "indicative",
        'D' => "imperative",
        'S' => "subjunctive",
        'O' => "optative",
        'N' => "infinitive",
        'P' => "participle",
        _ => null,
    };

    public static string? Person(string parse) => At(parse, PersonSlot) switch
    {
        '1' => "first",
        '2' => "second",
        '3' => "third",
        _ => null,
    };

    /// <summary>
    /// Comparative and superlative. Nestle's annotation has no degree at all, so every one of the
    /// 303 words that carry it here carries something the corpus could not previously say.
    /// </summary>
    public static string? Degree(string parse) => At(parse, DegreeSlot) switch
    {
        'C' => "comparative",
        'S' => "superlative",
        _ => null,
    };

    private static char At(string parse, int slot)
    {
        if (parse.Length != Length)
        {
            throw new ArgumentException(
                $"A MorphGNT parsing code is {Length} characters and this one is {parse.Length}: " +
                $"\"{parse}\". Read it with MorphGntReader, which checks the column count, rather " +
                "than splitting the line by hand.",
                nameof(parse));
        }

        return parse[slot];
    }
}
