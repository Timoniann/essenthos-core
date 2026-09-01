namespace Essenthos.Core.Bhsa.Attributes;

public class WordPersonClass : StringEnum<WordPersonClass>
{
    public static readonly WordPersonClass Person1 = new("p1");
    public static readonly WordPersonClass Person2 = new("p2");
    public static readonly WordPersonClass Person3 = new("p3");
    public static readonly WordPersonClass NotApplicable = new("NA");
    public static readonly WordPersonClass Unknown = new("unknown");

    private WordPersonClass(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator WordPersonClass(string value) => Parse(value);
}