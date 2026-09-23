using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One picture of a person or a place, and whose it is.
///
/// <para>
/// Two kinds, and a reader is always told which. A <see cref="Kind"/> of <c>public</c> is somebody
/// else's work under an open licence — a photograph of the site as it stands today, or an old
/// engraving of the person — and carries its author, where it was published and its licence, because
/// a picture with no credit under it reads as ours. <c>generated</c> is ours: the one identity
/// portrait a person may be given so a reader recognises them at a glance, and it is marked as made
/// here rather than passed off as a likeness anybody drew from life.
/// </para>
///
/// <para>
/// The bytes are not in the corpus. <see cref="File"/> names a file under the images folder by a
/// path that does not change across rebuilds — the gazetteer's own file name, a Commons file name,
/// a slug — which is what the API serves it by. <see cref="Digest"/> is the file's content hash, so a
/// picture that is replaced under the same name reaches a reader through a new address rather than a
/// cached old one.
/// </para>
/// </summary>
[Index(nameof(EntityId), nameof(File), IsUnique = true)]
public class EntityImage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary><c>public</c> for an openly licensed work of somebody else's, <c>generated</c> for ours.</summary>
    public required string Kind { get; set; }

    /// <summary><c>primary</c> for the one picture a page leads with, <c>gallery</c> for the rest.</summary>
    public required string Role { get; set; }

    /// <summary>Where it stands among the entity's pictures of its role, from zero.</summary>
    public int Ordinal { get; set; }

    /// <summary>The path under the images folder, with forward slashes: <c>openbible/m9f98b7.ie61056.jpg</c>.</summary>
    public required string File { get; set; }

    /// <summary>The first twelve hex digits of the file's SHA-256.</summary>
    public required string Digest { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>What the picture shows, in a sentence that serves as its alternative text.</summary>
    public string? Caption { get; set; }

    /// <summary>Who made it, as its source asks to be credited.</summary>
    public required string Credit { get; set; }

    /// <summary>The page the picture was published on, which is where a reader checks the credit.</summary>
    public string? CreditUrl { get; set; }

    /// <summary>The licence's short name: <c>CC BY-SA 4.0</c>, <c>Public domain</c>.</summary>
    public required string Licence { get; set; }

    public string? LicenceUrl { get; set; }

    /// <summary>The collection it was taken from, in prose, as every other row's source is.</summary>
    public required string Source { get; set; }

    /// <summary>
    /// Where the picture's subject is, as fractions of its width and height from the top left, for a
    /// crop that must not cut it off. Null for the middle.
    /// </summary>
    public double? FocusX { get; set; }

    public double? FocusY { get; set; }

    public override string ToString() => $"EntityImage({Kind} {Role} {File} of entity {EntityId})";
}
