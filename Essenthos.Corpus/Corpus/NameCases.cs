namespace Essenthos.Core.Corpus;

/// <summary>
/// Which printed spelling of a name is which case, read off the spellings a text prints rather than
/// made: a Ukrainian or Russian name's genitive and locative are chosen among what the texts print,
/// never built from a stem.
/// </summary>
internal static class NameCases
{
    /// <summary>The languages whose texts decline a name, so that a genitive is a spelling of its own.</summary>
    public static readonly IReadOnlySet<string> Declining = new HashSet<string>(StringComparer.Ordinal) { "ukr", "rus" };

    /// <summary>The endings by which a name in <em>-а</em> or <em>-я</em> is in another case: <em>Варнави</em>, <em>Марією</em>.</summary>
    private static readonly string[] FeminineEndings = ["ы", "и", "і", "ї", "у", "ю", "е", "є", "ою", "ею", "єю", "ой", "ей"];

    /// <summary>
    /// The endings by which any other name is in another case: <em>Петра</em>, <em>Мойсея</em>,
    /// <em>Павлові</em>, <em>Авраамом</em>.
    /// </summary>
    private static readonly string[] OtherEndings = ["а", "я", "у", "ю", "і", "е", "є", "ові", "еві", "єві", "ом", "ем", "єм", "ым", "ой", "ы"];

    /// <summary>
    /// Whether a spelling is another case of a printed spelling on the same stem: <em>Васемафы</em>
    /// beside <em>Васемафа</em>, <em>Петра</em> beside <em>Петр</em> or <em>Петро</em>, <em>Мойсея</em>
    /// beside <em>Мойсей</em>; and a word made from it, as the possessive <em>Еліцафанових</em> is made
    /// from <em>Еліцафан</em>. Two nominatives are not: <em>Петро</em> is not a case of <em>Петр</em>.
    /// The letters are compared as printed, so <em>Галілеї</em> is no case of <em>Ґаліл</em>.
    /// </summary>
    public static bool Declines(string form, string nominative)
    {
        if (nominative.Length < 2 || form.Length <= nominative.Length - 1)
        {
            return false;
        }

        var last = char.ToLowerInvariant(nominative[^1]);
        var stem = "аяоьй".Contains(last) ? nominative[..^1] : nominative;
        var endings = last is 'а' or 'я' ? FeminineEndings : OtherEndings;
        if (!form.StartsWith(stem, StringComparison.Ordinal))
        {
            return false;
        }

        var ending = form[stem.Length..].ToLowerInvariant();
        return endings.Contains(ending, StringComparer.Ordinal) || (ending.Length > 1 && form.Length > nominative.Length);
    }

    /// <summary>
    /// The genitive among the printed spellings of a name: the one printed most of those whose ending
    /// a genitive of that nominative takes. A name ending in <em>-а</em> or <em>-я</em> takes
    /// <em>-и</em> (<em>Юда</em>, <em>Юди</em>; never the dative <em>Юді</em>), one in <em>-я</em> takes
    /// <em>-і</em> or <em>-ї</em> (<em>Марія</em>, <em>Марії</em>), and any other takes <em>-а</em> or
    /// <em>-я</em> (<em>Павло</em>, <em>Павла</em>); Russian likewise, with <em>-ы</em>. Where no printed
    /// spelling ends so, there is no genitive.
    /// </summary>
    public static string? Genitive(
        string language,
        IReadOnlyList<(string Form, string Folded, int Occurrences)> family,
        string nominative)
    {
        if (!Declining.Contains(language) || nominative.Length == 0)
        {
            return null;
        }

        var last = char.ToLowerInvariant(nominative[^1]);
        var endings = (last, language) switch
        {
            ('а', "rus") => "ыи",
            ('а', _) => "и",
            ('я', "rus") => "и",
            ('я', _) => "іїи",
            _ => "ая",
        };
        return family
            .Where(s => !string.Equals(s.Form, nominative, StringComparison.Ordinal)
                        && s.Form.Length > 0
                        && endings.Contains(char.ToLowerInvariant(s.Form[^1]))
                        && !s.Form.Contains(' '))
            .OrderByDescending(s => s.Occurrences)
            .ThenBy(s => s.Form, StringComparer.Ordinal)
            .Select(s => s.Form)
            .FirstOrDefault();
    }

    /// <summary>
    /// The locative among the printed spellings of a name, one letter on from its stem as a phrase
    /// <em>у …</em> puts it: <em>у Галілеї</em>, <em>у Самарії</em>, <em>у Рамі</em>, <em>у Єрусалимі</em>;
    /// Russian <em>в Галилее</em>, <em>в Самарии</em>. Only a spelling that is the nominative's stem and
    /// that one letter, so an adjective made from the name is never taken for it; where none is
    /// printed there is no locative.
    /// </summary>
    public static string? Locative(
        string language,
        IReadOnlyList<(string Form, string Folded, int Occurrences)> family,
        string nominative)
    {
        if (!Declining.Contains(language) || nominative.Length < 2)
        {
            return null;
        }

        var last = char.ToLowerInvariant(nominative[^1]);
        var stem = "аяоьй".Contains(last) ? nominative[..^1] : nominative;
        var endings = (last, language) switch
        {
            ('а', "rus") => "е",
            ('я', "rus") => "еи",
            (_, "rus") => "е",
            ('а', _) => "і",
            ('я', _) => "ії",
            _ => "іїу",
        };
        return family
            .Where(s => s.Form.Length == stem.Length + 1
                        && s.Form.StartsWith(stem, StringComparison.Ordinal)
                        && !string.Equals(s.Form, nominative, StringComparison.Ordinal)
                        && endings.Contains(char.ToLowerInvariant(s.Form[^1])))
            .OrderByDescending(s => s.Occurrences)
            .ThenBy(s => s.Form, StringComparer.Ordinal)
            .Select(s => s.Form)
            .FirstOrDefault();
    }
}
