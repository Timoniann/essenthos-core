using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One of the 613 commandments of the Torah, numbered as Maimonides counted them in the Sefer
/// HaMitzvot: 248 positive and 365 negative.
///
/// <para>
/// The count is his and it is one count among several — Saadia, the Halakhot Gedolot and
/// Nachmanides each count differently — so the number is always shown with whose it is. The title is
/// Moses Hyamson's English of the Mishneh Torah's own list of the same commandments, shortened and
/// never reworded.
/// </para>
/// </summary>
[Index(nameof(Kind), nameof(Number), IsUnique = true)]
public class Commandment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>One of <see cref="CommandmentKinds"/>.</summary>
    public required string Kind { get; set; }

    /// <summary>Its number among the commandments of its kind in the Sefer HaMitzvot.</summary>
    public int Number { get; set; }

    /// <summary>
    /// The number the Mishneh Torah's list gives it, which is the same number except in three
    /// places where the two works order a few commandments differently.
    /// </summary>
    public int MishnehTorahNumber { get; set; }

    /// <summary>What it commands, in Hyamson's English.</summary>
    public required string Title { get; set; }

    public required string Source { get; set; }

    /// <summary>
    /// What this project did to the verses the source prints: which it moved to the English
    /// numbering, and which are its own and why. Null where they are as printed.
    /// </summary>
    public string? Note { get; set; }

    public List<CommandmentReference> References { get; set; } = [];

    public override string ToString() => $"Commandment({Kind} {Number}: {Title})";
}

/// <summary>
/// A run of verses a commandment rests on, inside one chapter of the shared frame. A run the source
/// states across a chapter end is stored as one row per chapter.
/// </summary>
[Index(nameof(CanonicalBook), nameof(CanonicalChapter))]
[Index(nameof(CommandmentId))]
public class CommandmentReference
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int CommandmentId { get; set; }

    public Commandment? Commandment { get; set; }

    public int CanonicalBook { get; set; }

    public int CanonicalChapter { get; set; }

    public int FirstVerse { get; set; }

    public int LastVerse { get; set; }

    public override string ToString() =>
        $"CommandmentReference({CommandmentId} at {CanonicalBook} {CanonicalChapter}:{FirstVerse}-{LastVerse})";
}

public static class CommandmentKinds
{
    public const string Positive = "positive";

    public const string Negative = "negative";

    public static bool IsKnown(string kind) => kind is Positive or Negative;
}
