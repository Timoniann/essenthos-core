namespace Essenthos.Core.Bhsa.Attributes;

public class VerbalTense : StringEnum<VerbalTense>
{
    public static readonly VerbalTense Perfect = new("perf");
    public static readonly VerbalTense Imperfect = new("impf");
    public static readonly VerbalTense Wayyiqtol = new("wayq");
    public static readonly VerbalTense Imperative = new("impv");
    public static readonly VerbalTense InfinitiveAbsolute = new("infa");
    public static readonly VerbalTense InfinitiveConstruct = new("infc");
    public static readonly VerbalTense Participle = new("ptca");
    public static readonly VerbalTense ParticiplePassive = new("ptcp");
    
    public static readonly VerbalTense NotApplicable = new("NA");

    private VerbalTense(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator VerbalTense(string value) => Parse(value);
}