using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One text's side of the parallel view for one canonical chapter, written ahead of time: its words with everything the
/// reader shows on them, the verses and notes it prints there, and the records its words name, as one gzip-compressed
/// JSON body (<c>Essenthos.Corpus/Corpus/Panes/PaneFormat.cs</c> describes it). The reader asks for one row per text it
/// shows, and the API sends the body as it is stored.
///
/// <para>
/// Language-neutral: a record is named by its slug, and the names a reader sees come from the record-names dictionary of
/// the reader's language. What depends on the pair of texts beside each other (how strongly a verse is linked to the
/// reference pane's) is not here; it is asked for apart.
/// </para>
///
/// <para>
/// A build product (<c>build/products</c>, <c>panes/{text}</c>): made from the served tables once a build has filled
/// them, and made again for a text whose inputs moved.
/// </para>
/// </summary>
[PrimaryKey(nameof(TextId), nameof(CanonicalBook), nameof(CanonicalChapter))]
public class Pane
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    /// <summary>The body, gzip-compressed, which is what the API sends with <c>Content-Encoding: gzip</c>.</summary>
    public required byte[] Body { get; set; }

    /// <summary>The first 16 hex digits of the SHA-256 of the uncompressed body: the response's entity tag.</summary>
    public required string Hash { get; set; }
}
