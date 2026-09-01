using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One word's membership of one side of a link. Either side may be empty: a translation supplying a
/// word its source only implies has nothing on the source side, and a reading one witness lacks has
/// nothing on that witness's side. Both are stored as rows rather than as silence.
/// </summary>
[PrimaryKey(nameof(LinkId), nameof(WordId), nameof(Side))]
[Index(nameof(WordId))]
public class LinkWord
{
    public long LinkId { get; set; }

    public Link? Link { get; set; }

    public long WordId { get; set; }

    public Word? Word { get; set; }

    public LinkSide Side { get; set; }
}
