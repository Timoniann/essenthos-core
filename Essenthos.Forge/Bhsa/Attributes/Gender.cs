namespace Essenthos.Core.Bhsa.Attributes;

public class Gender : StringEnum<Gender>
{
    public static readonly Gender Masculine = new("m");
    public static readonly Gender Feminine = new("f");
    public static readonly Gender NotApplicable = new("NA");
    public static readonly Gender Unknown = new("unknown");

    protected Gender(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator Gender(string value) => Parse(value);
}