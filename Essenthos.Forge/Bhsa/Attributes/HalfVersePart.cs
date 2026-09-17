namespace Essenthos.Core.Bhsa.Attributes;

public class HalfVersePart : StringEnum<HalfVersePart>
{
    public static readonly HalfVersePart A = new("A");
    public static readonly HalfVersePart B = new("B");
    public static readonly HalfVersePart C = new("C");

    protected HalfVersePart(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator HalfVersePart(string value) => Parse(value);
}