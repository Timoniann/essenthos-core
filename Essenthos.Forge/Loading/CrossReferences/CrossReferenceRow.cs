namespace Essenthos.Core.Loading.CrossReferences;

/// <summary>A verse in the shared frame: canonical book, chapter and verse.</summary>
internal readonly record struct VerseAddress(int Book, int Chapter, int Verse) : IComparable<VerseAddress>
{
    public int CompareTo(VerseAddress other) =>
        (Book, Chapter, Verse).CompareTo((other.Book, other.Chapter, other.Verse));

    public override string ToString() => $"{Book} {Chapter}:{Verse}";
}

/// <summary>
/// One reference as a set states it, before it is written: from one verse to a verse or a run of
/// them. <paramref name="Rank"/> is left to the loader where the set ranks by something else.
/// </summary>
/// <param name="End">Where the far end stops, when it is more than one verse.</param>
internal sealed record CrossReferenceRow(
    VerseAddress From,
    VerseAddress To,
    VerseAddress? End,
    int Rank,
    int? Votes = null,
    string? Note = null,
    int? Passage = null,
    string? MatchedIn = null,
    string? Matched = null);
