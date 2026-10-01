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

    /// <summary>
    /// Null for a word that renders the other side by itself, which is nearly every word. Set where
    /// it renders it as part of another word of its own text, <see cref="HeadWordId"/>.
    /// </summary>
    public LinkWordRole? Role { get; set; }

    /// <summary>
    /// The word of the same text this one goes with: <em>see</em> for the <em>did</em> of <em>did
    /// see</em>. Null where the word has no role, and where the head was later taken out of the text.
    /// </summary>
    public long? HeadWordId { get; set; }

    public Word? HeadWord { get; set; }
}
