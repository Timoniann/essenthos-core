namespace Essenthos.Core.Bhsa.Attributes;

/// <summary>
/// This feature divides the clauses into three types: verbal, nominal and without predication. It is related to the
/// feature typ on clauses, in the sense that each of the values of kind corresponds to a set of values of typ.
/// So, this is essentially a feature for convenience: it leads to more concise queries of which the intention is also clearer.
/// </summary>
public class ClauseKind : StringEnum<ClauseKind>
{
    // Correspondence with typ values on clauses: InfA InfC Ptcp Way0 WayX WIm0 WImX WQt0 WQtX WxI0 WXIm WxIX WxQ0 WXQt WxQX WxY0 WXYq WxYX WYq0 WYqX xIm0 XImp xImX xQt0 XQtl xQtX xYq0 XYqt xYqX ZIm0 ZImX ZQt0 ZQtX ZYq0 ZYqX
    public static readonly ClauseKind VerbalClause = new("VC");

    // Correspondence with typ values on clauses: AjCl NmCl
    public static readonly ClauseKind NominalClause = new("NC");

    // Correspondence with typ values on clauses: CPen Ellp MSyn Reop Voct XPos
    public static readonly ClauseKind ClauseWithoutPredication = new("WP");

    protected ClauseKind(string value, bool external = false)
        : base(value, external)
    {
    }
}