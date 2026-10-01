using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// Who says a link is true and, for a link a person made, why: the source and the note a
/// <see cref="Link"/> or a <see cref="LinkClaim"/> rests on, kept once and pointed at by id.
///
/// <para>
/// Twenty-five million claims and twenty-one million links are said by a few tens of thousands of
/// distinct sources, and carrying the text on every row made the claims ten gigabytes, most of it
/// the same few strings over and over, and their unique index four of those because it carried the
/// source too. The pair is what is distinct, not either half: one source writes different notes,
/// and one note is written by several sources.
/// </para>
/// </summary>
public class Provenance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The file, the algorithm and its version, or the person. Never empty.</summary>
    public required string Source { get; set; }

    /// <summary>For a manual link, why; for an inferred one, what the method weighed.</summary>
    public string? Note { get; set; }

    public override string ToString() => Note is null ? Source : $"{Source} ({Note})";
}
