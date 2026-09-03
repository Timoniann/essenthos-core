namespace Essenthos.Core.Nestle;

/// <summary>
/// A Greek word's case, read from the form code rather than from the attribute that claims to hold
/// it.
///
/// **The `case` attribute in Nestle 1904 does not hold a case.** It writes `neuter` — a gender —
/// where the word is nominative, and the value `nominative` never appears in the file at all. It is
/// not a rename either: measured over every word of the file, `case="neuter"` stands against a
/// nominative form code 20,629 times and against an accusative one 5. The attribute is unreliable
/// and the form code is not. PRB-0066.
///
/// <para>
/// The form code is the standard morphology string — <c>N-NSF</c>, <c>V-PAP-NSM</c>, <c>P-1AS</c> —
/// whose last group, where it is three characters, is case, number and gender. A pronoun writes the
/// person first, so the case is the second character there. A finite verb's last group is person
/// and number, two characters, and has no case, which is correct: <c>V-PAI-3S</c> is not in any case.
/// </para>
///
/// <para>
/// Measured against the whole file, the code is strictly the better source:
/// </para>
///
/// <code>
/// both present and agreeing         49,043
/// both present and disagreeing      20,640   (20,629 of them neuter against nominative)
/// code has a case, attribute none   12,401   (mostly adjectives, which carry no case attribute)
/// attribute has one, code none         653
/// </code>
///
/// <para>
/// So the code wins where it speaks, and the attribute is read only where the code is silent — and
/// then only if it names a real case, because <c>neuter</c> is not one and the gender attribute
/// already carries the gender correctly. Gender was never wrong: masculine, feminine and neuter
/// agree with the code everywhere, which is why PRB-0162's reading of this as *gender stored under
/// case* is a misdiagnosis of the same fault.
/// </para>
/// </summary>
internal static class NestleCase
{
    private static string? Named(char letter) => letter switch
    {
        'N' => "nominative",
        'G' => "genitive",
        'D' => "dative",
        'A' => "accusative",
        'V' => "vocative",
        _ => null,
    };

    /// <summary>
    /// The case the word is in, or null where it is in none — which is the right answer for a
    /// finite verb, a preposition, a conjunction and an adverb, and is not the same as unknown.
    /// </summary>
    /// <param name="form">The morphology code, as the file's <c>form</c> attribute writes it.</param>
    /// <param name="attribute">
    /// The file's own <c>case</c> attribute, read only where the code says nothing and only if it
    /// names a case. It says <c>neuter</c> 138 times where the code is silent, and a gender is not
    /// an answer to this question.
    /// </param>
    public static string? Of(string? form, string? attribute)
    {
        if (FromForm(form) is { } stated)
        {
            return stated;
        }

        return attribute is not null && Cases.Contains(attribute, StringComparer.Ordinal)
            ? attribute
            : null;
    }

    private static readonly string[] Cases =
        ["nominative", "genitive", "dative", "accusative", "vocative"];

    private static string? FromForm(string? form)
    {
        if (string.IsNullOrEmpty(form))
        {
            return null;
        }

        // A code with no hyphen is a part of speech and nothing else -- CONJ, PREP, ADV, PRT -- and
        // has no groups to read. Without this check ADV was three characters beginning with A and
        // came back accusative, which a test caught and the file would not have.
        var hyphen = form.LastIndexOf('-');
        if (hyphen < 0)
        {
            return null;
        }

        var last = form.AsSpan()[(hyphen + 1)..];
        if (last.Length != 3)
        {
            return null;
        }

        // A pronoun writes its person first: P-1AS is first person, accusative, singular.
        return char.IsAsciiDigit(last[0]) ? Named(last[1]) : Named(last[0]);
    }
}
