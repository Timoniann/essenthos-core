using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Database;

/// <summary>
/// Every method that made a claim on a link, as one small number: <see cref="Entities.Link.Methods"/>.
///
/// <para>
/// Each method has a bit of its own, fixed here and in the database's <c>link_method_bit</c>, because the
/// value is stored: numbering by the enum's order would let a method added in the middle silently rename
/// every stored set. A trigger on <c>link_claim</c> keeps the column true whoever writes the claims; the
/// mapping load, which writes with the triggers off, computes it with <see cref="Of(IEnumerable{string})"/>.
/// </para>
///
/// <para>
/// A served release carries no <c>link_claim</c>; this column is what the word detail reads instead: two bytes,
/// which on a row with no null column fit in the alignment padding the row already has.
/// </para>
/// </summary>
internal static class LinkMethods
{
    public static short Bit(LinkMethod method) => method switch
    {
        LinkMethod.StatedBySource => 1,
        LinkMethod.StrongNumber => 2,
        LinkMethod.Lexical => 4,
        LinkMethod.Aligner => 8,
        LinkMethod.Manual => 16,
        LinkMethod.ModelReading => 32,
        LinkMethod.RuleBased => 64,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "A link method needs a bit of its own here and in link_method_bit."),
    };

    /// <summary>The set of these methods, given as the database spells them.</summary>
    public static short Of(IEnumerable<string> spellings)
    {
        var set = 0;
        foreach (var spelling in spellings)
        {
            set |= (ushort)Bit(EnumSpelling.ToLinkMethod(spelling));
        }

        return (short)set;
    }

    /// <summary>The methods a set holds, in the enum's order.</summary>
    public static IEnumerable<LinkMethod> In(short set) =>
        Enum.GetValues<LinkMethod>().Where(method => (set & Bit(method)) != 0);

    /// <summary>
    /// The SQL function the migration creates and the trigger, the backfill and verify call: a method's bit from
    /// its spelling, null for a spelling it does not know.
    /// </summary>
    public static string Function =>
        "CREATE OR REPLACE FUNCTION link_method_bit(method text) RETURNS smallint LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$\n" +
        "    SELECT CASE method\n" +
        string.Concat(Enum.GetValues<LinkMethod>().Select(m => $"        WHEN '{EnumSpelling.Of(m)}' THEN {Bit(m)}\n")) +
        "    END::smallint\n$$";
}
