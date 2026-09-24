using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A word the aligner had linked, taken out of that link so that an absence EVIDENTIA holds more
/// surely could be written: a word shown as supplied and as rendered at once is a contradiction,
/// and where the only thing saying it is rendered is a statistical guess, the guess gives way.
///
/// <para>
/// **Everything the link said is kept**, so the withdrawal reads back as what it was and can be
/// undone: its relation, its settled answer, both sides' words as they were, and each claim that
/// stood on it. Only a link every claim of which is the aligner's is ever withdrawn from, so the
/// claims need no method of their own. <see cref="LinkId"/> is the link where it still stands with
/// its other words, and null where the word was the only one on its side and the link went with it.
/// </para>
/// </summary>
[Index(nameof(ReviewId))]
[Index(nameof(WordId))]
[Index(nameof(LinkId))]
public class EvidentiaWithdrawal
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>The verdict whose absence the word was withdrawn for; its decision names the run and the rule.</summary>
    public long ReviewId { get; set; }

    public EvidentiaReview? Review { get; set; }

    /// <summary>The word taken out of the link.</summary>
    public long WordId { get; set; }

    public Word? Word { get; set; }

    public long? LinkId { get; set; }

    public Link? Link { get; set; }

    public int FromTextId { get; set; }

    public int ToTextId { get; set; }

    public LinkRelation Relation { get; set; }

    public LinkMethod Method { get; set; }

    public double? Confidence { get; set; }

    public required string Source { get; set; }

    public string? Note { get; set; }

    public required long[] FromWordIds { get; set; }

    public required long[] ToWordIds { get; set; }

    public required string[] ClaimSources { get; set; }

    public required double?[] ClaimConfidences { get; set; }

    public required string?[] ClaimNotes { get; set; }

    public DateTimeOffset WithdrawnAt { get; set; }

    public override string ToString() => $"EvidentiaWithdrawal(word {WordId} for review {ReviewId})";
}
