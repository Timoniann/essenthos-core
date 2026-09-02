using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
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
    /// <summary>
    /// The datasets, declared rather than parsed.
    ///
    /// The rows carry their source as one prose string — *"BibleData by Brady Stephenson,
    /// github.com/BradyStephenson/bible-data, CC BY 4.0"* — which is right for a row and wrong for
    /// a page: pulling a licence back out of prose with a regex is how a page ends up quietly
    /// asserting the wrong one. So the fields are written here and matched to rows by prefix, and
    /// anything whose source matches nothing is reported rather than hidden.
    /// </summary>
    private static readonly Dataset[] Datasets =
    [
        new("bibledata", "BibleData", "Brady Stephenson", "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/BradyStephenson/bible-data",
            "The people and places the text names, how they stand to one another, and a chronology "
            + "of the Old Testament in which every year is computed from a verse and shows its "
            + "arithmetic.",
            "BibleData by"),

        new("theographic", "Theographic Bible Data", "Robert Rouse", "CC BY-SA 4.0",
            "https://creativecommons.org/licenses/by-sa/4.0/",
            "https://github.com/robertrouse/theographic-bible-metadata",
            "The New Testament chronology, which the other dataset does not have: its method stops "
            + "where the genealogies stop. Share-alike, unlike everything around it.",
            "Theographic"),

        new("wikidata", "Wikidata", "the Wikidata contributors", "CC0",
            "https://creativecommons.org/publicdomain/zero/1.0/",
            "https://query.wikidata.org",
            "World history on the same axis: battles, cities founded, dynasties, writing systems "
            + "and archaeological ages, so the text can be read against what else was happening.",
            "Wikidata"),

        // What this project asserts itself. One row today — the entity BibleData folds into the
        // divine name and this corpus does not — and it belongs in the list precisely because it
        // is ours: a claim of our own, printed beside the ones we merely carry.
        new("essenthos", "Essenthos", "this project", "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/",
            "Corrections and separations this project makes to the datasets it carries, each "
            + "recorded on the row it changed.",
            "Essenthos"),
    ];

    public static void MapDatasets(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/datasets", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var entities = await Counted(db.Entities.Select(e => e.Source), cancellationToken);
            var events = await Counted(db.Events.Select(e => e.Source), cancellationToken);
            var periods = await Counted(db.Periods.Select(p => p.Source), cancellationToken);

            var answers = new List<DatasetResponse>();
            foreach (var dataset in Datasets)
            {
                var counts = new DatasetCounts(
                    Of(entities, dataset.Prefix),
                    Of(events, dataset.Prefix),
                    Of(periods, dataset.Prefix));

                if (counts is { Entities: 0, Events: 0, Periods: 0 })
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
                    counts));
            }

            // Rows whose source no declaration claims. Reported rather than dropped: a dataset
            // loaded without being declared here is exactly the thing this endpoint exists to
            // catch, and a silent zero would let it be published unattributed.
            var undeclared = Undeclared(entities).Concat(Undeclared(events)).Concat(Undeclared(periods))
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

    private static int Of(IEnumerable<(string Source, int Rows)> counted, string prefix) =>
        counted.Where(row => row.Source.StartsWith(prefix, StringComparison.Ordinal)).Sum(row => row.Rows);

    private static IEnumerable<(string Source, int Rows)> Undeclared(
        IEnumerable<(string Source, int Rows)> counted) =>
        counted.Where(row => !Datasets.Any(d => row.Source.StartsWith(d.Prefix, StringComparison.Ordinal)));

    private sealed record Dataset(
        string Id,
        string Name,
        string Author,
        string Licence,
        string LicenceUrl,
        string Url,
        string Covers,
        string Prefix);
}

/// <param name="Counts">
/// How many rows of each kind it accounts for, so a reader can see how much of what they are
/// looking at rests on which licence.
/// </param>
public record DatasetResponse(
    string Id,
    string Name,
    string Author,
    string Licence,
    string LicenceUrl,
    string Url,
    string Covers,
    DatasetCounts Counts);

public record DatasetCounts(int Entities, int Events, int Periods);

/// <param name="Source">
/// The source string as the rows carry it. A dataset that reaches the database without being
/// declared shows up here, which is the point: unattributed rows are worse than absent ones.
/// </param>
public record UndeclaredResponse(string Source, int Rows);

public record DatasetListResponse(IList<DatasetResponse> Items, IList<UndeclaredResponse> Undeclared);
