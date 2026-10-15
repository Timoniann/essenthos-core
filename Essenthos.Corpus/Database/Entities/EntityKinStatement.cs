using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What a verse says about a person's children beyond naming them: how many there were
/// (<em>Gideon had threescore and ten sons</em>, JDG 8:30), that there were more than it names
/// (<em>and he begat sons and daughters</em>, GEN 5:4), or a child's place among them (<em>David the
/// seventh</em>, 1CH 2:15).
///
/// <para>
/// A descriptor clause names somebody; none of these does, which is why they are not clauses. A
/// count of named children is a count of the people the records hold, and a reader asked for the
/// number the text gives: Jair had thirty sons and none of them is named.
/// </para>
/// </summary>
[Index(nameof(EntityId))]
public class EntityKinStatement
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Whom the verse says it of: the parent for a count or for more children, the child for a place
    /// among the children.
    /// </summary>
    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>One of <see cref="KinStatementKinds"/>.</summary>
    [MaxLength(16)]
    public required string Kind { get; set; }

    /// <summary>
    /// Which children a count counts, where one person's verses count two sets of them: Job's seven
    /// sons and three daughters of JOB 1:2 died, and the seven and three of JOB 42:13 are others, so
    /// the two counts add up. Counts with the same family count the same children, and the larger
    /// one stands. Null is the one family most people have.
    /// </summary>
    [MaxLength(64)]
    public string? Family { get; set; }

    public int? Sons { get; set; }

    public int? Daughters { get; set; }

    /// <summary>Children the verse counts without saying sons or daughters.</summary>
    public int? Children { get; set; }

    /// <summary>
    /// Sons' sons, counted only where a verse counts them (JDG 12:14); never added up from a tree,
    /// whose gaps would make the number wrong without saying so.
    /// </summary>
    public int? Grandsons { get; set; }

    /// <summary>The verse says there were sons beyond those it names, and not how many.</summary>
    public bool MoreSons { get; set; }

    /// <summary>The verse says there were daughters beyond those it names, and not how many.</summary>
    public bool MoreDaughters { get; set; }

    /// <summary>The child's place, from 1 for the firstborn, as the verse states it and never from a list's order.</summary>
    public int? Ordinal { get; set; }

    /// <summary>The parent the child's place is counted among the children of.</summary>
    public int? ParentEntityId { get; set; }

    public Entity? Parent { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int CanonicalVerse { get; set; }

    /// <summary>Every verse the statement rests on where one does not hold it, whose first is the address above.</summary>
    public string? Citation { get; set; }

    /// <summary>The text the statement was read in: <c>BHSA</c>, <c>NESTLE1904</c>.</summary>
    [MaxLength(64)]
    public string? Witness { get; set; }

    /// <summary>The words of <see cref="Witness"/> it rests on, as that text prints them.</summary>
    [MaxLength(256)]
    public string? Original { get; set; }

    public LinkMethod Method { get; set; }

    public double? Confidence { get; set; }

    /// <summary>Who read it and when. Never empty.</summary>
    public required string Source { get; set; }

    public string? Note { get; set; }

    public override string ToString() => $"EntityKinStatement({EntityId} {Kind})";
}

/// <summary>The three things <see cref="EntityKinStatement"/> can say.</summary>
public static class KinStatementKinds
{
    /// <summary>How many children, by sex where the verse says.</summary>
    public const string Count = "count";

    /// <summary>More sons or daughters than the verse names, with no number.</summary>
    public const string More = "more";

    /// <summary>A child's place among a parent's children: firstborn, second, seventh.</summary>
    public const string Order = "order";

    public static readonly IReadOnlyList<string> All = [Count, More, Order];
}
