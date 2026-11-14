using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// Every word of one edition carrying a Strong number that a text renders, each with the phrase it is rendered by, in the
/// order the list of the number's places shows them: the places behind each row of <see cref="StrongRendering"/>, counted at
/// build from the same index (<c>texts/&lt;SLUG&gt;/index/strong</c>), so a list filtered by a phrase holds exactly the words
/// its count counts.
///
/// <para>
/// Kept because finding them as asked hashed every link word of the number in every text against the pair's links: H3068
/// <em>lord</em> in the King James took 5 s with a cold cache on the workstation and 20 s on the server.
/// </para>
/// </summary>
[PrimaryKey(nameof(TextId), nameof(StrongNumber), nameof(WitnessId))]
public class StrongRenderingPlace
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    public required string StrongNumber { get; set; }

    public int WitnessId { get; set; }

    public Text? Witness { get; set; }

    /// <summary>The phrases the text renders the number's words with here, each once.</summary>
    public required string[] Phrases { get; set; }

    /// <summary>The words, by id, in the order of their primary place in the shared frame, then position, then id.</summary>
    public required long[] Words { get; set; }

    /// <summary>For each of <see cref="Words"/>, its phrase, as an index into <see cref="Phrases"/> from 0.</summary>
    public required int[] PhraseOf { get; set; }

    /// <summary>For each of <see cref="Words"/>, the canonical ordinal of the edition's book it stands in.</summary>
    public required short[] BookOf { get; set; }
}
