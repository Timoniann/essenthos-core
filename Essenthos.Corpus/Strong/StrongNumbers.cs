namespace Essenthos.Core.Strong;

/// <summary>
/// Strong's numbers are written a dozen ways in the wild — "h430", "H0430", "430". The corpus
/// stores one form: a language letter followed by the number with no padding.
/// </summary>
public static class StrongNumbers
{
    public const char Hebrew = 'H';
    public const char Greek = 'G';

    /// <summary>The last entry of Strong's Hebrew dictionary; a source's number above it is a tense-voice-mood code or its own, not an entry.</summary>
    public const int LastHebrew = 8674;

    /// <summary>The last entry of Strong's Greek dictionary.</summary>
    public const int LastGreek = 5624;

    /// <summary>
    /// Returns the canonical form of a Strong's number, or null when the input is not one.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var language = char.ToUpperInvariant(trimmed[0]);
        if (language != Hebrew && language != Greek)
        {
            return null;
        }

        var digits = trimmed.AsSpan(1);
        if (digits.IsEmpty || !int.TryParse(digits, out var number) || number <= 0)
        {
            return null;
        }

        return $"{language}{number}";
    }
}
