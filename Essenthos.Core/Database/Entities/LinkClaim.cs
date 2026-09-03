using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One method saying that a link is true, and how sure it is.
///
/// A link is a claim about words — *these words render those*. Until this table it also carried the
/// only answer to *who says so*, one method and one source, and the first method to speak won. So a
/// word pair the Ukrainian interlinear states and the aligner independently arrived at was stored
/// as **two links**, competing, each looking like the other's rival; and a word pair only the
/// aligner ever guessed at was stored identically to one four methods agree on. AUD-0111 could
/// measure the corpus at 92.1% correct and could not say which 8% was wrong, and this is why.
///
/// <para>
/// **Agreement is the cheapest evidence there is and it was being thrown away.** Two methods that
/// share no reasoning landing on the same pair of words is worth more than either of them alone,
/// and it costs nothing to record because both already ran. What was missing was somewhere to put
/// the second one.
/// </para>
///
/// <para>
/// The link keeps a settled answer of its own — <see cref="Link.Method"/>, <see cref="Link.Source"/>
/// and <see cref="Link.Confidence"/> hold the strongest claim, so every reader that existed before
/// this table still works and no query pays for a join it does not need. The claims are the
/// reasoning; the link is the conclusion.
/// </para>
/// </summary>
[Index(nameof(LinkId))]
[Index(nameof(LinkId), nameof(Method), nameof(Source), IsUnique = true)]
public class LinkClaim
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long LinkId { get; set; }

    public Link? Link { get; set; }

    public LinkMethod Method { get; set; }

    /// <summary>
    /// How sure this method is, and null exactly when a source stated it or a person set it. The
    /// database enforces both directions, the same way it does for the link itself.
    /// </summary>
    public double? Confidence { get; set; }

    public required string Source { get; set; }

    public string? Note { get; set; }

    public override string ToString() => $"LinkClaim({Method} on link {LinkId})";
}

/// <summary>
/// Which claim a link shows as its own, where several speak.
///
/// Testimony outranks inference, and a person outranks both: somebody who corrected a link by hand
/// did it knowing what the sources said. Below that the order is how much a method knows before it
/// starts — a stated number, then a lexicon, then a model that learned the pair from the text.
/// </summary>
public static class ClaimStanding
{
    public static int Of(LinkMethod method) => method switch
    {
        LinkMethod.Manual => 5,
        LinkMethod.StatedBySource => 4,
        LinkMethod.StrongNumber => 3,
        LinkMethod.Lexical => 2,
        LinkMethod.Aligner => 1,
        _ => 0,
    };
}
