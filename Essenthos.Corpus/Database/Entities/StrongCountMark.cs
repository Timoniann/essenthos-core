using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What a text's Strong counts were last counted from: a digest of everything the count reads and of
/// the rows it left, so a load counts again only the texts where one of them moved.
///
/// <para>
/// Counting every text's phrases takes minutes and almost always finds what is already stored. The
/// digest takes seconds: the text's words and verses, its links to every original and those
/// originals' words, verses and books, the parts of speech the lexical class is read from, and the
/// stored rows themselves, so rows changed or lost by anything else are counted again too.
/// </para>
///
/// <para>
/// No row, or a null digest, means nothing records what the stored counts rest on, and the next
/// load counts that text.
/// </para>
/// </summary>
public class StrongCountMark
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int TextId { get; set; }

    public Text? Text { get; set; }

    /// <summary>The digest behind the text's phrases, reach, methods and reach by book.</summary>
    public Guid? Phrases { get; set; }

    /// <summary>The digest behind the text's numbers by book, where it is an edition some text is counted over.</summary>
    public Guid? Books { get; set; }
}
