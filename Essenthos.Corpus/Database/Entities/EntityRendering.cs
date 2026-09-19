using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One spelling one text prints for an entity, and how many times it prints it.
///
/// <para>
/// The entity is the one record and every text's name for it hangs off it: <em>Aaron</em> in the
/// King James, <em>Аарон</em> and <em>Аарона</em> in the Ohienko Bible, <em>Aarón</em> in the Reina
/// Valera, <em>אַהֲרֹן</em> in BHSA. Nothing here is asserted about a name: a row is a count of the
/// words <see cref="WordEntity"/> already says name the entity in that text, so it is exactly as good
/// as those annotations and no better, and it is rebuilt whenever they change. That is what makes it
/// derived and not sourced — a reader is told <em>how the text prints it</em>, never that some
/// authority says this is the name.
/// </para>
///
/// <para>
/// A Slavic text prints a name in every case it declines, so the commonest spelling is often not the
/// name — the Ohienko Bible prints <em>Аарона</em> more often than <em>Аарон</em>. <see cref="Heading"/>
/// marks the one spelling a list should show, chosen by a stated rule, and every other spelling stays
/// beside it because each of them is something a reader may type.
/// </para>
/// </summary>
[Index(nameof(EntityId), nameof(TextId), nameof(Form), IsUnique = true)]
[Index(nameof(TextId))]
public class EntityRendering
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    public int TextId { get; set; }

    public Text? Text { get; set; }

    /// <summary>
    /// The spelling as the text prints it, with the quotation marks around it and an English
    /// possessive taken off, and two adjacent words named as one entity joined into one name. For a
    /// Hebrew or Greek witness with a lemma it is the lemma, since the form carries an article or a
    /// case and the lemma is what a reader calls the name.
    /// </summary>
    public required string Form { get; set; }

    /// <summary>
    /// <see cref="Form"/> the way a search compares it: lower case, without Hebrew points, Greek
    /// accents or Latin diacritics. Written by <c>NameFolding</c>, which also folds what is typed.
    /// </summary>
    public required string Folded { get; set; }

    /// <summary>How many times the text prints this spelling for this entity.</summary>
    public int Occurrences { get; set; }

    /// <summary>
    /// The one spelling of this text a list heads the entity with. Exactly one per entity and text.
    /// </summary>
    public bool Heading { get; set; }

    public override string ToString() => $"EntityRendering({EntityId} in {TextId}: {Form} ×{Occurrences})";
}
