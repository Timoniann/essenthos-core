namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// How the corpus ranks its methods where two links or proposals are otherwise equally sure: the order the mapping's merge
/// ranks them in, each method at the highest rank any of the file methods it gathers can reach (a person, a stated source,
/// a Strong pairing on the text's own numbers, a rule, a model's reading, an alignment, form matching).
/// </summary>
public static class LinkMethodPrecedence
{
    /// <summary>Higher first; the numbers are only compared with each other.</summary>
    public static int Of(LinkMethod method) => method switch
    {
        LinkMethod.Manual => 7,
        LinkMethod.StatedBySource => 6,
        LinkMethod.StrongNumber => 5,
        LinkMethod.RuleBased => 4,
        LinkMethod.ModelReading => 3,
        LinkMethod.Aligner => 2,
        LinkMethod.Lexical => 1,
        _ => 0,
    };
}
