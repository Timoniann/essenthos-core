using System.Text.RegularExpressions;

namespace Essenthos.Core.Strong;

/// <summary>
/// What a gentilic is named after, in Strong's own two words. <c>patronymic</c> says a people is
/// named after the man it descends from and <c>patrial</c> says it is named after the place it
/// lives in, which is the difference between reaching a person and reaching a map.
/// </summary>
public static class GentilicKinds
{
    public const string Patronymic = "patronymic";

    public const string Patrial = "patrial";

    /// <summary>
    /// Where Strong writes both and chooses neither — <em>patrial or patronymic from Agag</em>. The
    /// origin is stated and what it is to the people is not, so the claim is kept and nothing is
    /// read out of it.
    /// </summary>
    public const string Either = "patronymic or patrial";
}

/// <summary>Why a derivation naming a gentilic was not turned into a claim.</summary>
public enum GentilicRefusal
{
    None,

    /// <summary>The entry never uses either word, so there is nothing here to read.</summary>
    NotAGentilic,

    /// <summary>
    /// It says a gentilic and then does not say of what: <em>patrial from an unused name</em>,
    /// <em>patrial from another place of similar name with Erech</em>. Strong names a number in
    /// the second of those and it is not the origin — it is the place the origin is unlike.
    /// </summary>
    NamesNoNumber,

    /// <summary>
    /// Strong hedged it himself — <em>perhaps</em>, <em>probably</em>, <em>apparently</em>. A
    /// hedge published as a fact is the corpus asserting something its source declined to.
    /// </summary>
    Hedged,

    /// <summary>
    /// The entry names two origins and prefers one in prose. H1511 is <em>patrial from Gezer; but
    /// better, by transposition, patrial of Gerizim</em>, and choosing between them is reading
    /// nineteenth-century English rather than parsing it.
    /// </summary>
    TwoCandidates,

    /// <summary>The entry derives itself, which says nothing and would draw a loop.</summary>
    PointsAtItself,
}

/// <param name="Statement">
/// The clause exactly as Strong wrote it, so a reader shown the claim can be shown what it rests on
/// rather than being asked to trust the parse.
/// </param>
public readonly record struct StatedGentilic(
    string StrongNumber,
    string OriginNumber,
    string Kind,
    string Statement);

/// <summary>
/// The kinship Strong's dictionary already states, read back out of its prose.
///
/// H4125 is <em>a Moabite or Moabitess, i.e. a descendant from Moab</em>, derived
/// <em>patronymical from מוֹאָב (H4124)</em>. That is a citable fact about a people and their
/// ancestor, made by a named source, and until now it sat in a free-text column nothing could use.
/// 192 Hebrew entries state one; no Greek entry does.
///
/// <para>
/// It is prose and not a field, so it is read strictly and refuses more than it accepts. The origin
/// has to be a Hebrew word standing immediately after <em>from</em> or <em>of</em> with its number
/// in parentheses after it: everything Strong hedges, everything he leaves at <em>an unused
/// name</em>, and everything that reaches its number through a comparison rather than a derivation
/// is refused outright and counted. A wrong ancestor on a people group is not a defect a reader can
/// see, which is the whole reason to prefer the gap.
/// </para>
/// </summary>
public static partial class GentilicDerivations
{
    /// <summary>
    /// The clause. Both words appear in four spellings across the dictionary — <em>patronymic</em>,
    /// <em>patronymical</em>, <em>patronymically</em>, <em>patrial</em> — and either may be joined
    /// to the other by <em>or</em> before the origin is named.
    ///
    /// The Hebrew is required rather than allowed. Strong writes the origin as its own word and
    /// then its number, and every derivation that instead describes the origin in English —
    /// <em>from an unused noun</em>, <em>from a name corresponding to</em> — is one where the number
    /// in the line belongs to something other than the ancestor.
    /// </summary>
    [GeneratedRegex(
        "(?<kind>patronymically|patronymical|patronymic|patrial)" +
        @"(?:\s+or\s+(?<second>patronymically|patronymical|patronymic|patrial))?" +
        @"\s+(?:from|of)\s+" +
        @"(?<origin>[\p{IsHebrew}\uFB1D-\uFB4F]+(?:[\s\u05BE]+[\p{IsHebrew}\uFB1D-\uFB4F]+)*)" +
        @"\s*\(\s*(?<number>H[0-9]+)\s*\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Statement();

    /// <summary>
    /// Strong's own doubt. Read over the clause it stands in rather than over the whole entry: H3614
    /// hedges its spelling and then states its origin flatly, and the two are separate sentences.
    /// </summary>
    [GeneratedRegex(@"\b(?:probabl|perhaps|apparentl|possibl|seem)", RegexOptions.IgnoreCase)]
    private static partial Regex Hedge();

    [GeneratedRegex("patronymic|patrial", RegexOptions.IgnoreCase)]
    private static partial Regex Gentilic();

    /// <summary>Whether the entry claims to be a gentilic at all, however it goes on to say it.</summary>
    public static bool Claims(string? derivation) =>
        derivation is { Length: > 0 } && Gentilic().IsMatch(derivation);

    /// <summary>
    /// The claim this entry states, or null with the reason it was refused.
    ///
    /// The entry is read a clause at a time because Strong punctuates variant spellings, cross
    /// references and the derivation itself into one semicolon-separated line, and a hedge or a
    /// second number in one of them says nothing about another.
    /// </summary>
    public static StatedGentilic? Read(string strongNumber, string? derivation, out GentilicRefusal refusal)
    {
        if (!Claims(derivation))
        {
            refusal = GentilicRefusal.NotAGentilic;
            return null;
        }

        StatedGentilic? stated = null;

        foreach (var clause in derivation!.Split(';'))
        {
            if (Statement().Match(clause) is not { Success: true } match)
            {
                continue;
            }

            if (Hedge().IsMatch(clause))
            {
                refusal = GentilicRefusal.Hedged;
                return null;
            }

            var origin = match.Groups["number"].Value;
            if (string.Equals(origin, strongNumber, StringComparison.Ordinal))
            {
                refusal = GentilicRefusal.PointsAtItself;
                return null;
            }

            if (stated is { } already)
            {
                if (!string.Equals(already.OriginNumber, origin, StringComparison.Ordinal))
                {
                    refusal = GentilicRefusal.TwoCandidates;
                    return null;
                }

                continue;
            }

            stated = new StatedGentilic(strongNumber, origin, Kind(match), clause.Trim(' ', ',', '.'));
        }

        refusal = stated is null ? GentilicRefusal.NamesNoNumber : GentilicRefusal.None;
        return stated;
    }

    /// <summary>
    /// Which of the two Strong said. A clause naming both leaves it undetermined rather than taking
    /// the first, because the first is an accident of how he wrote the sentence.
    /// </summary>
    private static string Kind(Match match)
    {
        if (match.Groups["second"].Success)
        {
            return GentilicKinds.Either;
        }

        return match.Groups["kind"].Value.StartsWith("patrial", StringComparison.OrdinalIgnoreCase)
            ? GentilicKinds.Patrial
            : GentilicKinds.Patronymic;
    }
}
