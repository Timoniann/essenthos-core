using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// The notes under a record of this corpus's own, in a reader's language: the paragraphs an object,
/// an appointed time, a narrative's being, a title, a people or a record of ours carries in English.
///
/// <para>
/// A dataset's notes are its words and stay English, for the reason its line does
/// (<see cref="EntityDistinguisher"/>): a translation would put ours in its mouth. Notes this corpus
/// wrote are ours in any language. <see cref="EnglishSha256"/> is the digest of the English the
/// translation renders, so notes rewritten since — the owner edits the records in his console — keep
/// their translation out of sight until it is rendered again, and the reader is shown the English,
/// which is what the record now says.
/// </para>
/// </summary>
[Index(nameof(EntityId), nameof(Language), IsUnique = true)]
public class EntityNoteTranslation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>The three-letter code the texts are tagged with: <c>ukr</c>, <c>deu</c>, <c>spa</c>.</summary>
    [MaxLength(8)]
    public required string Language { get; set; }

    public required string Text { get; set; }

    /// <summary>SHA-256 of the English notes this renders, UTF-8, lower-case hex.</summary>
    [MaxLength(64)]
    public required string EnglishSha256 { get; set; }

    /// <summary>Who rendered it, with what effort and when. Never empty.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"EntityNoteTranslation({EntityId} {Language})";
}
