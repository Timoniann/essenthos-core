using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A name for a people, and whom or where Strong's dictionary says they are named after. The
/// Moabites are H4125 and Moab is H4124, and Strong wrote the tie between them himself.
///
/// <para>
/// It is keyed on Strong numbers rather than on two entities, and that is deliberate: the claim is
/// about two words, and it stays true whether or not the encyclopedia holds a page for either end.
/// <see cref="EntityRelationship"/> is the right table for <em>this person is that person's son</em>
/// and would be the wrong one for this even now, because the gentilic number sometimes matches a
/// name in the encyclopedia and where it does it matches the wrong thing — H6430 is the Philistines
/// and the only person carrying it is Goliath, whose name in the dataset is <em>The Philistine</em>.
/// An edge built on that join reads <em>Goliath is a descendant of Philistia</em>, and a reader
/// could not tell it from scholarship.
/// </para>
///
/// <para>
/// So both entity columns are found rather than joined. <see cref="OriginEntityId"/> is the page for
/// whom or where the people are named after; <see cref="PeopleEntityId"/> is the page for the people
/// themselves, which had nowhere to point until <see cref="Enums.EntityKind.People"/> existed and is
/// what this row was always describing. Either can be null and the sentence still stands: a reader
/// hovering <em>моавітяни</em> reaches the Hebrew word, the word carries H4125, and this says the
/// people are named after Moab whether or not there is a page behind either name.
/// </para>
/// </summary>
[Index(nameof(StrongNumber), IsUnique = true)]
[Index(nameof(OriginNumber))]
[Index(nameof(OriginEntityId))]
[Index(nameof(PeopleEntityId))]
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
    /// The people this lexeme names, as a page a reader can open.
    ///
    /// This is the near end the row was written without, and the reason it was written without one
    /// is that <c>entity.kind</c> held a person and a place and nothing else. It is filled from the
    /// gentilic itself rather than by matching the name: a name match is what puts Goliath on
    /// H6430, and the whole point of the column is to stop pointing at a man when the word means a
    /// nation.
    /// </summary>
    public int? PeopleEntityId { get; set; }

    public Entity? People { get; set; }

    /// <summary>
    /// Strong's clause, verbatim. The claim is his and the reader is shown it in his words, which
    /// is the difference between citing a lexicon and asserting a genealogy.
    /// </summary>
    public required string Statement { get; set; }

    /// <summary>Which dictionary said so, for the day a second one disagrees.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"StrongGentilic({StrongNumber} {Kind} of {OriginNumber})";
}
