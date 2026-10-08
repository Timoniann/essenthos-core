using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// The article one language's Wikipedia has on a person, a place or a thing the corpus holds a
/// record of, and how the record was tied to it.
///
/// <para>
/// The tie runs through a Wikidata item: the record is matched to an item, and the item's sitelinks
/// name the articles. A language the item has no article in has no row, so a page never sends a
/// reader to another language's article under a link that reads as their own.
/// </para>
///
/// <para>
/// <see cref="MatchedBy"/> and <see cref="Confidence"/> say how the item was chosen, as every other
/// claim here says how it was made: a record is tied to an item only where nothing else could be
/// the item, and a namesake the evidence cannot tell apart is left unlinked or decided by the owner.
/// </para>
/// </summary>
[Index(nameof(EntityId), nameof(Language), IsUnique = true)]
[Index(nameof(Qid))]
public class EntityWikipedia
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EntityId { get; set; }

    public Entity? Entity { get; set; }

    /// <summary>The two-letter code of the Wikipedia, as an interface locale spells it: <c>en</c>, <c>uk</c>, <c>de</c>, <c>es</c>.</summary>
    public required string Language { get; set; }

    /// <summary>The article's title as Wikidata names it, with spaces, not yet escaped for an address.</summary>
    public required string Title { get; set; }

    /// <summary>The Wikidata item the record was tied to: <c>Q1613144</c>.</summary>
    public required string Qid { get; set; }

    /// <summary>What tied the record to the item; one of <see cref="Evidence"/>.</summary>
    public required string MatchedBy { get; set; }

    /// <summary>How sure that tie is, from 0 to 1. Always 1 where <see cref="MatchedBy"/> is <c>owner</c>.</summary>
    public double Confidence { get; set; }

    public override string ToString() => $"EntityWikipedia({Language} {Title} of entity {EntityId})";

    /// <summary>The ways a record is tied to an item, from the owner's word down.</summary>
    public static class Evidence
    {
        /// <summary>The owner chose the item in his console.</summary>
        public const string Owner = "owner";

        /// <summary>The item's own statement names a verse the record is named in.</summary>
        public const string Verse = "verse";

        /// <summary>The item's family statements name the record's relatives.</summary>
        public const string Kin = "kin";

        /// <summary>The item is said to be present in a chapter that names the record, and nothing else could be the item.</summary>
        public const string Chapter = "chapter";

        /// <summary>The item is the record's only candidate by every spelling of its name, in both directions.</summary>
        public const string Name = "name";

        /// <summary>The gazetteer's identification of the place is the item, and the item is a place of the Bible.</summary>
        public const string Identification = "identification";

        public static readonly string[] All = [Owner, Verse, Kin, Chapter, Name, Identification];
    }

    /// <summary>The Wikipedias a link is kept for: the interface's own languages.</summary>
    public static readonly string[] Languages = ["en", "uk", "de", "es"];
}
