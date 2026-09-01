namespace Essenthos.Core.Bhsa;

public readonly struct RangeInt : IEquatable<RangeInt>
{
    // ReSharper disable once MemberCanBePrivate.Global
    public readonly int Start;

    // ReSharper disable once MemberCanBePrivate.Global
    public readonly int End;

    // ReSharper disable once ConvertToPrimaryConstructor
    public RangeInt(int start, int end)
    {
        Start = start;
        End = end;
    }

    public RangeInt(int value)
    {
        Start = value;
        End = value;
    }

    public bool Contains(int value) => value >= Start && value <= End;

    public bool Equals(RangeInt other) => Start == other.Start && End == other.End;

    public override bool Equals(object? obj) => obj is RangeInt other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Start * 397) ^ End;
        }
    }

    public static bool operator ==(RangeInt left, RangeInt right) => left.Equals(right);

    public static bool operator !=(RangeInt left, RangeInt right) => !left.Equals(right);

    public override string ToString()
    {
        return Start == End ? Start.ToString() : $"{Start}-{End}";
    }
}