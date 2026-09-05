using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A name for a people, and whom or where Strong's dictionary says they are named after. The
/// Moabites are H4125 and Moab is H4124, and Strong wrote the tie between them himself.
///
/// <para>
/// It is keyed on Strong numbers rather than on two entities, and that is the finding rather than a
/// shortcut. <see cref="EntityRelationship"/> is the right table for <em>this person is that
/// person's son</em> and it cannot hold this, because the encyclopedia has no people: every entity
/// is a person or a place, so <em>the Moabites</em> has nothing to be the near end of. Worse, the
/// gentilic number does sometimes reach an entity, and where it does it reaches the wrong one — 22
/// of these numbers match a name in the encyclopedia and most of those matches are a man described
/// by his people rather than the people. H6430 is the Philistines and the only entity carrying it
/// is Goliath, whose name in the dataset is <em>The Philistine</em>. An edge built on that join
/// would read <em>Goliath is a descendant of Philistia</em>, and a reader could not tell it from
/// scholarship.
/// </para>
///
/// <para>
/// So the near end stays a lexeme, which is what Strong is talking about, and the far end reaches
/// the encyclopedia when it can. That is enough for the thing this exists for: a reader hovering
/// <em>моавітяни</em> reaches the Hebrew word behind it, the word carries H4125, and this says the
/// people are named after Moab and hands over the page for the man.
/// </para>
/// </summary>
[Index(nameof(StrongNumber), IsUnique = true)]
[Index(nameof(OriginNumber))]
[Index(nameof(OriginEntityId))]
public class StrongGentilic
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The gentilic itself — H4125, the Moabite.</summary>
    public required string StrongNumber { get; set; }

    /// <summary>The ancestor or the place, as Strong numbers it — H4124, Moab.</summary>
    public required string OriginNumber { get; set; }

    /// <summary>Which of the two it is. One of <see cref="Strong.GentilicKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>
    /// The origin as a page a reader can open, where exactly one entity answers to its number in
    /// the sense Strong stated — one person for a patronymic, one place for a patrial.
    ///
    /// Null far more often than not, and deliberately so. Moab is two entities under one number,
    /// the man and the land, and the word Strong chose says which is meant; but 23 men are called
    /// Zechariah and nothing in the derivation says which of them a Zechariahite descends from.
    /// The claim above stays true either way, so the row is written and only the link is withheld.
    /// </summary>
    public int? OriginEntityId { get; set; }

    public Entity? Origin { get; set; }

    /// <summary>
    /// Strong's clause, verbatim. The claim is his and the reader is shown it in his words, which
    /// is the difference between citing a lexicon and asserting a genealogy.
    /// </summary>
    public required string Statement { get; set; }

    /// <summary>Which dictionary said so, for the day a second one disagrees.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"StrongGentilic({StrongNumber} {Kind} of {OriginNumber})";
}
