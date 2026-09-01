namespace Essenthos.Core.Bhsa.Attributes;

/// <summary>
/// This feature contains property at the level of text.
/// </summary>
public class ClauseTextType : StringEnum<ClauseTextType>
{
    public static readonly ClauseTextType Unknown = new("?");
    public static readonly ClauseTextType Narrative = new("N");
    public static readonly ClauseTextType Discursive = new("D");
    public static readonly ClauseTextType Quotation = new("Q");

    protected ClauseTextType(string value, bool external = false) : base(value, external)
    {
    }
}