using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Withdrawn">Records removed on this run, which is all of them on a cold corpus and none after.</param>
/// <param name="Kept">
/// Records the list names that carry something of ours the owner has not ruled on, or that an event or a
/// period stands on, left where they are and named in the log.
/// </param>
/// <param name="RuledOut">
/// The withdrawn records that carried something of ours, which went with them on the owner's ruling and
/// are named with what went.
/// </param>
internal sealed record WithdrawnRecordOutcome(
    int Withdrawn,
    IReadOnlyList<string> Kept,
    TimeSpan Elapsed,
    IReadOnlyList<string>? RuledOut = null)
{
    public override string ToString() =>
        Withdrawn == 0 && Kept.Count == 0
            ? "the records a dataset filed under a word that is no name are already withdrawn"
            : $"{Withdrawn} records a dataset filed under a word that is no name withdrawn" +
              (RuledOut is not { Count: > 0 }
                  ? ""
                  : $", {RuledOut.Count} of them with what this corpus held on them, as the owner ruled ({string.Join("; ", RuledOut)})") +
              (Kept.Count == 0 ? "" : $", {Kept.Count} kept for what this corpus holds on them ({string.Join(", ", Kept)})") +
              $", in {Elapsed}";
}

/// <summary>
/// The records a dataset filed under a word that is nobody's name — <em>waters</em>, <em>a despicable
/// person</em> — withdrawn on the owner's decision that the encyclopedia is this project's.
///
/// <para>
/// **Only a dataset's record, and only one nothing of ours stands on.** The list names a record by
/// its slug and by the dataset's own id for it, and a slug that has since come to mean another
/// record is not touched. A record this corpus annotated a word to, cited a verse for, described,
/// tied to another, pictured or dated an event by is left however the list names it, and the outcome
/// names it: withdrawing it would take our own rows with it, and that is a decision about those
/// rows and not about the heading; the list says so where the owner has decided it.
/// </para>
///
/// <para>
/// **Unless the owner has ruled the record out whatever stands on it.** A record the list marks
/// <see cref="WithdrawnRecord.OwnerRuled"/> goes with everything this corpus holds on it — the lines,
/// ties, names, verses and annotated words, which the database removes with the record, so nothing is
/// left pointing at it. The one thing that stops it is an event or a period dated on it: those are
/// the dataset's timeline, which the record does not own, and the outcome names it.
/// </para>
///
/// <para>
/// What the dataset says of the record stays in its files on disk. Idempotent: a record already
/// gone is not looked for again.
/// </para>
/// </summary>
internal sealed class WithdrawnRecordLoader(AppDbContext db, ILogger<WithdrawnRecordLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.WithdrawnRecords.json";

    private const string TimelineKind = "events or periods";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<WithdrawnRecordOutcome> Load(CancellationToken cancellationToken = default) =>
        await Load(Read(), cancellationToken);

    internal async Task<WithdrawnRecordOutcome> Load(
        IReadOnlyList<WithdrawnRecord> records,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var slugs = records.Select(record => record.Slug).ToList();
        var held = await db.Entities.Where(entity => slugs.Contains(entity.Slug)).ToListAsync(cancellationToken);

        var withdrawn = 0;
        var kept = new List<string>();
        var ruledOut = new List<string>();
        foreach (var record in records)
        {
            var entity = held.FirstOrDefault(e => e.Slug == record.Slug && e.SourceId == record.SourceId);
            if (entity is null || Datasets.Of(entity.Source) == Datasets.Own)
            {
                continue;
            }

            var dataset = Datasets.Of(entity.Source);
            var ours = await Ours(entity.Id, dataset, cancellationToken);
            var standing = record.OwnerRuled is null
                ? ours
                : ours.Where(kind => kind == TimelineKind).ToList();
            if (standing.Count > 0)
            {
                logger.LogWarning(
                    "{Slug} is listed as no name and kept: this corpus holds {Ours} on it. Decide what becomes " +
                    "of those before it is withdrawn",
                    record.Slug, string.Join(", ", standing));
                kept.Add(record.Slug);
                continue;
            }

            if (ours.Count > 0)
            {
                ruledOut.Add($"{record.Slug}: {string.Join(", ", ours)}");
            }

            logger.LogInformation("Withdrew {Slug}, a record under a word that is no name: {Why}", record.Slug, record.Why);
            db.Entities.Remove(entity);
            withdrawn++;
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new WithdrawnRecordOutcome(withdrawn, kept, started.Elapsed, ruledOut);
        logger.LogInformation("The records that are no name: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>What this corpus, and not the record's own dataset, holds on the record, by kind.</summary>
    private async Task<List<string>> Ours(int entity, string? dataset, CancellationToken cancellationToken)
    {
        var ours = new List<string>();
        if (await db.WordEntities.AnyAsync(a => a.EntityId == entity, cancellationToken))
        {
            ours.Add("annotated words");
        }

        var verses = await db.EntityVerses.Where(v => v.EntityId == entity).Select(v => v.Source).Distinct()
            .ToListAsync(cancellationToken);
        if (verses.Any(source => Datasets.Of(source) == Datasets.Own))
        {
            ours.Add("verses read off our words");
        }

        if (await db.EntityDescriptors.AnyAsync(d => d.EntityId == entity || d.TargetEntityId == entity, cancellationToken))
        {
            ours.Add("a line under a name");
        }

        var ties = await db.EntityRelationships
            .Where(r => r.FromEntityId == entity || r.ToEntityId == entity)
            .Select(r => r.Source).Distinct().ToListAsync(cancellationToken);
        if (ties.Any(source => Datasets.Of(source) != dataset))
        {
            ours.Add("relationships");
        }

        if (await db.EntityImages.AnyAsync(i => i.EntityId == entity, cancellationToken))
        {
            ours.Add("pictures");
        }

        if (await db.Events.AnyAsync(e => e.EntityId == entity, cancellationToken)
            || await db.Periods.AnyAsync(p => p.EntityId == entity, cancellationToken))
        {
            ours.Add(TimelineKind);
        }

        if (await db.MergedRecords.AnyAsync(m => m.EntityId == entity, cancellationToken))
        {
            ours.Add("records folded into it");
        }

        return ours;
    }

    internal static IReadOnlyList<WithdrawnRecord> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<WithdrawnRecords>(stream, Shape)?.Records ?? [];
    }
}

/// <summary>The embedded list, as a file.</summary>
internal sealed record WithdrawnRecords(string DecidedBy, string Policy, IReadOnlyList<WithdrawnRecord> Records);

/// <summary>One record, by its slug and by the dataset's own id for it, with why its heading is no name.</summary>
/// <param name="OwnerRuled">
/// Set where the owner ruled the record out although this corpus holds something on it: what he ruled
/// and when. The record then goes with what stands on it.
/// </param>
internal sealed record WithdrawnRecord(string Slug, string SourceId, string Why, string? OwnerRuled = null);
