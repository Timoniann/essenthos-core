using Essenthos.Core.Corpus;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Withheld">
/// Clauses a claim of higher standing about the same pair answers differently. Counted rather than
/// written, because a page showing both would be the corpus contradicting itself in public.
/// </param>
/// <param name="Disputed">
/// Pairs where two claims of equal standing and equal confidence answer the same question two ways,
/// so nothing is written for them at all.
/// </param>
/// <param name="Held">Clauses <see cref="OwnRelationshipLoader.WithheldClauses"/> lists, left unwritten.</param>
/// <param name="Withdrawn">
/// Relationships no clause states any more, or that said again what another said: a clause read
/// again, a record folded or a verse put right since they were written.
/// </param>
internal sealed record OwnRelationshipOutcome(
    bool AlreadyLoaded,
    int Entities,
    int Written,
    int Withheld,
    int Disputed,
    TimeSpan Elapsed,
    int Held = 0,
    int Withdrawn = 0)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the relationships this corpus reads for itself already follow its clauses"
            : $"{Written} relationships over {Entities} entities written and {Withdrawn} no clause states "
              + $"withdrawn, read off the descriptor clauses in {Elapsed}. {Withheld} clauses were withheld "
              + $"because a claim of higher standing answers the same question differently, {Disputed} "
              + $"pairs are disputed between claims of equal standing, and {Held} clauses are on the list "
              + "of readings held back";
}

/// <summary>A reading the list holds back: a clause by the slugs of its two ends and its relation.</summary>
/// <param name="Reference">The verse the clause was read from, for whoever weighs the entry.</param>
/// <param name="Why">What stood against the reading, and what this corpus holds for the pair.</param>
internal sealed record WithheldClause(string Entity, string Relation, string Target, string? Reference, string? Why);

/// <param name="DecidedBy">Whose decision the list rests on, and when.</param>
/// <param name="Policy">What being on the list means, and how an entry leaves it.</param>
internal sealed record WithheldClauseList(string DecidedBy, string Policy, IReadOnlyList<WithheldClause> Clauses);

/// <summary>
/// The relationships an entity page draws, read off the clauses this corpus wrote for itself.
///
/// A descriptor clause already <em>is</em> a relationship — an entity, a relation from a closed
/// vocabulary, a target the encyclopedia holds, and the verse it was read from — and the only thing
/// missing was that it reached the table the page draws. So this generates nothing: it is one pass
/// over <see cref="EntityDescriptor"/>, and every row it writes names the model that read the verse
/// or the person who decided it.
///
/// <para>
/// **Where two clauses about one pair disagree, the rule is <see cref="Endpoints.Annotations"/>'s
/// and not a second one.** For each ordered pair, every clause about it is ranked by claim standing
/// and then by confidence — a decision of the owner's above a model's reading — and what the
/// strongest rank asserts is what the pair stands in. A clause is written unless it answers the
/// same question differently, and nothing is written for a pair where two clauses of equal standing
/// and equal confidence answer it two ways. That is <see cref="Endpoints.Annotations"/>'s
/// <c>Settle</c> and <c>Disputed</c> with one change the domain forces: a word names one entity,
/// and a pair of people stand in as many relations as they stand in, so the answer is a set and
/// <see cref="RelationshipVocabulary.Branch"/> says which relations are answers to the same
/// question.
/// </para>
///
/// <para>
/// **A pair is read from its own subject's end only.** The two sides of one pair were written by one
/// pass in one ordered answer: Lot is a descendant of Terah and Terah is Lot's grandfather, and
/// setting those against each other would be a witness disputing itself.
/// </para>
///
/// <para>
/// **Some readings are held back by name** (<see cref="WithheldClauses"/>). Until 2026-09-30 a
/// dataset's rows stood in this table as a second witness and outranked a model's reading, so a
/// clause that answered a pair otherwise than the dataset did was never written. The dataset's rows
/// are gone, on the owner's decision that every relationship is this project's own, and those
/// clauses are listed so that what a page says did not change the day they left. An entry taken
/// off the list is written on the next load that reads its record.
/// </para>
///
/// <para>
/// **The rows follow the clauses.** Every load settles every clause again and compares the answer
/// with the rows this loader wrote: a row the answer holds stays as it is, a row it lacks is written,
/// and a row no clause states any more is withdrawn. A pass asked again replaces its clauses, a fold
/// takes the clauses of a record of another kind away and says a repeated clause once, and a verse
/// put right moves the clause to it; a row left behind by any of them would put on a page a reading
/// the corpus no longer holds.
/// </para>
///
/// <para>
/// **Every row names the verse it read.** The database refuses a verseless row from any method but
/// <see cref="LinkMethod.StatedBySource"/>, which nothing here writes.
/// </para>
/// </summary>
internal sealed class OwnRelationshipLoader(AppDbContext db, ILogger<OwnRelationshipLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.WithheldClauses.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<OwnRelationshipOutcome> Load(CancellationToken cancellationToken = default) =>
        await Load(WithheldClauses().Clauses, cancellationToken);

    internal async Task<OwnRelationshipOutcome> Load(
        IReadOnlyList<WithheldClause> heldByName,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();

        var read = await Clauses(cancellationToken);
        var named = heldByName.Select(c => (c.Entity, c.Relation, c.Target)).ToHashSet();
        var clauses = read
            .Where(c => !named.Contains((c.Subject, c.Clause.Relation, c.Target)))
            .Select(c => c.Clause)
            .ToList();
        var held = read.Count - clauses.Count;

        var (wanted, withheld, disputed) = Settle(clauses);

        var standing = await db.EntityRelationships
            .Where(r => r.Source.StartsWith(EntityDescriptorLoader.SourcePrefix))
            .ToListAsync(cancellationToken);
        var unmatched = standing
            .GroupBy(Key)
            .ToDictionary(rows => rows.Key, rows => new Queue<EntityRelationship>(rows.OrderBy(r => r.Id)));
        var rows = new List<EntityRelationship>();
        foreach (var row in wanted)
        {
            if (!unmatched.TryGetValue(Key(row), out var same) || !same.TryDequeue(out _))
            {
                rows.Add(row);
            }
        }

        var gone = unmatched.Values.SelectMany(left => left).ToList();
        if (rows.Count == 0 && gone.Count == 0)
        {
            logger.LogInformation(
                "Every relationship this corpus reads for itself already follows its clauses; nothing to do");
            return new OwnRelationshipOutcome(true, 0, 0, withheld, disputed, started.Elapsed, held);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.EntityRelationships.RemoveRange(gone);
        db.EntityRelationships.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new OwnRelationshipOutcome(
            AlreadyLoaded: false,
            Entities: rows.Select(r => r.FromEntityId).Distinct().Count(),
            Written: rows.Count,
            withheld,
            disputed,
            started.Elapsed,
            held,
            gone.Count);

        logger.LogInformation("Read the relationships off the clauses: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>Everything a relationship read off a clause says, so two rows saying the same are one.</summary>
    private static (int, int, string, string, int?, int?, int?, string?, LinkMethod, double?, string, string?) Key(
        EntityRelationship row) =>
        (row.FromEntityId, row.ToEntityId, row.Type, row.Category, row.CanonicalBook, row.CanonicalChapter,
            row.CanonicalVerse, row.Citation, row.Method, row.Confidence, row.Source, row.Notes);

    /// <summary>The readings held back by name, as the list beside this loader states them.</summary>
    internal static WithheldClauseList WithheldClauses()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"The list of readings held back is not in the assembly as {Resource}. Check that "
                + "WithheldClauses.json is an EmbeddedResource of Essenthos.Forge.");
        return JsonSerializer.Deserialize<WithheldClauseList>(stream, Shape)
            ?? throw new InvalidOperationException(
                $"{Resource} is empty. Give it the list, or an object with an empty clauses array.");
    }

    /// <summary>
    /// The pick, over one pair at a time. Public to the test so the rule can be asked directly
    /// rather than only through a database.
    /// </summary>
    internal static (List<EntityRelationship> Rows, int Withheld, int Disputed) Settle(
        IReadOnlyCollection<Clause> clauses)
    {
        var rows = new List<EntityRelationship>(clauses.Count);
        var written = new HashSet<(int From, int To, string Relation)>();
        int withheld = 0, disputed = 0;

        foreach (var pair in clauses.Where(c => c.From != c.To).GroupBy(c => (c.From, c.To)))
        {
            var candidates = pair
                .Select(c => new Asserted(ClaimStanding.Of(c.Method), c.Confidence ?? 1, c.Relation))
                .ToList();
            var best = candidates.Max(c => (c.Standing, c.Confidence));
            var top = candidates.Where(c => (c.Standing, c.Confidence) == best).ToList();

            if (Disputed(top))
            {
                disputed++;
                withheld += pair.Count();
                continue;
            }

            var settled = top.Select(c => c.Relation).ToHashSet(StringComparer.Ordinal);
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
            one.Relation != other.Relation
            && RelationshipVocabulary.Branch(one.Relation) == RelationshipVocabulary.Branch(other.Relation)));

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

    /// <summary>Each clause with the slugs of its two ends, which is how a reading held back is named.</summary>
    private async Task<List<(Clause Clause, string Subject, string Target)>> Clauses(
        CancellationToken cancellationToken)
    {
        var rows = await db.EntityDescriptors
            .Select(d => new
            {
                d.EntityId, d.TargetEntityId, d.Relation,
                d.CanonicalBook, d.CanonicalChapter, d.CanonicalVerse,
                d.Method, d.Confidence, d.Source, d.Note, d.Citation,
                Subject = d.Entity!.Slug,
                Target = d.Target!.Slug,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(d => (
                new Clause(
                    d.EntityId, d.TargetEntityId, d.Relation,
                    d.CanonicalBook, d.CanonicalChapter, d.CanonicalVerse,
                    d.Method, d.Confidence, d.Source, d.Note)
                {
                    Citation = d.Citation,
                },
                d.Subject,
                d.Target)),
        ];
    }

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

    /// <summary>One clause about one pair, as the pick sees it: how much its method knew, how sure it is, and what it says.</summary>
    private sealed record Asserted(int Standing, double Confidence, string Relation);
}
