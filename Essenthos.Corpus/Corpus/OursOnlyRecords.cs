namespace Essenthos.Core.Corpus;

/// <summary>
/// A record a dataset supplied that this project has since read for itself is credited as ours: at least one of its verses
/// is a row of ours, or it stands in a relationship of ours. The record files hold what our own rows say of it in its own
/// columns, and the pages credit it to <see cref="Credit"/> where no claim names who said so.
/// </summary>
internal static class OursOnlyRecords
{
    /// <summary>What a qualifying record is credited to; the dataset it belongs to is <see cref="Datasets.Own"/>.</summary>
    public const string Credit = "Essenthos, the line under the name and the verses read from the text";
}
