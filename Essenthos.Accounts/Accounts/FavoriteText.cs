namespace Essenthos.Core.Accounts;

/// <summary>
/// A text a reader keeps at hand: listed first wherever texts are chosen, in an order of their own,
/// so the few they move between are one press away rather than a search through every language.
///
/// The text is named by its canonical slug — <c>KJV</c> — as a bookmark names one, never by a corpus
/// row id, and an account has it once.
/// </summary>
public class FavoriteText : IRevised
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>The text's canonical slug.</summary>
    public required string Text { get; set; }

    /// <summary>Where it stands in the reader's own order of the list.</summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Revision { get; set; }

    public override string ToString() => $"FavoriteText({Id}, {Text}, {Position})";
}
