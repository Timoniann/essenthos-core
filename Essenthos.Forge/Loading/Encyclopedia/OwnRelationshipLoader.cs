using Essenthos.Core.Corpus;
using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Withheld">
/// Clauses a witness of higher standing answers differently. Counted rather than written, because a
/// page showing both would be the corpus contradicting itself in public.
/// </param>
/// <param name="Disputed">
/// Pairs where two claims of equal standing and equal confidence answer the same question two ways,
/// so nothing of ours is written for them at all.
/// </param>
internal sealed record OwnRelationshipOutcome(
    bool AlreadyLoaded,
    int Entities,
    int Written,
    int Withheld,
    int Disputed,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the relationships this corpus reads for itself are already there"
            : $"{Written} relationships over {Entities} entities, read off the descriptor clauses "
              + $"in {Elapsed}. {Withheld} clauses were withheld because a witness of higher "
              + $"standing answers the same question differently, and {Disputed} pairs are disputed "
              + "between claims of equal standing";
}

/// <summary>
/// The relationships an entity page draws, read off the clauses this corpus wrote for itself rather
/// than taken from a dataset's edge list.
///
/// Every relationship a reader is shown is BibleData's, and none of them needs to be. A descriptor
/// clause already <em>is</em> a relationship — an entity, a relation from a closed vocabulary, a
/// target the encyclopedia holds, and the verse it was read from — and the only thing missing was
/// that it reached the table the page draws. So this generates nothing: it is one pass over
/// <see cref="EntityDescriptor"/>, and the claims it writes were made by a model that never saw
/// BibleData's sentence.
///
/// <para>
/// **BibleData's rows stay**, for the reason they stayed in <see cref="OwnReferenceLoader"/>: they
/// are the second witness ours are measured against, they reach entities and relations ours does
/// not, and the corpus has already lost 14,515 rows once to a pass that thought it could rebuild
/// them. Nothing here deletes or rewrites a row it did not write.
/// </para>
///
/// <para>
/// **Where the two disagree, the rule is <see cref="Endpoints.Annotations"/>'s and not a second
/// one.** For each ordered pair, every claim about it is ranked by claim standing and then by
/// confidence — BibleData's rows as testimony, ours as a model reading — and what the strongest
/// rank asserts is what the pair stands in. A clause of ours is written unless it answers the same
/// question differently, and nothing of ours is written for a pair where two claims of equal
/// standing and equal confidence answer it two ways. That is
/// <see cref="Endpoints.Annotations"/>'s <c>Settle</c> and <c>Disputed</c> with one change the
/// domain forces: a word names one entity, and a pair of people stand in as many relations as they
/// stand in, so the answer is a set and <see cref="RelationshipVocabulary.Branch"/> says which
/// relations are answers to the same question.
/// </para>
///
/// <para>
/// **A witness's rows are read from both ends and our own clauses only from their own.** BibleData
/// states <em>Bani is the ancestor of Adaiah</em> and the encyclopedia answers it on Adaiah's page,
/// so a row of a witness has to reach the pair read backwards or 463 corroborations read as
/// disagreements. Our own clauses do not, because the two sides of one pair were written by one
/// pass in one ordered answer: Lot is a descendant of Terah and Terah is Lot's grandfather, and
/// setting those against each other would be a witness disputing itself.
/// </para>
///
/// <para>
/// **A witness of ours always names the verse it read.** BibleData states 40 rows with no reference
/// and 14 of those it calls <c>explicit</c>, which is a fact about that dataset and not a licence to
/// write a citation nobody can follow. The asymmetry is kept rather than flattened: the column
/// stays nullable because a witness may honestly have given none, and the database refuses a
/// verseless row from any method but <see cref="LinkMethod.StatedBySource"/>.
/// </para>
/// </summary>
internal sealed class OwnRelationshipLoader(AppDbContext db, ILogger<OwnRelationshipLoader> logger)
{
    public async Task<OwnRelationshipOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var already = await Described(cancellationToken);
        var clauses = await Clauses(already, cancellationToken);
        if (clauses.Count == 0)
        {
            logger.LogInformation(
                "Every entity with a descriptor clause already has its relationships; nothing to do");
            return new OwnRelationshipOutcome(true, 0, 0, 0, 0, started.Elapsed);
        }

        var stated = await Stated(cancellationToken);
        var (rows, withheld, disputed) = Settle(clauses, stated);

        db.EntityRelationships.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = new OwnRelationshipOutcome(
            AlreadyLoaded: false,
            Entities: rows.Select(r => r.FromEntityId).Distinct().Count(),
            Written: rows.Count,
            withheld,
            disputed,
            started.Elapsed);

        logger.LogInformation("Read the relationships off the clauses: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The pick, over one pair at a time. Public to the test so the rule can be asked directly
    /// rather than only through a database.
    /// </summary>
    internal static (List<EntityRelationship> Rows, int Withheld, int Disputed) Settle(
        IReadOnlyCollection<Clause> clauses,
        ILookup<(int From, int To), Asserted> stated)
    {
        var rows = new List<EntityRelationship>(clauses.Count);
        var written = new HashSet<(int From, int To, string Relation)>();
        int withheld = 0, disputed = 0;

        foreach (var pair in clauses.GroupBy(c => (c.From, c.To)))
        {
            var mine = pair
                .Select(c => new Asserted(ClaimStanding.Of(c.Method), c.Confidence ?? 1, One(c.Relation)))
                .ToList();

            var candidates = stated[pair.Key].Concat(mine).ToList();
            var best = candidates.Max(c => (c.Standing, c.Confidence));
            var top = candidates.Where(c => (c.Standing, c.Confidence) == best).ToList();

            if (Disputed(top))
            {
                disputed++;
                withheld += pair.Count();
                continue;
            }

            var settled = top.SelectMany(c => c.Relations).ToHashSet(StringComparer.Ordinal);
            var standing = pair
                .Where(clause => !RelationshipVocabulary.Contradicts(clause.Relation, settled))
                .ToList();
            withheld += pair.Count() - standing.Count;

            foreach (var clause in standing.OrderByDescending(c => c.Confidence ?? 1))
            {
                // Son of Haran already says descendant of Haran; a page saying both says it twice.
                if (standing.Any(closer =>
                        RelationshipVocabulary.Implies(closer.Relation, clause.Relation)
                        && (closer.Confidence ?? 1) >= (clause.Confidence ?? 1)))
                {
                    continue;
                }

                if (!written.Add((clause.From, clause.To, clause.Relation)))
                {
                    continue;
                }

                rows.Add(Row(clause));
            }
        }

        return (rows, withheld, disputed);
    }

    /// <summary>
    /// Whether the strongest rank answers one question two ways. Two claims that share no relation
    /// and whose relations are answers to the same question are the case
    /// <see cref="Endpoints.Annotations"/> shows nothing for, and this is that test at the pair.
    /// </summary>
    private static bool Disputed(List<Asserted> top) =>
        top.Any(one => top.Any(other =>
            !ReferenceEquals(one, other)
            && !one.Relations.Overlaps(other.Relations)
            && one.Relations.Any(mine => other.Relations.Any(theirs =>
                RelationshipVocabulary.Branch(mine) == RelationshipVocabulary.Branch(theirs)))));

    private static EntityRelationship Row(Clause clause) =>
        new()
        {
            FromEntityId = clause.From,
            ToEntityId = clause.To,
            Type = clause.Relation,
            Category = RelationshipCategories.Read,
            CanonicalBook = clause.Book,
            CanonicalChapter = clause.Chapter,
            CanonicalVerse = clause.Verse,
            Citation = clause.Citation,
            Method = clause.Method,
            Confidence = clause.Confidence,
            Source = clause.Source,
            Notes = clause.Note,
        };

    /// <summary>
    /// The entities this loader has already written for. On its own rows and per entity, because
    /// the descriptor passes arrive in batches over days and a second batch has to load beside the
    /// first — and because a guard on the table would find BibleData's 5,448 rows and conclude the
    /// work was done.
    /// </summary>
    private async Task<HashSet<int>> Described(CancellationToken cancellationToken) =>
        [.. await db.EntityRelationships
            .Where(r => r.Source.StartsWith(EntityDescriptorLoader.SourcePrefix))
            .Select(r => r.FromEntityId)
            .Distinct()
            .ToListAsync(cancellationToken)];

    private async Task<List<Clause>> Clauses(
        HashSet<int> already,
        CancellationToken cancellationToken) =>
        await db.EntityDescriptors
            .Where(d => !already.Contains(d.EntityId))
            .Select(d => new Clause(
                d.EntityId, d.TargetEntityId, d.Relation,
                d.CanonicalBook, d.CanonicalChapter, d.CanonicalVerse,
                d.Method, d.Confidence, d.Source, d.Note)
            {
                Citation = d.Citation,
            })
            .ToListAsync(cancellationToken);

    /// <summary>
    /// What every other witness already asserts, per ordered pair — each row on the pair it names
    /// and again, inverted, on the pair read from the other end, because a clause is written from
    /// its own subject's side and <em>Bani is the ancestor of Adaiah</em> is answered on Adaiah's
    /// page. A type the vocabulary has no word for asserts nothing here: it can neither corroborate
    /// a clause nor contradict one, and counting it as a disagreement would make the encyclopedia
    /// silent about a pair over a word it does not have.
    /// </summary>
    private async Task<ILookup<(int From, int To), Asserted>> Stated(CancellationToken cancellationToken)
    {
        var rows = await db.EntityRelationships
            .Select(r => new { r.FromEntityId, r.ToEntityId, r.Type, r.Method, r.Confidence })
            .ToListAsync(cancellationToken);

        return rows
            .SelectMany(row =>
            {
                if (RelationshipVocabulary.SaysFromTheOtherEnd.TryGetValue(row.Type, out var otherEnd))
                {
                    return new[]
                    {
                        ((row.ToEntityId, row.FromEntityId),
                            new Asserted(ClaimStanding.Of(row.Method), row.Confidence ?? 1, One(otherEnd))),
                    };
                }

                if (!RelationshipVocabulary.Says.TryGetValue(row.Type, out var relation))
                {
                    return Enumerable.Empty<(( int From, int To) Pair, Asserted Claim)>();
                }

                var standing = ClaimStanding.Of(row.Method);
                var confidence = row.Confidence ?? 1;
                var forward = ((row.FromEntityId, row.ToEntityId),
                    new Asserted(standing, confidence, One(relation)));

                var reversed = RelationshipVocabulary.Reversed(relation);
                return reversed.Count == 0
                    ? [forward]
                    : new[]
                    {
                        forward,
                        ((row.ToEntityId, row.FromEntityId),
                            new Asserted(standing, confidence, reversed)),
                    };
            })
            .ToLookup(entry => entry.Item1, entry => entry.Item2);
    }

    private static IReadOnlySet<string> One(string relation) =>
        new HashSet<string>(StringComparer.Ordinal) { relation };

    /// <summary>One descriptor clause, flattened for the pick.</summary>
    internal sealed record Clause(
        int From,
        int To,
        string Relation,
        int Book,
        int Chapter,
        int Verse,
        LinkMethod Method,
        double? Confidence,
        string Source,
        string? Note)
    {
        /// <summary>The passage or the two verses composed, where one verse does not hold the clause.</summary>
        public string? Citation { get; init; }
    }

    /// <summary>
    /// One claim about one pair, as the pick sees it: how much its method knew, how sure it is, and
    /// what it says. The relations are a set because a claim read from the other end is often two —
    /// <em>father of</em> reversed is <em>son of</em> or <em>daughter of</em>, and the witness does
    /// not say which.
    /// </summary>
    internal sealed record Asserted(int Standing, double Confidence, IReadOnlySet<string> Relations);
}
