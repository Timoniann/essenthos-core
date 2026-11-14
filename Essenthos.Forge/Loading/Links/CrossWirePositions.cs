using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Links;

/// <param name="Module">The SWORD module the positions are read from, as its configuration names it.</param>
/// <param name="Slug">The corpus text loaded from it, whose words the links start from.</param>
/// <param name="Credit">What every link drawn from the positions carries, and the dataset declaration claims.</param>
internal sealed record SwordPositions(string Module, string Slug, string Credit);

/// <summary>
/// The Revised Literal Translation's and the King James's New Testaments linked to the Received Text by the positions
/// CrossWire's module states (<see cref="ReceivedTextPositions"/>): each tagged element names the
/// places of the Greek words it renders, and where the witness holds the named word at the place the
/// link is <c>stated-by-source</c>, with no confidence.
///
/// <para>
/// **Both printed editions are witnesses to it.** The positions count the KJV2003 project's own
/// Received Text, which is neither of Robinson's: measured over the whole New Testament, 99.43% of the
/// elements hold their words in Scrivener and 99.40% in Stephanus, and where the two editions differ
/// 582 agree with Scrivener only and 539 with Stephanus only. So each edition is linked where it holds
/// the words, and an element neither holds — 138, most of them the subscriptions Stephanus prints after
/// the epistles, which the corpus does not load as words — links nothing.
/// </para>
///
/// <para>
/// **The Strong-number links stay.** <c>crosswire-strong</c> matched the same words by number; where
/// a position names the words a number link already names, the statement becomes a claim on it and
/// its settled answer, and where it names others both stand, which is the count of places the number
/// match picked another occurrence of the same number.
/// </para>
/// </summary>
internal static class CrossWirePositions
{
    public static readonly SwordPositions RevisedLiteral = new("RLT", SwordTextSource.RevisedLiteral, Sources.RevisedLiteralPositionCredit);

    /// <summary>
    /// CrossWire's King James, whose module states the same positions on another digitisation of the King James than the
    /// corpus loads, so its elements are laid onto bible4u's words first (<see cref="CrossWireKingJames"/>).
    /// </summary>
    public static readonly SwordPositions KingJames = new(CrossWireKingJames.Module, Sources.KingJamesSlug, Sources.KingJamesPositionCredit);

    public static readonly IReadOnlyList<SwordPositions> All = [RevisedLiteral, KingJames];

    /// <summary>The Received Text editions the positions are laid against.</summary>
    public static readonly IReadOnlyList<string> Witnesses = [Sources.ScrivenerSlug, Sources.StephanusSlug];
}
