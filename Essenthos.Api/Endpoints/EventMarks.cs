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

    /// <summary>
    /// The title as a heading: without the source's trailing asterisk, and opening with a capital, since
    /// Wikidata labels its items in lower case (<em>general anaesthesia</em>) and a quoted title may open
    /// mid-sentence.
    /// </summary>
    public static string Unstarred(string name) => Capitalised(name.TrimEnd().TrimEnd('*').TrimEnd());

    /// <summary>The same for a title that may be missing, such as a translation not held.</summary>
    public static string? UnstarredOrNull(string? name) => name is null ? null : Unstarred(name);

    /// <summary>The first letter in upper case, past any opening quotation mark or bracket; a title opening with a digit is left.</summary>
    private static string Capitalised(string title)
    {
        for (var i = 0; i < title.Length; i++)
        {
            var letter = title[i];
            if (char.IsLetter(letter))
            {
                return char.IsLower(letter)
                    ? title[..i] + char.ToUpperInvariant(letter) + title[(i + 1)..]
                    : title;
            }

            if (char.IsLetterOrDigit(letter))
            {
                return title;
            }
        }

        return title;
    }

    /// <summary>What the notes say the year rests on, where they flag it with a starred heading.</summary>
    public static string? Of(string? notes) =>
        notes is null ? null
        : notes.Contains("*HYPOTHESIS", StringComparison.Ordinal) ? Hypothesis
        : notes.Contains("*EXTRA-BIBLICAL", StringComparison.Ordinal) ? OutsideScripture
        : null;
}
