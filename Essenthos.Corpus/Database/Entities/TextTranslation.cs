using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A machine translation of English a record carries from its source — an event's title, description
/// and notes, a reckoning's arithmetic, a place's modern site, a record's note — keyed by the digest of
/// that English and nothing else, so one rendering serves every row that says the same words.
///
/// <para>
/// It is never the source's words and is served marked as a machine translation beside them. A row
/// whose English changes no longer finds its rendering, and the reader is shown the English, which is
/// what the source says.
/// </para>
/// </summary>
[Index(nameof(Language), nameof(EnglishSha256), IsUnique = true)]
public class TextTranslation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The three-letter code the texts are tagged with: <c>ukr</c>, <c>deu</c>, <c>spa</c>.</summary>
    [MaxLength(8)]
    public required string Language { get; set; }

    /// <summary>SHA-256 of the English this renders, UTF-8, lower-case hex.</summary>
    [MaxLength(64)]
    public required string EnglishSha256 { get; set; }

    public required string Text { get; set; }

    /// <summary>What rendered it, with which model, prompt and date. Never empty.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"TextTranslation({Language} {EnglishSha256[..12]})";
}
