using System.Linq.Expressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>An entity beside its name in the language asked for, so the database can order and file by it.</summary>
internal sealed class LocalisedEntity
{
    public required Entity Entity { get; init; }

    /// <summary>The name in the language asked for, or null where the corpus has none.</summary>
    public string? LocalName { get; init; }

    /// <summary>What a list shows: the local name, or the English headword where there is none.</summary>
    public required string Shown { get; init; }
}

/// <summary>
/// What an entity is called in a reader's language, and how a typed name finds it.
///
/// <para>
/// The entity is one record and its English headword is only one of its names. In another language
/// it is called, first, by the nominative this corpus holds for it in that language — a form a
/// generation pass produced with the name, which is what the descriptor lines already decline.
/// Failing that, in a language that does not decline its names, it is called by the heading spelling
/// of the text in that language that prints the name most often: the Reina Valera's <em>Aarón</em>.
/// Failing both it has no local name, and the caller shows the English one: a Latin name in a
/// Ukrainian list is a gap a reader can see, where an invented Ukrainian one would be a claim.
/// </para>
///
/// <para>
/// A declining language is kept off the second step because a name printed once is printed in
/// whatever case its sentence wanted: the Ohienko Bible's only Eran is <em>Ерана</em> and Luther's
/// only Sharar is <em>Sarars</em>, and a list headed with a genitive states a name nobody has.
/// </para>
///
/// <para>
/// The rule is written twice, once for a page of known entities and once as a query the database
/// can order and file by, because the provider cannot turn one into the other. A test holds the two
/// to the same answer.
/// </para>
/// </summary>
internal static class EntityNames
{
    /// <summary>The language the headwords are in, which therefore has no local name to add.</summary>
    private const string Headword = "eng";

    /// <summary>
    /// The alphabet an index in each language always offers. A name opening with any other letter —
    /// an English name in a Ukrainian list, where the corpus has no Ukrainian one — is filed after
    /// them under its own letter.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Alphabets = new Dictionary<string, string>
    {
        ["ukr"] = "АБВГҐДЕЄЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЬЮЯ",
        ["rus"] = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ",
    };

    private const string Latin = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// The languages whose texts print a name the same way in every sentence, so that the spelling a
    /// text heads with is the name and not one case of it.
    /// </summary>
    private static readonly HashSet<string> Undeclined = new(StringComparer.Ordinal) { "spa" };

    /// <summary>Whether a record with no form in this language is called by its text's printed spelling.</summary>
    public static bool NamesByThePrintedSpelling(string? language) => Local(language) is { } local && Undeclined.Contains(local);

    /// <summary>
    /// The collation a language's names sort in. Without it Postgres orders by the database's
    /// English rules, which put <em>Є</em> and <em>І</em> nowhere a Ukrainian reader looks for them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Collations = new Dictionary<string, string>
    {
        ["ukr"] = "uk-x-icu",
        ["rus"] = "ru-x-icu",
        ["deu"] = "de-x-icu",
        ["spa"] = "es-x-icu",
    };

    /// <summary>The language as the query spelled it, or null where it asks for the headword's.</summary>
    public static string? Local(string? language) =>
        language is { Length: > 0 } && !string.Equals(language, Headword, StringComparison.OrdinalIgnoreCase)
            ? language.ToLowerInvariant()
            : null;

    public static string Alphabet(string? language) =>
        Local(language) is { } local && Alphabets.TryGetValue(local, out var alphabet) ? alphabet : Latin;

    public static string? Collation(string? language) =>
        Local(language) is { } local ? Collations.GetValueOrDefault(local) : null;

    /// <summary>The entities beside their local names, for the database to order, file and page by.</summary>
    public static IQueryable<LocalisedEntity> Localised(AppDbContext db, IQueryable<Entity> entities, string? language)
    {
        var local = Local(language);
        if (local is null)
        {
            return entities.Select(e => new LocalisedEntity { Entity = e, LocalName = null, Shown = e.Name });
        }

        var printed = Undeclined.Contains(local);

        return entities
            .Select(e => new
            {
                Entity = e,
                LocalName = db.EntityNameForms
                                .Where(f => f.EntityId == e.Id && f.Language == local
                                            && f.GrammaticalCase == GrammaticalCases.Nominative)
                                .Select(f => f.Form)
                                .FirstOrDefault()
                            ?? db.EntityRenderings
                                .Where(r => printed && r.EntityId == e.Id && r.Heading && r.Text!.Language == local)
                                .OrderByDescending(r => r.Occurrences).ThenBy(r => r.TextId)
                                .Select(r => r.Form)
                                .FirstOrDefault(),
            })
            .Select(e => new LocalisedEntity { Entity = e.Entity, LocalName = e.LocalName, Shown = e.LocalName ?? e.Entity.Name });
    }

    /// <summary>
    /// The local names of a set of entities, keyed by entity id, holding only those that have one.
    /// Two indexed reads, whatever the size of the set.
    /// </summary>
    public static async Task<Dictionary<int, string>> Of(
        AppDbContext db,
        IReadOnlyCollection<int> entities,
        string? language,
        CancellationToken cancellationToken)
    {
        var local = Local(language);
        if (local is null || entities.Count == 0)
        {
            return [];
        }

        var names = await db.EntityNameForms
            .Where(f => entities.Contains(f.EntityId) && f.Language == local
                        && f.GrammaticalCase == GrammaticalCases.Nominative)
            .Select(f => new { f.EntityId, f.Form })
            .ToDictionaryAsync(f => f.EntityId, f => f.Form, cancellationToken);

        if (!Undeclined.Contains(local))
        {
            return names;
        }

        var printed = await db.EntityRenderings
            .Where(r => entities.Contains(r.EntityId) && r.Heading && r.Text!.Language == local)
            .Select(r => new { r.EntityId, r.TextId, r.Form, r.Occurrences })
            .ToListAsync(cancellationToken);

        foreach (var entity in printed.Where(r => !names.ContainsKey(r.EntityId)).GroupBy(r => r.EntityId))
        {
            names[entity.Key] = entity.OrderByDescending(r => r.Occurrences).ThenBy(r => r.TextId).First().Form;
        }

        return names;
    }

    /// <summary>
    /// The entities among these that only the source names, to a reader of a language other than
    /// English: the corpus holds no name for them in the language, and no verse of any text stands
    /// behind them, so there is no printed spelling to borrow either. A client shows the English
    /// headword for such a record and says whose name it is, where it shows the record at all.
    /// </summary>
    /// <param name="localNames">The names <see cref="Of"/> found for these entities.</param>
    public static async Task<HashSet<int>> NamedOnlyBySource(
        AppDbContext db,
        IReadOnlyCollection<int> entities,
        string? language,
        IReadOnlyDictionary<int, string> localNames,
        CancellationToken cancellationToken)
    {
        if (Local(language) is null)
        {
            return [];
        }

        var unnamed = entities.Where(id => !localNames.ContainsKey(id)).Distinct().ToList();
        if (unnamed.Count == 0)
        {
            return [];
        }

        var cited = await db.EntityVerses
            .Where(v => unnamed.Contains(v.EntityId))
            .Select(v => v.EntityId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. unnamed.Except(cited)];
    }

    /// <summary>
    /// The entities some name of which contains what was typed: the English headword, the slug, every
    /// label a dataset gives it, its name in any language this corpus holds, and every spelling any
    /// text prints for it — so <em>Аарон</em>, <em>Aarón</em>, <em>Aarons</em> and <em>אהרן</em> all
    /// find Aaron.
    /// </summary>
    /// <remarks>
    /// A title in front of the name is read past as well: <em>цар Давид</em> finds David by
    /// <em>Давид</em>, as <em>King David</em> finds him by the whole label.
    /// </remarks>
    public static IQueryable<Entity> Matching(AppDbContext db, IQueryable<Entity> entities, string q)
    {
        var matching = entities.Where(Containing(db, q, titles: true));
        return TitleWords.Without(q) is { } name
            ? matching.Union(entities.Where(Containing(db, name, titles: true)))
            : matching;
    }

    /// <summary>
    /// The labels a dataset gives as a title or a description rather than a name: <em>Son of
    /// David</em> on Jesus, <em>King of Israel</em> on David.
    /// </summary>
    private static readonly string[] TitleKinds = ["title", "description"];

    /// <summary>Whether a label is a title or a description rather than a name.</summary>
    public static bool IsTitle(string? kind) => kind is not null && TitleKinds.Contains(kind);

    /// <summary>
    /// Whether some name of the entity contains what was typed — and, with <paramref name="titles"/>,
    /// a title of it too.
    /// </summary>
    private static Expression<Func<Entity, bool>> Containing(AppDbContext db, string q, bool titles)
    {
        var like = LikePatterns.Containing(q);
        var folded = NameFolding.Fold(q);
        var foldedLike = LikePatterns.Containing(folded);
        var anyFolded = folded.Length > 0;

        return e =>
            EF.Functions.ILike(e.Name, like)
            || EF.Functions.ILike(e.Slug, like)
            || e.Names.Any(n => (titles || n.Kind == null || !TitleKinds.Contains(n.Kind)) && EF.Functions.ILike(n.Label, like))
            || db.EntityNameForms.Any(f => f.EntityId == e.Id && EF.Functions.ILike(f.Form, like))
            || (anyFolded && db.EntityRenderings.Any(r => r.EntityId == e.Id && EF.Functions.Like(r.Folded, foldedLike)));
    }

    /// <summary>
    /// A searched index, in the order of how well each row answers what was typed.
    ///
    /// The entity actually called that — in any of the names <see cref="Matching"/> reads, because
    /// Peter is Cephas and Aaron is <em>Аарон</em>, and a reader who types the other name has typed
    /// the thing itself — then those whose shown name opens with it, then those one of whose names
    /// holds it, and last those only a title holds it in: <em>David</em> is David's name and only
    /// inside a title of Jesus. Inside a band the corpus's own weight breaks the tie, so the Judah
    /// with a thousand namings comes before the Judah with two.
    /// </summary>
    public static IOrderedQueryable<LocalisedEntity> ByRelevance(
        AppDbContext db,
        IQueryable<LocalisedEntity> entities,
        string q,
        string? language) =>
        Alphabetical(
            Banded(db, entities, q).ThenByDescending(l => l.Entity.Verses.AsQueryable().Count(ShownVerses.IsShown)),
            language);

    /// <summary>
    /// The bands of <see cref="ByRelevance"/>, for an order that then sorts inside them by something
    /// else: an index ordered by how often each is named still heads with the one called what was
    /// typed, and ends with those that only carry it in a title.
    /// </summary>
    public static IOrderedQueryable<LocalisedEntity> Banded(AppDbContext db, IQueryable<LocalisedEntity> entities, string q)
    {
        var name = TitleWords.Without(q) ?? q;
        var exactly = LikePatterns.Exactly(q);
        var exactName = LikePatterns.Exactly(name);
        var opening = LikePatterns.StartingWith(q);
        var openingName = LikePatterns.StartingWith(name);

        // Each band beyond the shown name is a set of ids read once, which the database hashes,
        // rather than a test of every row's names and spellings one row at a time: the second costs
        // half a second on a two-letter search, which Go To asks as the reader types.
        var calledThat = name == q ? CalledExactly(db, q) : CalledExactly(db, q).Concat(CalledExactly(db, name));
        var named = name == q ? NamedContaining(db, q) : NamedContaining(db, q).Concat(NamedContaining(db, name));

        return entities.OrderBy(l =>
            EF.Functions.ILike(l.Shown, exactly) || EF.Functions.ILike(l.Shown, exactName)
            || calledThat.Contains(l.Entity.Id)
                ? 0
                : EF.Functions.ILike(l.Shown, opening) || EF.Functions.ILike(l.Shown, openingName)
                  || EF.Functions.ILike(l.Entity.Name, opening) || EF.Functions.ILike(l.Entity.Name, openingName)
                    ? 1
                    : named.Contains(l.Entity.Id)
                        ? 2
                        : 3);
    }

    /// <summary>The entities one of whose names is exactly what was typed, in any language or text.</summary>
    private static IQueryable<int> CalledExactly(AppDbContext db, string typed)
    {
        var exactly = LikePatterns.Exactly(typed);
        var folded = NameFolding.Fold(typed);

        return db.Entities.Where(e => EF.Functions.ILike(e.Name, exactly)).Select(e => e.Id)
            .Concat(db.EntityNames.Where(n => EF.Functions.ILike(n.Label, exactly)).Select(n => n.EntityId))
            .Concat(db.EntityNameForms
                .Where(f => f.GrammaticalCase == GrammaticalCases.Nominative && EF.Functions.ILike(f.Form, exactly))
                .Select(f => f.EntityId))
            .Concat(db.EntityRenderings.Where(r => folded.Length > 0 && r.Folded == folded).Select(r => r.EntityId));
    }

    /// <summary>The entities some name of which — not a title — holds what was typed.</summary>
    private static IQueryable<int> NamedContaining(AppDbContext db, string typed)
    {
        var like = LikePatterns.Containing(typed);
        var folded = NameFolding.Fold(typed);
        var foldedLike = LikePatterns.Containing(folded);

        return db.Entities.Where(e => EF.Functions.ILike(e.Name, like) || EF.Functions.ILike(e.Slug, like)).Select(e => e.Id)
            .Concat(db.EntityNames
                .Where(n => (n.Kind == null || !TitleKinds.Contains(n.Kind)) && EF.Functions.ILike(n.Label, like))
                .Select(n => n.EntityId))
            .Concat(db.EntityNameForms.Where(f => EF.Functions.ILike(f.Form, like)).Select(f => f.EntityId))
            .Concat(db.EntityRenderings
                .Where(r => folded.Length > 0 && EF.Functions.Like(r.Folded, foldedLike))
                .Select(r => r.EntityId));
    }

    /// <summary>
    /// The name each of these entities was found by, where a reader looking at the row would not see
    /// it: <em>Варнава</em> finds the record headed <em>Йосип</em>, <em>Paul</em> the one headed
    /// <em>Saul</em>, and <em>David</em> finds Jesus only through <em>Son of David</em>. Absent where
    /// the row's own name holds what was typed, in whatever language or case — the reader can see why
    /// <em>Давида</em> found <em>Давид</em>.
    /// </summary>
    /// <param name="shown">The name each row is headed with, by entity id.</param>
    /// <param name="renamed">
    /// The rows headed by another name than the one the record is filed under, whose filed name is
    /// therefore one of the other names a search can find them by.
    /// </param>
    public static async Task<Dictionary<int, EntityMatchResponse>> FoundAs(
        AppDbContext db,
        IReadOnlyDictionary<int, string> shown,
        string q,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, RenamedHeading>? renamed = null)
    {
        string[] typed = [.. new[] { q, TitleWords.Without(q) }.OfType<string>().Select(NameFolding.Fold).Where(t => t.Length > 0)];
        var ids = shown.Keys.ToList();
        if (typed.Length == 0 || ids.Count == 0)
        {
            return [];
        }

        var headwords = await db.Entities
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.Name })
            .ToListAsync(cancellationToken);
        var nominatives = await db.EntityNameForms
            .Where(f => ids.Contains(f.EntityId) && f.GrammaticalCase == GrammaticalCases.Nominative)
            .Select(f => new { f.EntityId, f.Form })
            .ToListAsync(cancellationToken);
        var labels = await db.EntityNames
            .Where(n => ids.Contains(n.EntityId))
            .Select(n => new { n.EntityId, n.Label, n.Kind })
            .ToListAsync(cancellationToken);
        var whole = LikePatterns.Containing(typed[0]);
        var named = LikePatterns.Containing(typed[^1]);
        var spellings = await db.EntityRenderings
            .Where(r => ids.Contains(r.EntityId) && (EF.Functions.Like(r.Folded, whole) || EF.Functions.Like(r.Folded, named)))
            .Select(r => new { r.EntityId, r.Form, r.Folded, r.Occurrences, r.Heading })
            .ToListAsync(cancellationToken);

        bool Holds(string name) => typed.Any(NameFolding.Fold(name).Contains);
        bool Opens(string folded) => typed.Any(folded.StartsWith);

        var found = new Dictionary<int, EntityMatchResponse>();
        foreach (var (id, heading) in shown)
        {
            var own = new List<string> { heading };
            if (renamed?.GetValueOrDefault(id) is { } heads)
            {
                own.Add(heads.Name);
            }
            else
            {
                own.AddRange(headwords.Where(h => h.Id == id).Select(h => h.Name));
                own.AddRange(nominatives.Where(f => f.EntityId == id).Select(f => f.Form));
            }
            if (own.Any(Holds))
            {
                continue;
            }

            var label = labels
                .Where(n => n.EntityId == id && Holds(n.Label))
                .OrderBy(n => IsTitle(n.Kind))
                .ThenBy(n => !Opens(NameFolding.Fold(n.Label)))
                .ThenBy(n => n.Label.Length)
                .FirstOrDefault();
            if (label is not null)
            {
                found[id] = new EntityMatchResponse(label.Label, IsTitle(label.Kind) ? MatchKinds.Title : MatchKinds.Name);
                continue;
            }

            var spelling = spellings
                .Where(r => r.EntityId == id && typed.Any(r.Folded.Contains))
                .OrderBy(r => !Opens(r.Folded))
                .ThenByDescending(r => r.Heading)
                .ThenByDescending(r => r.Occurrences)
                .FirstOrDefault();
            if (spelling is not null && !own.Any(name => SameName(NameFolding.Fold(name), spelling.Folded)))
            {
                found[id] = new EntityMatchResponse(spelling.Form, MatchKinds.Name);
            }
        }

        return found;
    }

    /// <summary>
    /// Whether a printed spelling is one case of a name rather than another name: <em>Йосипа</em> and
    /// <em>Йосипові</em> are <em>Йосип</em> and <em>Павла</em> is <em>Павло</em>; <em>Варнава</em> is
    /// not <em>Йосип</em>, and <em>Авраам</em> is not <em>Аврам</em>. Both are folded. A declined form keeps
    /// the whole name, less the vowel or soft sign a case ending replaces.
    /// </summary>
    internal static bool SameName(string name, string spelling) =>
        name.Length > 0 && spelling.StartsWith(Stem(name), StringComparison.Ordinal);

    /// <summary>A folded name without the last letter a case ending may replace.</summary>
    private static string Stem(string name) =>
        name.Length > MinimumStem && ReplacedByEndings.Contains(name[^1]) ? name[..^1] : name;

    /// <summary>The letters a case ending takes the place of: <em>Павл-о</em>, <em>Ілл-я</em>, <em>Но-й</em>.</summary>
    private const string ReplacedByEndings = "аеєиіїоуюяыэйьaeiouy";

    /// <summary>The fewest letters a stem keeps, so a two-letter name is not reduced to one.</summary>
    private const int MinimumStem = 2;

    /// <summary>By the shown name in the language's own order, and by slug where two names are the same.</summary>
    public static IOrderedQueryable<LocalisedEntity> Alphabetical(
        IOrderedQueryable<LocalisedEntity> entities,
        string? language) =>
        Collation(language) is { } collation
            ? entities.ThenBy(l => EF.Functions.Collate(l.Shown, collation)).ThenBy(l => l.Entity.Slug)
            : entities.ThenBy(l => l.Shown).ThenBy(l => l.Entity.Slug);

    /// <summary>The same order, from nothing.</summary>
    public static IOrderedQueryable<LocalisedEntity> Alphabetical(
        IQueryable<LocalisedEntity> entities,
        string? language) =>
        Collation(language) is { } collation
            ? entities.OrderBy(l => EF.Functions.Collate(l.Shown, collation)).ThenBy(l => l.Entity.Slug)
            : entities.OrderBy(l => l.Shown).ThenBy(l => l.Entity.Slug);
}
