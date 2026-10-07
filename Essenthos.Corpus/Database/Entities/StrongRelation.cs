using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What one Strong entry's etymology says about another: H1006 is <em>the same as</em> H1004,
/// H1007 comes <em>from</em> H1004 and H205, G2076 is a form of G1510.
///
/// <para>
/// Keyed on numbers, like <see cref="StrongGentilic"/>, because the claim is about two words of the
/// language and holds whether or not any text uses either. The kind is read from the words before
/// the reference and the clause is kept beside it, so a reader shown the relation can be shown the
/// sentence it rests on, and a wrong reading can be traced to the words that produced it.
/// </para>
///
/// <para>
/// Each row names its source. Strong's own etymology and a compiler's root list are two readings
/// of the same entries, and a root a compiler gives is never presented as one Strong stated.
/// </para>
/// </summary>
[Index(nameof(Source), nameof(FromNumber), nameof(Position), IsUnique = true)]
[Index(nameof(FromNumber))]
[Index(nameof(ToNumber))]
public class StrongRelation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The entry whose etymology states the relation.</summary>
    public required string FromNumber { get; set; }

    /// <summary>The entry it names. Null only where the entry is said to come from nothing — a primitive root.</summary>
    public string? ToNumber { get; set; }

    /// <summary>
    /// <c>same-as</c>, <c>from</c>, <c>same-root-as</c>, <c>form-of</c>, <c>variant</c>,
    /// <c>contracted-from</c>, <c>corresponds-to</c>, <c>loan-from</c>, <c>patronymic</c>,
    /// <c>patrial</c>, <c>patronymic-or-patrial</c>, <c>compare</c>, <c>primitive</c>, a compiler's
    /// <c>root</c>, or <c>unclassified</c> where the words fit none of them.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The source qualified it — <em>probably</em>, <em>perhaps</em>, <em>apparently</em>.</summary>
    public bool Hedged { get; set; }

    /// <summary>Its order among the entry's references in this source, from 1; 0 for a primitive.</summary>
    public int Position { get; set; }

    /// <summary>The clause the relation was read from, as the source writes it.</summary>
    public required string Statement { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"StrongRelation({FromNumber} {Kind} {ToNumber})";
}
