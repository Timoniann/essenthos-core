using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A Hebrew Strong number and a Greek one that stand for the same word, from Hebrew to Greek and back: the Septuagint
/// renders the one by the other, counted from its settled links to the Hebrew, or Strong's Greek dictionary says the Greek
/// word comes from the Hebrew. Keyed on numbers: a claim about two words of the two languages, made at build from files.
/// </summary>
[PrimaryKey(nameof(Hebrew), nameof(Greek))]
[Index(nameof(Greek))]
public class StrongCounterpart
{
    public required string Hebrew { get; set; }

    public required string Greek { get; set; }

    /// <summary>The Hebrew words of <see cref="Hebrew"/> whose Septuagint link holds a word of <see cref="Greek"/>.</summary>
    public int Septuagint { get; set; }

    /// <summary>The Greek words of <see cref="Greek"/> whose link holds a word of <see cref="Hebrew"/>.</summary>
    public int GreekWords { get; set; }

    /// <summary>The Hebrew words of <see cref="Hebrew"/> the links pair with any Greek number.</summary>
    public int HebrewLinked { get; set; }

    /// <summary>The Greek words of <see cref="Greek"/> the links pair with any Hebrew number.</summary>
    public int GreekLinked { get; set; }

    /// <summary>Of <see cref="Septuagint"/>, the Hebrew words whose link a source states rather than a model.</summary>
    public int Stated { get; set; }

    /// <summary>Strong's relation between the two entries (<c>loan-from</c>, <c>corresponds-to</c>), null where he states none.</summary>
    public string? Dictionary { get; set; }

    /// <summary>Strong's own clause for <see cref="Dictionary"/>.</summary>
    public string? Statement { get; set; }

    public override string ToString() => $"StrongCounterpart({Hebrew} {Greek} {Septuagint})";
}
