using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What one run decided about one source word: the target it proposed and why, or that it placed
/// the word nowhere and why not.
///
/// <para>
/// **The evidence is columns, one per signal kind, rather than rows or JSON.** A whole-Bible run is
/// hundreds of thousands of decisions and each carries three to six signals; a row per signal
/// multiplies the table by five and a JSON document repeats every key name on every row. A null
/// column costs one bit, so the sparse vector is nearly free, and a question like "every proposal a
/// learned rendering carried without grammatical agreement" is a plain <c>WHERE</c>. The name of
/// the evidence source behind each kind is constant within a run and lives on the run.
/// </para>
///
/// <para>
/// **An absence is a decision about one word too**, with only that word's side filled: a word of
/// the translation the original does not have (<see cref="LinkRelation.Expands"/>, a source word
/// and no target) or a word of the original the translation does not render
/// (<see cref="LinkRelation.Omits"/>, a target word and no source). It carries no candidate edges;
/// its evidence is the rule that made it, in <see cref="Kind"/> and <see cref="Rationale"/>, and the
/// placed pair it rests on, in <see cref="AnchorSourceWordId"/> and <see cref="AnchorTargetWordId"/>.
/// </para>
///
/// <para>
/// For an abstention the signal columns and <see cref="Score"/> are the best candidate's, so the row
/// still says what evidence the policy declined.
/// </para>
///
/// <para>
/// **Alternatives are parallel arrays, capped at <see cref="MaximumAlternatives"/>.** They are
/// read beside the decision and never joined to on their own, so a table of their own would pay a
/// row header, a key and an index for each without buying a query.
/// </para>
/// </summary>
[Index(nameof(RunId), nameof(CanonicalBook), nameof(CanonicalChapter), nameof(CanonicalVerse))]
[Index(nameof(SourceWordId))]
[Index(nameof(TargetWordId))]
public class EvidentiaDecision
{
    public const int MaximumAlternatives = 3;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int RunId { get; set; }

    public EvidentiaRun? Run { get; set; }

    /// <summary>Null exactly for an <see cref="LinkRelation.Omits"/> absence, which is about a word of the target alone.</summary>
    public long? SourceWordId { get; set; }

    public Word? SourceWord { get; set; }

    /// <summary>The canonical address the run read the word at, so a verse ranks without a join.</summary>
    public short CanonicalBook { get; set; }

    public short CanonicalChapter { get; set; }

    public short CanonicalVerse { get; set; }

    /// <summary>Whether a language pack called the word a content word, which is what coverage counts.</summary>
    public bool Content { get; set; }

    /// <summary>Null where the run placed the word nowhere, or where it says the word has no counterpart.</summary>
    public long? TargetWordId { get; set; }

    public Word? TargetWord { get; set; }

    /// <summary>The resolver's own name for the proposal, such as <c>global-review-known-rendering</c>.</summary>
    public string? Kind { get; set; }

    /// <summary><c>safe</c> where the safe tier chose the same pair, otherwise the tier the resolver named.</summary>
    public string? Tier { get; set; }

    public string? Rationale { get; set; }

    public float? Confidence { get; set; }

    /// <summary>The candidate edge's summed score, for a proposal; the best candidate's, for an abstention.</summary>
    public float? Score { get; set; }

    /// <summary>
    /// How far the chosen edge's summed evidence stands above the strongest edge it was chosen over,
    /// before either is capped at 1; negative where the policy chose against the larger sum.
    /// </summary>
    public float? Margin { get; set; }

    /// <summary>How many candidate edges the word had at all.</summary>
    public short Candidates { get; set; }

    public EvidentiaAbstention? Abstention { get; set; }

    /// <summary>
    /// Set where the decision is that a word has no counterpart, as the link it would become says
    /// it: <see cref="LinkRelation.Expands"/> for a word the translation supplies,
    /// <see cref="LinkRelation.Omits"/> for a word of the original it does not render.
    /// </summary>
    public LinkRelation? Absence { get; set; }

    /// <summary>
    /// The source word of the placed pair an absence or an attached word rests on: the head of a
    /// supplied article, the word a prefix is written onto, or the word an attached word goes with.
    /// </summary>
    public long? AnchorSourceWordId { get; set; }

    public long? AnchorTargetWordId { get; set; }

    public float? ExactAddress { get; set; }

    public float? NeighbouringAddress { get; set; }

    public float? MatchingForm { get; set; }

    public float? SharedStrong { get; set; }

    public float? DictionarySense { get; set; }

    public float? TargetGloss { get; set; }

    public float? KnownRendering { get; set; }

    public float? Morphology { get; set; }

    public float? Syntax { get; set; }

    public float? StatisticalAligner { get; set; }

    /// <summary>How often the learned index saw the source form, whichever sense it landed on.</summary>
    public int? RenderingObservations { get; set; }

    public float? RenderingShare { get; set; }

    public float? RenderingNextShare { get; set; }

    /// <summary>Which key of the learned index answered: <c>surface</c>, <c>lemma</c> or <c>normalised</c>.</summary>
    public string? RenderingForm { get; set; }

    /// <summary>The strongest competing targets, best first.</summary>
    public long[]? AlternativeWordIds { get; set; }

    public float[]? AlternativeScores { get; set; }

    /// <summary>Verses between the source word and each alternative; <c>short.MaxValue</c> across a chapter.</summary>
    public short[]? AlternativeDistances { get; set; }

    /// <summary>Whether another proposal of the run took each alternative's target word.</summary>
    public bool[]? AlternativeTaken { get; set; }

    public EvidentiaReview? Review { get; set; }

    public override string ToString() =>
        $"EvidentiaDecision({SourceWordId?.ToString() ?? "nothing"} to {TargetWordId?.ToString() ?? "nothing"}, {Kind ?? Abstention?.ToString()})";
}
