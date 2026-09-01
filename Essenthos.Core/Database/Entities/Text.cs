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

    public int? PublishedYear { get; set; }

    /// <summary>Where this text was obtained, so a reader can go back to what we loaded.</summary>
    public string? SourceUrl { get; set; }

    public string? RightsHolder { get; set; }

    /// <summary>
    /// Licence name, an SPDX identifier where one applies, otherwise the name the licensor uses.
    /// Null means nobody has checked yet, which is not the same as public domain.
    /// </summary>
    public string? Licence { get; set; }

    public string? LicenceUrl { get; set; }

    public Redistribution Redistribution { get; set; } = Redistribution.Unknown;

    /// <summary>Masoretic, Alexandrian, Byzantine, Samaritan — free text, because the list is open.</summary>
    public string? TextualFamily { get; set; }

    public ICollection<Book> Books { get; set; } = [];

    public override string ToString() => $"Text({Slug}, {Kind}, {Language})";
}
