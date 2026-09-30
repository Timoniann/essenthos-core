using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The verse references a reader is shown. BibleData's rows are left out before anything is counted,
/// listed or ranked, so a reference only that dataset states is not shown and one it shares with
/// ours is ours alone, with no label or credit of the dataset's beside it: the owner's ruling of
/// 2026-09-29 that the encyclopedia is this project's, with the dataset kept for events and the
/// timeline. The rows stay stored as the witness ours are compared with, and the passes that read
/// the corpus read them too.
/// </summary>
internal static class ShownVerses
{
    /// <summary>What the source of every row the witness states starts with.</summary>
    public const string Witness = "BibleData by";

    public static IQueryable<EntityVerse> Shown(this IQueryable<EntityVerse> references) =>
        references.Where(v => !v.Source.StartsWith(Witness));
}
