using System.Text.RegularExpressions;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a place's card says it is called today.
///
/// <para>
/// The stored name is one source's statement and the point, the picture and the list of sites are
/// the gazetteer's likeliest answer, and where the two disagree the card contradicts its own map:
/// Horeb named as Jabal al Lawz above a photograph of Jebel Musa. So the stored name stands only
/// where it does not name another of the gazetteer's sites for the place, and does not point at
/// another entry of the gazetteer by its catalogue number (<em>about 20 km around Goshen 3</em>);
/// otherwise the card names the likeliest site, which is where the point, the picture and the first
/// line of the list already are. Where the gazetteer's likeliest answer is no site, the stored name
/// stands with its catalogue numbers taken off.
/// </para>
/// </summary>
internal static partial class PlaceToday
{
    /// <summary>A name followed by the gazetteer's catalogue number: <em>Goshen 3</em>, <em>Mount Hor 1</em>.</summary>
    [GeneratedRegex(@"\p{Lu}[\p{L}’'‘\-]*(?: \p{Lu}[\p{L}’'‘\-]*)* \d+\b")]
    private static partial Regex CatalogueNumber();

    public static string? Of(string? stated, IList<PlaceCandidateResponse>? answers) => Of(stated, answers, s => s);

    /// <summary>
    /// The same answer, said through <paramref name="say"/> — the reader's language — and null where
    /// <paramref name="say"/> has no words for the English it chose.
    /// </summary>
    public static string? Of(string? stated, IList<PlaceCandidateResponse>? answers, Func<string, string?> say)
    {
        if (stated is null || answers?.FirstOrDefault(a => a.Likeliest) is not { Special: null, Site: { } likeliest })
        {
            return stated is null ? null : WithoutCatalogueNumbers(say(stated));
        }

        if (CatalogueNumber().IsMatch(stated))
        {
            return say(likeliest);
        }

        return say(Elsewhere(stated, answers) is { } site ? site : stated);
    }

    /// <summary>
    /// A place's line under its name. Where the line is the stored name and that name is another of the
    /// place's sites, it says the site the point is at, as the card does; otherwise it loses its
    /// catalogue numbers.
    /// </summary>
    public static string? Line(string? line, string? stated, IList<PlaceCandidateResponse>? answers) =>
        line is not null && stated is not null && Same(line, stated) && Elsewhere(stated, answers) is { } site
            ? site
            : WithoutCatalogueNumbers(line);

    /// <summary>The likeliest site, where the stored name names another of the place's sites instead.</summary>
    private static string? Elsewhere(string stated, IList<PlaceCandidateResponse>? answers) =>
        answers?.FirstOrDefault(a => a.Likeliest) is { Special: null, Site: { } likeliest }
        && !Same(likeliest, stated)
        && answers.Any(a => !a.Likeliest && a.Site is { } site && Same(site, stated))
            ? likeliest
            : null;

    /// <summary>
    /// A place's line with the gazetteer's catalogue numbers taken off the names it mentions: <em>about
    /// 20 km around Goshen</em>, not <em>Goshen 3</em>. The number tells the catalogue's namesakes apart
    /// and is no part of any name.
    /// </summary>
    public static string? WithoutCatalogueNumbers(string? line) =>
        line is null ? null : CatalogueNumber().Replace(line, match => match.Value[..match.Value.LastIndexOf(' ')]);

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
