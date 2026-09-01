namespace Essenthos.Core.Bhsa.Attributes;

/// <summary>
/// This feature contains property at the level of text.
/// </summary>
public class ClauseDomain : StringEnum<ClauseDomain>
{
    public static readonly ClauseDomain Unknown = new("?");
    public static readonly ClauseDomain Narrative = new("N");
    public static readonly ClauseDomain Discursive = new("D");
    public static readonly ClauseDomain Quotation = new("Q");

    protected ClauseDomain(string value, bool external = false) : base(value, external)
    {
    }
}