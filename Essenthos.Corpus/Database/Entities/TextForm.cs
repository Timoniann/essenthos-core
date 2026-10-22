using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One word form a text prints, as a search compares it, with its stem and how often the text prints it.
///
/// <para>
/// A verse search reads it twice. A typed word is turned into every form of the text that shares its
/// stem — <em>воскресіння</em> into <em>воскресіння</em>, <em>воскресінням</em>, <em>воскресінні</em> —
/// and those are looked up in the words' own index, which is how a stem is matched without a stem on
/// each of thirty million words. And a word the text never prints, or prints rarely beside a near
/// spelling it prints often, is answered with that spelling as a suggestion
/// (<em>воскресення</em>), from the trigram index on <see cref="Form"/>.
/// </para>
///
/// <para>
/// Counted from <see cref="Word.NormalisedText"/> by the load's <c>search-forms</c> step; derived, and
/// rebuilt whenever the words move.
/// </para>
/// </summary>
[PrimaryKey(nameof(TextId), nameof(Form))]
[Index(nameof(TextId), nameof(Stem))]
public class TextForm
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    /// <summary>The form as <see cref="Word.NormalisedText"/> holds it.</summary>
    public required string Form { get; set; }

    /// <summary>
    /// What the forms of one word share in this text's language, or the form itself in a language
    /// nothing here stems (<c>SearchWords</c>).
    /// </summary>
    public required string Stem { get; set; }

    /// <summary>How many words of the text are this form.</summary>
    public int Occurrences { get; set; }
}
