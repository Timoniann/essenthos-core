using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A reader's language as the corpus spells it, from however the request spelled it.
///
/// The corpus names languages by their three-letter codes — <c>ukr</c>, <c>deu</c> — and a request
/// asking for <c>uk</c>, the two-letter code a browser and an interface locale use, was answered as
/// though it had asked for a language the corpus holds nothing in: every name came back English.
/// Both spellings of the languages the corpus speaks are accepted; anything else is passed on as it
/// was written, lower-cased, and finds nothing, as before.
/// </summary>
internal static class LanguageCodes
{
    private static readonly IReadOnlyDictionary<string, string> ThreeLetter =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "eng",
            ["uk"] = "ukr",
            ["de"] = "deu",
            ["es"] = "spa",
            ["ru"] = "rus",
        };

    /// <summary>The query parameter every endpoint names a reader's language with.</summary>
    public const string Parameter = "language";

    /// <summary>The request's language, if it names one by a spelling the corpus does not use, respelled.</summary>
    public static void Rewrite(HttpRequest request)
    {
        if (!request.Query.TryGetValue(Parameter, out var asked) || asked.Count != 1
            || Normalise(asked[0]) is not { } code || code == asked[0])
        {
            return;
        }

        var query = request.Query.ToDictionary(pair => pair.Key, pair => pair.Value);
        query[Parameter] = new StringValues(code);
        request.Query = new QueryCollection(query);
    }

    public static string? Normalise(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return language;
        }

        var trimmed = language.Trim();
        return ThreeLetter.TryGetValue(trimmed, out var code) ? code : trimmed.ToLowerInvariant();
    }
}
