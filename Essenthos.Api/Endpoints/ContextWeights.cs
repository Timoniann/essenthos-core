using System.Collections.Concurrent;
using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Who a topic of Nave's is about, where its heading is a record's name: the records bearing that
/// name, and whether any of them is a person or a people.
/// </summary>
internal sealed record TopicSubject(IReadOnlySet<string> Slugs, bool Personal);

/// <summary>
/// What the Chapter Context weighs a chapter's topics and records against, counted over the whole
/// Bible: how many verses each topic files, whom a topic is about, and in how many chapters each
/// record is found.
///
/// <para>
/// None of it changes while the process runs, because the corpus is read-only here, and every
/// chapter asks for it, so it is counted once. What each chapter's English says of its topics is
/// kept as it is worked out, one small set per chapter.
/// </para>
/// </summary>
internal sealed class ContextWeights
{
    /// <summary>The verses of the Bible in the shared frame, which a topic's verses are a share of.</summary>
    public const int BibleVerses = 31102;

    /// <summary>
    /// The share of a topic's verses that must be verses its record is named in before the topic is
    /// taken to be about that record. Nave's ADAM is the man; the town of Adam at the Jordan is named
    /// in one verse of it.
    /// </summary>
    public const double AboutShare = 0.2;

    private readonly ConcurrentDictionary<(int Book, int Chapter), IReadOnlySet<string>> _said = new();

    private ContextWeights(
        IReadOnlyDictionary<string, int> topicVerses,
        IReadOnlyDictionary<string, TopicSubject> about,
        IReadOnlySet<string> places,
        IReadOnlyDictionary<string, int> entityChapters)
    {
        TopicVerses = topicVerses;
        About = about;
        Places = places;
        EntityChapters = entityChapters;
    }

    /// <summary>Every verse of the Bible each topic files, by topic slug; a whole chapter counts as every verse of it.</summary>
    public IReadOnlyDictionary<string, int> TopicVerses { get; }

    /// <summary>The topics headed with a record's name and mostly filing verses that name it, by topic slug.</summary>
    public IReadOnlyDictionary<string, TopicSubject> About { get; }

    /// <summary>The names, as <see cref="Head"/> reads them, that some place bears.</summary>
    public IReadOnlySet<string> Places { get; }

    /// <summary>How many chapters each record is found in, by slug.</summary>
    public IReadOnlyDictionary<string, int> EntityChapters { get; }

    /// <summary>The topics a chapter's English says, once worked out for it.</summary>
    public async Task<IReadOnlySet<string>> Said(
        int book,
        int chapter,
        Func<Task<IReadOnlySet<string>>> count)
    {
        if (_said.TryGetValue((book, chapter), out var said))
        {
            return said;
        }

        said = await count();
        _said[(book, chapter)] = said;
        return said;
    }

    public static async Task<ContextWeights> Count(AppDbContext db, CancellationToken cancellationToken)
    {
        var lengths = (await db.VerseReferences
                .Where(r => r.IsPrimary)
                .GroupBy(r => new { r.CanonicalBook, r.CanonicalChapter })
                .Select(g => new { g.Key.CanonicalBook, g.Key.CanonicalChapter, Last = g.Max(r => r.CanonicalVerse) })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => (row.CanonicalBook, row.CanonicalChapter), row => row.Last);

        var references = await db.TopicReferences
            .Select(r => new { r.Topic!.Slug, r.CanonicalBook, r.CanonicalChapter, r.FirstVerse, r.LastVerse })
            .ToListAsync(cancellationToken);
        var cited = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var r in references)
        {
            if (!cited.TryGetValue(r.Slug, out var verses))
            {
                cited[r.Slug] = verses = [];
            }

            var first = r.FirstVerse ?? 1;
            var last = r.LastVerse ?? Math.Max(first, lengths.GetValueOrDefault((r.CanonicalBook, r.CanonicalChapter)));
            for (var verse = first; verse <= last; verse++)
            {
                verses.Add(Key(r.CanonicalBook, r.CanonicalChapter, verse));
            }
        }

        var entityChapters = (await db.EntityVerses
                .Where(v => !v.Disputed)
                .Select(v => new { v.Entity!.Slug, v.CanonicalBook, v.CanonicalChapter })
                .Distinct()
                .GroupBy(v => v.Slug)
                .Select(g => new { Slug = g.Key, Chapters = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.Slug, row => row.Chapters, StringComparer.Ordinal);

        var (about, places) = await Subjects(db, cited, cancellationToken);

        return new ContextWeights(
            cited.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal),
            about,
            places,
            entityChapters);
    }

    /// <summary>
    /// The topics whose heading, up to its first comma, is the name of a record — <em>JESUS, THE
    /// CHRIST</em> is headed Jesus — and at least <see cref="AboutShare"/> of whose verses are verses
    /// one of the records so named is listed in. Nave's headings are names, and a name is shared: the
    /// topic is about every record of the name that clears the share.
    ///
    /// <para>
    /// The words for God are no one's topic here: Nave's GOD is a subject of every chapter that
    /// speaks of him, not a reading of one.
    /// </para>
    /// </summary>
    private static async Task<(Dictionary<string, TopicSubject> About, HashSet<string> Places)> Subjects(
        AppDbContext db,
        IReadOnlyDictionary<string, HashSet<int>> cited,
        CancellationToken cancellationToken)
    {
        var topics = await db.Topics.Select(t => new { t.Slug, t.Name }).ToListAsync(cancellationToken);
        var headed = topics
            .GroupBy(t => Head(t.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Slug).ToList(), StringComparer.Ordinal);

        var records = (await db.Entities
                .Select(e => new { e.Id, e.Slug, e.Name, e.Kind })
                .ToListAsync(cancellationToken))
            .Where(e => !ChapterSalience.WordsForGod.Contains(e.Slug))
            .ToList();
        var byId = records.ToDictionary(r => r.Id);
        var labels = await db.EntityNames
            .Where(n => n.Kind == ProperName || n.Kind == PlainName)
            .Select(n => new { n.EntityId, n.Label })
            .ToListAsync(cancellationToken);
        var named = records
            .Select(r => (r.Id, Name: Head(r.Name)))
            .Concat(labels.Where(l => byId.ContainsKey(l.EntityId)).Select(l => (Id: l.EntityId, Name: Head(l.Label))))
            .Where(pair => headed.ContainsKey(pair.Name))
            .Distinct()
            .ToList();
        var places = named
            .Where(pair => byId[pair.Id].Kind == EntityKind.Place)
            .Select(pair => pair.Name)
            .ToHashSet(StringComparer.Ordinal);

        var ids = named.Select(pair => pair.Id).Distinct().ToList();
        var listed = (await db.EntityVerses
                .Where(v => !v.Disputed && ids.Contains(v.EntityId))
                .Select(v => new { v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
                .ToListAsync(cancellationToken))
            .GroupBy(v => v.EntityId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(v => Key(v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse)).ToHashSet());

        var about = new Dictionary<string, TopicSubject>(StringComparer.Ordinal);
        foreach (var name in named.GroupBy(pair => pair.Name, StringComparer.Ordinal))
        {
            foreach (var topic in headed[name.Key])
            {
                if (!cited.TryGetValue(topic, out var verses) || verses.Count == 0)
                {
                    continue;
                }

                var shares = name
                    .Select(pair => pair.Id)
                    .Distinct()
                    .Select(id => (Id: id, Shared: listed.TryGetValue(id, out var at) ? verses.Count(at.Contains) : 0))
                    .Where(share => share.Shared > 0)
                    .ToList();
                var enough = AboutShare * verses.Count;

                // A heading shared by many bearers, JOHN or MARY, files each of them a little: the
                // topic is about the name's bearers together where none of them clears the share alone.
                var clearing = shares.Where(share => share.Shared >= enough).ToList();
                var bearers = (clearing.Count > 0 ? clearing : shares.Sum(share => share.Shared) >= enough ? shares : [])
                    .Select(share => byId[share.Id])
                    .ToList();
                if (bearers.Count > 0)
                {
                    about[topic] = new TopicSubject(
                        bearers.Select(b => b.Slug).ToHashSet(StringComparer.Ordinal),
                        bearers.Any(b => b.Kind is EntityKind.Person or EntityKind.People));
                }
            }
        }

        return (about, places);
    }

    private const string ProperName = "proper name";

    private const string PlainName = "name";

    private const string Article = "the ";

    /// <summary>
    /// A heading or a name as the two are compared: up to the first comma or bracket, lower case,
    /// without an article. <em>REBEKAH (REBECCA)</em> is Rebekah.
    /// </summary>
    internal static string Head(string name)
    {
        var end = name.IndexOfAny([',', '(']);
        var head = (end < 0 ? name : name[..end]).Trim().ToLowerInvariant();
        return head.StartsWith(Article, StringComparison.Ordinal) ? head[Article.Length..] : head;
    }

    private static int Key(int book, int chapter, int verse) => (book * 1000 + chapter) * 1000 + verse;
}

/// <summary>The weights, counted once and kept: as the process starts, or on the first chapter asked for.</summary>
internal sealed class ContextWeightsCache(IServiceScopeFactory scopes, ILogger<ContextWeightsCache> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ContextWeights? _weights;

    public async Task<ContextWeights> Get(CancellationToken cancellationToken)
    {
        if (_weights is { } weights)
        {
            return weights;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_weights is null)
            {
                var started = Stopwatch.StartNew();
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                _weights = await ContextWeights.Count(db, cancellationToken);
                logger.LogInformation("The Chapter Context's weights were counted in {Elapsed}", started.Elapsed);
            }

            return _weights;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Counts the weights ahead of the first reader; a failure here is retried by that reader's request.</summary>
    public async Task Warm()
    {
        try
        {
            await Get(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The Chapter Context's weights could not be counted ahead of the first request");
        }
    }
}
