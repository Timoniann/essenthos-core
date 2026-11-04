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
    /// An absence a source states or a person decides outranks any correspondence on its word
    /// (DOC-0224 §6.4.3: the King James's italics), whichever method draws the correspondence.
    /// </summary>
    public const int StatedAbsence = 95;

    /// <summary>
    /// An approved EVIDENTIA verdict's absence beats only the aligner's guess: it ranks just above the aligner,
    /// and gives way to any link on its word that another method claims (<see cref="GivesWay"/>), so a rule's
    /// "no counterpart" never withdraws a hand alignment, a Strong pairing, a composition or a placed name.
    /// </summary>
    public const int EvidentiaAbsence = 41;

    /// <summary>The rank of an EVIDENTIA absence that a link of another method than the aligner contests: below every link.</summary>
    public const int GivesWay = 0;

    /// <summary>Whether every claim of a link is the aligner's own, the only kind an EVIDENTIA absence withdraws.</summary>
    public static bool OnlyTheAligner(IEnumerable<string> methods) => methods.All(m => m == Aligner);

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
    /// The merge's precedence, highest first: a person, a ruling's pin aside; a stated source; a Strong pairing at
    /// 0.9 or more from the text's own tags (the owner, NOT-0218: the second surest record after a source, a wrong
    /// number being put right at the word); an approved EVIDENTIA verdict, the safe tier before the attached; the
    /// names placed by whom both texts name; a composition admitted by its floor; a Strong pairing at 0.9 or more
    /// from numbers laid onto the text from another edition (<paramref name="laidStrong"/>); a Strong pairing at
    /// 0.7 to 0.9; the aligner; a Strong pairing under 0.7; form matching. The numbers are only compared with each
    /// other.
    /// </summary>
    public static int Rank(string method, double? confidence, string? evidence, bool laidStrong = false) => method switch
    {
        Manual or Ruling => 100,
        Stated => 90,
        Strong when confidence >= 0.9 && !laidStrong => 85,
        Evidentia => Tier(evidence) switch
        {
            "safe" => 80,
            "attached" => 79,
            _ => 78,
        },
        Names => 77,
        Possessive or Rule => 76,
        ModelReading => 65,
        Composed => 60,
        Strong when confidence >= 0.9 => 55,
        Strong when confidence >= 0.7 => 50,
        Aligner or NameList => 40,
        Strong => 30,
        Lexical => 20,
        _ => 0,
    };

    /// <summary>
    /// Whether a pair's Strong rows draw on numbers laid onto one of its texts from a numbered edition of
    /// the same translation (<see cref="Conversion.SideStrongNumbers"/>) rather than on the text's own tags.
    /// </summary>
    public static bool LaysStrong(string fromText, string toText) =>
        Conversion.SideStrongNumbers.Layered(fromText) || Conversion.SideStrongNumbers.Layered(toText);

    /// <summary>A link restored to a word the competition would leave with none must be at least this sure.</summary>
    public const double OrphanFloor = 0.7;

    /// <summary>Whether a link is sure enough to keep a word that would otherwise have none (<see cref="OrphanFloor"/>).</summary>
    public static bool KeepsOrphan(MappingRow settled, bool pinned) =>
        pinned || settled.Confidence is null || settled.Confidence >= OrphanFloor;

    /// <summary>How a stated source was made, as its credit says.</summary>
    public enum StatedMaking
    {
        /// <summary>Aligned by hand to this edition: Clear Bible's <c>-manual</c> alignments, "made by hand".</summary>
        ByHand,

        /// <summary>Tables, mappings, italics and the rest that say nothing about it.</summary>
        Unsaid,

        /// <summary>
        /// Carried from an alignment to another edition: to unfoldingWord's Hebrew Bible and Greek New Testament
        /// (Door43, translationCore), or Clear Bible's <c>-transfer</c> alignments.
        /// </summary>
        Transferred,
    }

    /// <summary>How a stated source was made, read from its credit; a credit that says both is taken as transferred.</summary>
    public static StatedMaking Making(string source) =>
        source.Contains("not made by hand", StringComparison.Ordinal)
        || source.Contains("transferred from", StringComparison.Ordinal)
        || source.Contains("to unfoldingWord's", StringComparison.Ordinal)
        || source.Contains("translationCore", StringComparison.Ordinal)
            ? StatedMaking.Transferred
            : source.Contains("made by hand", StringComparison.Ordinal)
                ? StatedMaking.ByHand
                : StatedMaking.Unsaid;

    /// <summary>The run number an EVIDENTIA claim's source names: <c>EVIDENTIA run 26 (…)</c>.</summary>
    public static int? EvidentiaRun(string source) =>
        RunPattern().Match(source) is { Success: true } match ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;

    [GeneratedRegex(@"^EVIDENTIA run (\d+)\b")]
    private static partial Regex RunPattern();

    /// <summary>
    /// Which of two claims of one link that <see cref="Rank"/> ranks equal is settled, negative when
    /// <paramref name="a"/> is: between stated sources, one aligned by hand before one carried from another
    /// edition (<see cref="Making"/>); then the surer, then method, source and evidence by name. The whole tie-break
    /// is this one function; the order among stated sources and among EVIDENTIA runs belongs here.
    /// </summary>
    public static int SettlesBefore(MappingRow a, MappingRow b)
    {
        var order = a.Method == Stated && b.Method == Stated ? Making(a.Source).CompareTo(Making(b.Source)) : 0;
        if (order == 0) order = (b.Confidence ?? 2).CompareTo(a.Confidence ?? 2);
        if (order == 0) order = string.CompareOrdinal(a.Method, b.Method);
        if (order == 0) order = string.CompareOrdinal(a.Source, b.Source);
        if (order == 0) order = string.CompareOrdinal(a.Evidence, b.Evidence);
        return order;
    }

    /// <summary>Whether a method states absences that win their word (<see cref="StatedAbsence"/>).</summary>
    public static bool StatesAbsence(string method) => method is Stated or Manual or Ruling;

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
