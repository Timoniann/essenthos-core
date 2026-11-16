namespace Essenthos.Core.Context;

/// <summary>How a chapter was placed in its periods, as <c>ChapterContextResponse.PeriodsFrom</c> spells it.</summary>
internal static class ChapterPeriods
{
    public const string FromItsEvents = "events";

    public const string FromTheEventsAround = "neighbours";
}

/// <param name="StartYear">Its first year from creation in the base reckoning, which placed the chapter in it.</param>
/// <param name="Years">
/// Its span in each reckoning that dates both its bounding events, keyed by the reckoning's slug, as
/// the timeline carries it; under the empty key for a period with no anchors, whose years belong to
/// no reckoning.
/// </param>
internal record ContextPeriodResponse(
    string Slug,
    string Name,
    string? Kind,
    int? StartYear,
    int? EndYear,
    Dictionary<string, int[]> Years)
{
    /// <summary>The name in the reader's language, said when the context is asked for; null for an English reader and where none is held.</summary>
    public string? LocalName { get; init; }
}

/// <param name="Number">The lexicon entry that is the unit: <c>H520</c> for the cubit.</param>
/// <param name="Verses">The verses of the chapter where a word carries it.</param>
internal record ContextMeasureResponse(string Number, IList<int> Verses);

internal record ContextCommandmentResponse(string Kind, int Number, string Title, IList<int> Verses);

/// <param name="Heading">The line the verses are filed under — <em>Makes the golden calf</em>; null where the topic cites them alone.</param>
/// <param name="Verses">The verses of this chapter the line cites.</param>
/// <param name="WholeChapter">Whether the line cites the whole chapter rather than verses of it.</param>
internal record ContextTopicEntryResponse(string? Heading, IList<int> Verses, bool WholeChapter);

/// <param name="Chronology">The reckoning, by slug.</param>
/// <param name="Year">Years from the reckoning's own creation, which orders the chapter's events.</param>
/// <param name="BceYear">The year as a reader writes it, <paramref name="Era"/> saying which side of the turn.</param>
internal record ContextEventDateResponse(string Chronology, int? Year, int? BceYear, string Era);
