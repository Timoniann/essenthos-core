using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Strong;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

/// <param name="Relations">Rows the sources state, written or found already there.</param>
/// <param name="Unclassified">Of them, references whose words none of the kinds reads.</param>
internal sealed record StrongRelationOutcome(bool AlreadyLoaded, int Relations, int Unclassified, TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the {Relations} relations between Strong's entries are already loaded"
            : $"{Relations} relations between Strong's entries in {Elapsed}, {Unclassified} of them in words no kind reads";
}

/// <summary>
/// The relations Strong states between his entries, which the lexicon's own load flattens into the
/// derivation's prose. A step of its own rather than part of that load, which loads once and never
/// again: this one is read from the files on every load and written only where the reading changed,
/// in one transaction, so a corpus loaded before it existed reaches it without the lexicon and
/// everything keyed on it being reloaded.
/// </summary>
internal sealed class StrongRelationLoader(AppDbContext db, ILogger<StrongRelationLoader> logger)
{
    public const string HebrewSource = StrongGentilicLoader.Source;

    public const string GreekSource = "Strong's Greek dictionary by James Strong, 1890, public domain";

    public async Task<StrongRelationOutcome> Load(
        string hebrewPath,
        string greekPath,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var parser = new StrongXmlParser();
        List<StrongParsedEntry> entries =
        [
            .. parser.ParseHebrew(await File.ReadAllTextAsync(hebrewPath, cancellationToken)),
            .. parser.ParseGreek(await File.ReadAllTextAsync(greekPath, cancellationToken)),
        ];

        var read = StrongRelationReading.Of(entries)
            .Select(r => new StrongRelation
            {
                FromNumber = r.FromNumber,
                ToNumber = r.ToNumber,
                Kind = r.Kind,
                Hedged = r.Hedged,
                Position = r.Position,
                Statement = r.Statement,
                Source = r.FromNumber.StartsWith(StrongNumbers.Greek) ? GreekSource : HebrewSource,
            })
            .ToList();

        var unclassified = read.Count(r => r.Kind == StrongRelationKinds.Unclassified);
        if (await Holds(read, cancellationToken))
        {
            logger.LogInformation("The relations between Strong's entries are already loaded; nothing to do");
            return new StrongRelationOutcome(true, read.Count, unclassified, started.Elapsed);
        }

        await using (var transaction = db.Database.CurrentTransaction is null
                         ? await db.Database.BeginTransactionAsync(cancellationToken)
                         : null)
        {
            await db.StrongRelations.ExecuteDeleteAsync(cancellationToken);
            db.StrongRelations.AddRange(read);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        db.ChangeTracker.Clear();

        var outcome = new StrongRelationOutcome(false, read.Count, unclassified, started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    private async Task<bool> Holds(IReadOnlyCollection<StrongRelation> read, CancellationToken cancellationToken)
    {
        var held = await db.StrongRelations
            .AsNoTracking()
            .Select(r => new { r.FromNumber, r.ToNumber, r.Kind, r.Hedged, r.Position, r.Statement, r.Source })
            .ToListAsync(cancellationToken);
        return held.Count == read.Count
               && held.Select(r => (r.FromNumber, r.ToNumber, r.Kind, r.Hedged, r.Position, r.Statement, r.Source)).ToHashSet()
                   .SetEquals(read.Select(r => (r.FromNumber, r.ToNumber, r.Kind, r.Hedged, r.Position, r.Statement, r.Source)));
    }
}
