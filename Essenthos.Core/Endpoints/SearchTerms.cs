namespace Essenthos.Core.Endpoints;

/// <summary>
/// How one term of a query was matched. The response names it per term, because a query can use
/// more than one strategy at once and reporting only the corpus-level one would hide that
/// "the" was matched literally while "beginning" was stemmed.
/// </summary>
internal enum TermMatching
{
    /// <summary>Matched through a Postgres dictionary, so "loved" answers a search for "love".</summary>
    Stemmed,

    /// <summary>
    /// The dictionary discards the term as a stop word and produces an empty tsquery, so the term
    /// is matched as a whole word instead. Without this the whole query answers nothing.
    /// </summary>
    Literal,

    /// <summary>Matched as a substring of the stored text, exactly as it was typed.</summary>
    Substring,

    /// <summary>
    /// Matched as a substring of the accent- and point-free form of the stored text, so an
    /// unvocalised Hebrew or unaccented Greek query finds the vocalised word.
    /// </summary>
    Folded,
}

/// <summary>
/// One term of a query and how it will be matched. <see cref="Match"/> is what is compared
/// against the corpus and <see cref="Text"/> is what the caller typed; they differ when the term
/// had to be folded, and the response reports the typed form.
/// </summary>
internal sealed record SearchTerm(string Text, TermMatching Matching, string Match)
{
    public SearchTerm(string text, TermMatching matching) : this(text, matching, text)
    {
    }
}

/// <summary>
/// Search matches whole words, because that is the granularity the corpus is stored at: a verse
/// matches when every term of the query matches some word in it.
/// </summary>
internal static class SearchTerms
{
    /// <summary>
    /// Each extra term is another index scan and another intersection, and a query longer than
    /// this is a sentence rather than a search.
    /// </summary>
    public const int MaxTerms = 8;

    public const string FullTextMatching = "fulltext";

    public const string SubstringMatching = "substring";

    /// <summary>
    /// Every term matched a whole word of the folded text. This is what a search of this corpus
    /// normally does — the words are stored one to a row, so matching one is matching a word and
    /// not a run of letters inside one, and calling that "substring" told the caller the opposite
    /// of what happened.
    /// </summary>
    public const string WholeWordMatching = "whole-word";

    /// <summary>
    /// More than one strategy answered the query — a stop word matched literally beside a stemmed
    /// term, say. The per-term list says which was which.
    /// </summary>
    public const string MixedMatching = "mixed";

    public const string EnglishDictionary = "english";

    public const string RussianDictionary = "russian";

    /// <summary>
    /// Postgres dictionaries the loaded languages can be stemmed with. Ukrainian, Hebrew and
    /// Greek have none, so those corpora fall back to substring matching and say so.
    /// </summary>
    private static readonly Dictionary<string, string> DictionariesByLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = EnglishDictionary,
        ["eng"] = EnglishDictionary,
        ["ru"] = RussianDictionary,
        ["rus"] = RussianDictionary,
    };

    public static string[] Parse(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Trim(TrimmedPunctuation))
            .Where(term => term.Length > 0)
            .Take(MaxTerms)
            .ToArray();
    }

    public static string? Dictionary(string language)
    {
        return DictionariesByLanguage.GetValueOrDefault(language);
    }

    public static string Matching(string language)
    {
        return Dictionary(language) is null ? SubstringMatching : FullTextMatching;
    }

    /// <summary>
    /// The corpus-level answer to "how was this query matched". It is only <c>fulltext</c> or
    /// <c>substring</c> when every term was matched that way; anything else is <c>mixed</c>, and
    /// the caller reads the per-term list for the detail.
    /// </summary>
    public static string Matching(IReadOnlyList<SearchTerm> terms)
    {
        if (terms.Count == 0)
        {
            return SubstringMatching;
        }

        if (terms.All(t => t.Matching == TermMatching.Stemmed))
        {
            return FullTextMatching;
        }

        if (terms.All(t => t.Matching == TermMatching.Folded))
        {
            return WholeWordMatching;
        }

        if (terms.All(t => t.Matching == TermMatching.Substring))
        {
            return SubstringMatching;
        }

        return MixedMatching;
    }

    public static string Name(TermMatching matching)
    {
        return matching switch
        {
            TermMatching.Stemmed => "stemmed",
            TermMatching.Literal => "literal",
            TermMatching.Folded => "folded",
            _ => "substring",
        };
    }

    public static string FormatHint()
    {
        return "A search needs a term. Pass ?q= with at least one word, for example /v1/search?q=mercy.";
    }

    private static readonly char[] TrimmedPunctuation = ['.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']'];
}
