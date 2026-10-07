using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// How many times a Strong number stands in the edition of the original a text is counted over, and
/// at how many of those places some link of the text renders it: the head of the entry page's
/// renderings.
///
/// <para>
/// Kept for the same reason as <see cref="StrongRendering"/>. Counting it as it is asked reads every
/// link of every occurrence of the number in every text linked to the edition, and the edition is
/// linked to dozens: אֱלֹהִים stands 2,598 times in BHSA and 88,369 link words name those places, of
/// which 2,566 belong to the King James. Read cold that ran past the time a request is allowed.
/// </para>
/// </summary>
[PrimaryKey(nameof(TextId), nameof(StrongNumber))]
public class StrongReach
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    public required string StrongNumber { get; set; }

    /// <summary>The edition the number was counted in, which a reader citing the count names.</summary>
    public int WitnessId { get; set; }

    public Text? Witness { get; set; }

    /// <summary>The words of the edition carrying the number.</summary>
    public int Occurrences { get; set; }

    /// <summary>Those of them a link of the text renders.</summary>
    public int Reached { get; set; }

    /// <summary>Every distinct phrase the text writes for the number, as printed.</summary>
    public int Phrases { get; set; }

    /// <summary>
    /// The distinct renderings once the words the text's language spends on grammar — pronouns,
    /// articles, prepositions, auxiliaries — are set aside and the rest reduced to their stems:
    /// <em>god</em>, <em>thy god</em> and <em>of the gods</em> are one. Null where the language has
    /// no such list, which is not a count of zero.
    /// </summary>
    public int? Renderings { get; set; }

    /// <summary>
    /// The links whose phrase carries a word of its own, which <see cref="Renderings"/> is counted
    /// over: a link writing only <em>him</em> for the object marker renders grammar and is outside it.
    /// </summary>
    public int RenderingLinks { get; set; }

    /// <summary>
    /// Whether the editions that state a part of speech call the number's words mostly nouns, names,
    /// verbs, adjectives or numerals - a word of content - rather than a preposition, a particle, a
    /// pronoun or the object marker, whose renderings vary with whatever stands beside them. Null
    /// where no edition states one.
    /// </summary>
    public bool? Lexical { get; set; }

    public ICollection<StrongReachMethod> Methods { get; set; } = [];

    public override string ToString() => $"StrongReach({StrongNumber} in {TextId}: {Reached} of {Occurrences})";
}

/// <summary>How many of the links reaching a number in a text were made by one method.</summary>
[PrimaryKey(nameof(TextId), nameof(StrongNumber), nameof(Method))]
public class StrongReachMethod
{
    public int TextId { get; set; }

    public required string StrongNumber { get; set; }

    public StrongReach? Reach { get; set; }

    public LinkMethod Method { get; set; }

    public int Links { get; set; }
}
