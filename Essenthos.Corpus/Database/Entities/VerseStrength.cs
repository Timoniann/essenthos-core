using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// How strongly one text's verses of one canonical chapter are linked to one reference text's, counted ahead of time
/// (<see cref="Corpus.VerseStrengths"/>): the reader lays it over two panes side by side. Only chapters where some link
/// joins the pair, or only verse links a reading drew do, have a row.
///
/// <para>
/// A build product (<c>build/products</c>, <c>strength/{text}</c>): counted from the served tables once a build has filled
/// them, and counted again for a text whose links, verses or frame moved.
/// </para>
/// </summary>
[PrimaryKey(nameof(TextId), nameof(ReferenceId), nameof(CanonicalBook), nameof(CanonicalChapter))]
public class VerseStrength
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    /// <summary>The text the verses are measured against.</summary>
    public int ReferenceId { get; set; }

    public Text? Reference { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    /// <summary>Whether the two are joined here only by verse links a reading of where each verse falls drew.</summary>
    public bool ByReading { get; set; }

    /// <summary>Each verse's links, stated links and mean confidence, as <see cref="Corpus.VerseStrengths.Pack"/> writes them.</summary>
    public required string Verses { get; set; }
}
