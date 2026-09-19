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
    /// The entities some name of which contains what was typed: the English headword, the slug, every
    /// label a dataset gives it, its name in any language this corpus holds, and every spelling any
    /// text prints for it — so <em>Аарон</em>, <em>Aarón</em>, <em>Aarons</em> and <em>אהרן</em> all
    /// find Aaron.
    /// </summary>
    public static IQueryable<Entity> Matching(AppDbContext db, IQueryable<Entity> entities, string q)
    {
        var like = LikePatterns.Containing(q);
        var folded = NameFolding.Fold(q);
        var foldedLike = LikePatterns.Containing(folded);
        var anyFolded = folded.Length > 0;

        return entities.Where(e =>
            EF.Functions.ILike(e.Name, like)
            || EF.Functions.ILike(e.Slug, like)
            || e.Names.Any(n => EF.Functions.ILike(n.Label, like))
            || db.EntityNameForms.Any(f => f.EntityId == e.Id && EF.Functions.ILike(f.Form, like))
            || (anyFolded && db.EntityRenderings.Any(r => r.EntityId == e.Id && EF.Functions.Like(r.Folded, foldedLike))));
    }

    /// <summary>
    /// A searched index, in the order of how well each row answers what was typed.
    ///
    /// The entity actually called that — in any of the names <see cref="Matching"/> reads, because
    /// Peter is Cephas and Aaron is <em>Аарон</em>, and a reader who types the other name has typed
    /// the thing itself — then those whose shown name opens with it, then everything it merely
    /// occurs in. Inside a band the corpus's own weight breaks the tie, so the Judah with a thousand
    /// namings comes before the Judah with two.
    /// </summary>
    public static IOrderedQueryable<LocalisedEntity> ByRelevance(
        AppDbContext db,
        IQueryable<LocalisedEntity> entities,
        string q,
        string? language)
    {
        var exactly = LikePatterns.Exactly(q);
        var opening = LikePatterns.StartingWith(q);
        var folded = NameFolding.Fold(q);
        var anyFolded = folded.Length > 0;

        return Alphabetical(
            entities
                .OrderBy(l =>
                    EF.Functions.ILike(l.Shown, exactly)
                    || EF.Functions.ILike(l.Entity.Name, exactly)
                    || l.Entity.Names.Any(n => EF.Functions.ILike(n.Label, exactly))
                    || db.EntityNameForms.Any(f => f.EntityId == l.Entity.Id
                                                   && f.GrammaticalCase == GrammaticalCases.Nominative
                                                   && EF.Functions.ILike(f.Form, exactly))
                    || (anyFolded && db.EntityRenderings.Any(r => r.EntityId == l.Entity.Id && r.Folded == folded))
                        ? 0
                        : EF.Functions.ILike(l.Shown, opening) || EF.Functions.ILike(l.Entity.Name, opening) ? 1 : 2)
                .ThenByDescending(l => l.Entity.Verses.Count),
            language);
    }

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
