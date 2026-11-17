using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// How many times a Strong number stands in one book of an edition of the original: the row of the
/// Strong page's map of where a word is used. Counted by the load beside <see cref="StrongReach"/>,
/// for every edition some text is counted over, because counting it as a page asks reads every word
/// of the edition.
/// </summary>
[PrimaryKey(nameof(WitnessId), nameof(StrongNumber), nameof(Book))]
public class StrongBook
{
    public int WitnessId { get; set; }

    public Text? Witness { get; set; }

    public required string StrongNumber { get; set; }

    /// <summary>The book's canonical ordinal, 1 to 66 and on, shared by every text.</summary>
    public int Book { get; set; }

    public int Occurrences { get; set; }
}

/// <summary>
/// How many of a number's words in one book of the edition a text's links render. Only books where
/// the text renders some: a book the text holds with no row here is a book it leaves the number
/// unrendered in, and a book the text does not hold is not held — which the page says, never zero.
/// </summary>
[PrimaryKey(nameof(TextId), nameof(StrongNumber), nameof(Book))]
public class StrongBookReach
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    public required string StrongNumber { get; set; }

    public int Book { get; set; }

    public int Reached { get; set; }
}

/// <summary>
/// The words of one book of an edition that carry any Strong number, the ETCBC prefix morphemes (H9…) left out: the
/// denominator the Strong map's rate per ten thousand words is taken over. Summed from <see cref="StrongBook"/> as the
/// build writes it, so the map reads one row a book instead of every number's row of the book.
/// </summary>
[PrimaryKey(nameof(WitnessId), nameof(Book))]
public class StrongBookWords
{
    public int WitnessId { get; set; }

    public Text? Witness { get; set; }

    public int Book { get; set; }

    public int Words { get; set; }
}

/// <summary>
/// A book in which some link of the text renders some numbered word of the edition it is counted over: the books of
/// <see cref="StrongBookReach"/> for any number, written beside it by the build. A book the text holds with no row here is
/// one it is not linked in at all.
/// </summary>
[PrimaryKey(nameof(TextId), nameof(Book))]
public class TextBookLinked
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    public int Book { get; set; }
}
