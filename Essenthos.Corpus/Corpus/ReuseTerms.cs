namespace Essenthos.Core.Corpus;

/// <summary>
/// What a licence lets a reader do, in one of six words, so that a page can say it in the reader's
/// own language instead of printing the licence's name and leaving the reading to them.
///
/// <list type="bullet">
/// <item><c>free</c>: public domain or dedicated to it; no conditions.</item>
/// <item><c>by</c>: free to share and adapt, with credit.</item>
/// <item><c>by-sa</c>: with credit, and what is adapted from it under the same licence.</item>
/// <item><c>by-nc</c>: with credit, and not commercially.</item>
/// <item><c>by-nc-sa</c>: with credit, not commercially, and adaptations under the same licence.</item>
/// <item><c>own</c>: terms of the source's own, or none stated. None stated is not permission.</item>
/// </list>
///
/// Read from the licence's short name, which every declaration here states. A recorded permission
/// decides only where the name is a publisher's sentence rather than a licence anyone could
/// recognise; a name that says nothing recognisable is <c>own</c>, never <c>free</c>.
/// </summary>
public static class ReuseTerms
{
    public const string Free = "free";
    public const string Attribution = "by";
    public const string ShareAlike = "by-sa";
    public const string NonCommercial = "by-nc";
    public const string NonCommercialShareAlike = "by-nc-sa";
    public const string Own = "own";

    public static readonly IReadOnlyList<string> All =
        [Free, Attribution, ShareAlike, NonCommercial, NonCommercialShareAlike, Own];

    private static readonly char[] Separators = [' ', '-', '_', ',', '.', ';', ':', '(', ')'];

    /// <param name="licence">The licence's short name as the declaration states it.</param>
    /// <param name="redistribution">A text's recorded permission, spelled as the API spells it, or null.</param>
    public static string Of(string? licence, string? redistribution = null)
    {
        var words = new HashSet<string>(
            (licence ?? string.Empty).ToUpperInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
        var nonCommercial = words.Contains("NC");
        var shareAlike = words.Contains("SA");
        if (words.Contains("CC") && words.Contains("BY"))
        {
            return nonCommercial
                ? shareAlike ? NonCommercialShareAlike : NonCommercial
                : shareAlike ? ShareAlike : Attribution;
        }

        if (words.Contains("CC0") || words.Contains("UNLICENSE") || (words.Contains("PUBLIC") && words.Contains("DOMAIN")))
        {
            return Free;
        }

        return redistribution switch
        {
            "public-domain" or "permitted" => Free,
            "permitted-with-attribution" => Attribution,
            "share-alike" => ShareAlike,
            "non-commercial-only" => NonCommercial,
            _ => Own,
        };
    }
}
