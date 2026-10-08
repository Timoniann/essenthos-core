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
/// <param name="Rows">Article links held: one per record and language.</param>
/// <param name="ByKind">For each kind of record: how many were tied, how many are ambiguous and how many no item goes by the name of.</param>
internal sealed record WikipediaOutcome(
    bool Skipped,
    IReadOnlyDictionary<string, int> Linked,
    int Ambiguous,
    int Questions,
    int Unmatched,
    int Decided,
    int Rows,
    int Written,
    int Removed,
    TimeSpan Elapsed,
    IReadOnlyDictionary<string, (int Tied, int Ambiguous, int Unmatched)>? ByKind = null,
    int Ties = 0)
{
    public override string ToString() =>
        Skipped
            ? "the Wikipedia links are as they were, because the Wikidata items are not here"
            : $"{Linked.Values.Sum()} records tied to a Wikidata item ({string.Join(", ", Linked.OrderByDescending(l => l.Value).Select(l => $"{l.Value} by {l.Key}"))}), " +
              $"{Ambiguous} left because the evidence does not tell them from a namesake ({Questions} listed for the owner, and {Ties} ties of the matching beside them), " +
              $"{Unmatched} that no item goes by the name of, {Decided} settled by the owner; {Rows} article links held " +
              $"({Written} written, {Removed} removed) in {Elapsed}" +
              (ByKind is null
                  ? string.Empty
                  : "; by kind: " + string.Join(
                      ", ", ByKind.OrderBy(k => k.Key, StringComparer.Ordinal)
                          .Select(k => $"{k.Key} {k.Value.Tied} tied / {k.Value.Ambiguous} ambiguous / {k.Value.Unmatched} unmatched")));
}

/// <summary>
/// Links each person, place and thing to the article each language's Wikipedia has on it, through the
/// Wikidata item the record is. The matching is <see cref="WikipediaMatcher"/>: a record is tied to an
/// item only where nothing else could be the item. The records it cannot decide are written to the
/// owner's list, and what he answers there overrides the matching.
///
/// <para>
/// Re-run whenever the corpus or his answers change: it writes only the rows that differ from what is
/// held, in one transaction, so a second run on an unchanged corpus leaves every row and id as it was.
/// </para>
/// </summary>
internal sealed class WikipediaLinkLoader(AppDbContext db, ReviewLists lists, ILogger<WikipediaLinkLoader> logger)
{
    /// <summary>The Wikidata item OpenBible gives an ancient place, by the source's own identifier for the place.</summary>
    private const string WikidataSource = "s7cc8b2";

    /// <summary>How sure each kind of tie is. These are this corpus's own estimates, checked against a sample read by hand.</summary>
    private static readonly Dictionary<string, double> Confidences = new(StringComparer.Ordinal)
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
        [EntityKind.Person, EntityKind.Place, EntityKind.People, EntityKind.Object, EntityKind.Observance, EntityKind.Title];

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
            return new WikipediaOutcome(true, new Dictionary<string, int>(), 0, 0, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var records = await Records(cancellationToken);
        var matcher = new WikipediaMatcher(wikidata, Identifications(Path.Combine(resources, "OpenBible", "ancient.jsonl")));
        var matches = matcher.Match(records);

        var owners = ReviewLists.Owners(resources, WikipediaReviewList.FileName);
        var answers = WikipediaReviewList.Answers(owners);

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

        var (rows, written, removed) = await Replace(chosen, cancellationToken);

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
            false, linked, ambiguous, questions.Count(q => q.Tied is null), unmatched, decided, rows, written, removed,
            started.Elapsed, byKind, questions.Count(q => q.Tied is not null));
        logger.LogInformation("Linked the records to Wikipedia: {Outcome}", outcome);
        return outcome;
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

    /// <summary>
    /// Makes the table hold exactly the links decided: rows that differ are rewritten, new ones added,
    /// rows no longer decided removed, all in one transaction so a failure leaves the links as they were.
    /// </summary>
    private async Task<(int Rows, int Written, int Removed)> Replace(
        Dictionary<int, (WikidataItem Item, string By)> chosen,
        CancellationToken cancellationToken)
    {
        var wanted = new Dictionary<(int, string), EntityWikipedia>();
        foreach (var (entityId, (item, by)) in chosen)
        {
            foreach (var (language, title) in item.Articles)
            {
                wanted[(entityId, language)] = new EntityWikipedia
                {
                    EntityId = entityId,
                    Language = language,
                    Title = title,
                    Qid = item.Id,
                    MatchedBy = by,
                    Confidence = Confidences[by],
                };
            }
        }

        var held = await db.EntityWikipedia.ToListAsync(cancellationToken);
        var written = 0;
        var removed = 0;
        foreach (var row in held)
        {
            if (!wanted.Remove((row.EntityId, row.Language), out var replacement))
            {
                db.EntityWikipedia.Remove(row);
                removed++;
            }
            else if (row.Title != replacement.Title || row.Qid != replacement.Qid
                                                    || row.MatchedBy != replacement.MatchedBy
                                                    || row.Confidence != replacement.Confidence)
            {
                row.Title = replacement.Title;
                row.Qid = replacement.Qid;
                row.MatchedBy = replacement.MatchedBy;
                row.Confidence = replacement.Confidence;
                written++;
            }
        }

        db.EntityWikipedia.AddRange(wanted.Values);
        written += wanted.Count;
        await db.SaveChangesAsync(cancellationToken);
        return (held.Count - removed + wanted.Count, written, removed);
    }

    /// <summary>Everything the matching reads about a record, from the corpus as it stands.</summary>
    internal async Task<List<WikipediaRecord>> Records(CancellationToken cancellationToken)
    {
        var entities = await db.Entities.AsNoTracking()
            .Select(e => new { e.Id, e.Slug, e.Kind, e.Name, e.Distinguisher, e.Sex, e.OpenBibleId })
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
