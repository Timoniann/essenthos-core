using System.ComponentModel.DataAnnotations;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A table's rows summed as a count and a 64-bit hash of every row, kept with what it was taken over:
/// the table's file, its columns and how many transactions had written it (<see cref="TableWrite"/>).
/// While all three are unchanged the rows are, and the sum need not be taken again.
/// </summary>
public class TableFingerprint
{
    [Key]
    public string TableName { get; set; } = string.Empty;

    /// <summary>The table's file; a rewrite, a truncation or a table made again moves it.</summary>
    public long FileNode { get; set; }

    /// <summary>The table's columns, their types and order, as the hash of a row reads them.</summary>
    public string Columns { get; set; } = string.Empty;

    /// <summary>How many transactions had written the table when the sum was taken.</summary>
    public long Writes { get; set; }

    public long Rows { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}
