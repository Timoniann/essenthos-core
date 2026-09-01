using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A span of words a text's own analysis names — a clause, a phrase, a sentence — with whatever
/// attributes that kind carries.
///
/// This is <see cref="Link"/> and <see cref="LinkWord"/> again, and that is the argument for it.
/// The schema already says "a thing that names a set of words, with its own attributes" once;
/// saying it twice keeps it one idea rather than nine. Eight typed tables would be the old schema's
/// mistake in a new place: a table per BHSA concept, populated by one text out of seven.
///
/// It is also not BHSA-only, which is the point. Nestle has syntax available as a Text-Fabric
/// conversion of the LowFat trees, and the Peshitta will have its own. A generic span table holds a
/// Greek treebank the day it arrives; eight Hebrew-shaped tables do not.
/// </summary>
[Index(nameof(TextId), nameof(Kind))]
[Index(nameof(ParentId))]
public class WordGroup
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int TextId { get; set; }

    public Text? Text { get; set; }

    public WordGroupKind Kind { get; set; }

    /// <summary>
    /// The group this one sits inside — a phrase's clause, a clause's sentence. Null where the kind
    /// has nothing above it, and null where the text's own analysis puts this group outside every
    /// group of the kind above, which happens and is not an error.
    /// </summary>
    public long? ParentId { get; set; }

    public WordGroup? Parent { get; set; }

    /// <summary>Its order among the groups of its kind, by where its first word stands.</summary>
    public int Position { get; set; }

    /// <summary>
    /// What this kind of group carries: a clause's domain and text type, a phrase's function and
    /// determination, a subphrase's relation. Held as JSON for the same reason
    /// <see cref="Word.Morphology"/> is — the attributes differ per kind and per text, and a column
    /// each does not scale past the first witness.
    /// </summary>
    public JsonDocument? Features { get; set; }

    public ICollection<WordGroupWord> Words { get; set; } = [];

    public override string ToString() => $"WordGroup({Kind}, {Id})";
}

/// <summary>
/// One word's membership of one group. A word sits in roughly seven of them, so this is the large
/// table — about 3.2 million rows for BHSA — and it is a plain btree in both directions: the
/// primary key answers which words are in a group, and the index on the word answers which groups a
/// word is in.
/// </summary>
[PrimaryKey(nameof(WordGroupId), nameof(WordId))]
[Index(nameof(WordId))]
public class WordGroupWord
{
    public long WordGroupId { get; set; }

    public WordGroup? WordGroup { get; set; }

    public long WordId { get; set; }

    public Word? Word { get; set; }
}
