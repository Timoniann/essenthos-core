using System.ComponentModel.DataAnnotations;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What a compiler of Strong's Hebrew adds to an entry that Strong's own file does not carry: the
/// part of speech, the gender, how often the word stands and where it first stands. One row per
/// number, beside <see cref="StrongEntry"/> rather than in it, because the analysis is the
/// compiler's and not Strong's and the row says whose it is. A Greek number's part of speech and
/// gender are a lexicon's (STEPBible's TBESG), which counts nothing and names no first verse.
///
/// <para>
/// The count is the compiler's, not this corpus's. The corpus counts its own Hebrew, and the
/// verification compares the two for every number rather than serving either as the other.
/// </para>
/// </summary>
public class StrongProfile
{
    [Key]
    public required string StrongNumber { get; set; }

    /// <summary><c>hbo</c> or <c>arc</c>: Hebrew, or the Aramaic of Daniel and Ezra; <c>grc</c> for a Greek number.</summary>
    public required string Language { get; set; }

    /// <summary>As the compiler writes it: <em>noun</em>, <em>verb</em>, <em>noun proper</em>, <em>noun location</em>.</summary>
    public string? PartOfSpeech { get; set; }

    /// <summary><em>masculine</em> or <em>feminine</em>, where the compiler gives one.</summary>
    public string? Gender { get; set; }

    /// <summary>How many times the compiler counts the word in the Hebrew Bible; null where the source counts none.</summary>
    public int? Occurrences { get; set; }

    /// <summary>The canonical book of the first verse the compiler finds it in, 1 to 66.</summary>
    public int? FirstBook { get; set; }

    public int? FirstChapter { get; set; }

    public int? FirstVerse { get; set; }

    public required string Source { get; set; }

    public override string ToString() => $"StrongProfile({StrongNumber}, {PartOfSpeech}, {Occurrences})";
}
