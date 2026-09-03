using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Where everything that is not a text came from, and under what licence.
///
/// The texts have published their provenance since the beginning; the encyclopedia and the
/// chronology have not, and the sources page has been telling readers so — *treat their contents
/// as unattributed*. That was honest and it is no longer necessary: every person, event and period
/// carries the string of its source, and three datasets now sit side by side under three different
/// licences.
///
/// **The licences differ, and that is the whole reason this exists.** CC BY 4.0 for the Old
/// Testament chronology, CC BY-SA 4.0 for the New, CC0 for the world layer. A page that printed one
/// licence over all three would be asserting what none of them says, and share-alike is not a
/// condition anybody should discover after the fact. PRB-0109.
/// </summary>
public static class DatasetEndpoints
{
    public static void MapDatasets(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/datasets", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var entities = await Counted(db.Entities.Select(e => e.Source), cancellationToken);
            var events = await Counted(db.Events.Select(e => e.Source), cancellationToken);
            var periods = await Counted(db.Periods.Select(p => p.Source), cancellationToken);

            // Only the links somebody stated. The aligner's millions are this project's own
            // inference and belong to no third party, and sweeping them would cost a second a call
            // to count nothing.
            var links = await Counted(
                db.Links.Where(link => link.Method != LinkMethod.Aligner).Select(link => link.Source),
                cancellationToken);

            var answers = new List<DatasetResponse>();
            foreach (var dataset in Datasets.All)
            {
                // A dataset that annotates a text rather than contributing rows is counted by the
                // words it annotates. Only GLAUx does this today, and its licence is share-alike,
                // so a dataset that fell out of this list for having no rows would be the one whose
                // attribution matters most.
                var lemmas = dataset.Lemmas is null
                    ? 0
                    : await db.Words.CountAsync(
                        word => word.Text!.Slug == dataset.Lemmas && word.Lemma != null,
                        cancellationToken);

                var lexicon = dataset.Lexicon
                    ? await db.StrongEntries.CountAsync(cancellationToken)
                    : 0;

                var counts = new DatasetCounts(
                    Of(entities, dataset),
                    Of(events, dataset),
                    Of(periods, dataset),
                    lemmas,
                    lexicon,
                    dataset.Links ? Of(links, dataset) : 0);

                if (counts is { Entities: 0, Events: 0, Periods: 0, Lemmas: 0, Lexicon: 0, Links: 0 })
                {
                    continue;
                }

                answers.Add(new DatasetResponse(
                    dataset.Id,
                    dataset.Name,
                    dataset.Author,
                    dataset.Licence,
                    dataset.LicenceUrl,
                    dataset.Url,
                    dataset.Covers,
                    dataset.Citation,
                    counts,
                    dataset.Contains is null
                        ? []
                        : [.. dataset.Contains.Select(work => new WorkResponse(
                            work.Name, work.Author, work.Licence, work.LicenceUrl, work.Covers))]));
            }

            // Rows whose source no declaration claims. Reported rather than dropped: a dataset
            // loaded without being declared here is exactly the thing this endpoint exists to
            // catch, and a silent zero would let it be published unattributed.
            var undeclared = Undeclared(entities).Concat(Undeclared(events)).Concat(Undeclared(periods))
                .Concat(Undeclared(links))
                .GroupBy(row => row.Source, StringComparer.Ordinal)
                .Select(group => new UndeclaredResponse(group.Key, group.Sum(row => row.Rows)))
                .OrderByDescending(row => row.Rows)
                .ToList();

            return Results.Ok(new DatasetListResponse(answers, undeclared));
        });
    }

    private static async Task<List<(string Source, int Rows)>> Counted(
        IQueryable<string> sources,
        CancellationToken cancellationToken)
    {
        var rows = await sources
            .GroupBy(source => source)
            .Select(group => new { Source = group.Key, Rows = group.Count() })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => (row.Source, row.Rows))];
    }

    private static int Of(IEnumerable<(string Source, int Rows)> counted, Datasets.Dataset dataset) =>
        counted.Where(row => Datasets.Claims(dataset, row.Source)).Sum(row => row.Rows);

    private static IEnumerable<(string Source, int Rows)> Undeclared(
        IEnumerable<(string Source, int Rows)> counted) =>
        counted.Where(row => Datasets.Match(row.Source) is null);

}

/// <param name="Counts">
/// How many rows of each kind it accounts for, so a reader can see how much of what they are
/// looking at rests on which licence.
/// </param>
/// <param name="Contains">
/// Further works bound into the same source under their own terms, empty for almost every dataset.
/// A reader who is told only that the lexicon is public domain has been told something false about
/// 6,070 of its entries.
/// </param>
/// <param name="Citation">
/// How the author asked to be cited, where the source publishes a form. Null where none is
/// published — silence, not an assertion that none is owed.
/// </param>
public record DatasetResponse(
    string Id,
    string Name,
    string Author,
    string Licence,
    string LicenceUrl,
    string Url,
    string Covers,
    string? Citation,
    DatasetCounts Counts,
    IList<WorkResponse> Contains);

/// <param name="Covers">Which part of the dataset is this work's, so the credit lands on the right rows.</param>
public record WorkResponse(
    string Name,
    string Author,
    string Licence,
    string LicenceUrl,
    string Covers);

public record DatasetCounts(int Entities, int Events, int Periods, int Lemmas, int Lexicon, int Links);

/// <param name="Source">
/// The source string as the rows carry it. A dataset that reaches the database without being
/// declared shows up here, which is the point: unattributed rows are worse than absent ones.
/// </param>
public record UndeclaredResponse(string Source, int Rows);

public record DatasetListResponse(IList<DatasetResponse> Items, IList<UndeclaredResponse> Undeclared);
