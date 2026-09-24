using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A published work that divides time into periods — a handbook, an excavation report, a museum's
/// thesaurus — as PeriodO records it.
///
/// The unit of disagreement. Two authorities dating the Late Bronze Age of the Levant differently
/// are two opinions with two names on them, and neither is the corpus's: that is the model
/// <see cref="Chronology"/> already follows for the reckonings from creation.
/// </summary>
[Index(nameof(PeriodoId), IsUnique = true)]
public class PeriodAuthority
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>PeriodO's own identifier, such as <c>p0f65r2</c>. Resolves under <c>n2t.net/ark:/99152/</c>.</summary>
    public required string PeriodoId { get; set; }

    /// <summary>Who and what, short enough to follow a date: <c>King &amp; Stager, Life in Biblical Israel (2001)</c>.</summary>
    public required string Attribution { get; set; }

    /// <summary>The citation as the source gives it, or as its parts compose one where it gives none.</summary>
    public required string Citation { get; set; }

    public string? Title { get; set; }

    /// <summary>The creators, as the source names them, separated by semicolons.</summary>
    public string? Creators { get; set; }

    public int? YearPublished { get; set; }

    /// <summary>The page or section the periods were read from.</summary>
    public string? Locator { get; set; }

    /// <summary>Where to find the work: a catalogue record, a DOI, a web page.</summary>
    public string? Uri { get; set; }

    public ICollection<PeriodDefinition> Periods { get; set; } = [];

    public override string ToString() => $"PeriodAuthority({PeriodoId})";
}

/// <summary>
/// One authority's definition of one period in one place: <em>Late Bronze, Levant, 1550–1200
/// B.C.E.</em>, after King and Stager.
///
/// Kept exactly as the source gives it rather than folded into <see cref="Period"/>: the same name
/// under five authorities is five rows here, and their disagreement is what a reader is shown. The
/// years are astronomical, as PeriodO writes them — <c>-3499</c> is 3500 BCE, with a year zero —
/// and each end is a range, earliest to latest, which is equal where the source gives one year.
/// </summary>
[Index(nameof(PeriodoId), IsUnique = true)]
[Index(nameof(Region))]
public class PeriodDefinition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>PeriodO's identifier for the definition, such as <c>p0f65r2qmh2</c>.</summary>
    public required string PeriodoId { get; set; }

    public int AuthorityId { get; set; }
    public PeriodAuthority? Authority { get; set; }

    /// <summary>The name in the language the authority wrote it in.</summary>
    public required string Label { get; set; }

    /// <summary>That language, as a BCP 47 tag.</summary>
    public string? LanguageTag { get; set; }

    /// <summary>
    /// The name in every language PeriodO has it in, as a JSON object of tag to label. The source's
    /// own translations, never ours.
    /// </summary>
    public string? Labels { get; set; }

    /// <summary>
    /// Which of the lands around the Bible this is drawn with: <c>levant</c>, <c>egypt</c>,
    /// <c>mesopotamia</c>, <c>anatolia</c>, <c>persia</c>, <c>aegean</c> or <c>rome</c>. Ours,
    /// read off <see cref="Coverage"/>; the source's own words are kept beside it.
    /// </summary>
    public required string Region { get; set; }

    /// <summary>The places the source says it covers, as a JSON array of Wikidata items and their labels.</summary>
    public string? Coverage { get; set; }

    /// <summary>The source's own words for where, such as <c>Levant</c> or <c>Southern Mesopotamia</c>.</summary>
    public string? CoverageDescription { get; set; }

    /// <summary>The start as the source writes it: <c>3500 B.C.E.</c>, <c>ca. 1550</c>.</summary>
    public string? StartLabel { get; set; }

    public int StartEarliest { get; set; }

    public int StartLatest { get; set; }

    public string? StopLabel { get; set; }

    public int StopEarliest { get; set; }

    public int StopLatest { get; set; }

    /// <summary>The PeriodO identifier of the period this one is part of, where the source says.</summary>
    public string? Broader { get; set; }

    public string? Note { get; set; }

    public string? EditorialNote { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"PeriodDefinition({PeriodoId})";
}
