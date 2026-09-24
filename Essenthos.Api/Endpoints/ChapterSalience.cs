using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>The labels a chapter's verses give one record, and the names the record is known by.</summary>
internal sealed record NamesUsedHere(IReadOnlyList<(string Label, int Verses, int First)> Labels, IReadOnlySet<string> ProperNames);

/// <summary>
/// How present a record is in a chapter, which is what the panel orders its records by.
///
/// <para>
/// A verse whose words name the record counts whole; a verse only a source's list gives counts
/// half, because the list is somebody's reading of the verse and the words are the verse. A passage
/// about an object or an observance counts whole where it runs: Exodus 25 describes the ark in verses
/// that say <em>it</em>, and the passage is what the chapter is about.
/// </para>
/// </summary>
internal static class ChapterSalience
{
    /// <summary>What a verse only a source's list gives counts for, against one the words name.</summary>
    public const double ListedOnly = 0.5;

    /// <summary>The group the words for God are shown in, as <see cref="ContextEntityResponse.Group"/> spells it.</summary>
    public const string GodGroup = "god";

    /// <summary>
    /// The records of the words for God. They stay separate records — Elohim is not YHVH — and a
    /// chapter's panel shows them as one row saying which of them its words use.
    /// </summary>
    public static readonly IReadOnlySet<string> WordsForGod =
        new HashSet<string>(StringComparer.Ordinal) { "yhvh", "elohim", "el", "eloah", "yhvh-2" };

    /// <summary>
    /// The sources that list a verse because of the words in it — the words this corpus annotates,
    /// the words Strong's numbers with a name, the verses where the King James prints it — as opposed
    /// to a dataset's own list of where it reads a record.
    /// </summary>
    private static readonly string[] WordSources =
    [
        "Essenthos, from the words",
        "Essenthos, from Strong's Dictionary entry for the word",
        "Essenthos, from the person split",
    ];

    public static bool FromTheWords(string source) =>
        WordSources.Any(prefix => source.StartsWith(prefix, StringComparison.Ordinal));

    public static double Of(
        string slug,
        IReadOnlyDictionary<string, SortedSet<int>> verses,
        IReadOnlyDictionary<string, SortedSet<string>> how,
        IReadOnlyDictionary<string, SortedSet<int>> spoken)
    {
        var all = verses.GetValueOrDefault(slug)?.Count ?? 0;
        var whole = how.GetValueOrDefault(slug)?.Contains("passage") == true
            ? all
            : spoken.GetValueOrDefault(slug)?.Count ?? 0;
        return whole + ListedOnly * (all - whole);
    }

    /// <summary>
    /// Takes a source's reading of one word for God off a verse where the words name another:
    /// BibleData files the <em>God</em> of Genesis 1 under YHVH, and the Hebrew there is elohim. A
    /// record left with no verse leaves the chapter.
    /// </summary>
    public static void SettleWordsForGod(
        Dictionary<string, SortedSet<int>> verses,
        Dictionary<string, SortedSet<string>> how,
        IReadOnlyDictionary<string, SortedSet<int>> spoken)
    {
        var present = WordsForGod.Where(verses.ContainsKey).ToList();
        foreach (var slug in present)
        {
            var own = spoken.GetValueOrDefault(slug);
            var others = present.Where(other => other != slug)
                .SelectMany(other => spoken.GetValueOrDefault(other) ?? [])
                .ToHashSet();
            verses[slug].RemoveWhere(verse => others.Contains(verse) && own?.Contains(verse) != true);
            if (verses[slug].Count == 0)
            {
                verses.Remove(slug);
                how.Remove(slug);
            }
        }
    }

    /// <summary>The labels the chapter's verses give each record, and each record's proper names.</summary>
    public static async Task<Dictionary<string, NamesUsedHere>> NamesUsed(
        AppDbContext db,
        int book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var labelled = await db.EntityVerses
            .Where(v => v.CanonicalBook == book && v.CanonicalChapter == chapter && !v.Disputed
                        && v.Label != null && v.Label != "")
            .Select(v => new { v.EntityId, v.Entity!.Slug, Label = v.Label!, v.CanonicalVerse })
            .Distinct()
            .ToListAsync(cancellationToken);
        if (labelled.Count == 0)
        {
            return [];
        }

        var ids = labelled.Select(v => v.EntityId).Distinct().ToList();
        var proper = (await db.EntityNames
                .Where(n => ids.Contains(n.EntityId) && n.Kind == ProperName)
                .Select(n => new { n.EntityId, n.Label })
                .ToListAsync(cancellationToken))
            .GroupBy(n => n.EntityId)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Label).ToHashSet(StringComparer.OrdinalIgnoreCase));

        return labelled
            .GroupBy(v => (v.EntityId, v.Slug))
            .ToDictionary(
                g => g.Key.Slug,
                g => new NamesUsedHere(
                    [
                        .. g.GroupBy(v => v.Label, StringComparer.OrdinalIgnoreCase)
                            .Select(label => (label.Key, label.Select(v => v.CanonicalVerse).Distinct().Count(),
                                label.Min(v => v.CanonicalVerse))),
                    ],
                    proper.GetValueOrDefault(g.Key.EntityId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
                StringComparer.Ordinal);
    }

    private const string ProperName = "proper name";

    /// <summary>
    /// The name a row shows where the chapter never uses the headword: the proper name of the record
    /// its verses are labelled with most. A title the verses carry — <em>the Lamb</em>, <em>Father</em>
    /// — is not a name, and leaves the headword where it is.
    /// </summary>
    public static string? NameUsed(string headword, NamesUsedHere? used)
    {
        var head = ContextWeights.Head(headword);
        if (used is null || used.Labels.Any(l => ContextWeights.Head(l.Label) == head))
        {
            return null;
        }

        return used.Labels
            .Where(l => used.ProperNames.Contains(l.Label))
            .OrderByDescending(l => l.Verses)
            .ThenBy(l => l.First)
            .Select(l => l.Label)
            .FirstOrDefault();
    }
}
