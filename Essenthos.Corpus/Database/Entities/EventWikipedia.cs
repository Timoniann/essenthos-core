using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// The article one language's Wikipedia has on a world event: the article the event's own Wikidata item
/// names. Nothing is matched here. A world event is drawn from its item, so the item is the event and its
/// sitelinks are its articles; a language the item has no article in has no row.
/// </summary>
[Index(nameof(EventId), nameof(Language), IsUnique = true)]
public class EventWikipedia
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int EventId { get; set; }

    public Event? Event { get; set; }

    /// <summary>The two-letter code of the Wikipedia: one of <see cref="EntityWikipedia.Languages"/>.</summary>
    public required string Language { get; set; }

    /// <summary>The article's title as Wikidata names it, with spaces, not yet escaped for an address.</summary>
    public required string Title { get; set; }

    /// <summary>The event's Wikidata item: <c>Q339489</c>.</summary>
    public required string Qid { get; set; }

    public override string ToString() => $"EventWikipedia({Language} {Title} of event {EventId})";
}
