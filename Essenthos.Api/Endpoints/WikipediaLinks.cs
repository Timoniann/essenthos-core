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
/// There is no fallback. A record whose item has no article in the reader's language gets no link, and
/// a link that reads as theirs is never to another language's article.
/// </para>
/// </summary>
internal static class WikipediaLinks
{
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
        if (WikipediaOf(language) is not { } wikipedia)
        {
            return null;
        }

        var title = await db.EntityWikipedia
            .Where(w => w.EntityId == entityId && w.Language == wikipedia)
            .Select(w => w.Title)
            .FirstOrDefaultAsync(cancellationToken);
        return title is null ? null : new EntityWikipediaResponse(wikipedia, title, Address(wikipedia, title));
    }

    /// <summary>The article's address: the title with its spaces as underscores, escaped as a path segment.</summary>
    public static string Address(string wikipedia, string title) =>
        $"https://{wikipedia}.wikipedia.org/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
}
