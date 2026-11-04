using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// The id a dataset gives one of our records, and how the two stand: the dataset's record is ours (<c>same-as</c>), or it was
/// a record of its own that was folded into ours (<c>folded</c>). A dataset's id lives here and nowhere else; a dataset never
/// makes a record.
/// </summary>
[Index(nameof(EntityId))]
[Index(nameof(Dataset), nameof(ExternalId))]
public class RecordExternalId
{
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>The dataset's slug: <c>bibledata</c>, <c>openbible</c>.</summary>
    public required string Dataset { get; set; }

    /// <summary>The dataset's own id for the record: <c>person:Moses_1</c>, <c>a5f43dd</c>.</summary>
    public required string ExternalId { get; set; }

    /// <summary>One of <see cref="RecordExternalRelations"/>.</summary>
    public required string Relation { get; set; }

    public string? Note { get; set; }

    public override string ToString() => $"RecordExternalId({Dataset} {ExternalId} {Relation} entity {EntityId})";
}

/// <summary>How a dataset's record stands to ours, as <c>record_external_id.relation</c> holds it.</summary>
public static class RecordExternalRelations
{
    /// <summary>The dataset's record is this one.</summary>
    public const string SameAs = "same-as";

    /// <summary>The dataset's record was a record of its own, folded into this one.</summary>
    public const string Folded = "folded";
}
