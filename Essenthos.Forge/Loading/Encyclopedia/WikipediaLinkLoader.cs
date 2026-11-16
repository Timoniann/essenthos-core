using System.Diagnostics;
using System.Text.Json;
using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Linked">Records tied to an item, by what tied them.</param>
/// <param name="Ambiguous">Records with more than one item it might be, or one the evidence does not clear.</param>
/// <param name="Questions">Of those, the records listed for the owner: the ones an item with an article might be.</param>
/// <param name="Ties">The records the matching tied on its own that the owner's list also holds, to confirm or take back.</param>
/// <param name="Unmatched">Records no item goes by the name of.</param>
/// <param name="Decided">Records settled by the owner's word.</param>
/// <param name="Rows">Article links the ties give: one per record and language.</param>
/// <param name="ByKind">For each kind of record: how many were tied, how many are ambiguous and how many no item goes by the name of.</param>
/// <param name="Held">Of the tied records, those tied as the owner's list had them where this matching alone would not tie them so.</param>
internal sealed record WikipediaOutcome(
    bool Skipped,
    IReadOnlyDictionary<string, int> Linked,
    int Ambiguous,
    int Questions,
    int Unmatched,
    int Decided,
    int Rows,
    TimeSpan Elapsed,
    IReadOnlyDictionary<string, (int Tied, int Ambiguous, int Unmatched)>? ByKind = null,
    int Ties = 0,
    int Held = 0)
{
    public override string ToString() =>
        Skipped
            ? "the owner's Wikipedia list is as it was, because the Wikidata items are not here"
            : $"{Linked.Values.Sum()} records tied to a Wikidata item ({string.Join(", ", Linked.OrderByDescending(l => l.Value).Select(l => $"{l.Value} by {l.Key}"))}; " +
              $"{Held} of them held as the list had them, where this matching no longer ties them so), " +
              $"{Ambiguous} left because the evidence does not tell them from a namesake ({Questions} listed for the owner, and {Ties} ties of the matching beside them), " +
              $"{Unmatched} that no item goes by the name of, {Decided} settled by the owner; {Rows} article links " +
              $"in {Elapsed}" +
              (ByKind is null
                  ? string.Empty
                  : "; by kind: " + string.Join(
                      ", ", ByKind.OrderBy(k => k.Key, StringComparer.Ordinal)
                          .Select(k => $"{k.Key} {k.Value.Tied} tied / {k.Value.Ambiguous} ambiguous / {k.Value.Unmatched} unmatched")));
}

/// <summary>
/// Matches each person, place and thing of a database to the Wikidata item the record is. The matching is
/// <see cref="WikipediaMatcher"/>: a record is tied to an item only where nothing else could be the item. The ties
/// and the records it cannot decide are written to the owner's list, and what he answers there overrides the
/// matching. It writes no database: records-derive makes <c>records/facts/wikipedia</c> from the list
/// (<see cref="Records.RecordProducers.Wikipedia"/>), linking each tied item's articles as <see cref="Links"/> does.
/// </summary>
internal sealed class WikipediaLinkLoader(AppDbContext db, ReviewLists lists, ILogger<WikipediaLinkLoader> logger)
{
    /// <summary>The Wikidata item OpenBible gives an ancient place, by the source's own identifier for the place.</summary>
    private const string WikidataSource = "s7cc8b2";

    /// <summary>How sure each kind of tie is. These are this corpus's own estimates, checked against a sample read by hand.</summary>
    internal static readonly IReadOnlyDictionary<string, double> Confidences = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [EntityWikipedia.Evidence.Owner] = 1.0,
        [EntityWikipedia.Evidence.Identification] = 0.95,
        [EntityWikipedia.Evidence.Verse] = 0.95,
        [EntityWikipedia.Evidence.Kin] = 0.9,
        [EntityWikipedia.Evidence.Chapter] = 0.85,
        [EntityWikipedia.Evidence.Name] = 0.8,
    };

    /// <summary>The kinds of record that have a page a reader could be sent from. Words for God are not names.</summary>
    private static readonly EntityKind[] Kinds =
        [EntityKind.Person, EntityKind.Place, EntityKind.People, EntityKind.Object, EntityKind.Observance, EntityKind.Title, EntityKind.Being];

    private const int MostReferencesShown = 6;

    public async Task<WikipediaOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        WikidataItems wikidata;
        try
        {
            wikidata = WikidataItems.Read(resources);
        }
        catch (FileNotFoundException exception)
        {
            logger.LogWarning("{Message}", exception.Message);
            return new WikipediaOutcome(true, new Dictionary<string, int>(), 0, 0, 0, 0, 0, started.Elapsed);
        }

        var records = await Records(cancellationToken);
        var matcher = new WikipediaMatcher(wikidata, Identifications(Path.Combine(resources, "OpenBible", "ancient.jsonl")));
        var matches = matcher.Match(records);

        var owners = ReviewLists.Owners(resources, WikipediaReviewList.FileName);
        var answers = WikipediaReviewList.Answers(owners);

        var folded = await db.MergedRecords.AsNoTracking()
            .Select(m => new { m.Slug, m.EntityId })
            .ToDictionaryAsync(m => m.Slug, m => m.EntityId, StringComparer.Ordinal, cancellationToken);
        var kept = Hold(
            records, matches, WikipediaReviewList.Ties(owners),
            File.Exists(owners) ? WikipediaReviewList.Records(owners) : [], folded, answers, wikidata);
        foreach (var hold in kept.Held)
        {
            logger.LogInformation(
                "Held {Record} at {Qid} by {By}{From}, as the owner's list had it: the matching {Now}",
                hold.Record, hold.Qid, hold.By, hold.From is null ? string.Empty : $" (carried from {hold.From}, folded into it)",
                hold.Instead is null ? "no longer ties it" : $"would tie it to {hold.Instead}");
        }

        foreach (var (record, why) in kept.Lost)
        {
            logger.LogWarning("The tie the owner's list held for {Record} is not kept: {Why}", record, why);
        }

        foreach (var (record, qid, holder) in kept.Unseated)
        {
            logger.LogWarning(
                "{Record} is left open for the owner: the matching ties it to {Qid}, which the list already gives {Holder}",
                record, qid, holder);
        }

        var chosen = new Dictionary<int, (WikidataItem Item, string By)>();
        var linked = new Dictionary<string, int>(StringComparer.Ordinal);
        var decided = 0;
        var ambiguous = 0;
        var unmatched = 0;
        var byKind = new Dictionary<string, (int Tied, int Ambiguous, int Unmatched)>(StringComparer.Ordinal);
        void Count(WikipediaRecord record, int tied, int open, int none)
        {
            var kind = record.Kind.ToString().ToLowerInvariant();
            var was = byKind.GetValueOrDefault(kind);
            byKind[kind] = (was.Tied + tied, was.Ambiguous + open, was.Unmatched + none);
        }

        foreach (var record in records)
        {
            if (answers.TryGetValue(record.Slug, out var answer))
            {
                decided++;
                if (answer.Answer != WikipediaReviewList.None && wikidata.TryGet(answer.Answer, out var item))
                {
                    chosen[record.Id] = (item, EntityWikipedia.Evidence.Owner);
                    linked[EntityWikipedia.Evidence.Owner] = linked.GetValueOrDefault(EntityWikipedia.Evidence.Owner) + 1;
                    Count(record, 1, 0, 0);
                }
                else if (answer.Answer != WikipediaReviewList.None)
                {
                    logger.LogWarning(
                        "The owner chose {Answer} for {Record}, and Wikidata items no longer hold that item; the record is left unlinked",
                        answer.Answer, record.Slug);
                }

                continue;
            }

            switch (matches[record.Id])
            {
                case WikipediaMatch.Linked tied:
                    chosen[record.Id] = (tied.Item, tied.By);
                    linked[tied.By] = linked.GetValueOrDefault(tied.By) + 1;
                    Count(record, 1, 0, 0);
                    break;
                case WikipediaMatch.Ambiguous:
                    ambiguous++;
                    Count(record, 0, 1, 0);
                    break;
                default:
                    unmatched++;
                    Count(record, 0, 0, 1);
                    break;
            }
        }

        var taken = chosen.Values.Select(c => c.Item.Id).ToHashSet(StringComparer.Ordinal);
        var questions = Questions(records, matches, taken, answers);

        var rows = chosen.Values.Sum(c => Links(c.Item, c.By).Count());

        var database = db.Database.GetDbConnection().Database;
        var target = lists.For(resources, WikipediaReviewList.FileName, database);
        if (lists.IsOwners(database) && !Directory.Exists(Path.GetDirectoryName(owners)))
        {
            logger.LogWarning("There is no review folder at {Folder}, so the owner's list of {Open} records was not written", Path.GetDirectoryName(owners), questions.Count);
        }
        else
        {
            WikipediaReviewList.Write(owners, target, questions);
            if (!lists.IsOwners(database))
            {
                logger.LogWarning(
                    "This load ran against {Database}, not the database the owner's console reads, so the list of {Open} " +
                    "records left for him was written to {Path} and his own at {Review} is as it was",
                    database, questions.Count, target, owners);
            }
        }

        var outcome = new WikipediaOutcome(
            false, linked, ambiguous, questions.Count(q => q.Tied is null), unmatched, decided, rows,
            started.Elapsed, byKind, questions.Count(q => q.Tied is not null), kept.Held.Count);
        logger.LogInformation("Matched the records to Wikidata: {Outcome}", outcome);
        return outcome;
    }

    /// <param name="Held">The ties the list held that this matching would have lost, now tied again as the list had them.</param>
    /// <param name="Lost">The ties the list held that are not kept, with why: the item is gone, has no article, no longer fits the record, or another record holds it.</param>
    /// <param name="Unseated">Records the matching tied to an item the list gives another record, left open instead: the record, the item, the record that holds it.</param>
    internal sealed record WikipediaHolds(
        List<WikipediaHold> Held,
        List<(string Record, string Why)> Lost,
        List<(string Record, string Qid, string Holder)> Unseated);

    /// <param name="From">The folded record whose tie this was, where the tie came through a fold.</param>
    /// <param name="Instead">The item this matching would have tied the record to, where it tied one.</param>
    internal sealed record WikipediaHold(string Record, string Qid, string By, string? From, string? Instead);

    /// <summary>
    /// Holds every tie the owner's list already holds, so that running the matching again never takes an
    /// article off the site on its own. A tie on the list is on the site, and every one of them was accepted
    /// on the owner's instructions, so it stands until he answers otherwise — not until the evidence of the
    /// day happens to move: a verse given to another record, a new record of the same name with the same
    /// kin, a record folded into another under a new name. A record folded into another
    /// (<see cref="MergedRecord"/>) passes its tie to the record it went into where the list held nothing
    /// for that record and the folded ties agree, as records-derive carries it. A tie is let go only where
    /// the owner answered for the record, the record is gone, or the item is: no longer in the Wikidata items,
    /// without an article, or no longer the kind of thing the record is. A record the matching now ties to
    /// an item the list gives another record is left open for the owner instead of taking it.
    /// </summary>
    /// <param name="matches">This matching's conclusions, changed in place where a tie is held or a record left open.</param>
    /// <param name="ties">The list's ties, by record: <see cref="WikipediaReviewList.Ties"/>.</param>
    /// <param name="listed">Every record the list holds an entry for.</param>
    /// <param name="folded">The record each folded record's address now arrives at, by that address.</param>
    internal static WikipediaHolds Hold(
        IReadOnlyList<WikipediaRecord> records,
        Dictionary<int, WikipediaMatch> matches,
        IReadOnlyDictionary<string, (string Qid, string By)> ties,
        IReadOnlySet<string> listed,
        IReadOnlyDictionary<string, int> folded,
        IReadOnlyDictionary<string, WikipediaDecision> answers,
        WikidataItems wikidata)
    {
        var holds = new WikipediaHolds([], [], []);
        var bySlug = records.ToDictionary(r => r.Slug, StringComparer.Ordinal);
        var byId = records.ToDictionary(r => r.Id);

        var wanted = new List<(WikipediaRecord Record, string Qid, string By, string? From)>();
        foreach (var (slug, (qid, by)) in ties.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (bySlug.TryGetValue(slug, out var record) && !answers.ContainsKey(slug))
            {
                wanted.Add((record, qid, by, null));
            }
        }

        foreach (var into in ties
                     .Where(t => !bySlug.ContainsKey(t.Key) && folded.TryGetValue(t.Key, out var id) && byId.ContainsKey(id))
                     .GroupBy(t => folded[t.Key])
                     .OrderBy(g => byId[g.Key].Slug, StringComparer.Ordinal))
        {
            var record = byId[into.Key];
            if (listed.Contains(record.Slug) || answers.ContainsKey(record.Slug))
            {
                continue;
            }

            var from = into.OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
            if (from.Select(t => t.Value.Qid).Distinct(StringComparer.Ordinal).Count() != 1)
            {
                holds.Lost.Add((record.Slug, $"the records folded into it were tied to different items ({string.Join(", ", from.Select(t => $"{t.Key} {t.Value.Qid}"))})"));
                continue;
            }

            wanted.Add((record, from[0].Value.Qid, from[0].Value.By, string.Join(", ", from.Select(t => t.Key))));
        }

        var holders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (record, qid, by, from) in wanted)
        {
            if (matches[record.Id] is WikipediaMatch.Linked same && same.Item.Id == qid)
            {
                holders.TryAdd(qid, record.Slug);
                continue;
            }

            if (!wikidata.TryGet(qid, out var item))
            {
                holds.Lost.Add((record.Slug, $"the Wikidata items no longer hold {qid}"));
                continue;
            }

            if (!item.HasArticle)
            {
                holds.Lost.Add((record.Slug, $"{qid} no longer has an article"));
                continue;
            }

            if (!WikipediaMatcher.Compatible(record, item))
            {
                holds.Lost.Add((record.Slug, $"{qid} is no longer the kind of thing the record is"));
                continue;
            }

            if (!holders.TryAdd(qid, record.Slug))
            {
                holds.Lost.Add((record.Slug, $"{qid} is held by {holders[qid]}"));
                continue;
            }

            var candidates = matches[record.Id] switch
            {
                WikipediaMatch.Linked { Candidates: { } all } => all,
                WikipediaMatch.Ambiguous ambiguous => ambiguous.Candidates,
                _ => [],
            };
            if (candidates.All(c => c.Item.Id != qid))
            {
                candidates = [new WikipediaCandidate(item, default), .. candidates];
            }

            var instead = matches[record.Id] is WikipediaMatch.Linked other ? other.Item.Id : null;
            matches[record.Id] = new WikipediaMatch.Linked(item, by, candidates);
            holds.Held.Add(new WikipediaHold(record.Slug, qid, by, from, instead));
        }

        var held = holds.Held.Select(h => h.Record).ToHashSet(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (!held.Contains(record.Slug) && !answers.ContainsKey(record.Slug)
                && matches[record.Id] is WikipediaMatch.Linked tied
                && holders.TryGetValue(tied.Item.Id, out var holder) && holder != record.Slug)
            {
                matches[record.Id] = new WikipediaMatch.Ambiguous(tied.Candidates ?? [new WikipediaCandidate(tied.Item, default)]);
                holds.Unseated.Add((record.Slug, tied.Item.Id, holder));
            }
        }

        return holds;
    }

    /// <summary>
    /// The entries of the owner's list: each ambiguous record an item with an article might be, and
    /// each record the matching tied on its own, so that he can take the tie back or set it to
    /// another item. Each offers the items with an article that no other record is already tied to,
    /// the tied item first, and always the item he chose. Those with most verses first.
    /// </summary>
    private static List<WikipediaQuestion> Questions(
        IReadOnlyList<WikipediaRecord> records,
        Dictionary<int, WikipediaMatch> matches,
        HashSet<string> taken,
        IReadOnlyDictionary<string, WikipediaDecision> answers)
    {
        var questions = new List<(WikipediaQuestion Question, int Verses)>();
        foreach (var record in records)
        {
            IReadOnlyList<WikipediaCandidate> candidates;
            WikipediaTie? tie = null;
            switch (matches[record.Id])
            {
                case WikipediaMatch.Ambiguous ambiguous:
                    candidates = ambiguous.Candidates;
                    break;
                case WikipediaMatch.Linked { Candidates: { } all } linked:
                    candidates = all;
                    tie = new WikipediaTie(linked.Item.Id, linked.By);
                    break;
                default:
                    continue;
            }

            var chosen = answers.TryGetValue(record.Slug, out var answer) ? answer.Answer : null;
            var free = candidates
                .Where(c => !taken.Contains(c.Item.Id) || c.Item.Id == tie?.Qid || c.Item.Id == chosen)
                .ToList();
            var offers = free.Where(c => c.Item.HasArticle).ToList();
            if (offers.Count == 0)
            {
                continue;
            }

            questions.Add((
                new WikipediaQuestion(
                    record.Slug,
                    record.Kind.ToString().ToLowerInvariant(),
                    record.Name,
                    record.Line,
                    [.. record.Verses.OrderBy(v => v).Take(MostReferencesShown).Select(Reference)],
                    [
                        .. offers
                            .OrderByDescending(c => c.Item.Id == tie?.Qid)
                            .ThenByDescending(c => c.Evidence.Named().Count())
                            .ThenByDescending(c => c.Item.Articles.Count)
                            .ThenBy(c => c.Item.Id.Length).ThenBy(c => c.Item.Id, StringComparer.Ordinal)
                            .Select(c => new WikipediaOffer(
                                c.Item.Id,
                                c.Item.Label,
                                c.Item.Descriptions.GetValueOrDefault("en") ?? c.Item.Descriptions.Values.FirstOrDefault(),
                                c.Item.Articles,
                                [.. c.Evidence.Named()])),
                    ],
                    free.Count - offers.Count,
                    tie),
                record.Verses.Count));
        }

        return [.. questions.OrderByDescending(q => q.Verses).ThenBy(q => q.Question.Record, StringComparer.Ordinal).Select(q => q.Question)];
    }

    private static string Reference((int Book, int Chapter, int Verse) verse) =>
        $"{BookCodes.Code(verse.Book) ?? verse.Book.ToString()} {verse.Chapter}:{verse.Verse}";

    /// <summary>The article links a record tied to <paramref name="item"/> by <paramref name="by"/> is given: one per language the item has an article in.</summary>
    internal static IEnumerable<EntityWikipedia> Links(WikidataItem item, string by, int entityId = 0) =>
        item.Articles.Select(article => new EntityWikipedia
        {
            EntityId = entityId,
            Language = article.Key,
            Title = article.Value,
            Qid = item.Id,
            MatchedBy = by,
            Confidence = Confidences[by],
        });

    /// <summary>Everything the matching reads about a record, from the corpus as it stands.</summary>
    internal async Task<List<WikipediaRecord>> Records(CancellationToken cancellationToken)
    {
        var entities = await db.Entities.AsNoTracking()
            .Select(e => new
            {
                e.Id, e.Slug, e.Kind, e.Name, e.Distinguisher, e.Sex,
                OpenBibleId = e.ExternalIds
                    .Where(x => x.Dataset == Datasets.OpenBibleId && x.Relation == RecordExternalRelations.SameAs)
                    .OrderBy(x => x.ExternalId)
                    .Select(x => x.ExternalId)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var names = new Dictionary<int, HashSet<string>>();
        void Add(int id, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var folded = NameFolding.Fold(name);
            if (folded.Length == 0)
            {
                return;
            }

            if (!names.TryGetValue(id, out var set))
            {
                names[id] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(folded);
        }

        foreach (var entity in entities)
        {
            Add(entity.Id, entity.Name);
        }

        foreach (var name in await db.EntityNames.AsNoTracking()
                     .Select(n => new { n.EntityId, n.Label, n.Hebrew, n.Greek }).ToListAsync(cancellationToken))
        {
            Add(name.EntityId, name.Label);
            Add(name.EntityId, name.Hebrew);
            Add(name.EntityId, name.Greek);
        }

        foreach (var form in await db.EntityNameForms.AsNoTracking()
                     .Where(f => f.GrammaticalCase == "nominative")
                     .Select(f => new { f.EntityId, f.Form }).ToListAsync(cancellationToken))
        {
            Add(form.EntityId, form.Form);
        }

        var verses = new Dictionary<int, HashSet<(int, int, int)>>();
        foreach (var verse in await db.EntityVerses.AsNoTracking().Shown()
                     .Select(v => new { v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
                     .ToListAsync(cancellationToken))
        {
            if (!verses.TryGetValue(verse.EntityId, out var set))
            {
                verses[verse.EntityId] = set = [];
            }

            set.Add((verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse));
        }

        var relatives = new Dictionary<int, List<WikipediaRelative>>();
        foreach (var descriptor in await db.EntityDescriptors.AsNoTracking()
                     .Select(d => new { d.EntityId, d.Relation, d.TargetEntityId }).ToListAsync(cancellationToken))
        {
            if (!names.TryGetValue(descriptor.TargetEntityId, out var targetNames))
            {
                continue;
            }

            if (!relatives.TryGetValue(descriptor.EntityId, out var list))
            {
                relatives[descriptor.EntityId] = list = [];
            }

            list.Add(new WikipediaRelative(descriptor.Relation, targetNames));
        }

        var records = new List<WikipediaRecord>();
        foreach (var entity in entities.Where(e => Kinds.Contains(e.Kind)))
        {
            var cited = verses.GetValueOrDefault(entity.Id) ?? [];
            foreach (var verse in Cited(entity.Distinguisher))
            {
                cited.Add(verse);
            }

            records.Add(new WikipediaRecord(
                entity.Id,
                entity.Slug,
                entity.Kind,
                entity.Name,
                entity.Distinguisher,
                entity.Sex,
                entity.OpenBibleId,
                names.GetValueOrDefault(entity.Id) ?? [],
                cited,
                relatives.GetValueOrDefault(entity.Id) ?? []));
        }

        return records;
    }

    /// <summary>The verses a record's line cites in the form the lines are written in: <c>2KI 12:21</c>, <c>GEN 4:19-20</c>.</summary>
    internal static IEnumerable<(int Book, int Chapter, int Verse)> Cited(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            yield break;
        }

        foreach (System.Text.RegularExpressions.Match match in CitedPattern.Matches(line))
        {
            if (!BookCodes.TryGetOrdinal(match.Groups[1].Value, out var book))
            {
                continue;
            }

            var chapter = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var first = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            var last = match.Groups[4].Success
                ? int.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture)
                : first;
            for (var verse = first; verse <= Math.Max(first, last) && verse - first < 60; verse++)
            {
                yield return (book, chapter, verse);
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex CitedPattern =
        new(@"\b([1-3]?[A-Z]{2,3}) (\d+):(\d+)(?:-(\d+))?", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The Wikidata item OpenBible gives each ancient place, by the gazetteer's identifier for the
    /// place: the one tie a gazetteer states between its place and an item, which no name is needed for.
    /// </summary>
    internal static Dictionary<string, string> Identifications(string ancientFile)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(ancientFile))
        {
            return found;
        }

        foreach (var line in File.ReadLines(ancientFile))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) || id.GetString() is not { } place
                || !root.TryGetProperty("linked_data", out var linked))
            {
                continue;
            }

            var data = linked.ValueKind == JsonValueKind.String ? JsonDocument.Parse(linked.GetString()!).RootElement : linked;
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(WikidataSource, out var wikidata)
                && wikidata.TryGetProperty("id", out var qid) && qid.GetString() is { } item)
            {
                found[place] = item;
            }
        }

        return found;
    }
}
