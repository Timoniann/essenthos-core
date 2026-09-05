using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

/// <param name="Resolved">
/// Claims whose origin reaches exactly one entity of the kind Strong stated, so a reader can click
/// from the people to the person or the place. The rest are true and unclickable.
/// </param>
/// <param name="Refused">
/// Entries that name a gentilic and were not turned into a claim. This is the number worth reading:
/// it is what the parse declined rather than guessed, and a refusal here costs a hover card while a
/// guess would cost the reader a false ancestor.
/// </param>
internal sealed record GentilicOutcome(
    bool AlreadyLoaded,
    int Claims,
    int Resolved,
    int Refused,
    int Words,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the stated gentilics are already loaded"
            : $"{Claims} gentilics Strong states an origin for, {Resolved} of them reaching one entity, " +
              $"over {Words} words of the corpus; {Refused} entries name a gentilic and were refused, in {Elapsed}";
}

/// <summary>
/// One name in the encyclopedia that carries a Hebrew Strong number, and what kind of thing it
/// names. The whole input to the resolution, so the rule can be checked without a database.
/// </summary>
internal sealed record NamedEntity(string Number, int EntityId, EntityKind Kind);

/// <summary>
/// The kinship Strong already states, made queryable.
///
/// Nothing here is derived, aligned or guessed: every row is one clause of the dictionary read back
/// out of prose, kept in his words, and refused wherever the prose stops short of naming an origin.
/// The reason to want it is that the corpus has no other route to it — BHSA classifies a lemma as a
/// name and stops there, and the encyclopedia knows persons and places and no peoples at all — so
/// <em>the Moabites descend from Moab</em> is a sentence this corpus could not say until the
/// dictionary was read.
///
/// <para>
/// It runs after the encyclopedia because the far end of each claim is resolved against the names
/// the encyclopedia carries, and after the lexicon because the claims are in it. Both are separate
/// loads with their own guards, so this one is guarded on its own rows.
/// </para>
/// </summary>
internal sealed class StrongGentilicLoader(AppDbContext db, ILogger<StrongGentilicLoader> logger)
{
    private const string Source = "Strong's Hebrew dictionary by James Strong, 1890, public domain";

    public async Task<GentilicOutcome> Load(CancellationToken cancellationToken = default)
    {
        if (await db.StrongGentilics.AnyAsync(cancellationToken))
        {
            logger.LogInformation("The stated gentilics are already loaded; nothing to do");
            return new GentilicOutcome(true, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();

        var entries = await db.StrongEntries
            .Where(e => e.Derivation != null && e.Derivation.Length > 0)
            .Select(e => new { e.StrongNumber, e.Derivation })
            .ToListAsync(cancellationToken);

        var refusals = new Dictionary<GentilicRefusal, int>();
        var stated = new List<StatedGentilic>();

        foreach (var entry in entries)
        {
            if (GentilicDerivations.Read(entry.StrongNumber, entry.Derivation, out var refusal) is { } claim)
            {
                stated.Add(claim);
                continue;
            }

            if (refusal is not GentilicRefusal.NotAGentilic)
            {
                refusals[refusal] = refusals.GetValueOrDefault(refusal) + 1;
            }
        }

        var origins = await Origins(stated, cancellationToken);
        var rows = stated
            .Select(claim => new StrongGentilic
            {
                StrongNumber = claim.StrongNumber,
                OriginNumber = claim.OriginNumber,
                Kind = claim.Kind,
                Statement = claim.Statement,
                OriginEntityId = origins.TryGetValue((claim.OriginNumber, claim.Kind), out var entity)
                    ? entity
                    : null,
                Source = Source,
            })
            .ToList();

        db.StrongGentilics.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (refusal, count) in refusals.OrderByDescending(r => r.Value))
        {
            logger.LogInformation(
                "{Count} Strong entries name a gentilic and state no origin this can read: {Refusal}",
                count, refusal);
        }

        var outcome = new GentilicOutcome(
            false,
            rows.Count,
            rows.Count(row => row.OriginEntityId is not null),
            refusals.Values.Sum(),
            await Words(stated, cancellationToken),
            started.Elapsed);

        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The entity each origin reaches, keyed by the number and the kind of claim together, because
    /// the kind is what settles the commonest ambiguity in the encyclopedia. H4124 is two entities,
    /// the man Moab and the land of Moab, and a dictionary that says <em>patronymical</em> has said
    /// which of them a Moabite descends from.
    ///
    /// Anything less clear than that resolves to nothing: several persons of one name, no entity at
    /// all, or a claim Strong left between the two kinds. A name whose Strong column holds a list
    /// is skipped outright — those are titles, several words and several numbers, and a title is
    /// not what a gentilic is named after.
    /// </summary>
    private async Task<Dictionary<(string Number, string Kind), int>> Origins(
        IReadOnlyCollection<StatedGentilic> stated,
        CancellationToken cancellationToken)
    {
        var wanted = stated.Select(claim => claim.OriginNumber).Distinct().ToList();

        var candidates = await db.EntityNames
            .Where(name => name.HebrewStrongNumber != null
                           && wanted.Contains(name.HebrewStrongNumber)
                           && !name.HebrewStrongNumber.Contains(","))
            .Select(name => new NamedEntity(name.HebrewStrongNumber!, name.EntityId, name.Entity!.Kind))
            .Distinct()
            .ToListAsync(cancellationToken);

        return Resolve(candidates);
    }

    /// <summary>
    /// The rule itself, over names already fetched: for each origin number, the one person a
    /// patronymic can mean and the one place a patrial can mean. Anything with a rival is left out
    /// of the dictionary entirely, which is how the caller ends up writing null.
    /// </summary>
    public static Dictionary<(string Number, string Kind), int> Resolve(
        IReadOnlyCollection<NamedEntity> candidates)
    {
        var resolved = new Dictionary<(string, string), int>();

        foreach (var group in candidates.GroupBy(candidate => candidate.Number))
        {
            Only(group, EntityKind.Person, GentilicKinds.Patronymic, resolved);
            Only(group, EntityKind.Place, GentilicKinds.Patrial, resolved);
        }

        return resolved;

        static void Only(
            IGrouping<string, NamedEntity> group,
            EntityKind kind,
            string claimed,
            Dictionary<(string, string), int> into)
        {
            var matching = group
                .Where(candidate => candidate.Kind == kind)
                .Select(candidate => candidate.EntityId)
                .Distinct()
                .ToList();

            if (matching.Count == 1)
            {
                into[(group.Key, claimed)] = matching[0];
            }
        }
    }

    /// <summary>
    /// How many words of the corpus a claim now reaches. Only BHSA tags these numbers directly, but
    /// every text linked to it inherits them through the links that already exist, which is what
    /// makes this a Ukrainian reader's hover card and not a Hebraist's footnote.
    /// </summary>
    private async Task<int> Words(IReadOnlyCollection<StatedGentilic> stated, CancellationToken cancellationToken)
    {
        var numbers = stated.Select(claim => claim.StrongNumber).Distinct().ToList();
        return await db.Words.CountAsync(word => numbers.Contains(word.StrongNumber!), cancellationToken);
    }
}
