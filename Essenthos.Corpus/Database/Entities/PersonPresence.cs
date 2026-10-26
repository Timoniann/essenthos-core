using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>A person's bodily presence or burial, with the complete attributed reading retained as evidence.</summary>
[Index(nameof(PersonId), nameof(PlaceId), nameof(Category), IsUnique = true)]
public class PersonPresence
{
    [Key]
    public int Id { get; set; }

    public int PersonId { get; set; }
    public Entity? Person { get; set; }
    public int PlaceId { get; set; }
    public Entity? Place { get; set; }

    [MaxLength(16)]
    public required string Category { get; set; }

    public required int[] Addresses { get; set; }
    public required string Source { get; set; }

    [Column(TypeName = "jsonb")]
    public required string Evidence { get; set; }
}
