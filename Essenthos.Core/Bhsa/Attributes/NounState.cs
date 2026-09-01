namespace Essenthos.Core.Bhsa.Attributes;

public class NounState : StringEnum<NounState>
{
    public static readonly NounState Absolute = new("a");
    public static readonly NounState Construct = new("c");
    public static readonly NounState Emphatic = new("e");
    public static readonly NounState NotApplicable = new("NA");

    private NounState(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator NounState(string value) => Parse(value);
}