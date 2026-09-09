using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// One fact, one row, with every witness that states it.
///
/// Two witnesses speak in the relationship table and 2,715 of the rows this corpus read for itself
/// restate a row BibleData already had, so a page projected row by row met the reader as
/// <em>son of Haran</em> followed by <em>son-of Haran</em> — the same sentence twice, in two
/// vocabularies, with nothing on the page saying they were one thing.
///
/// <para>
/// **The row that survives is this corpus's own.** A relation read out of Genesis 11:27 and
/// corroborated by a dataset is better evidenced than the dataset's row alone, and the reader is
/// owed the reading rather than the edge list. What the other witness said travels with it under
/// <see cref="EntityRelationshipResponse.Corroboration"/> — its own word for the relation, its own
/// verse, its own method and its own credit — because a page that quietly dropped the second
/// witness would have thrown away the best thing about having two.
/// </para>
///
/// <para>
/// **Nothing is merged that was not already agreed.** Where the two answer one question differently
/// the loader has already withheld ours (127 clauses), and nothing here re-decides that: a row this
/// pass cannot pair stays exactly as it was written, which is also what happens to every relation
/// BibleData states and no reading of ours reached.
/// </para>
///
/// <para>
/// **A witness is paired on the pair it names before it is paired on the pair read backwards.**
/// BibleData records both <em>Lot son Haran</em> and <em>Haran father Lot</em>, and both agree with
/// both of our rows about the two men; taking the reversed reading first would put one witness on
/// one row and leave the other with none. So the direct reading runs over the whole page and the
/// reversed reading only over what it did not claim — which is what leaves
/// <em>Bani ancestor Adaiah</em> free to answer our <em>Adaiah descendant-of Bani</em>, the case
/// <see cref="RelationshipVocabulary.Reversed"/> exists for.
/// </para>
/// </summary>
internal static class Relationships
{
    /// <summary>
    /// One entity's relationships as its page shows them.
    ///
    /// Both directions in one query rather than one each: a father is not recorded twice, so
    /// reading only one side would give Isaac a father and no sons — and a row of ours is often
    /// answered by a row of a witness's on the pair read backwards, which is in the other
    /// direction and has to be in hand before either row is shown.
    /// </summary>
    public static async Task<List<EntityRelationshipResponse>> Of(
        AppDbContext db,
        int entityId,
        CancellationToken cancellationToken) =>
        Merged(await db.EntityRelationships
            .Where(r => r.FromEntityId == entityId || r.ToEntityId == entityId)
            .OrderBy(r => r.ToEntityId == entityId)
            .ThenBy(r => r.Id)
            .Select(r => new Related(
                r.FromEntityId,
                r.ToEntityId,
                r.Type,
                r.Category,
                r.ToEntityId == entityId ? r.From!.Slug : r.To!.Slug,
                r.ToEntityId == entityId ? r.From!.Name : r.To!.Name,
                r.ToEntityId == entityId ? r.From!.Distinguisher : r.To!.Distinguisher,
                r.ToEntityId == entityId,
                BookReferences.At(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse),
                r.Notes,
                r.Method,
                r.Confidence,
                r.Source))
            .ToListAsync(cancellationToken));

    /// <summary>
    /// The rows of one page, agreement folded together, in the order they were read — outward
    /// before inward, and a row that absorbed a witness keeps the place it already had.
    /// </summary>
    public static List<EntityRelationshipResponse> Merged(IReadOnlyList<Related> rows)
    {
        var ours = new List<int>();
        var witnesses = new List<int>();
        for (var row = 0; row < rows.Count; row++)
        {
            (Ours(rows[row]) ? ours : witnesses).Add(row);
        }

        if (ours.Count == 0 || witnesses.Count == 0)
        {
            return [.. rows.Select(row => Show(row, []))];
        }

        var corroboration = new List<Related>?[rows.Count];
        var absorbed = new bool[rows.Count];

        foreach (var pairing in Pairings)
        {
            foreach (var witness in witnesses.Where(witness => !absorbed[witness]))
            {
                var mine = ours.FirstOrDefault(
                    mine => pairing(rows[mine], rows[witness]),
                    NoRow);
                if (mine == NoRow)
                {
                    continue;
                }

                (corroboration[mine] ??= []).Add(rows[witness]);
                absorbed[witness] = true;
            }
        }

        return
        [
            .. rows
                .Index()
                .Where(row => !absorbed[row.Index])
                .Select(row => Show(row.Item, corroboration[row.Index] ?? [])),
        ];
    }

    /// <summary>
    /// The readings of a witness's row, strongest first: the pair it names, and then the pair read
    /// backwards.
    /// </summary>
    private static readonly Func<Related, Related, bool>[] Pairings = [Restates, RestatesBackwards];

    private const int NoRow = -1;

    /// <summary>The witness states this very pair, in a relation that is this one in its words.</summary>
    private static bool Restates(Related mine, Related witness) =>
        witness.From == mine.From
        && witness.To == mine.To
        && Says(witness) == mine.Type;

    /// <summary>
    /// The witness states the same fact from the other end. BibleData writes
    /// <em>Bani is the ancestor of Adaiah</em> where the encyclopedia answers on Adaiah, and read
    /// one-directionally that corroboration would be invisible.
    /// </summary>
    private static bool RestatesBackwards(Related mine, Related witness) =>
        witness.From == mine.To
        && witness.To == mine.From
        && Says(witness) is { } relation
        && RelationshipVocabulary.Reversed(relation).Contains(mine.Type);

    /// <summary>
    /// What a witness's relation is called in this corpus's vocabulary, or null where the corpus
    /// has no word for it. <c>cousin</c>, <c>rabbi</c> and <c>concubinator</c> neither corroborate
    /// a reading nor contradict one, so a row carrying one is a row of its own.
    /// </summary>
    private static string? Says(Related witness) =>
        RelationshipVocabulary.Says.GetValueOrDefault(witness.Type);

    private static bool Ours(Related row) => Datasets.Of(row.Source) == Datasets.Own;

    private static EntityRelationshipResponse Show(Related row, IReadOnlyList<Related> corroboration) =>
        new(row.Type, row.Category, row.Slug, row.Name, row.Distinguisher, row.Inward,
            row.Reference, row.Notes)
        {
            Method = EnumSpelling.Of(row.Method),
            Confidence = row.Confidence,
            Source = row.Source,
            Corroboration = [.. corroboration.Select(witness => Corroborating(row, witness))],
        };

    private static EntityRelationshipWitnessResponse Corroborating(Related row, Related witness) =>
        new(witness.Type, witness.Category, witness.Inward != row.Inward, witness.Reference, witness.Notes)
        {
            Method = EnumSpelling.Of(witness.Method),
            Confidence = witness.Confidence,
            Source = witness.Source,
        };

    /// <summary>
    /// One relationship row as one page reads it, with the pair it was stored on kept beside the
    /// entity it points at — the slug is what a reader follows and the two ids are what says two
    /// rows are about the same two people.
    /// </summary>
    /// <param name="Inward">
    /// True where the row names this page's entity second: Isaac is recorded as the son of Abraham,
    /// and Abraham's page reads that row from his side.
    /// </param>
    internal sealed record Related(
        int From,
        int To,
        string Type,
        string Category,
        string Slug,
        string Name,
        string? Distinguisher,
        bool Inward,
        VerseRefResponse? Reference,
        string? Notes,
        LinkMethod Method,
        double? Confidence,
        string Source);
}
