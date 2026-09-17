using System.Diagnostics.Contracts;

namespace Essenthos.Core.Bhsa.Attributes;

public class Nametype : StringEnum<Nametype>
{
    public static readonly Nametype People = new("gens");
    public static readonly Nametype God = new("god");
    public static readonly Nametype MeasurementUnit = new("mens");
    public static readonly Nametype Person = new("pers");
    public static readonly Nametype DemonstrativePersonalPronoun = new("ppde");
    public static readonly Nametype Place = new("topo");

    protected Nametype(string value, bool external = false) : base(value, external)
    {
    }

    public static implicit operator Nametype?(string? value)
    {
        // ReSharper disable once InvocationIsSkipped
        Contract.Ensures(
            value == null ? Contract.Result<LexicalSet?>() == null : Contract.Result<LexicalSet?>() != null);
        return value == null ? null : Parse(value);
    }

    public static Nametype[] ParseMultiple(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        var values = value.Split(',');
        var result = new Nametype[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = Parse(values[i]);
        }

        return result;
    }
}