using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A title or a gloss from the names block of a record, in a reader's language: <em>King of
/// Israel</em>, <em>son [of] Jesse</em>, <em>beloved one</em>.
///
/// <para>
/// Keyed by the English it renders rather than by a name row, because the same title and the same
/// gloss stand under many records and a name row is renumbered by every load. A label or a meaning
/// whose English has changed simply finds no row, and the reader is shown the English, which is
/// true, rather than a rendering of something the source no longer says.
/// </para>
/// </summary>
[Index(nameof(Language), nameof(English), IsUnique = true)]
public class EntityNameTranslation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The three-letter code the texts are tagged with: <c>ukr</c>, <c>deu</c>, <c>spa</c>.</summary>
    public required string Language { get; set; }

    /// <summary>The label or meaning as <see cref="EntityName"/> holds it.</summary>
    public required string English { get; set; }

    public required string Text { get; set; }

    /// <summary>Who rendered it and when. Never empty.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"EntityNameTranslation({Language} {English})";
}
