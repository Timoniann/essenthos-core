using Essenthos.Core.Corpus;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// How much of the widening a caller will accept. The endpoint tries a whole word, then the word
/// as it is printed, then part of one, and reports which happened — but a reader who knows the
/// word wants nothing but that word, and a reader who does not wants the widening asked for rather
/// than arrived at.
/// </summary>
internal enum SearchWidening
{
    /// <summary>Whole word, then printed word, then part of one. What a caller who says nothing gets.</summary>
    AsFarAsNeeded,

    /// <summary>Whole word and printed word only: <em>beginn</em> answers nothing rather than <em>beginning</em>.</summary>
    WholeWordOnly,

    /// <summary>Part of a word straight away, so a stem answers without having to fail twice first.</summary>
    PartOfAWord,
}

/// <summary>
/// Which books a search runs over, and how far it may widen a term.
///
/// A search of the whole corpus and a search of one book were the only two things that could be
/// asked. A reader looking for a word in the gospels had to ask four times and add up the totals
/// themselves, which cannot be done honestly — the paging is per request.
///
/// <para>
/// The books come from three parameters that narrow each other: a testament, a range, and a list.
/// Each is a filter on the canonical ordinal, so asking for two of them asks for both, and a
/// combination that can hold no book is refused rather than silently answering nothing.
/// </para>
/// </summary>
/// <param name="From">The first canonical ordinal the search may reach, or null for no lower bound.</param>
/// <param name="To">The last, or null for no upper bound.</param>
/// <param name="Books">The books named one by one, or null where none were.</param>
internal sealed record SearchScope(int? From, int? To, IReadOnlyList<int>? Books)
{
    /// <summary>
    /// The deuterocanon is books 67 and up and belongs to neither testament, so it is reached by
    /// naming its books rather than by asking for a testament.
    /// </summary>
    private const int FirstNewTestamentBook = BookReferences.OldTestamentBookCount + 1;

    /// <summary>
    /// The scope the query parameters ask for, or a hint saying what to pass instead. A hint names
    /// the value that was not understood and what the accepted ones are, because a search that
    /// answers nothing and does not say why reads as an empty corpus.
    /// </summary>
    public static (SearchScope? Scope, string? Hint) Resolve(
        string? books,
        string? fromBook,
        string? toBook,
        string? testament)
    {
        int? from = null;
        int? to = null;

        if (testament is { Length: > 0 })
        {
            switch (testament.Trim().ToLowerInvariant())
            {
                case BookReferences.OldTestament:
                    (from, to) = (1, BookReferences.OldTestamentBookCount);
                    break;
                case BookReferences.NewTestament:
                    (from, to) = (FirstNewTestamentBook, BookReferences.CanonBookCount);
                    break;
                default:
                    return (null, $"'{testament}' is not a testament. Expected " +
                                  $"'{BookReferences.OldTestament}' or '{BookReferences.NewTestament}'. " +
                                  "The deuterocanon is in neither; name its books instead.");
            }
        }

        if (fromBook is { Length: > 0 })
        {
            if (BookReferences.ResolveOrdinal(fromBook) is not { } first)
            {
                return (null, BookReferences.FormatHint(fromBook));
            }

            from = Math.Max(from ?? first, first);
        }

        if (toBook is { Length: > 0 })
        {
            if (BookReferences.ResolveOrdinal(toBook) is not { } last)
            {
                return (null, BookReferences.FormatHint(toBook));
            }

            to = Math.Min(to ?? last, last);
        }

        List<int>? named = null;
        if (books is { Length: > 0 })
        {
            named = [];
            foreach (var book in books.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (BookReferences.ResolveOrdinal(book) is not { } ordinal)
                {
                    return (null, BookReferences.FormatHint(book));
                }

                if (!named.Contains(ordinal))
                {
                    named.Add(ordinal);
                }
            }

            named = [.. named.Where(ordinal => ordinal >= (from ?? 1) && ordinal <= (to ?? BookReferences.LastOrdinal))];
            if (named.Count == 0)
            {
                return (null, "No book is in all of what was asked for. A testament, a range and a " +
                              "list of books narrow each other, so a book outside the range or the " +
                              "testament leaves nothing to search.");
            }

            // The list is already inside the range, and a range beside it only makes the query
            // longer to plan.
            return (new SearchScope(null, null, named), null);
        }

        return from > to
            ? (null, "The range runs backwards. Pass fromBook before toBook in canonical order.")
            : (new SearchScope(from, to, null), null);
    }

    /// <summary>
    /// How far a term may widen, or a hint. The default is the ladder, which is what the endpoint
    /// did before anybody could ask for anything else.
    /// </summary>
    public static (SearchWidening Widening, string? Hint) ResolveWidening(string? match) =>
        match is { Length: > 0 } ? match.Trim().ToLowerInvariant() switch
        {
            "whole" => (SearchWidening.WholeWordOnly, null),
            "substring" => (SearchWidening.PartOfAWord, null),
            _ => (SearchWidening.AsFarAsNeeded,
                $"'{match}' is not a way of matching. Expected 'whole' for whole words only or " +
                "'substring' for part of a word; leave it out to widen only where a whole word " +
                "answers nothing."),
        } : (SearchWidening.AsFarAsNeeded, null);
}
