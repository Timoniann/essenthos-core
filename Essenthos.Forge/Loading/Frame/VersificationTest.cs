using System.Globalization;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// One condition the versification data states beside a rule, and the whole point of reading it:
/// the data describes several editions per tradition and every rule carries the test that says
/// which of them it is about.
///
/// <code>
///   Exo.21:36=Last                 the chapter ends there
///   Gen.6:1.2=Exist                the address is printed in two pieces
///   Est.A:17=NotExist              the edition does not print that verse
///   Exo.39:19&lt;Exo.39:21           the first verse is the shorter of the two
///   Mal.3:23*2&gt;Mal.3:22            twice the first is still longer than the second
/// </code>
///
/// A test naming a book this corpus does not hold cannot be answered, which is not the same as
/// failing: the cell is left unanswered and the rule is judged on the tests that can be read.
/// </summary>
internal sealed record VersificationTest
{
    private const string ExistsMarker = "=Exist";

    private const string AbsentMarker = "=NotExist";

    private const string LastMarker = "=Last";

    private const string DoubleMarker = "*2";

    private const char ConditionSeparator = '&';

    private VersificationTest(
        Comparison comparison,
        CanonicalReference left,
        int leftPiece,
        int leftFactor,
        CanonicalReference right = default,
        int rightPiece = 0,
        int rightFactor = 1)
    {
        Kind = comparison;
        Left = left;
        LeftPiece = leftPiece;
        LeftFactor = leftFactor;
        Right = right;
        RightPiece = rightPiece;
        RightFactor = rightFactor;
    }

    private enum Comparison
    {
        Exists,
        Absent,
        EndsChapter,
        Shorter,
        Longer,
    }

    private Comparison Kind { get; }

    private CanonicalReference Left { get; }

    private int LeftPiece { get; }

    private int LeftFactor { get; }

    private CanonicalReference Right { get; }

    private int RightPiece { get; }

    private int RightFactor { get; }

    /// <summary>
    /// The conditions in one cell, which are joined by <c>&amp;</c> and all have to hold. Null when
    /// none of them can be read at all — a cell about Sirach and Tobit says nothing about a Bible
    /// that holds neither.
    /// </summary>
    public static VersificationConditions? ParseAll(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            return null;
        }

        var tests = new List<VersificationTest>(4);
        var whole = true;

        foreach (var condition in cell.Split(ConditionSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = condition.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (TryParse(trimmed, out var test))
            {
                tests.Add(test);
                continue;
            }

            whole = false;
        }

        return tests.Count > 0 ? new VersificationConditions(tests, whole) : null;
    }

    internal bool IsAbout(EditionShape edition) =>
        edition.Carries(Left.Book) && (Kind is Comparison.Exists or Comparison.Absent or Comparison.EndsChapter ||
                                       edition.Carries(Right.Book));

    internal bool Holds(EditionShape edition) => Kind switch
    {
        Comparison.Exists => edition.Prints(Left, LeftPiece),
        Comparison.Absent => !edition.Prints(Left, LeftPiece),
        Comparison.EndsChapter => edition.EndsChapter(Left),
        _ => Compare(edition),
    };

    private static bool TryParse(string condition, out VersificationTest test)
    {
        test = null!;

        foreach (var (marker, kind) in new[]
                 {
                     (ExistsMarker, Comparison.Exists),
                     (AbsentMarker, Comparison.Absent),
                     (LastMarker, Comparison.EndsChapter),
                 })
        {
            if (!condition.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryParseOperand(condition[..^marker.Length], out var only, out var piece, out _))
            {
                return false;
            }

            test = new VersificationTest(kind, only, piece, 1);
            return true;
        }

        var at = condition.IndexOfAny(['<', '>']);
        if (at < 0 ||
            !TryParseOperand(condition[..at], out var left, out var leftPiece, out var leftFactor) ||
            !TryParseOperand(condition[(at + 1)..], out var right, out var rightPiece, out var rightFactor))
        {
            return false;
        }

        test = new VersificationTest(
            condition[at] == '<' ? Comparison.Shorter : Comparison.Longer,
            left, leftPiece, leftFactor, right, rightPiece, rightFactor);
        return true;
    }

    /// <summary>
    /// One side of a condition: a reference, optionally a piece of it, optionally doubled. The
    /// piece is written as a trailing <c>.1</c>, which a reference like <c>Gen.6:1</c> cannot be
    /// confused with because its own dot comes before the colon.
    /// </summary>
    private static bool TryParseOperand(string value, out CanonicalReference reference, out int piece, out int factor)
    {
        reference = default;
        piece = 0;
        factor = 1;

        var text = value.Trim();
        if (text.EndsWith(DoubleMarker, StringComparison.Ordinal))
        {
            factor = 2;
            text = text[..^DoubleMarker.Length].TrimEnd();
        }

        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        var dot = text.IndexOf('.', colon);
        if (dot > 0)
        {
            if (!int.TryParse(text.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out piece))
            {
                return false;
            }

            text = text[..dot];
        }

        return CanonicalReference.TryParse(text, out reference);
    }

    /// <summary>
    /// A length comparison against a verse the edition does not print holds of nothing. Zero is
    /// shorter than everything, so answering it as a comparison would let a rule about an edition
    /// with more verses than this one pass on the strength of the verses it is missing.
    /// </summary>
    private bool Compare(EditionShape edition)
    {
        var left = edition.Length(Left, LeftPiece) * LeftFactor;
        var right = edition.Length(Right, RightPiece) * RightFactor;
        return left != 0 && right != 0 && (Kind == Comparison.Shorter ? left < right : left > right);
    }
}

/// <param name="Whole">
/// Whether every condition in the cell could be read. A cell that also names Sirach or Mark is
/// answered on what is left of it, and a scheme is never chosen on the strength of a partial
/// answer — but a condition that plainly fails is a failure whatever else the cell says.
/// </param>
internal sealed record VersificationConditions(IReadOnlyList<VersificationTest> Stated, bool Whole)
{
    /// <summary>
    /// Whether this edition answers to the cell: false as soon as a condition it can answer fails,
    /// null while some condition is about a book it does not carry, and true when the whole cell
    /// was read and held.
    /// </summary>
    public bool? Answer(EditionShape edition)
    {
        var whole = Whole;

        foreach (var test in Stated)
        {
            if (!test.IsAbout(edition))
            {
                whole = false;
                continue;
            }

            if (!test.Holds(edition))
            {
                return false;
            }
        }

        return whole ? true : null;
    }
}
