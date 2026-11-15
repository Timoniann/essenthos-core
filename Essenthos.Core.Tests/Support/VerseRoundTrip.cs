using System.Text;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// How closely a rebuilt verse has to match its source.
/// </summary>
internal enum RoundTripTolerance
{
    /// <summary>
    /// Character for character. What a tokeniser owes when it is handed the verse as a single
    /// string, and what every loader owes when it writes a parser's words to the database.
    /// </summary>
    Exact,

    /// <summary>
    /// Character for character once runs of whitespace are collapsed to one space and the ends are
    /// trimmed. A source that indents its markup puts newlines and padding between words that are
    /// not part of the text. Collapsing a run is not the same as deleting it: a trailer that lost
    /// the space after its comma still fails, which is the corruption this is here to catch.
    /// </summary>
    CollapsingWhitespace,
}

internal sealed record RoundTripFailure(string Reference, string Expected, string Actual, int FirstDifference)
{
    private const int ContextCharacters = 30;

    /// <summary>
    /// Names the verse, the offset, and the two texts around it. A verse can be hundreds of
    /// characters long and the difference is usually one, so the window is what makes the message
    /// readable at all.
    /// </summary>
    public string Describe()
    {
        var from = Math.Max(0, FirstDifference - ContextCharacters);
        return $"{Reference} does not survive the round trip: the words and trailers rebuild something " +
               $"other than the source, first differing at character {FirstDifference}." +
               $"{Environment.NewLine}  source: …{Window(Expected, from)}…" +
               $"{Environment.NewLine}  words:  …{Window(Actual, from)}…";
    }

    private static string Window(string value, int from)
    {
        if (from >= value.Length)
        {
            return string.Empty;
        }

        var length = Math.Min(ContextCharacters * 2, value.Length - from);
        return value.Substring(from, length);
    }
}

/// <summary>
/// A verse is what its words say it is. Concatenating a verse's words with their trailers must
/// give back the verse that was read, and it is checked on every load rather than in a unit test:
/// the Greek that lost the last letter of 19,740 words and the English that lost the space after
/// punctuation in 72,277 were both corruptions in the data, reached by no code path a unit test
/// touches, and both would have failed this on the first verse.
/// </summary>
internal static class VerseRoundTrip
{
    public static string Rebuild<T>(IEnumerable<T> words, Func<T, string> surface, Func<T, string> trailer)
    {
        var builder = new StringBuilder(256);
        foreach (var word in words)
        {
            builder.Append(surface(word)).Append(trailer(word));
        }

        return builder.ToString();
    }

    public static RoundTripFailure? Check(
        string reference,
        string rebuilt,
        string source,
        RoundTripTolerance tolerance = RoundTripTolerance.Exact)
    {
        var expected = Reduce(source, tolerance);
        var actual = Reduce(rebuilt, tolerance);
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return null;
        }

        return new RoundTripFailure(reference, expected, actual, FirstDifference(expected, actual));
    }

    /// <summary>
    /// The form a loader uses: a verse that does not round-trip stops the load. A corpus that is
    /// silently wrong is worse than one that is missing, because only the second is noticed.
    /// </summary>
    public static void Ensure(
        string reference,
        string rebuilt,
        string source,
        RoundTripTolerance tolerance = RoundTripTolerance.Exact)
    {
        var failure = Check(reference, rebuilt, source, tolerance);
        if (failure is not null)
        {
            throw new InvalidOperationException(failure.Describe());
        }
    }

    private static string Reduce(string value, RoundTripTolerance tolerance) =>
        tolerance == RoundTripTolerance.Exact
            ? value
            : WordSeparation.NormalizeWhitespace(value).Trim();

    private static int FirstDifference(string expected, string actual)
    {
        var shared = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < shared; i++)
        {
            if (expected[i] != actual[i])
            {
                return i;
            }
        }

        return shared;
    }
}
