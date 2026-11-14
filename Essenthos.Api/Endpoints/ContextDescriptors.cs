using System.Text.Json.Serialization;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The lines under the names of a chapter's context, cut to what its panel draws.
///
/// <para>
/// The panel draws each line from its claims, one line to a row with no verse after it, so a claim
/// here is its relation and its target and what changes the words: the parent a half-sibling shares,
/// the marriage an in-law runs through, what tells two targets of one name apart. The verse each
/// claim was read from, who read it, how sure it was and the line's own phrasing are the record
/// page's, which keeps the whole descriptor. The target is a slug into one map of records for the
/// whole answer, so a name is said once however many lines point at it.
/// </para>
/// </summary>
internal static class ContextDescriptors
{
    internal sealed record Slimmed(
        IReadOnlyDictionary<string, ContextDescriptorResponse> Lines,
        IDictionary<string, ContextRecordResponse> Records);

    internal static Slimmed Slim(IReadOnlyDictionary<string, EntityDescriptorResponse> described)
    {
        var records = new SortedDictionary<string, ContextRecordResponse>(StringComparer.Ordinal);
        string Point(DescriptorTargetResponse target)
        {
            records.TryAdd(target.Slug, new ContextRecordResponse(target.Kind, target.Name)
            {
                EnglishName = target.EnglishName == target.Name ? null : target.EnglishName,
                Forms = target.Forms,
            });
            return target.Slug;
        }

        var lines = described.ToDictionary(
            line => line.Key,
            line => new ContextDescriptorResponse(
                line.Value.Language,
                [
                    .. line.Value.Claims.Select(claim => new ContextClaimResponse(claim.Relation, Point(claim.Target))
                    {
                        Distinction = claim.Target.Distinction,
                        SharedParent = claim.SharedParent,
                        ThroughSpouse = claim.ThroughSpouse,
                    }),
                ])
            {
                Counted = line.Value.Counted?
                    .Select(count => new ContextCountResponse(count.Relation, count.Text)
                    {
                        Parts = count.Parts?
                            .Select(part => part.Entity is null
                                ? new ContextCountPartResponse(part.Text)
                                : part.Entity.Kind == OccupationLines.TargetKind
                                    ? new ContextCountPartResponse(part.Text) { Occupation = part.Entity.Slug }
                                    : new ContextCountPartResponse(part.Text) { Entity = Point(part.Entity) })
                            .ToList(),
                        At = count.At,
                    })
                    .ToList(),
                Sex = line.Value.Sex,
            },
            StringComparer.Ordinal);
        return new Slimmed(lines, records);
    }
}

/// <param name="Language">The language the line is phrased for, as <see cref="EntityDescriptorResponse.Language"/>.</param>
/// <param name="Claims">What the line is made of, in its order.</param>
internal record ContextDescriptorResponse(string Language, IList<ContextClaimResponse> Claims)
{
    /// <summary>The relations the line says as a count, as <see cref="EntityDescriptorResponse.Counted"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<ContextCountResponse>? Counted { get; init; }

    /// <summary><c>male</c> or <c>female</c> where the corpus states it; what agrees with the subject needs it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sex { get; init; }
}

/// <param name="Relation">What the subject is to the target: <c>son-of</c>.</param>
/// <param name="Target">The target's slug, a key of <see cref="ChapterContextResponse.Records"/>.</param>
internal record ContextClaimResponse(string Relation, string Target)
{
    /// <summary>What tells the target from another the same line names by its name, as <see cref="DescriptorTargetResponse.Distinction"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Distinction { get; init; }

    /// <summary>For a half-sibling, the parent the two share: <c>father</c> or <c>mother</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SharedParent { get; init; }

    /// <summary>For an in-law, the marriage the tie runs through: <c>wife</c> or <c>husband</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThroughSpouse { get; init; }
}

/// <param name="Relation">The relation whose claims the words stand for.</param>
/// <param name="Text">The whole clause in the language rendered.</param>
internal record ContextCountResponse(string Relation, string Text)
{
    /// <summary>The same words as pieces, each name a slug; null where they name nobody.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<ContextCountPartResponse>? Parts { get; init; }

    /// <summary>How many claims come before the words where no claim of the relation stands for them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? At { get; init; }
}

/// <param name="Text">The piece as it is printed.</param>
internal record ContextCountPartResponse(string Text)
{
    /// <summary>The record the piece names, a key of <see cref="ChapterContextResponse.Records"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Entity { get; init; }

    /// <summary>The occupation the piece names, by slug; the piece's text is its word for the subject.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Occupation { get; init; }
}

/// <param name="Kind"><c>person</c>, <c>place</c>, <c>people</c> and the rest: which page the name links to.</param>
/// <param name="Name">The nominative in the language rendered, or the English name where none is held.</param>
internal record ContextRecordResponse(string Kind, string Name)
{
    /// <summary>The English name where it is not <see cref="Name"/>: what a case with no form falls back to.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EnglishName { get; init; }

    /// <summary>The name in every case a pass produced for the language, keyed by case.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Forms { get; init; }
}

/// <summary>
/// One event the sources set in the chapter, as the panel lists it: its name, its verse, and its
/// year in each reckoning that dates it. The description, the notes and the working are the event's
/// own page's.
/// </summary>
internal record ContextEventResponse(
    string Slug,
    string Name,
    VerseRefResponse? Reference,
    IList<ContextEventDateResponse> Dates)
{
    /// <summary>The title in the reader's language, a machine translation; null for an English reader and where none is held.</summary>
    public string? LocalName { get; init; }

    internal static ContextEventResponse Of(EventResponse e) =>
        new(e.Slug, e.Name, e.Reference, [.. e.Dates.Select(d => new ContextEventDateResponse(d.Chronology, d.Year, d.BceYear, d.Era))])
        {
            LocalName = e.LocalName,
        };
}

/// <param name="Chronology">The reckoning, by slug.</param>
/// <param name="Year">Years from the reckoning's own creation, which orders the chapter's events.</param>
/// <param name="BceYear">The year as a reader writes it, <paramref name="Era"/> saying which side of the turn.</param>
internal record ContextEventDateResponse(string Chronology, int? Year, int? BceYear, string Era);
