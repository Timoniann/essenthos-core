using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A spelling a text prints for an entity that a search finds it by and nothing else reads.
///
/// <para>
/// <see cref="EntityRendering"/> keeps a spelling only if it opens like the text's commonest one, which
/// the headings and the concordance need and which costs the genuinely different spelling a text also
/// prints: <em>Цофнат-Панеах</em> for Joseph, <em>Хафира</em> for Chephirah. A reader who types one of
/// those is looking for the record, so it stands here, on the rule the Forge's <c>SearchSpellings</c>
/// states. It is never shown as a name of the record except to say why a search found it.
/// </para>
/// </summary>
[Index(nameof(EntityId), nameof(Folded), IsUnique = true)]
public class EntitySearchSpelling
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>The spelling as the texts most often print it, with what the sentence put around it taken off.</summary>
    [Required]
    public required string Form { get; set; }

    /// <summary>The spelling as a search compares it (<see cref="Corpus.NameFolding"/>).</summary>
    [Required]
    public required string Folded { get; set; }

    public override string ToString() => $"EntitySearchSpelling({EntityId}: {Form})";
}
