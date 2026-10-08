using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Relation">How the record stands to the other: <c>son-of</c>, <c>wife-of</c>, <c>father-of</c>.</param>
/// <param name="Names">Every name the other record goes by, folded.</param>
internal sealed record WikipediaRelative(string Relation, IReadOnlySet<string> Names);

/// <summary>
/// What the matching knows of one record: its names in every language, the verses that name it, the
/// kin its lines state, and the line under its name, which cites a verse and often a father.
/// </summary>
/// <param name="Names">Every name the record goes by, folded the way the corpus folds a name.</param>
/// <param name="Line">The line under its name, which states the relations a description of the item states too.</param>
internal sealed record WikipediaRecord(
    int Id,
    string Slug,
    EntityKind Kind,
    string Name,
    string? Line,
    string? Sex,
    string? OpenBibleId,
    IReadOnlySet<string> Names,
    IReadOnlySet<(int Book, int Chapter, int Verse)> Verses,
    IReadOnlyList<WikipediaRelative> Relatives);

/// <summary>What there is to say for an item as the record's article: each field is one kind of agreement.</summary>
/// <param name="Identification">The gazetteer names this item as the ancient place.</param>
/// <param name="Verse">A verse the item's own description cites is a verse that names the record.</param>
/// <param name="Kin">The item's family statements, or its description, name the record's kin.</param>
/// <param name="Chapter">The item is said to be present in a chapter that names the record.</param>
internal readonly record struct WikipediaEvidence(bool Identification, bool Verse, int Kin, bool Chapter)
{
    public bool Any => Identification || Verse || Kin > 0 || Chapter;

    /// <summary>Whether the evidence holds the given tier.</summary>
    public bool Has(string tier) => tier switch
    {
        EntityWikipedia.Evidence.Identification => Identification,
        EntityWikipedia.Evidence.Verse => Verse,
        EntityWikipedia.Evidence.Kin => Kin > 0,
        EntityWikipedia.Evidence.Chapter => Chapter,
        _ => false,
    };

    /// <summary>The kinds of agreement, in the words the owner's list shows them in.</summary>
    public IEnumerable<string> Named()
    {
        if (Identification)
        {
            yield return EntityWikipedia.Evidence.Identification;
        }

        if (Verse)
        {
            yield return EntityWikipedia.Evidence.Verse;
        }

        if (Kin > 0)
        {
            yield return EntityWikipedia.Evidence.Kin;
        }

        if (Chapter)
        {
            yield return EntityWikipedia.Evidence.Chapter;
        }
    }
}

/// <summary>One item a record might be, and what there is for it.</summary>
internal sealed record WikipediaCandidate(WikidataItem Item, WikipediaEvidence Evidence);

/// <summary>What the matching concluded for one record.</summary>
internal abstract record WikipediaMatch
{
    /// <summary>Tied to one item, by <see cref="By"/>.</summary>
    /// <param name="By">One of <see cref="EntityWikipedia.Evidence"/> that is not the owner's.</param>
    public sealed record Linked(WikidataItem Item, string By) : WikipediaMatch;

    /// <summary>Two or more items it might be, or one that the evidence does not clear. Not guessed.</summary>
    public sealed record Ambiguous(IReadOnlyList<WikipediaCandidate> Candidates) : WikipediaMatch;

    /// <summary>No item goes by any of its names.</summary>
    public sealed record Unmatched : WikipediaMatch;
}

/// <summary>
/// Ties each record to the Wikidata item it is, where nothing else could be, and leaves the rest.
///
/// <para>
/// A record is tied to an item only when the item is the single candidate that anything beyond the
/// name speaks for — a verse its description cites, kin its family statements name, a chapter it is
/// present in, the gazetteer's own identification of the place — and no other record has the same
/// claim on it. Failing that, to its only candidate by every spelling of its name, when no other
/// record could be that item either and Wikidata states no namesake it is to be told from — for a
/// person or a place; the name alone never ties a thing. Anything
/// else is ambiguous, and an ambiguous record is not given the famous namesake: it is listed for the
/// owner with its candidates.
/// </para>
///
/// <para>
/// The candidates are counted over every item, not only those with an article: the dangerous case is
/// a minor man matched to the famous item of his name, and the minor namesakes are mostly the items
/// that have no article.
/// </para>
/// </summary>
internal sealed class WikipediaMatcher
{
    /// <summary>The tiers, strongest first. A record is tied at the first tier that picks out exactly one item.</summary>
    private static readonly string[] Tiers =
    [
        EntityWikipedia.Evidence.Identification,
        EntityWikipedia.Evidence.Verse,
        EntityWikipedia.Evidence.Kin,
        EntityWikipedia.Evidence.Chapter,
    ];

    /// <summary>Which of an item's family properties each relation of a record is stated by.</summary>
    private static readonly Dictionary<string, string[]> Properties = new(StringComparer.Ordinal)
    {
        ["son-of"] = ["P22", "P25"],
        ["daughter-of"] = ["P22", "P25"],
        ["father-of"] = ["P40"],
        ["mother-of"] = ["P40"],
        ["brother-of"] = ["P3373"],
        ["sister-of"] = ["P3373"],
        ["half-brother-of"] = ["P3373"],
        ["wife-of"] = ["P26"],
        ["husband-of"] = ["P26"],
    };

    private readonly WikidataItems _wikidata;

    private readonly IReadOnlyDictionary<string, string> _identified;

    private readonly Dictionary<string, List<WikidataItem>> _byName = new(StringComparer.Ordinal);

    /// <param name="identified">The gazetteer's Wikidata item for each ancient place, by the gazetteer's identifier.</param>
    public WikipediaMatcher(WikidataItems wikidata, IReadOnlyDictionary<string, string> identified)
    {
        _wikidata = wikidata;
        _identified = identified;
        foreach (var item in wikidata.Items.Values)
        {
            foreach (var name in item.Names.Union(item.Aliases))
            {
                if (!_byName.TryGetValue(name, out var list))
                {
                    _byName[name] = list = [];
                }

                list.Add(item);
            }
        }
    }

    public Dictionary<int, WikipediaMatch> Match(IReadOnlyList<WikipediaRecord> records)
    {
        var candidates = new Dictionary<int, List<WikidataItem>>();
        var byItem = new Dictionary<string, List<WikipediaRecord>>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var items = Candidates(record);
            candidates[record.Id] = items;
            foreach (var item in items)
            {
                if (!byItem.TryGetValue(item.Id, out var list))
                {
                    byItem[item.Id] = list = [];
                }

                list.Add(record);
            }
        }

        var evidence = new Dictionary<(int, string), WikipediaEvidence>();
        WikipediaEvidence EvidenceFor(WikipediaRecord record, WikidataItem item)
        {
            if (!evidence.TryGetValue((record.Id, item.Id), out var found))
            {
                evidence[(record.Id, item.Id)] = found = Evidence(record, item);
            }

            return found;
        }

        var matches = new Dictionary<int, WikipediaMatch>();
        foreach (var record in records)
        {
            var items = candidates[record.Id];
            if (items.Count == 0)
            {
                matches[record.Id] = new WikipediaMatch.Unmatched();
                continue;
            }

            var linked = TieredMatch(record, items, byItem, EvidenceFor) ?? SoleMatch(record, items, byItem, EvidenceFor);
            matches[record.Id] = linked is not null
                ? new WikipediaMatch.Linked(linked.Value.Item, linked.Value.By)
                : new WikipediaMatch.Ambiguous(
                    [.. items.Select(item => new WikipediaCandidate(item, EvidenceFor(record, item)))]);
        }

        return matches;
    }

    private static (WikidataItem Item, string By)? TieredMatch(
        WikipediaRecord record,
        List<WikidataItem> items,
        Dictionary<string, List<WikipediaRecord>> byItem,
        Func<WikipediaRecord, WikidataItem, WikipediaEvidence> evidence)
    {
        foreach (var tier in Tiers)
        {
            var have = items.Where(item => evidence(record, item).Has(tier)).ToList();
            if (have.Count == 0)
            {
                continue;
            }

            if (have.Count > 1)
            {
                return null;
            }

            var item = have[0];
            var strongerOrEqual = Tiers.TakeWhile(t => t != tier).Append(tier).ToList();
            var rival = byItem[item.Id].Any(other => other.Id != record.Id
                                                     && strongerOrEqual.Any(t => evidence(other, item).Has(t)));
            if (rival)
            {
                return null;
            }

            return (item, tier);
        }

        return null;
    }

    /// <summary>The only candidate by every spelling of the name, when nothing speaks against it.</summary>
    private (WikidataItem Item, string By)? SoleMatch(
        WikipediaRecord record,
        List<WikidataItem> items,
        Dictionary<string, List<WikipediaRecord>> byItem,
        Func<WikipediaRecord, WikidataItem, WikipediaEvidence> evidence)
    {
        if (items.Count != 1 || evidence(record, items[0]).Any)
        {
            return null;
        }

        var item = items[0];
        if (byItem[item.Id].Count != 1 || HasNamesake(record, item))
        {
            return null;
        }

        // A man may be known by an alias; a place that merely goes by another item's alias is not that
        // item: Persia is not Iran. And a name alone never ties an object, a feast, a people or a title:
        // the Wave Sheaf is also an omer, and an omer is a measure.
        if (record.Kind is not (EntityKind.Person or EntityKind.Place)
            || (record.Kind == EntityKind.Place && !item.Names.Overlaps(record.Names)))
        {
            return null;
        }

        return (item, EntityWikipedia.Evidence.Name);
    }

    /// <summary>
    /// Whether Wikidata itself says the item has a namesake to be told from: an item said to be
    /// different from it that is the same kind of thing or goes by one of the record's names, or one
    /// said to be the same as it, which scholars argue about and the owner decides.
    /// </summary>
    private bool HasNamesake(WikipediaRecord record, WikidataItem item)
    {
        if (item.SameAs.Count > 0)
        {
            return true;
        }

        foreach (var other in item.DifferentFrom)
        {
            if (_wikidata.TryGet(other, out var sibling))
            {
                if (sibling.Kind == item.Kind || sibling.Names.Overlaps(record.Names))
                {
                    return true;
                }
            }
            else if (_wikidata.NamesOf(other).Overlaps(record.Names))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The items that go by one of the record's names and could be the same kind of thing.</summary>
    private List<WikidataItem> Candidates(WikipediaRecord record)
    {
        var found = new Dictionary<string, WikidataItem>(StringComparer.Ordinal);
        foreach (var name in record.Names)
        {
            if (_byName.TryGetValue(name, out var items))
            {
                foreach (var item in items)
                {
                    found[item.Id] = item;
                }
            }
        }

        if (record.Kind == EntityKind.Place && record.OpenBibleId is { } place
            && _identified.TryGetValue(place, out var identified) && _wikidata.TryGet(identified, out var gazetteers))
        {
            found[gazetteers.Id] = gazetteers;
        }

        return
        [
            .. found.Values
                .Where(item => Compatible(record, item))
                .Where(item => record.Kind != EntityKind.Place || !OnlyAGazetteersModernSite(record, item))
                .OrderBy(item => item.Id.Length).ThenBy(item => item.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Whether the item came only from the gazetteer and does not go by the record's own name: then it
    /// is the modern site the place is identified with, a tell or a village, not the place the text names.
    /// </summary>
    private bool OnlyAGazetteersModernSite(WikipediaRecord record, WikidataItem item) =>
        item.Seeds.Count == 1 && item.Seeds[0] == "openbible" && !item.Names.Contains(Fold(record.Name))
        && !(record.OpenBibleId is { } place && _identified.TryGetValue(place, out var q) && q == item.Id);

    private static string Fold(string name) => Essenthos.Core.Corpus.NameFolding.Fold(name);

    private static bool Compatible(WikipediaRecord record, WikidataItem item)
    {
        if (item.IsAWikimediaPage || (record.Kind == EntityKind.Place && item.IsOnlyAPeople))
        {
            return false;
        }

        var fits = record.Kind switch
        {
            EntityKind.Person => item.Kind == WikidataKind.Person,
            EntityKind.Place => item.Kind == WikidataKind.Place,
            EntityKind.People => item.Kind == WikidataKind.Other,
            EntityKind.Title => item.Kind is WikidataKind.Other or WikidataKind.Person,
            _ => item.Kind == WikidataKind.Other,
        };

        if (!fits)
        {
            return false;
        }

        if (record.Kind != EntityKind.Person)
        {
            return true;
        }

        if (record.Sex is not null && item.Sex is not null && record.Sex != item.Sex)
        {
            return false;
        }

        // A man is not an angel because an angel's alias is his name: Rephael is not Raphael.
        return !item.Supernatural || SaysSupernatural(record.Line);
    }

    private static readonly string[] SupernaturalWords =
        ["angel", "cherub", "seraph", "demon", "spirit", "god", "satan", "adversary", "devil", "lord"];

    private static bool SaysSupernatural(string? line) =>
        line is not null && SupernaturalWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase));

    private WikipediaEvidence Evidence(WikipediaRecord record, WikidataItem item)
    {
        var identification = record.Kind == EntityKind.Place && record.OpenBibleId is { } place
            && _identified.TryGetValue(place, out var identified) && identified == item.Id;

        var verse = item.Verses.Any(span =>
            Enumerable.Range(span.First, span.Last - span.First + 1).Any(v => record.Verses.Contains((span.Book, span.Chapter, v))));

        var chapters = record.Verses.Select(v => (v.Book, v.Chapter)).ToHashSet();
        var chapter = item.Chapters.Overlaps(chapters);

        var kin = 0;
        var stated = new HashSet<(string, string)>(Statements(record.Line));
        foreach (var relative in record.Relatives)
        {
            if (Properties.TryGetValue(relative.Relation, out var properties)
                && properties.Any(property => item.Relatives.TryGetValue(property, out var items)
                                              && items.Any(other => _wikidata.NamesOf(other).Overlaps(relative.Names))))
            {
                kin++;
            }
        }

        kin += item.StatedRelations.Count(stated.Contains);
        return new WikipediaEvidence(identification, verse, kin, chapter);
    }

    private static IEnumerable<(string, string)> Statements(string? line) => CitedScripture.Relations(line);
}
