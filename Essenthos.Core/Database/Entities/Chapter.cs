using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

[Index(nameof(BookId), nameof(Number), IsUnique = true)]
public class Chapter
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int TextId { get; set; }
    public Text? Text { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>This text's own chapter number. The shared address is on the verse's reference.</summary>
    public int Number { get; set; }

    public ICollection<Verse> Verses { get; set; } = [];
}
