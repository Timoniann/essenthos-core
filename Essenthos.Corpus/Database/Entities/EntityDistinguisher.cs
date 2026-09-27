using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// The line under a record's name in a reader's language, for a record whose English line this
/// corpus wrote itself — an object, an appointed time, a title, a tribe.
///
/// <para>
/// A dataset's own line is its words and stays English: it is shown as the source's, and a
/// translation of it would put ours in its mouth. A line this corpus wrote is ours in any language,
/// so it is rendered into each one a reader can ask for, and <see cref="English"/> keeps the line
/// it renders, so that a record whose English has since been rewritten is not answered with a
/// translation of what it used to say.
/// </para>
/// </summary>
[Index(nameof(EntityId), nameof(Language), IsUnique = true)]
public class EntityDistinguisher
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>The three-letter code the texts are tagged with: <c>ukr</c>, <c>deu</c>, <c>spa</c>.</summary>
    public required string Language { get; set; }

    public required string Text { get; set; }

    /// <summary>The English line this renders, as <see cref="Entity.Distinguisher"/> held it.</summary>
    public required string English { get; set; }

    /// <summary>Who rendered it and when. Never empty.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"EntityDistinguisher({EntityId} {Language})";
}
