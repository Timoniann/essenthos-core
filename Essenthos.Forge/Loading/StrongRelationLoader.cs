using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

/// <param name="Relations">Rows the sources state, written or found already there.</param>
/// <param name="Unclassified">Of them, references whose words none of the kinds reads.</param>
/// <param name="Profiles">Hebrew entries the compiler profiles, or 0 where his file is not there.</param>
internal sealed record StrongRelationOutcome(
    bool AlreadyLoaded,
    int Relations,
    int Unclassified,
    int Profiles,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the {Relations} relations between Strong's entries and {Profiles} profiles are already loaded"
            : $"{Relations} relations between Strong's entries, {Unclassified} of them in words no kind reads, " +
              $"and {Profiles} profiles of the Hebrew, in {Elapsed}";
}

/// <summary>
/// The relations Strong states between his entries, which the lexicon's own load flattens into the
/// derivation's prose, and the compiler's root lists and profiles of the Hebrew beside them under his
/// own name. A step of its own rather than part of the lexicon's load, which loads once and never
/// again: this one reads the files on every load and writes a table only where the reading changed,
/// each in one transaction, so a corpus loaded before it existed reaches it without the lexicon and
/// everything keyed on it being reloaded.
/// </summary>
internal sealed class StrongRelationLoader(AppDbContext db, ILogger<StrongRelationLoader> logger)
{
    public const string HebrewSource = StrongGentilicLoader.Source;

    public const string GreekSource = "Strong's Greek dictionary by James Strong, 1890, public domain";

    public async Task<StrongRelationOutcome> Load(
        string hebrewPath,
        string greekPath,
        string? compiledPath,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var parser = new StrongXmlParser();
        List<StrongParsedEntry> entries =
        [
            .. parser.ParseHebrew(await File.ReadAllTextAsync(hebrewPath, cancellationToken)),
            .. parser.ParseGreek(await File.ReadAllTextAsync(greekPath, cancellationToken)),
        ];

        var stated = StrongRelationReading.Of(entries)
            .Select(r => Row(r, r.FromNumber.StartsWith(StrongNumbers.Greek) ? GreekSource : HebrewSource))
            .ToList();

        // The compiler's file is optional: a corpus without it holds Strong's reading alone, and says so.
        var compiled = compiledPath is not null && File.Exists(compiledPath) ? CompiledHebrewStrongs.Read(compiledPath) : null;
        if (compiled is { UnreadFirstVerses: > 0 } unread)
        {
            logger.LogWarning("{Unread} first verses in the compiler's file name no book this corpus knows", unread.UnreadFirstVerses);
        }

        stated.AddRange((compiled?.Roots ?? []).Select(r => Row(r, CompiledHebrewStrongs.Source)));
        var profiles = compiled?.Profiles ?? [];

        var unclassified = stated.Count(r => r.Kind == StrongRelationKinds.Unclassified);
        var relationsHeld = await RelationsHold(stated, cancellationToken);
        var profilesHeld = await ProfilesHold(profiles, cancellationToken);
        if (relationsHeld && profilesHeld)
        {
            logger.LogInformation("The relations between Strong's entries are already loaded; nothing to do");
            return new StrongRelationOutcome(true, stated.Count, unclassified, profiles.Count, started.Elapsed);
        }

        await using (var transaction = db.Database.CurrentTransaction is null
                         ? await db.Database.BeginTransactionAsync(cancellationToken)
                         : null)
        {
            if (!relationsHeld)
            {
                await db.StrongRelations.ExecuteDeleteAsync(cancellationToken);
                db.StrongRelations.AddRange(stated);
            }

            if (!profilesHeld)
            {
                await db.StrongProfiles.ExecuteDeleteAsync(cancellationToken);
                db.StrongProfiles.AddRange(profiles);
            }

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        db.ChangeTracker.Clear();

        var outcome = new StrongRelationOutcome(false, stated.Count, unclassified, profiles.Count, started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    private static StrongRelation Row(StatedRelation relation, string source) => new()
    {
        FromNumber = relation.FromNumber,
        ToNumber = relation.ToNumber,
        Kind = relation.Kind,
        Hedged = relation.Hedged,
        Position = relation.Position,
        Statement = relation.Statement,
        Source = source,
    };

    private async Task<bool> RelationsHold(IReadOnlyCollection<StrongRelation> read, CancellationToken cancellationToken)
    {
        var held = await db.StrongRelations
            .AsNoTracking()
            .Select(r => new { r.FromNumber, r.ToNumber, r.Kind, r.Hedged, r.Position, r.Statement, r.Source })
            .ToListAsync(cancellationToken);
        return held.Count == read.Count
               && held.Select(r => (r.FromNumber, r.ToNumber, r.Kind, r.Hedged, r.Position, r.Statement, r.Source)).ToHashSet()
                   .SetEquals(read.Select(r => (r.FromNumber, r.ToNumber, r.Kind, r.Hedged, r.Position, r.Statement, r.Source)));
    }

    private async Task<bool> ProfilesHold(IReadOnlyCollection<StrongProfile> read, CancellationToken cancellationToken)
    {
        var held = await db.StrongProfiles.AsNoTracking().ToListAsync(cancellationToken);
        static object Key(StrongProfile p) =>
            (p.StrongNumber, p.Language, p.PartOfSpeech, p.Gender, p.Occurrences, p.FirstBook, p.FirstChapter, p.FirstVerse, p.Source);
        return held.Count == read.Count && held.Select(Key).ToHashSet().SetEquals(read.Select(Key));
    }
}
