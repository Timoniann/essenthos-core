using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The Wikipedia article a record's page links to, in the language the reader's page is in.
///
/// <para>
/// The language is the interface's, the one every word on the page but the names is said in — not the
/// language of the text being read, which only names people and places. An article is a page a reader
/// reads, so it is in the language they read the site in; a Ukrainian reader of the King James is sent
/// to the Ukrainian article.
/// </para>
///
/// <para>
/// Where the reader's Wikipedia has no article on the record, or the reader's language has no Wikipedia
/// kept here, the English article stands in. The response says which Wikipedia it is, and the page says
/// so beside the link, so an English article never reads as one in the reader's language.
/// </para>
/// </summary>
internal static class WikipediaLinks
{
    private const string Fallback = "en";

    /// <summary>The Wikipedia of a language the corpus spells <c>ukr</c> or the interface spells <c>uk</c>, or null where none is kept.</summary>
    public static string? WikipediaOf(string? language) => LanguageCodes.Normalise(language) switch
    {
        "eng" => "en",
        "ukr" => "uk",
        "deu" => "de",
        "spa" => "es",
        _ => null,
    };

    public static async Task<EntityWikipediaResponse?> Of(
        AppDbContext db, int entityId, string? language, CancellationToken cancellationToken)
    {
        var wikipedia = WikipediaOf(language) ?? Fallback;
        var articles = await db.EntityWikipedia
            .Where(w => w.EntityId == entityId && (w.Language == wikipedia || w.Language == Fallback))
            .Select(w => new { w.Language, w.Title })
            .ToListAsync(cancellationToken);
        var article = articles.FirstOrDefault(a => a.Language == wikipedia)
            ?? articles.FirstOrDefault(a => a.Language == Fallback);
        return article is null
            ? null
            : new EntityWikipediaResponse(article.Language, article.Title, Address(article.Language, article.Title));
    }

    /// <summary>The article's address: the title with its spaces as underscores, escaped as a path segment.</summary>
    public static string Address(string wikipedia, string title) =>
        $"https://{wikipedia}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
}
