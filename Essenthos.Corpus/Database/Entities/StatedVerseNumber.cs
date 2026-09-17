using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// An address the edition itself prints for a verse, in the numbering the edition follows, where
/// that is not the numbering the verse is stored under.
///
/// <para>
/// <see cref="Verse"/> is a verse in one text's own numbering and <see cref="VerseReference"/> is
/// where that numbering sits in the shared frame, and between them there is no room for a third
/// fact: that the file a text was loaded from had already been renumbered by whoever published it.
/// bible4u's Synodal and Ukrainian files are both numbered the way the King James is — which is
/// what lets them be placed in the frame at all — and both print the edition's own address in the
/// verse text where the two disagree. That address is what these rows hold.
/// </para>
///
/// <para>
/// It is read out of the file and not computed from anything: no row here is an inference about
/// where a verse "should" sit, and none is written for a verse whose edition printed nothing. So a
/// text with no rows is a text that stated nothing, which is different from a text whose stated
/// numbering happens to agree — the Synodal states nothing at Genesis 1:1 because its printed
/// numbering there is the stored one, and reading the silence as agreement is the client's
/// inference to make and not the corpus's to record.
/// </para>
/// </summary>
[Index(nameof(VerseId), nameof(Position), IsUnique = true)]
public class StatedVerseNumber
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int VerseId { get; set; }
    public Verse? Verse { get; set; }

    /// <summary>
    /// Where this address stands among the verse's own, counting from one. A verse usually prints
    /// one, and prints two where the edition divides what the stored numbering merges: the Synodal
    /// gives Psalm 12 a verse for its superscription and a verse for its body, and both of them are
    /// this corpus's 12:1. The order is the order they are printed in, which is the order of the
    /// text they address.
    /// </summary>
    public int Position { get; set; }

    public int ChapterNumber { get; set; }

    public int Number { get; set; }

    public override string ToString() =>
        $"StatedVerseNumber(verse {VerseId}, printed {ChapterNumber}:{Number})";
}
