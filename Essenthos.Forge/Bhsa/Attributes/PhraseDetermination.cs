namespace Essenthos.Core.Bhsa.Attributes;

public class PhraseDetermination : StringEnum<PhraseDetermination>
{
    public static readonly PhraseDetermination Determined = new("det");
    public static readonly PhraseDetermination Undetermined = new("und");
    public static readonly PhraseDetermination NotApplicable = new("NA");

    private PhraseDetermination(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator PhraseDetermination(string value) => Parse(value);
}