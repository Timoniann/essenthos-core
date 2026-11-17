using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// How many links join a text to one edition of the original, counted both ways, and whether any word of the edition
/// carries a Strong number: what every count of how a text renders an original is counted over is chosen by. Written by
/// the build for every pair some link joins, editions that are not translations only.
/// </summary>
[PrimaryKey(nameof(TextId), nameof(EditionId))]
public class TextPairLinks
{
    public int TextId { get; set; }

    public Text? Text { get; set; }

    public int EditionId { get; set; }

    public Text? Edition { get; set; }

    public int Links { get; set; }

    public bool Numbered { get; set; }
}
