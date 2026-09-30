using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;

using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>
/// One fact, one row, however many ends state it.
///
/// Every relationship in the table is this project's own: read from a verse by a model, decided by
/// the owner, or written with a record of ours. The descriptor pass writes each side of a tie from
/// its own subject — <em>Lot, son of Haran</em> on Lot and <em>Haran, father of Lot</em> on Haran —
/// so a page projected row by row met the reader with every family tie twice, and what this does is
/// fold the two ends into the one the page is about.
/// </summary>
internal static class Relationships
{
    /// <summary>
    /// One entity's relationships as its page shows them.
    ///
    /// Both directions in one query rather than one each: a father is not recorded twice, so
    /// reading only one side would give Isaac a father and no sons — and a tie is often stated
    /// again on the pair read backwards, which is in the other direction and has to be in hand
    /// before either row is shown.
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
    /// The rows of one page, a tie said from both ends folded into one, in the order they were
    /// read — outward before inward.
    /// </summary>
    public static List<EntityRelationshipResponse> Merged(IReadOnlyList<Related> rows)
    {
        var corroboration = new List<Related>?[rows.Count];
        var absorbed = new bool[rows.Count];
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
    /// The descriptor pass writes each side from its own subject, so a page met every family tie
    /// twice: fourteen rows on Lot for seven facts. The owner's answer is that once a page has
    /// said <em>Lot, son of Haran</em> it has said the thing, and Haran's page is where it is
    /// read the other way.
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
    /// ends are read separately, often from different verses and with different confidence, and
    /// one may be the owner's decision where the other is a model's reading: collapsing them
    /// without saying so would promote one or demote the other silently.
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

    /// <summary>Whether these two rows are one fact read from opposite ends.</summary>
    private static bool Reciprocal(Related outward, Related inward) =>
        outward.From == inward.To
        && outward.To == inward.From
        && RelationshipVocabulary.Reversed(outward.Type).Contains(inward.Type);

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
