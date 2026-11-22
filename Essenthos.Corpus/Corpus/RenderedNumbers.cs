using System.Globalization;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The Strong numbers a word renders through its links, as a reader is given them: <c>rn</c> in a pane and
/// <c>renderedStrong</c> in /v1/parallel, both read by <see cref="ChapterReading"/>.
/// </summary>
internal static class RenderedNumbers
{
    /// <summary>
    /// A word's numbers as one value: a single number as it is, several (a word rendering two original words as one,
    /// «twelve» for שְׁנֵים עָשָׂר) joined by a space. Several are ordered by where the first original word carrying each
    /// stands among <paramref name="originals"/> (the words the word is linked to, with their numbers and places), which is
    /// the order its numbers file lists them in; a number none of them carries comes after, in number order.
    /// </summary>
    public static string Join(IReadOnlyCollection<string> numbers, IReadOnlyCollection<(string Number, int Verse, int Position)>? originals = null)
    {
        if (numbers.Count == 1)
        {
            return numbers.First();
        }

        var places = new Dictionary<string, (int Verse, int Position)>(StringComparer.Ordinal);
        foreach (var (number, verse, position) in originals ?? [])
        {
            var key = Canonical(number);
            if (!places.TryGetValue(key, out var held) || (verse, position).CompareTo(held) < 0)
            {
                places[key] = (verse, position);
            }
        }

        return string.Join(' ', numbers.Distinct(StringComparer.Ordinal)
            .OrderBy(n => places.TryGetValue(Canonical(n), out var place) ? place : (int.MaxValue, int.MaxValue))
            .ThenBy(n => n[..1], StringComparer.Ordinal)
            .ThenBy(Value)
            .ThenBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>The number without the zeros an edition may pad it with: H0430 is H430.</summary>
    private static string Canonical(string number) =>
        number.Length > 1 && Value(number) != int.MaxValue ? number[..1] + Value(number).ToString(CultureInfo.InvariantCulture) : number;

    private static int Value(string number)
    {
        var digits = number.AsSpan(1);
        var end = 0;
        while (end < digits.Length && char.IsAsciiDigit(digits[end]))
        {
            end++;
        }

        return int.TryParse(digits[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
    }
}
