namespace Essenthos.Core.Database.Entities;

/// <summary>
/// That a transaction wrote a table: one row per table per transaction, added by a statement trigger
/// on every table in the same transaction as the write, so it is there exactly when the write is.
/// Rows of one table are folded into one now and then, their <see cref="Weight"/> summed, so the sum
/// per table only ever grows.
/// </summary>
public class TableWrite
{
    public long Id { get; set; }

    public string TableName { get; set; } = string.Empty;

    public long Weight { get; set; } = 1;
}
