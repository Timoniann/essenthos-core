using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One original standing behind one spelling of a text in one edition, counted at build from the links: the rows the
/// concordance read backwards (<c>/v1/renderings</c>) adds up for the spellings a reader's word gathers.
///
/// <para>
/// Kept because counting it as it is asked walks every occurrence's links, a random read of four large tables each:
/// <em>people</em> in the Berean took 15 s cold and <em>the</em> could not finish.
/// </para>
/// </summary>
public class WordRendering
{
    public long Id { get; set; }

    public int TextId { get; set; }

    public Text? Text { get; set; }

    /// <summary>The spelling, as <c>word.normalised_text</c> holds it.</summary>
    public required string Form { get; set; }

    public int WitnessId { get; set; }

    public Text? Witness { get; set; }

    /// <summary>The Strong number the edition's words carry; null where they carry none and <see cref="Lemma"/> names the original.</summary>
    public string? StrongNumber { get; set; }

    /// <summary>The lemma naming the original where there is no number; beside a number, the lemma of the word at its first place.</summary>
    public string? Lemma { get; set; }

    /// <summary>The words printing the spelling it stands behind.</summary>
    public int Occurrences { get; set; }

    /// <summary>The links those are counted from.</summary>
    public int Links { get; set; }

    /// <summary>How many of those links each method made, as <c>RenderingPlaces.WriteMethods</c> writes them.</summary>
    public required string Methods { get; set; }

    /// <summary>The links of these holding another spelling one request may gather with this one, as <c>RenderingPlaces.WriteLinks</c> writes them.</summary>
    public string? Shared { get; set; }

    /// <summary>The places it stands at, each once.</summary>
    public int Places { get; set; }

    /// <summary>The first few of them, as <c>RenderingPlaces.Write</c> writes them; all of them where there are no more.</summary>
    public required string Samples { get; set; }

    public WordRenderingPlaces? AllPlaces { get; set; }
}

/// <summary>Every place of a <see cref="WordRendering"/> with more than its samples, read only when a reader asks for more.</summary>
public class WordRenderingPlaces
{
    public long RenderingId { get; set; }

    public WordRendering? Rendering { get; set; }

    /// <summary>As <c>RenderingPlaces.Write</c> writes them.</summary>
    public required string Places { get; set; }
}

/// <summary>One spelling of a text as one edition is counted against it, counted at build beside <see cref="WordRendering"/>.</summary>
[PrimaryKey(nameof(TextId), nameof(Form), nameof(WitnessId))]
public class WordRenderingWitness
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    public required string Form { get; set; }

    public int WitnessId { get; set; }

    public Text? Witness { get; set; }

    /// <summary>The edition's place among the originals the text is linked to, from 0: critical editions first, then the most linked.</summary>
    public int Rank { get; set; }

    /// <summary>The words printing the spelling in the books the edition holds.</summary>
    public int Occurrences { get; set; }

    /// <summary>How many of those some link joins to a word of the edition.</summary>
    public int Reached { get; set; }

    public int Links { get; set; }

    public required string Methods { get; set; }

    public string? Shared { get; set; }

    /// <summary>Who states the links, as numbers of <see cref="WordRenderingSource"/>.</summary>
    public required int[] Sources { get; set; }
}

/// <summary>A source a text's <see cref="WordRenderingWitness"/> rows name by number.</summary>
[PrimaryKey(nameof(TextId), nameof(Number))]
public class WordRenderingSource
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    public int Number { get; set; }

    public required string Source { get; set; }
}
