namespace Essenthos.Core.Bhsa.Attributes;

public class WordNumberClass : StringEnum<WordNumberClass>
{
    public static readonly WordNumberClass Singular = new("sg");
    public static readonly WordNumberClass Dual = new("du");
    public static readonly WordNumberClass Plural = new("pl");
    public static readonly WordNumberClass NotApplicable = new("NA");
    public static readonly WordNumberClass Unknown = new("unknown");

    private WordNumberClass(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator WordNumberClass(string value) => Parse(value);
}