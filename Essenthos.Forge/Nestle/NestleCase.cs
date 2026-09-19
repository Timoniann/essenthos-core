namespace Essenthos.Core.Nestle;

/// <summary>
/// A Greek word's case, read from the form code rather than from the attribute that claims to hold
/// it.
///
/// **The `case` attribute in Nestle 1904 does not hold a case.** It writes `neuter` — a gender —
/// where the word is nominative, and the value `nominative` never appears in the file at all. It is
/// not a rename either: measured over every word of the file, `case="neuter"` stands against a
/// nominative form code 20,629 times and against an accusative one 5. The attribute is unreliable
/// and the form code is not.
///
/// <para>
/// The form code is the standard morphology string — <c>N-NSF</c>, <c>V-PAP-NSM</c>, <c>P-1AS</c> —
/// whose second group, or third for a verb, is case, number and gender. A pronoun writes the person
/// first, so the case is the second character there. A finite verb's group is person and number and
/// an infinitive has none, which is correct: <c>V-PAI-3S</c> and <c>V-AAN</c> are not in any case.
/// Any group after that one is a note about the form, <c>-ATT</c> or <c>-S</c>, and not a case.
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
/// agree with the code everywhere, so reading this as *gender stored under case* is a
/// misdiagnosis: the gender is right and the case simply holds a value from the wrong vocabulary.
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

        // Where the file offers two readings, V-PEM-2P@@V-PNM-2P, the first is the one printed.
        var alternative = form.IndexOf("@@", StringComparison.Ordinal);
        var groups = (alternative < 0 ? form : form[..alternative]).Split('-');

        // The case lives in one group at a fixed position: straight after the part of speech, or
        // after the tense, voice and mood for a verb. Whatever follows it is a note about the form
        // -- ATT, ABB, N, S, K, C -- and never a case, and a verb's own tense group can look like one:
        // the aorist passive infinitive V-APN would otherwise read as accusative plural neuter.
        var position = groups[0] == "V" ? 2 : 1;
        return groups.Length > position ? FromGroup(groups[position]) : null;
    }

    /// <summary>
    /// The case letter of a case group, found by its shape rather than by its place, so that a group
    /// which only happens to begin with N, G, D, A or V is not read as one: NUI is an indeclinable
    /// numeral, PRI an indeclinable proper noun, 3S a person and number, and none of them has a case.
    /// </summary>
    private static string? FromGroup(string group) => group.Length switch
    {
        // NSF: case, number, gender.
        3 when IsCase(group[0]) && IsNumber(group[1]) && IsGender(group[2]) => Named(group[0]),
        // 1AS: a pronoun writes its person first.
        3 when IsPerson(group[0]) && IsCase(group[1]) && IsNumber(group[2]) => Named(group[1]),
        // 3ASM: a reflexive pronoun, the person and then the case, number and gender.
        4 when IsPerson(group[0]) && IsCase(group[1]) && IsNumber(group[2]) && IsGender(group[3])
            => Named(group[1]),
        // 1SASF: a possessive, the owner's person and number and then the word's own case group.
        5 when IsPerson(group[0]) && IsNumber(group[1]) && IsCase(group[2]) && IsNumber(group[3])
            && IsGender(group[4]) => Named(group[2]),
        _ => null,
    };

    private static bool IsCase(char letter) => Named(letter) is not null;

    private static bool IsNumber(char letter) => letter is 'S' or 'P';

    private static bool IsGender(char letter) => letter is 'M' or 'F' or 'N';

    private static bool IsPerson(char letter) => letter is '1' or '2' or '3';
}
