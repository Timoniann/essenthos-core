using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One Bible, of whatever kind: a manuscript tradition, a critical edition or a translation. There
/// is deliberately no "is original" column — the old schema had one slot per canonical book and
/// identified a corpus by the language on its words, which is why it could hold neither a second
/// Greek witness nor the Septuagint.
/// </summary>
[Index(nameof(Slug), IsUnique = true)]
public class Text
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// The public identifier, stable and lower case: <c>bhsa</c>, <c>nestle1904</c>,
    /// <c>tr-scrivener</c>, <c>lxx-rahlfs</c>, <c>kjv</c>, <c>rusv</c>. It appears in every word
    /// address the API hands out, so it may not change once a text is published.
    /// </summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    public string? NameNative { get; set; }

    public TextKind Kind { get; set; }

    /// <summary>ISO 639-3: <c>hbo</c>, <c>arc</c>, <c>grc</c>, <c>eng</c>, <c>rus</c>, <c>ukr</c>.</summary>
    public required string Language { get; set; }

    public TextDirection Direction { get; set; } = TextDirection.LeftToRight;

    /// <summary>Which frame this text's own chapter and verse numbers follow.</summary>
    public Versification Versification { get; set; } = Versification.Unknown;

    /// <summary>
    /// Who put the text into the language it is in. A body where there is no single person — the
    /// King James has forty-seven translators in six companies and naming none of them would be as
    /// false as naming one. Null where nobody is known, which is the Septuagint's translators, and
    /// where the text is not a translation at all.
    /// </summary>
    public string? Translators { get; set; }

    /// <summary>
    /// Who established this edition of it, which is a different person from the translator and
    /// often the only one there is: Nestle edited a Greek text he did not write, Scrivener
    /// reconstructed one, the ETCBC annotated a printed edition.
    /// </summary>
    public string? Editors { get; set; }

    /// <summary>
    /// Which edition or revision this is, where that is what identifies it and the year does not.
    /// Every digital "King James" is the modern standard text rather than the 1611 printing, and
    /// nothing in a row that says 1611 tells a reader so.
    /// </summary>
    public string? Edition { get; set; }

    /// <summary>
    /// When the work was first published, which for a revised text is not when the edition served
    /// here was printed — see <see cref="EditionYear"/>.
    /// </summary>
    public int? PublishedYear { get; set; }

    /// <summary>
    /// The year of the edition actually loaded, where it is not <see cref="PublishedYear"/>. Null
    /// means the two are the same, not that nobody looked.
    /// </summary>
    public int? EditionYear { get; set; }

    /// <summary>
    /// What this text is and how it came to be, in a paragraph, because the columns beside it
    /// cannot say that a translation begun in 1917 and finished in 1940 was first printed in 1962.
    /// </summary>
    public string? About { get; set; }

    /// <summary>
    /// What is unsettled or additional about the rights, beside the licence the source states.
    /// It sits next to <see cref="Licence"/> rather than inside <see cref="About"/> because a
    /// reader deciding whether they may republish needs it where they are already looking, and a
    /// contested public-domain claim is worse than an unchecked one when it is not shown.
    /// </summary>
    public string? RightsNote { get; set; }

    /// <summary>Where this text was obtained, so a reader can go back to what we loaded.</summary>
    public string? SourceUrl { get; set; }

    public string? RightsHolder { get; set; }

    /// <summary>
    /// Licence name, an SPDX identifier where one applies, otherwise the name the licensor uses.
    /// Null means nobody has checked yet, which is not the same as public domain.
    /// </summary>
    public string? Licence { get; set; }

    public string? LicenceUrl { get; set; }

    /// <summary>
    /// How this text must be cited, where its licence asks for something a name and a URL cannot
    /// carry. BHSA requires the DOI 10.17026/dans-z6y-skyh in anything published from it; that is
    /// an obligation, and there was nowhere to put it (PRB-0067).
    /// </summary>
    public string? Citation { get; set; }

    public Redistribution Redistribution { get; set; } = Redistribution.Unknown;

    /// <summary>Masoretic, Alexandrian, Byzantine, Samaritan — free text, because the list is open.</summary>
    public string? TextualFamily { get; set; }

    public ICollection<Book> Books { get; set; } = [];

    public override string ToString() => $"Text({Slug}, {Kind}, {Language})";
}
