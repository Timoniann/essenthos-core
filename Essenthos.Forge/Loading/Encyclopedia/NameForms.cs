using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// What a stored name form may not carry: the preposition or the article the phrase around it
/// already supplies.
///
/// <para>
/// <c>DescriptorPhrasings</c> renders a place clause as <em>похований у </em> and then the target's
/// locative, so a locative stored as <em>в Авані</em> reaches a reader as
/// <em>похований у в Авані</em>. Hundreds of the forms the generation pass has produced carry
/// one, and the count grows with every batch it loads, because the prompt that produced them never
/// said the sentence supplies its own.
/// </para>
///
/// <para>
/// **Taken off on the way in rather than at render time, and only when a word stands before
/// another.** At render time nothing knows which words of a form are the name; here the rule is
/// narrow enough to be certain — a form of two or more words whose first is a bare preposition or
/// article of that language. No Biblical name is the word <em>у</em>, and a one-word form is left
/// exactly as it stands, so the ambiguity the render-time version would have had does not arise.
/// The rest of the form is the model's word and is never altered.
/// </para>
/// </summary>
internal static class NameForms
{
    /// <summary>
    /// What the phrase supplies for itself, per language, and so what a form may not repeat. The
    /// Slavic sets are the prepositions the place phrasings use; the German and Spanish sets are
    /// those plus the articles, which stand where a Slavic preposition does.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, HashSet<string>> Supplied =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["eng"] = new(StringComparer.OrdinalIgnoreCase) { "in", "at", "on", "of", "the" },
            ["ukr"] = new(StringComparer.OrdinalIgnoreCase)
                { "в", "у", "на", "при", "під", "до", "з", "із", "зі" },
            ["rus"] = new(StringComparer.OrdinalIgnoreCase)
                { "в", "во", "на", "при", "под", "до", "с", "со", "у" },
            ["deu"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "in", "im", "zu", "zum", "zur", "am", "an", "auf", "bei", "beim", "nach", "von",
                "vom",
            },
            ["spa"] = new(StringComparer.OrdinalIgnoreCase)
                { "en", "a", "de", "del", "al", "el", "la", "los", "las" },
        };

    /// <summary>
    /// The German articles, which a phrase supplies only before a nominative. A German genitive or
    /// dative of a name that is a common noun keeps its own — <em>Sohn der breiten Mauer</em>,
    /// <em>wohnte im alten Teich</em> — because no phrasing can know the name's gender, and without
    /// it the phrase reads <em>wohnte in breite Mauer</em>.
    /// </summary>
    private static readonly HashSet<string> GermanArticles =
        new(StringComparer.OrdinalIgnoreCase) { "der", "die", "das", "des", "dem", "den" };

    /// <summary>
    /// The form with nothing in front of the name that the phrase already says. Returns what it was
    /// given when there is nothing to take off, so a caller compares the two to learn whether the
    /// form it holds is sound.
    /// </summary>
    public static string Bare(string language, string form, string? grammaticalCase = null)
    {
        var bare = form.Trim();
        if (!Supplied.TryGetValue(language, out var supplied))
        {
            return bare;
        }

        if (language == "deu" && grammaticalCase is null or GrammaticalCases.Nominative)
        {
            supplied = [.. supplied, .. GermanArticles];
        }

        while (true)
        {
            var space = bare.IndexOf(' ');
            if (space <= 0 || !supplied.Contains(bare[..space].Trim('.', ',')))
            {
                return bare;
            }

            bare = bare[(space + 1)..].TrimStart();
        }
    }

    /// <summary>
    /// Whether a form is the one held with the article it was produced with in front of it: the
    /// German genitive and dative the loaders stored bare before an article stayed on them.
    /// </summary>
    public static bool RestoresItsArticle(string language, string grammaticalCase, string held, string form)
    {
        if (language != "deu" || grammaticalCase == GrammaticalCases.Nominative)
        {
            return false;
        }

        var space = form.IndexOf(' ');
        return space > 0 && GermanArticles.Contains(form[..space]) && form[(space + 1)..].TrimStart() == held;
    }
}
