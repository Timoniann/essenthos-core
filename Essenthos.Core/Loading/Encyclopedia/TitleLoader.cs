using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Retitled">Records that were a person and are now held as a title.</param>
/// <param name="Retired">Records written for one bearer of a title, which the title now answers for.</param>
/// <param name="Missing">Titles whose record the encyclopedia does not hold, which is a corpus not yet loaded.</param>
internal sealed record TitleOutcome(bool AlreadyLoaded, int Retitled, int Retired, int Missing, TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the names the owner ruled titles are already held as titles"
            : $"{Retitled} records held as titles rather than as one person and {Retired} records for a " +
              $"single bearer of one withdrawn, in {Elapsed}" +
              (Missing > 0 ? $"; {Missing} titles name a record the encyclopedia does not hold" : "");
}

/// <summary>
/// The names the owner ruled are titles rather than one man's — Abimelech of the kings of Gerar,
/// Phicol, Ahuzzath — held as what the decision says they are.
///
/// <para>
/// **The record is kept and its kind changes.** The occurrences, the verses the datasets list, the
/// names, their Strong numbers and their forms in every language are all still true of the title,
/// and a slug readers and links already reach keeps reaching it. What stops being true is that it
/// is one person, so the kind, the line under the name and the notes are the decision's, the source
/// is the decision's, and the claims that established it as one man are withdrawn: a reading that
/// assigned sixteen verses to <em>this bearer</em> is the answer the decision replaced, and a page
/// listing it under what established the record would say the opposite of the record. A dataset's
/// own testimony that it holds the entry as a person stays, because that is still what it says.
/// </para>
///
/// <para>
/// **What the decision leaves open is written down, not implied.** Whether Abraham's Abimelech and
/// Isaac's are one man, two men of one name, or two holders of a title is exactly what is not
/// known, so each reading is an alternative on the record with its reason, and the page says the
/// identification is open.
/// </para>
///
/// <para>
/// **A record written for one bearer is withdrawn.** A reading of the namesake verses had split
/// Isaac's king off as a second Abimelech; the title answers for both accounts without deciding
/// that, and the second man survives as one of the alternatives rather than as a page.
/// </para>
///
/// <para>
/// Idempotent per record. It runs after every pass that writes a person and before the references
/// and descriptions, so on a cold corpus the records are persons while names are resolved among
/// persons and titles by the time anything is read off them; on a loaded corpus a record already
/// held as a title under this decision is left exactly as it is.
/// </para>
/// </summary>
internal sealed class TitleLoader(AppDbContext db, ILogger<TitleLoader> logger)
{
    public async Task<TitleOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var decision = SenseReadingFiles.Titles();

        var wanted = decision.Titles
            .SelectMany(title => (title.Replaces ?? []).Select(replaced => replaced.Slug)
                .Concat(title.Alternatives?.Select(alternative => alternative.Slug) ?? [])
                .Append(title.Slug))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var records = await db.Entities
            .Where(e => wanted.Contains(e.Slug))
            .Include(e => e.Claims)
            .Include(e => e.Alternatives)
            .AsSplitQuery()
            .ToDictionaryAsync(e => e.Slug, StringComparer.Ordinal, cancellationToken);

        int retitled = 0, retired = 0, missing = 0;
        foreach (var title in decision.Titles)
        {
            if (!records.TryGetValue(title.Slug, out var record))
            {
                logger.LogWarning(
                    "The decision holds \"{Slug}\" as a title, and the encyclopedia holds no record of that " +
                    "slug. Either the encyclopedia has not been loaded yet or the record was renamed, and " +
                    "TitleRecords.json has to follow it",
                    title.Slug);
                missing++;
                continue;
            }

            if (record.Kind != EntityKind.Title
                || !record.Claims.Any(claim => claim.Source == decision.Source))
            {
                Retitle(record, title, decision, records);
                retitled++;
            }

            foreach (var replaced in title.Replaces ?? [])
            {
                if (records.Remove(replaced.Slug, out var bearer))
                {
                    logger.LogInformation(
                        "Withdrew {Slug}, a record for one bearer of the title {Title}: {Why}",
                        replaced.Slug, title.Slug, replaced.Why);
                    db.Entities.Remove(bearer);
                    retired++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var outcome = new TitleOutcome(
            retitled == 0 && retired == 0 && missing == 0, retitled, retired, missing, started.Elapsed);
        logger.LogInformation("The titles: {Outcome}", outcome);
        return outcome;
    }

    private static void Retitle(
        Entity record,
        TitleRecord title,
        TitleDecision decision,
        IReadOnlyDictionary<string, Entity> records)
    {
        record.Kind = EntityKind.Title;
        record.Name = title.Name;
        record.Distinguisher = title.Distinguisher;
        record.Notes = title.Notes;
        record.Sex = null;
        record.Source = decision.Source;

        foreach (var superseded in record.Claims
                     .Where(claim => claim.Method != LinkMethod.StatedBySource)
                     .ToList())
        {
            record.Claims.Remove(superseded);
        }

        record.Claims.Add(new EntityClaim
        {
            Method = EnumSpelling.ToLinkMethod(decision.Method),
            Confidence = null,
            Source = decision.Source,
            Note = title.Why,
        });

        foreach (var stale in record.Alternatives.Where(a => a.Source == decision.Source).ToList())
        {
            record.Alternatives.Remove(stale);
        }

        foreach (var alternative in title.Alternatives ?? [])
        {
            var other = alternative.Slug is null ? null : records.GetValueOrDefault(alternative.Slug);
            record.Alternatives.Add(new EntityAlternative
            {
                Alternative = other,
                Describes = other is null ? alternative.Describes ?? alternative.Slug : null,
                Reason = alternative.Reason,
                Source = decision.Source,
            });
        }
    }
}
