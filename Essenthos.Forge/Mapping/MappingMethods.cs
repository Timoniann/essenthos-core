using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The methods of the mapping files and the order the merge ranks them in (DOC-0224 §6.4.1). A file
/// method is finer than the corpus's <see cref="LinkMethod"/>: a composition and an alignment are both
/// the aligner's in the corpus, a placed name and an EVIDENTIA verdict are both rule-based, and the
/// merge ranks each apart.
/// </summary>
internal static partial class MappingMethods
{
    public const string Stated = "stated";
    public const string Strong = "strong";
    public const string Aligner = "aligner";
    public const string Composed = "composed";
    public const string Evidentia = "evidentia";
    public const string Names = "names";
    public const string NameList = "name-list";
    public const string Possessive = "possessive";
    public const string Rule = "rule";
    public const string Lexical = "lexical";
    public const string Ruling = "ruling";
    public const string Manual = "manual";
    public const string ModelReading = "model-reading";

    /// <summary>A pinned shape outranks every method.</summary>
    public const int Pinned = 1000;

    /// <summary>
    /// An absence a source states or an approved verdict decides outranks any correspondence on its word
    /// (DOC-0224 §6.4.3: the King James's italics), whichever method draws the correspondence.
    /// </summary>
    public const int StatedAbsence = 95;

    /// <summary>Which file a claim of the corpus belongs in, by its method and its source.</summary>
    public static string Classify(string databaseMethod, string source) => databaseMethod switch
    {
        "stated-by-source" => Stated,
        "strong-number" => Strong,
        "lexical" => Lexical,
        "manual" => Manual,
        "model-reading" => ModelReading,
        "rule-based" when source.StartsWith("EVIDENTIA run ", StringComparison.Ordinal) => Evidentia,
        "rule-based" when source == NamesFirstPass.Source => Names,
        "rule-based" when source == PossessivePass.Source => Possessive,
        "rule-based" => Rule,
        "aligner" when source.StartsWith(CompositionPipeline.Machine + ",", StringComparison.Ordinal) => Composed,
        "aligner" when source == NameListPass.Source => NameList,
        "aligner" => Aligner,
        _ => throw new InvalidDataException($"No mapping method for the corpus method '{databaseMethod}'."),
    };

    /// <summary>The corpus method a file method's claims are loaded as.</summary>
    public static LinkMethod DatabaseMethod(string method) => method switch
    {
        Stated => LinkMethod.StatedBySource,
        Strong => LinkMethod.StrongNumber,
        Lexical => LinkMethod.Lexical,
        Manual or Ruling => LinkMethod.Manual,
        ModelReading => LinkMethod.ModelReading,
        Evidentia or Names or Possessive or Rule => LinkMethod.RuleBased,
        Aligner or Composed or NameList => LinkMethod.Aligner,
        _ => throw new InvalidDataException($"No corpus method for the mapping method '{method}'."),
    };

    /// <summary>
    /// The merge's precedence, highest first: a person, a ruling's pin aside; a stated source; an approved
    /// EVIDENTIA verdict, the safe tier before the attached; the names placed by whom both texts name; a
    /// Strong pairing at 0.9 or more; a composition admitted by its floor; a Strong pairing at 0.7 to 0.9;
    /// the aligner; a Strong pairing under 0.7; form matching. The numbers are only compared with each other.
    /// </summary>
    public static int Rank(string method, double? confidence, string? evidence) => method switch
    {
        Manual or Ruling => 100,
        Stated => 90,
        Evidentia => Tier(evidence) switch
        {
            "safe" => 80,
            "attached" => 79,
            _ => 78,
        },
        Names => 77,
        Possessive or Rule => 76,
        Strong when confidence >= 0.9 => 70,
        ModelReading => 65,
        Composed => 60,
        Strong when confidence >= 0.7 => 50,
        Aligner or NameList => 40,
        Strong => 30,
        Lexical => 20,
        _ => 0,
    };

    /// <summary>Whether a method states absences that win their word (<see cref="StatedAbsence"/>).</summary>
    /// <summary>
    /// Which of two claims of one link that <see cref="Rank"/> ranks equal is settled, negative when
    /// <paramref name="a"/> is: the surer, then method, source and evidence by name. The whole tie-break
    /// is this one function; the order among stated sources and among EVIDENTIA runs belongs here.
    /// </summary>
    public static int SettlesBefore(MappingRow a, MappingRow b)
    {
        var order = (b.Confidence ?? 2).CompareTo(a.Confidence ?? 2);
        if (order == 0) order = string.CompareOrdinal(a.Method, b.Method);
        if (order == 0) order = string.CompareOrdinal(a.Source, b.Source);
        if (order == 0) order = string.CompareOrdinal(a.Evidence, b.Evidence);
        return order;
    }

    public static bool StatesAbsence(string method) => method is Stated or Evidentia or Manual or Ruling;

    /// <summary>
    /// The methods a model drew, which a changed verse loses until the pair is aligned again (DOC-0224
    /// §6.2, the owner's D1): a model trained again is not the same model.
    /// </summary>
    public static bool Drawn(string method) => method is Aligner or Composed;

    /// <summary>What must run to give a changed verse rows of this method again.</summary>
    public static string Regenerates(string method) => method switch
    {
        Aligner => "align",
        Composed => "compose",
        Evidentia => "evidentia",
        Names => "names-first",
        NameList => "names",
        Possessive => "possessives",
        Strong => "strong",
        Stated => "the pair's stated loader",
        Lexical => "the pair's lexical loader",
        _ => method,
    };

    /// <summary>The tier an EVIDENTIA claim's evidence names: <c>decision 4205125, attached tier: …</c>.</summary>
    public static string? Tier(string? evidence) =>
        evidence is not null && TierPattern().Match(evidence) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex(@"\b(\w+) tier\b")]
    private static partial Regex TierPattern();

    public static bool IsAbsence(string relation) => relation is "expands" or "omits";
}
