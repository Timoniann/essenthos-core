namespace Essenthos.Core.Endpoints;

/// <summary>
/// The asterisk BibleData puts after some events' titles (<em>Abram departed Ur*</em>), and what it
/// stands for.
///
/// <para>
/// The source does not explain it. Of the 201 starred titles, 152 have notes opening
/// <c>*HYPOTHESIS:</c> or <c>*EXTRA-BIBLICAL SOURCE(S)/HISTORY:</c>, the starred headings of the
/// compiler's own reasoning; the rest carry page references only. So the mark is read from the notes:
/// a year the compiler inferred, or took from outside Scripture. The title loses the asterisk either
/// way, because a reader cannot be expected to know what it means and a translation keeps it as a stray
/// character.
/// </para>
/// </summary>
internal static class EventMarks
{
    public const string Hypothesis = "hypothesis";

    public const string OutsideScripture = "outside-scripture";

    /// <summary>The title without the source's trailing asterisk.</summary>
    public static string Unstarred(string name) => name.TrimEnd().TrimEnd('*').TrimEnd();

    /// <summary>The same for a title that may be missing, such as a translation not held.</summary>
    public static string? UnstarredOrNull(string? name) => name is null ? null : Unstarred(name);

    /// <summary>What the notes say the year rests on, where they flag it with a starred heading.</summary>
    public static string? Of(string? notes) =>
        notes is null ? null
        : notes.Contains("*HYPOTHESIS", StringComparison.Ordinal) ? Hypothesis
        : notes.Contains("*EXTRA-BIBLICAL", StringComparison.Ordinal) ? OutsideScripture
        : null;
}
