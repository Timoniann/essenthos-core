using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

[PrimaryKey(nameof(VerseLinkId), nameof(VerseId), nameof(Side))]
[Index(nameof(VerseId))]
public class VerseLinkVerse
{
    public int VerseLinkId { get; set; }

    public VerseLink? VerseLink { get; set; }

    public int VerseId { get; set; }

    public Verse? Verse { get; set; }

    public LinkSide Side { get; set; }
}
