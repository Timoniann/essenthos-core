using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// A language's name for a record put back to the spelling its texts print where the record is
/// named, in place of one a generation pass read off a few verses.
/// </summary>
/// <param name="Cases">
/// The forms to hold, by case; a case mapped to null is one whose old form goes and that the texts
/// give no new form for.
/// </param>
/// <param name="Replaced">The forms they replace, by case, for the source to say.</param>
internal sealed record PrintedName(
    int EntityId,
    string Language,
    IReadOnlyDictionary<string, string?> Cases,
    IReadOnlyDictionary<string, string> Replaced);

/// <summary>
/// The rule that a name in a language comes from where the record is mostly named.
///
/// <para>
/// The descriptor pass gave the model a record's verses with the Ohienko text of the first few,
/// which are the first in the canon. Galilee's first verses are Joshua's, where the Ohienko Bible
/// writes <em>Ґаліл</em>; its seventy Gospel and Acts verses say <em>Галілеї</em>, and the nominative
/// the texts print is <em>Галілея</em>. So the name a pass read is held only where some text of the
/// language prints it in at least a third of the record's words. Where every text prints it less —
/// and prints it at all, so the form came from somewhere rather than from the model's own idea of the
/// name — the name is the nominative of the spelling the language's texts print most.
/// </para>
///
/// <para>
/// Only another spelling of the same name: the record has one name (Abraham, filed as Abram, has
/// two, and his heading moves by the verses that call him each), the two spellings fold to one
/// opening the other or one letter apart, and neither is a word of the other (<em>Kirjath</em> is
/// not <em>Kirjath-Jearim</em>). The nominative is a spelling the texts print — the one the
/// commonest spelling is a case of, even when it is printed once — and in a declining language the
/// genitive and locative are printed spellings too, or nothing changes: a name with a borrowed case
/// is worse than the name it replaces.
/// </para>
/// </summary>
internal static class PrintedNames
{
    /// <summary>The language of the headwords, whose name is the record's own.</summary>
    private const string Headword = "eng";

    private const string German = "deu";

    /// <summary>
    /// A text printing the held name in this share of the record's words or more is one where the
    /// record is named so, and the name stands.
    /// </summary>
    private const double HeldShare = 1.0 / 3.0;

    /// <summary>The share of the language's words the new spelling must print, so it is the name and not a rival.</summary>
    private const double MostShare = 0.5;

    /// <summary>Fewer words than this in a language say too little about where the record is named.</summary>
    private const int FewestWords = 6;

    /// <summary>The letters a spelling may run past the stem every held case shares and still be one of them.</summary>
    private const int EndingLetters = 3;

    /// <summary>The shortest stem the held cases must share for that test to say anything.</summary>
    private const int ShortestStem = 3;

    /// <summary>The kinds of label that are a record's name rather than a title or a people's word.</summary>
    internal static readonly IReadOnlySet<string> NameKinds = new HashSet<string>(StringComparer.Ordinal) { "name", "proper name" };

    /// <param name="renderings">Every text's spellings.</param>
    /// <param name="languages">Each text's language.</param>
    /// <param name="held">The forms a generation pass read, by record and language, and by case.</param>
    /// <param name="oneNamed">The people and places whose names are one name.</param>
    public static IEnumerable<PrintedName> Of(
        IEnumerable<Rendering> renderings,
        IReadOnlyDictionary<int, string> languages,
        IReadOnlyDictionary<(int Entity, string Language), IReadOnlyDictionary<string, string>> held,
        IReadOnlySet<int> oneNamed)
    {
        var printed = renderings
            .Where(r => oneNamed.Contains(r.EntityId) && languages.TryGetValue(r.TextId, out var language) && language != Headword)
            .GroupBy(r => (r.EntityId, Language: languages[r.TextId]));

        foreach (var spelled in printed)
        {
            if (held.GetValueOrDefault(spelled.Key) is not { } forms
                || forms.GetValueOrDefault(GrammaticalCases.Nominative) is not { } nominative
                || Corrected(spelled.Key.EntityId, spelled.Key.Language, [.. spelled], forms, nominative) is not { } name)
            {
                continue;
            }

            yield return name;
        }
    }

    private static PrintedName? Corrected(
        int entity,
        string language,
        IReadOnlyList<Rendering> spellings,
        IReadOnlyDictionary<string, string> forms,
        string nominative)
    {
        var stem = SharedStem(forms.Values);
        bool Held(string form) => IsHeld(form, nominative, forms.Values, stem);

        if (!spellings.Any(s => Held(s.Form)))
        {
            return null;
        }

        if (spellings.GroupBy(s => s.TextId)
            .Any(text => text.Where(s => Held(s.Form)).Sum(s => s.Occurrences) >= text.Sum(s => s.Occurrences) * HeldShare))
        {
            return null;
        }

        var all = spellings.Sum(s => s.Occurrences);
        if (all < FewestWords)
        {
            return null;
        }

        var counted = spellings
            .GroupBy(s => s.Form, StringComparer.Ordinal)
            .Select(g => (Form: g.Key, Folded: g.First().Folded, Occurrences: g.Sum(s => s.Occurrences)))
            .ToList();
        var commonest = counted
            .Where(s => !Held(s.Form))
            .OrderByDescending(s => s.Occurrences).ThenByDescending(s => s.Form.Length)
            .ThenBy(s => s.Form, StringComparer.Ordinal)
            .Select(s => s.Form)
            .FirstOrDefault();
        if (commonest is null)
        {
            return null;
        }

        var name = counted
                       .Where(s => s.Form != commonest && CaseOf(language, commonest, s.Form))
                       .OrderByDescending(s => s.Occurrences).ThenBy(s => s.Form, StringComparer.Ordinal)
                       .Select(s => s.Form)
                       .FirstOrDefault()
                   ?? commonest;
        var family = counted.Where(s => s.Form == name || CaseOf(language, s.Form, name)).ToList();
        if (family.Sum(s => s.Occurrences) < all * MostShare || !SameName(name, nominative))
        {
            return null;
        }

        var cases = new Dictionary<string, string?>(StringComparer.Ordinal) { [GrammaticalCases.Nominative] = name };
        foreach (var grammaticalCase in forms.Keys.Where(c => c != GrammaticalCases.Nominative))
        {
            var form = grammaticalCase switch
            {
                GrammaticalCases.Genitive when NameCases.Declining.Contains(language) => NameCases.Genitive(language, family, name),
                GrammaticalCases.Locative when NameCases.Declining.Contains(language) => NameCases.Locative(language, family, name),
                GrammaticalCases.Genitive when language == German => family.Any(s => s.Form == name + "s") ? name + "s" : null,
                GrammaticalCases.Genitive => null,
                _ => name,
            };

            if (form is null && NameCases.Declining.Contains(language))
            {
                return null;
            }

            cases[grammaticalCase] = form;
        }

        return new PrintedName(entity, language, cases, forms);
    }

    /// <summary>
    /// Whether a printed spelling is the held name: one of its held cases, another case of its
    /// nominative, or its stem with an ending — <em>Єгипту</em> beside <em>Єгипет</em>, <em>Єгипта</em>.
    /// </summary>
    private static bool IsHeld(string form, string nominative, IEnumerable<string> forms, string stem) =>
        forms.Any(f => string.Equals(f, form, StringComparison.OrdinalIgnoreCase))
        || NameCases.Declines(form, nominative)
        || (stem.Length >= ShortestStem
            && form.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
            && form.Length - stem.Length <= EndingLetters);

    /// <summary>The letters every held case of the name opens with.</summary>
    private static string SharedStem(IEnumerable<string> forms) =>
        forms.Aggregate((string?)null, (shared, form) =>
        {
            if (shared is null)
            {
                return form;
            }

            var length = 0;
            while (length < shared.Length && length < form.Length
                   && char.ToLowerInvariant(shared[length]) == char.ToLowerInvariant(form[length]))
            {
                length++;
            }

            return shared[..length];
        }) ?? "";

    /// <summary>
    /// Whether a printed spelling is another case of a nominative: as <see cref="NameCases.Declines"/>
    /// reads a declining language, and German's genitive <em>-s</em>.
    /// </summary>
    private static bool CaseOf(string language, string form, string nominative) =>
        language == German ? form == nominative + "s" : NameCases.Declines(form, nominative);

    /// <summary>
    /// Whether two spellings are one name: folded, one opens the other or they are a letter apart,
    /// and neither is one word of the other.
    /// </summary>
    internal static bool SameName(string one, string other)
    {
        var a = NameFolding.Fold(one);
        var b = NameFolding.Fold(other);
        if (a.Length == 0 || b.Length == 0 || IsAWordOf(one, other) || IsAWordOf(other, one))
        {
            return false;
        }

        return a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal) || OneLetterApart(a, b);
    }

    private static readonly char[] WordBreaks = [' ', '-'];

    private static bool IsAWordOf(string word, string name)
    {
        var parts = name.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 && parts.Any(part => NameFolding.Fold(part) == NameFolding.Fold(word));
    }

    private static bool OneLetterApart(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1)
        {
            return false;
        }

        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
        {
            i++;
        }

        var j = 0;
        while (j < a.Length - i && j < b.Length - i && a[^(j + 1)] == b[^(j + 1)])
        {
            j++;
        }

        return a.Length - i - j <= 1 && b.Length - i - j <= 1;
    }
}
