namespace Essenthos.Core.Accounts;

/// <summary>
/// One of the site's sitemap lists as it was last seen: a hash of the addresses it holds, and when that
/// hash was first seen. It is what dates the list in the sitemap index (<c>lastmod</c>): a list whose
/// addresses did not change keeps its date across releases, and one that did is dated the moment the
/// API first wrote it so. Here rather than in the corpus, which is rebuilt with every release and
/// remembers nothing of the one before; every API process of one environment shares it.
/// </summary>
public class SitemapList
{
    /// <summary>The list's name, as the index names it without its extension: <c>verses-john</c>.</summary>
    public required string Name { get; set; }

    public required string Hash { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public override string ToString() => $"SitemapList({Name}, {Hash}, {ChangedAt:O})";
}
