using System.Diagnostics.CodeAnalysis;

namespace Essenthos.Core.Bhsa.Attributes;

public class ClauseAtomRelation
{
    private ClauseAtomRelation(int value, bool external = false)
    {
        Value = value;
        External = external;
    }

    public int Value { get; }

    public bool External { get; }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static ClauseAtomRelation Parse(int s)
    {
        if (s < 0)
        {
            throw new ArgumentException("Value cannot be negative.", nameof(s));
        }

        return new ClauseAtomRelation(s);
    }

    public static bool TryParse([NotNullWhen(true)] int? s, [MaybeNullWhen(false)] out ClauseAtomRelation result)
    {
        if (s is null or < 0)
        {
            result = null;
            return false;
        }

        result = Parse(s.Value);
        return true;
    }
}