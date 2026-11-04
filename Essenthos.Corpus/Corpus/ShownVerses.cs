using System.Linq.Expressions;
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

    private const string OpenBible =
        "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    // Ezekiel's Tarshish stone describes the wheels' appearance, not the geographical place.
    // Keep OpenBible's testimony stored, but do not count this lexical sense as a place reference.
    internal static readonly Expression<Func<EntityVerse, bool>> IsShown =
        v => !v.Source.StartsWith(Witness)
             && !(v.Source == OpenBible && v.CanonicalBook == 26 && v.CanonicalChapter == 10 && v.CanonicalVerse == 9
                  && v.Entity!.ExternalIds.Any(x => x.Dataset == Datasets.OpenBibleId && x.ExternalId == TarshishStone
                                                    && x.Relation == RecordExternalRelations.SameAs));

    /// <summary>OpenBible's entry for the Tarshish of Ezekiel 10:9's stone.</summary>
    private const string TarshishStone = "a5f43dd";

    /// <summary>
    /// What the source of every row read off the settled annotations starts with, a person's or a
    /// people's.
    /// </summary>
    private const string FromOurWords = "Essenthos, from the words this corpus annotates";

    /// <summary>
    /// Whether a row says that a word of the verse, in some text, names the record as the reader is
    /// told it. A row holds a canonical verse and no text, so a question about one text's words is not
    /// answered here.
    /// </summary>
    public static bool ReadOffTheWords(string source, bool names) =>
        names && source.StartsWith(FromOurWords, StringComparison.Ordinal);

    public static IQueryable<EntityVerse> Shown(this IQueryable<EntityVerse> references) =>
        references.Where(IsShown);
}
