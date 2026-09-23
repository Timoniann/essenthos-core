using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A record a dataset wrote for somebody it had already written, folded into the one that stays.
///
/// <para>
/// BibleData files a man once for each list he stands in: the priest of 1 Chronicles 9:11 and the
/// priest of Nehemiah 11:11 are one man in two parallel lists, and the dataset holds two records for
/// him. Every word naming him then orphans one of them, and a reader is shown two men where the text
/// has one. The fold moves everything the second record held onto the first; this row is what is
/// left of the second: its public address, so a link to it still arrives, and what the dataset said
/// about it under its own id, so the merge never erases what the source said.
/// </para>
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
[Index(nameof(EntityId))]
public class MergedRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The address the folded record had, which now arrives at <see cref="Entity"/>.</summary>
    public required string Slug { get; set; }

    /// <summary>The record it was folded into.</summary>
    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public required string Name { get; set; }

    public string? Distinguisher { get; set; }

    /// <summary>The dataset's own id for the folded record: <c>person:Hilkiah_6</c>.</summary>
    public required string RecordSourceId { get; set; }

    /// <summary>The dataset the folded record came from, as its <see cref="Entity.Source"/> said.</summary>
    public required string RecordSource { get; set; }

    /// <summary>What established that the two are one, and how surely.</summary>
    public LinkMethod Method { get; set; }

    public double? Confidence { get; set; }

    /// <summary>Why they are one person, in a sentence a reader can check against the verses.</summary>
    public required string Reason { get; set; }

    /// <summary>Who read it so.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"MergedRecord({Slug} into {EntityId})";
}
