namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a place's card says it is called today.
///
/// <para>
/// The stored name is one source's statement and the point, the picture and the list of sites are
/// the gazetteer's likeliest answer, and where the two disagree the card contradicts its own map:
/// Horeb named as Jabal al Lawz above a photograph of Jebel Musa. So the stored name stands only
/// where it does not name another of the gazetteer's sites for the place; otherwise the card names the
/// likeliest site, which is where the point, the picture and the first line of the list already are.
/// Where the gazetteer's likeliest answer is no site, the stored name stands.
/// </para>
/// </summary>
internal static class PlaceToday
{
    public static string? Of(string? stated, IList<PlaceCandidateResponse>? answers) => Of(stated, answers, s => s);

    /// <summary>
    /// The same answer, said through <paramref name="say"/> — the reader's language — and null where
    /// <paramref name="say"/> has no words for the English it chose.
    /// </summary>
    public static string? Of(string? stated, IList<PlaceCandidateResponse>? answers, Func<string, string?> say)
    {
        if (stated is null || answers?.FirstOrDefault(a => a.Likeliest) is not { Special: null, Site: { } likeliest })
        {
            return stated is null ? null : say(stated);
        }

        return say(Elsewhere(stated, answers) is { } site ? site : stated);
    }

    /// <summary>
    /// A place's line under its name. Where the line is the stored name and that name is another of the
    /// place's sites, it says the site the point is at, as the card does.
    /// </summary>
    public static string? Line(string? line, string? stated, IList<PlaceCandidateResponse>? answers) =>
        line is not null && stated is not null && Same(line, stated) && Elsewhere(stated, answers) is { } site
            ? site
            : line;

    /// <summary>The likeliest site, where the stored name names another of the place's sites instead.</summary>
    private static string? Elsewhere(string stated, IList<PlaceCandidateResponse>? answers) =>
        answers?.FirstOrDefault(a => a.Likeliest) is { Special: null, Site: { } likeliest }
        && !Same(likeliest, stated)
        && answers.Any(a => !a.Likeliest && a.Site is { } site && Same(site, stated))
            ? likeliest
            : null;

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
