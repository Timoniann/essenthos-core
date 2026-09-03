namespace Essenthos.Core.Endpoints;

/// <summary>
/// The other identifiers a text answers to.
///
/// One translation is spelled differently by every piece of Bible software that serves it: the
/// 1876 Russian Synodal is <c>RUSV</c> at Bible Gateway, <c>SYNO</c> at YouVersion and
/// <c>SYNOD</c> at bolls.life, and a reader pasting a reference from one of them into a URL here
/// should reach the text rather than a 404. Only the canonical slug is ever answered with, so an
/// alias is a way in and never a second name for the same text in a response.
///
/// The declarations live here rather than on the <c>text</c> row for two reasons. A row is written
/// once — the loader returns early for a text that is already loaded — so a column would reach
/// none of the nine texts already in the corpus without a reload. And the invariant that matters,
/// that no identifier resolves to two texts, spans the aliases *and* the canonical slugs, which no
/// index on a single column can express; here it is one static check over the whole set.
///
/// **Every alias names a source.** An identifier nobody publishes is one nobody will type, and a
/// wrong one silently serves the wrong text — which is why neither Textus Receptus carries the
/// <c>TR</c> that bolls.life uses, that being a third edition, Elzevir 1624.
/// </summary>
internal static class TextAliases
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Declared =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Bible Gateway spells the Russian Synodal RUSV, which is where our slug agrees with
            // the field; YouVersion spells it SYNO (version 400) and bolls.life SYNOD. All three
            // name the same 1876 translation.
            ["rusv"] = ["syno", "synod"],

            // Door43 publishes the Ohienko text as uk_ubio and bolls.life serves it as UBIO. It is
            // the identifier that established which Ukrainian Bible this file is.
            ["ukr"] = ["ubio"],
        };

    private static readonly Dictionary<string, string> CanonicalBySpelling = Index();

    /// <summary>The aliases each text answers to, by its canonical slug.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> All => Declared;

    /// <summary>
    /// The canonical slug this spelling stands for, or null where it stands for nothing. Case is
    /// ignored, the way every other identifier lookup here ignores it.
    /// </summary>
    public static string? Canonical(string spelling) =>
        CanonicalBySpelling.GetValueOrDefault(spelling);

    /// <summary>
    /// The other names this text answers to, empty where it has none. Empty rather than null
    /// because a caller listing them wants a list either way.
    /// </summary>
    public static IReadOnlyList<string> Of(string canonicalSlug) =>
        Declared.GetValueOrDefault(canonicalSlug) ?? [];

    private static Dictionary<string, string> Index()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, aliases) in Declared)
        {
            foreach (var alias in aliases)
            {
                if (Declared.ContainsKey(alias))
                {
                    throw new InvalidOperationException(
                        $"\"{alias}\" is declared as an alias of \"{canonical}\" and is also a text's own slug. " +
                        "An identifier a text already answers to cannot be given away; drop the alias.");
                }

                if (index.TryGetValue(alias, out var taken))
                {
                    throw new InvalidOperationException(
                        $"\"{alias}\" is declared as an alias of both \"{taken}\" and \"{canonical}\". One " +
                        "identifier reaches one text; drop it from whichever text does not publish it.");
                }

                index[alias] = canonical;
            }
        }

        return index;
    }
}
