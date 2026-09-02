namespace Essenthos.Core.Endpoints;

/// <summary>
/// The datasets that are not texts, and which row came from which.
///
/// A row carries its source as one prose string — *"Wikidata, query.wikidata.org, CC0"* — which is
/// the right thing to store and the wrong thing to show. A reader looking at a date needs to know
/// it was taken from somewhere, whose it is, and what the licence permits, and needs the name and
/// the licence to be things they can follow. So the fields are declared here once, every row that
/// carries a source also answers which of these it is, and the page renders a credit rather than
/// printing a sentence.
///
/// Declared rather than parsed. Pulling a licence back out of prose with a regex is how a page ends
/// up quietly asserting the wrong one, and the wrong one here is share-alike.
/// </summary>
public static class Datasets
{
    public sealed record Dataset(
        string Id,
        string Name,
        string Author,
        string Licence,
        string LicenceUrl,
        string Url,
        string Covers,
        string Prefix);

    public static readonly Dataset[] All =
    [
        new("bibledata", "BibleData", "Brady Stephenson", "CC BY 4.0",
            "https://creativecommons.org/licenses/by/4.0/",
            "https://github.com/BradyStephenson/bible-data",
            "The people the text names, how they stand to one another, and a chronology of the Old "
            + "Testament in which every year is computed from a verse and shows its arithmetic. Its "
            + "places are marked in progress by its author and read that way here: 118 of them, "
            + "named but not placed, and referenced only through Genesis and Exodus.",
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

    /// <summary>Which dataset a row's source string belongs to, or null if none claims it.</summary>
    public static string? Of(string? source) => Match(source)?.Id;

    public static Dataset? Match(string? source) =>
        source is null
            ? null
            : Array.Find(All, d => source.StartsWith(d.Prefix, StringComparison.Ordinal));

}
