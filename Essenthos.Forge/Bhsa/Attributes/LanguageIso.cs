namespace Essenthos.Core.Bhsa.Attributes;

public class LanguageIso : StringEnum<LanguageIso>
{
    public static readonly LanguageIso Hebrew = new("hbo");
    public static readonly LanguageIso Aramaic = new("arc");

    protected LanguageIso(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator LanguageIso(string value) => Parse(value);
}