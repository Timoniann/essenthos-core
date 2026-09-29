using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;

using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

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
        string? language,
        CancellationToken cancellationToken)
    {
        var rows = await Rows(db, entityId, cancellationToken);
        var merged = Merged(rows);

        // The counterpart's name in the case the reader's language puts it in. Without it the
        // section is English on a Ukrainian page, because the phrase and the case are one decision
        // and a stemmer guessing the genitive of a Hebrew proper name is wrong often and silently.
        // A language with no form for a name is a gap the client fills with the English one, which
        // is the same fallback the descriptor line already has.
        var counterparts = rows
            .Select(row => row.Inward ? row.From : row.To)
            .Distinct()
            .ToList();

        var forms = await Forms(db, counterparts, language, cancellationToken);
        return forms.Count == 0
            ? merged
            : [.. merged.Select(row => row with { Forms = forms.GetValueOrDefault(row.Slug) })];
    }

    /// <summary>
    /// Every case these entities have a form in, in one language, keyed by slug. Empty where the
    /// language is one no pass has declined a name into, which is every language but the two.
    /// </summary>
    public static async Task<Dictionary<string, Dictionary<string, string>>> Forms(
        AppDbContext db,
        IReadOnlyCollection<int> entities,
        string? language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(language) || entities.Count == 0)
        {
            return [];
        }

        var rows = await db.EntityNameForms
            .Where(f => entities.Contains(f.EntityId) && f.Language == language)
            .Select(f => new { f.Entity!.Slug, f.GrammaticalCase, f.Form })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(f => f.Slug, StringComparer.Ordinal)
            .ToDictionary(
                one => one.Key,
                one => one.ToDictionary(f => f.GrammaticalCase, f => f.Form, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static async Task<List<Related>> Rows(
        AppDbContext db,
        int entityId,
        CancellationToken cancellationToken) =>
        await db.EntityRelationships
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
                r.ToEntityId == entityId ? r.From!.Kind : r.To!.Kind,
                r.ToEntityId == entityId,
                BookReferences.At(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse),
                r.Notes,
                r.Method,
                r.Confidence,
                r.Source)
            {
                Citation = r.Citation,
            })
            .ToListAsync(cancellationToken);

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

        var corroboration = new List<Related>?[rows.Count];
        var absorbed = new bool[rows.Count];

        // A page with only one witness on it still says every fact twice, because each witness
        // writes both ends itself. So the fold below runs whether or not there is a second witness
        // to absorb first.
        if (ours.Count == 0 || witnesses.Count == 0)
        {
            FoldTheOtherEnd(rows, corroboration, absorbed);
            return
            [
                .. rows
                    .Index()
                    .Where(row => !absorbed[row.Index])
                    .Select(row => Show(row.Item, corroboration[row.Index] ?? [])),
            ];
        }

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

        FoldTheOtherEnd(rows, corroboration, absorbed);

        return
        [
            .. rows
                .Index()
                .Where(row => !absorbed[row.Index])
                .Select(row => Show(row.Item, corroboration[row.Index] ?? [])),
        ];
    }

    /// <summary>
    /// One fact said from both ends, folded into the side this page is about.
    ///
    /// <para>
    /// Both witnesses write reciprocals — BibleData has <em>Lot husband of his wife</em> and
    /// <em>his wife wife of Lot</em>, and the descriptor pass writes each side from its own
    /// subject — so a page met every family tie twice: fourteen rows on Lot for seven facts. The
    /// owner's answer is that once a page has said <em>Lot, son of Haran</em> it has said the
    /// thing, and Haran's page is where it is read the other way.
    /// </para>
    ///
    /// <para>
    /// The side that survives is this page's own — <em>Lot, father of Moab</em> rather than
    /// <em>Moab, son of Lot</em> — because the page is about Lot and that is how a reader reads a
    /// genealogy. Where only the other end exists, it stays as it is: what is folded is the
    /// repetition, never the fact.
    /// </para>
    ///
    /// <para>
    /// The folded row travels on the surviving one rather than being dropped, because the two
    /// directions are graded separately and by different witnesses: <em>Moses son of Amram</em> is
    /// inferred where <em>Amram father of Moses</em> is stated, and collapsing them without saying
    /// so would promote one grading or demote the other silently. It renders where a second
    /// witness renders, which is what it is.
    /// </para>
    /// </summary>
    private static void FoldTheOtherEnd(
        IReadOnlyList<Related> rows,
        List<Related>?[] corroboration,
        bool[] absorbed)
    {
        for (var inward = 0; inward < rows.Count; inward++)
        {
            if (absorbed[inward] || !rows[inward].Inward)
            {
                continue;
            }

            for (var outward = 0; outward < rows.Count; outward++)
            {
                if (absorbed[outward] || rows[outward].Inward
                    || !Reciprocal(rows[outward], rows[inward]))
                {
                    continue;
                }

                var kept = corroboration[outward] ??= [];
                kept.Add(rows[inward]);
                if (corroboration[inward] is { } theirs)
                {
                    kept.AddRange(theirs);
                }

                absorbed[inward] = true;
                break;
            }
        }
    }

    /// <summary>
    /// Whether these two rows are one fact read from opposite ends. Each is put into this corpus's
    /// vocabulary first, so BibleData's <c>father</c> and a reading's <c>son-of</c> are compared as
    /// one question rather than as two words.
    /// </summary>
    private static bool Reciprocal(Related outward, Related inward) =>
        outward.From == inward.To
        && outward.To == inward.From
        && InOurWords(outward) is { } said
        && (InOurWords(inward) is { } answered && RelationshipVocabulary.Reversed(said).Contains(answered)
            // A word for the other end states the outward row itself, not its reverse.
            || RelationshipVocabulary.SaysFromTheOtherEnd.GetValueOrDefault(inward.Type) == said);

    /// <summary>
    /// The relation in this corpus's vocabulary: a reading already speaks it, and a witness's word
    /// is looked up. Null where the vocabulary has no word for what the witness said, which is a
    /// row nothing here can pair.
    /// </summary>
    private static string? InOurWords(Related row) =>
        Ours(row) ? row.Type : Says(row);

    /// <summary>
    /// The readings of a witness's row, strongest first: the pair it names, then the pair read
    /// backwards, and only after every row on the page has had those, the looser tie a closer
    /// reading of ours already states.
    /// </summary>
    private static readonly Func<Related, Related, bool>[] Pairings =
        [Restates, RestatesBackwards, RestatesFromTheOtherEnd, NamesOneOf, StatesLoosely, StatesLooselyBackwards];

    /// <summary>
    /// The witness states this very fact in a word for the other end: BibleData's <em>Abram
    /// concubinator Hagar</em> is <em>Hagar, concubine of Abram</em>.
    /// </summary>
    private static bool RestatesFromTheOtherEnd(Related mine, Related witness) =>
        witness.From == mine.To
        && witness.To == mine.From
        && RelationshipVocabulary.SaysFromTheOtherEnd.GetValueOrDefault(witness.Type) == mine.Type;

    /// <summary>
    /// The witness's word is one of several relations and the reading says which: BibleData's
    /// <em>Sisera victim Jael</em> is <em>Sisera, killed by Jael</em>, and from the other end
    /// <em>Jael, killer of Sisera</em>.
    /// </summary>
    private static bool NamesOneOf(Related mine, Related witness) =>
        RelationshipVocabulary.SaysOneOf.GetValueOrDefault(witness.Type) is { } either
        && ((witness.From == mine.From && witness.To == mine.To && either.Contains(mine.Type))
            || (witness.From == mine.To && witness.To == mine.From
                && either.Any(relation => RelationshipVocabulary.Reversed(relation).Contains(mine.Type))));

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
    /// The witness states the wider tie our reading states closely: BibleData's <em>descendant</em>
    /// where the verse was read as <em>son of</em>. The loader wrote ours because the two agree, so
    /// the page shows the closer one with the looser one beside it rather than both as facts.
    /// </summary>
    private static bool StatesLoosely(Related mine, Related witness) =>
        witness.From == mine.From
        && witness.To == mine.To
        && Says(witness) is { } relation
        && RelationshipVocabulary.Implies(mine.Type, relation);

    /// <summary>The same looser tie, stated from the other end: <em>Haran ancestor of Lot</em>.</summary>
    private static bool StatesLooselyBackwards(Related mine, Related witness) =>
        witness.From == mine.To
        && witness.To == mine.From
        && Says(witness) is { } relation
        && RelationshipVocabulary.Reversed(relation).Any(wider => RelationshipVocabulary.Implies(mine.Type, wider));

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
            Kind = EnumSpelling.Of(row.Kind),
            Method = EnumSpelling.Of(row.Method),
            Confidence = row.Confidence,
            Source = row.Source,
            Verses = Verses(row.Citation),
            Corroboration = [.. corroboration.Select(witness => Corroborating(row, witness))],
        };

    /// <summary>Every verse a citation of more than one verse names, in order; null for one verse.</summary>
    internal static List<VerseRefResponse>? Verses(string? citation) =>
        Citation.Parse(citation) is { Single: false } cited
            ? [.. cited.Verses.Select(v => BookReferences.At(v.Book, v.Chapter, v.Verse)!)]
            : null;

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
        EntityKind Kind,
        bool Inward,
        VerseRefResponse? Reference,
        string? Notes,
        LinkMethod Method,
        double? Confidence,
        string Source)
    {
        /// <summary>The passage or the two verses composed, where one verse does not hold the row.</summary>
        public string? Citation { get; init; }
    }
}
