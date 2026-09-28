using System.Diagnostics;
using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Withdrawn">BibleData rows the owner's removals now hold back from every page.</param>
/// <param name="Restored">Rows held back by an earlier load whose removal he has since taken back.</param>
/// <param name="Elsewhere">
/// Rows a removal names that the table no longer holds as BibleData's, or that no longer join either
/// person it was decided about: left alone, because the list was keyed by the rows the database held
/// the day he decided.
/// </param>
internal sealed record WithdrawnRelationshipOutcome(int Withdrawn, int Restored, int Elsewhere, TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{Withdrawn} of BibleData's relationship rows held back as the owner removed them, {Restored} " +
        $"restored, {Elsewhere} named by a removal and no longer where it was decided, in {Elapsed}";
}

/// <summary>
/// BibleData's relationship rows the owner removed in his console, held back from the reader.
///
/// <para>
/// **His word takes effect at the next load, not at the cut-over.** A removal used to wait in
/// <c>bibledata-removed.json</c> for the day BibleData's relationships leave the page altogether,
/// and until then his page still said what he had struck out: Mary the mother of James, Joses, Simon
/// and Judas, on her page, on theirs and on the tree. The row stays in the table as BibleData's
/// testimony, and <see cref="Database.Entities.EntityRelationship.Withdrawn"/> keeps it from every
/// query, which also stops it outranking a reading of ours in <see cref="OwnRelationshipLoader"/>.
/// </para>
///
/// <para>
/// **The decisions are read, not the cut-over list.** That list is what <c>relationships.py
/// decide</c> last wrote from them; the review list is his word as it stands, so a removal he takes
/// back is restored on the next load whether or not decide has run since. A row counts as removed
/// when the latest decision naming it says so, since a fact decided twice keeps his later answer.
/// </para>
///
/// <para>
/// **A decision names rows by id, and is only applied where they still fit it.** The ids are the
/// ones the database held when he decided, which a rebuild from the same files reproduces; a row is
/// held back only while it is BibleData's and one of its two people is one the decision names, so a
/// renumbered table leaves rows shown rather than hiding somebody else's.
/// </para>
/// </summary>
internal sealed class WithdrawnRelationshipLoader(AppDbContext db, ILogger<WithdrawnRelationshipLoader> logger)
{
    /// <summary>The owner's decisions on the relationships only BibleData states.</summary>
    public const string ReviewFile = "bibledata-relationships.json";

    /// <summary>The decision that takes a fact off the page.</summary>
    public const string Remove = "remove";

    public async Task<WithdrawnRelationshipOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var removed = Removed(ReviewLists.Owners(resources, ReviewFile));

        var rows = await db.EntityRelationships
            .IgnoreQueryFilters()
            .Where(r => r.Source == BibleDataLoader.Source)
            .Select(r => new { r.Id, From = r.From!.Slug, To = r.To!.Slug, r.Withdrawn })
            .ToListAsync(cancellationToken);

        var wanted = rows
            .Where(r => removed.TryGetValue(r.Id, out var people) && (people.Contains(r.From) || people.Contains(r.To)))
            .Select(r => r.Id)
            .ToHashSet();
        var withdraw = rows.Where(r => !r.Withdrawn && wanted.Contains(r.Id)).Select(r => r.Id).ToList();
        var restore = rows.Where(r => r.Withdrawn && !wanted.Contains(r.Id)).Select(r => r.Id).ToList();
        var elsewhere = removed.Count - wanted.Count;

        await Mark(withdraw, true, cancellationToken);
        await Mark(restore, false, cancellationToken);

        if (elsewhere > 0)
        {
            logger.LogWarning(
                "{Elsewhere} rows the owner removed are not BibleData's or no longer join either person the " +
                "decision names, so they were left as they are. Run relationships.py decide against this " +
                "database and compare its removal list with {File}",
                elsewhere, ReviewFile);
        }

        return new WithdrawnRelationshipOutcome(wanted.Count, restore.Count, elsewhere, started.Elapsed);
    }

    /// <summary>Every row whose latest decision is a removal, with the two people it was decided about.</summary>
    internal static Dictionary<int, HashSet<string>> Removed(string path)
    {
        var latest = new Dictionary<int, (string At, string Decision, HashSet<string> People)>();
        if (!File.Exists(path))
        {
            return [];
        }

        var decisions = JsonNode.Parse(File.ReadAllText(path))?["decisions"]?.AsObject() ?? [];
        foreach (var (_, node) in decisions)
        {
            if (node is not JsonObject decision
                || decision["rows"]?.GetValue<string>() is not { Length: > 0 } rows)
            {
                continue;
            }

            var at = decision["decidedAt"]?.GetValue<string>() ?? string.Empty;
            var verdict = decision["decision"]?.GetValue<string>() ?? string.Empty;
            HashSet<string> people =
            [
                .. new[] { decision["a"]?.GetValue<string>(), decision["b"]?.GetValue<string>() }.OfType<string>(),
            ];
            foreach (var row in rows.Split('|'))
            {
                if (int.TryParse(row, out var id)
                    && (!latest.TryGetValue(id, out var before) || string.CompareOrdinal(at, before.At) > 0))
                {
                    latest[id] = (at, verdict, people);
                }
            }
        }

        return latest
            .Where(p => p.Value.Decision == Remove)
            .ToDictionary(p => p.Key, p => p.Value.People);
    }

    private async Task Mark(List<int> ids, bool withdrawn, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await db.EntityRelationships
            .IgnoreQueryFilters()
            .Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Withdrawn, withdrawn), cancellationToken);
    }
}
