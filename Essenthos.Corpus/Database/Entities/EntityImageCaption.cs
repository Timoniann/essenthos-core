using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A picture's caption in a reader's language, where its list gives one.
///
/// <para>
/// <see cref="EntityImage.Caption"/> is the English a list states, and most pictures have nothing
/// more: a photograph of a place needs no sentence to be understood. Some do. A picture on a thing's
/// page is never the thing itself — the ark and the tabernacle are not standing anywhere to be
/// photographed — and what it is instead, and how far anyone may take it, is said only in its
/// caption, which a reader has to be able to read.
/// </para>
/// </summary>
[Index(nameof(EntityImageId), nameof(Language), IsUnique = true)]
public class EntityImageCaption
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityImageId { get; set; }

    public EntityImage? EntityImage { get; set; }

    /// <summary>The three-letter code the texts are tagged with: <c>ukr</c>, <c>deu</c>, <c>spa</c>.</summary>
    public required string Language { get; set; }

    public required string Caption { get; set; }

    public override string ToString() => $"EntityImageCaption({Language} of image {EntityImageId})";
}
