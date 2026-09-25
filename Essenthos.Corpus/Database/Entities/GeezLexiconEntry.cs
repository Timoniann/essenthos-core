using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// One entry of Dillmann's Lexicon Linguae Aethiopicae as Beta maṣāḥǝft digitised it: the headword,
/// the Latin Dillmann glosses it with, and the Greek he sets beside it.
///
/// <para>
/// Nothing here says which word of which text is a form of it. That is concluded when a Ge'ez word
/// is read, from its letters and the Greek it is aligned to, and the reading says which way it went —
/// the same arrangement as <see cref="LexiconGloss"/> for the Greek.
/// </para>
/// </summary>
[Index(nameof(Entry), IsUnique = true)]
public class GeezLexiconEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The digital edition's own identifier for the entry, <c>L968149f7fc1748479a9f8482bc3f49c0</c>.</summary>
    public required string Entry { get; set; }

    public required string Headword { get; set; }

    /// <summary>
    /// The headword, then every spelling another entry of the lexicon sends to it — <em>እለ</em> "who"
    /// is filed under <em>ዘ</em> — in Ge'ez letters and nothing else.
    /// </summary>
    public required string[] Forms { get; set; }

    /// <summary>The consonants of each form, which is what a word of the text is looked up by.</summary>
    public required string[] Consonants { get; set; }

    /// <summary>Dillmann's Latin glosses, in his order, without the ones inside his etymological notes.</summary>
    public required string[] Latin { get; set; }

    /// <summary>The Greek Dillmann gives as its equivalent, as he prints it, in his order.</summary>
    public required string[] Greek { get; set; }

    /// <summary>Which lexicon, whose digitisation, and under what terms. Never empty.</summary>
    public required string Source { get; set; }

    public override string ToString() => $"GeezLexiconEntry({Entry} {Headword})";
}
