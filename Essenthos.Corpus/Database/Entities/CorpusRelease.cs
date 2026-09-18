using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// The label on a corpus that was released: which bytes it was built from, by which code, and what it
/// held.
///
/// Written by <c>forge release</c> into the working database immediately before the dump and deleted
/// immediately after, so it travels inside the dump and is absent from the database that keeps being
/// loaded. A database with a row is therefore a restored release, and one without is a working copy
/// whose contents no label describes — which is exactly what the health endpoint reports.
///
/// A reader who disagrees with a row, and a bug report six months old, both need to say which corpus
/// they saw. The name answers it in conversation; the fingerprint and the version answer it exactly.
///
/// The counts are here because they never change between releases, so computing them once at release
/// time turns the health report into a one-row read instead of counting millions of rows.
/// </summary>
public class CorpusRelease
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The build date and a letter, <c>20260918a</c>: sortable, and sayable out loud.</summary>
    [MaxLength(32)]
    public required string Name { get; set; }

    public DateTimeOffset BuiltAt { get; set; }

    /// <summary>
    /// SHA-256 of <c>Resources/MANIFEST.json</c> as it stood, which is itself a fingerprint of every
    /// source folder. Two releases with the same value were built from the same bytes.
    /// </summary>
    [MaxLength(64)]
    public required string ManifestSha { get; set; }

    /// <summary>The last corpus migration applied, so a restore target can tell what schema it holds.</summary>
    [MaxLength(150)]
    public required string MigrationHead { get; set; }

    /// <summary>
    /// The informational version of the Forge that released it — commit, and <c>-dirty</c> when the
    /// tree had uncommitted changes. It names the code that dumped the corpus; the loads that wrote
    /// it ran earlier, possibly from other commits, and their passes carry their own versions.
    /// </summary>
    [MaxLength(100)]
    public required string ForgeVersion { get; set; }

    public int OriginalWords { get; set; }

    public int Translations { get; set; }

    public int StrongEntries { get; set; }

    public int People { get; set; }

    public int Places { get; set; }

    public int Peoples { get; set; }

    public override string ToString() => $"CorpusRelease({Name}, {ForgeVersion}, {ManifestSha[..12]})";
}
