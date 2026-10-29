using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Read">Peoples whose descent a clause read from a verse already states.</param>
/// <param name="Derived">Peoples given their ancestor here, from the patronymic Strong states.</param>
/// <param name="Ambiguous">
/// Peoples whose ancestor's name several persons bear, none of them the people's own origin: left
/// without one rather than given a namesake, each listed with the persons it could be.
/// </param>
/// <param name="Unheld">Peoples Strong derives from a name no person in the encyclopedia bears.</param>
internal sealed record PeopleAncestorOutcome(
    int Peoples,
    int Read,
    int Derived,
    int Written,
    int Withdrawn,
    IReadOnlyList<(string People, IReadOnlyList<string> Persons)> Ambiguous,
    IReadOnlyList<string> Unheld,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{Read + Derived} of {Peoples} peoples name the person they came from: {Read} by a verse, {Derived} by "
        + $"the patronymic Strong's Dictionary states ({Written} written, {Withdrawn} withdrawn). "
        + $"{Ambiguous.Count} left without one because several persons bear the name: "
        + string.Join("; ", Ambiguous.Select(a => $"{a.People} ({string.Join(", ", a.Persons)})"))
        + $". {Unheld.Count} derived from a name no person here bears: {string.Join(", ", Unheld)}; in {Elapsed}";
}

/// <summary>
/// Whom each people came from, where Strong's Dictionary says it: <em>Moabites</em> is his
/// patronymic of H4124, which is Moab's name, so the Moabites are Moab's descendants and their page
/// links him.
///
/// <para>
/// A people a verse already states the descent of keeps that statement and is given nothing here.
/// For the rest, the patronymic's origin number is matched to the persons whose names carry it: one
/// person is the ancestor, and of several the one the people's own record names as its origin (the
/// tribes the owner's file ties to Jacob's sons). Several and none of them its origin is left open
/// rather than guessed, and so is a patrial, which names a place.
/// </para>
///
/// <para>
/// The rows are Strong's statement and carry no verse, which is what <see cref="LinkMethod.StatedBySource"/>
/// is for. Every load settles them again: a row the patronymic no longer gives is withdrawn, and a
/// second load writes nothing.
/// </para>
/// </summary>
internal sealed class PeopleAncestorLoader(AppDbContext db, ILogger<PeopleAncestorLoader> logger)
{
    public const string Source = StrongGentilicLoader.Source;

    public const string Relation = "descendants-of";

    /// <summary>The relations a verse states a people's descent by.</summary>
    private static readonly string[] Descent = [Relation, "descendant-of"];

    private const string Patronymic = "patronymic";

    private const string PatronymicOrPatrial = "patronymic or patrial";

    public async Task<PeopleAncestorOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var peoples = await db.Entities
            .Where(e => e.Kind == EntityKind.People)
            .Select(e => new { e.Id, e.Slug, e.OriginEntityId })
            .ToListAsync(cancellationToken);

        var read = (await db.EntityRelationships
                .Where(r => Descent.Contains(r.Type)
                            && r.From!.Kind == EntityKind.People
                            && r.To!.Kind == EntityKind.Person
                            && !(r.Method == LinkMethod.StatedBySource && r.Source == Source))
                .Select(r => r.FromEntityId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var gentilics = await db.StrongGentilics
            .Where(g => g.PeopleEntityId != null && (g.Kind == Patronymic || g.Kind == PatronymicOrPatrial))
            .Select(g => new { People = g.PeopleEntityId!.Value, g.OriginNumber, g.Kind })
            .ToListAsync(cancellationToken);

        var numbers = gentilics.Select(g => g.OriginNumber).Distinct().ToList();
        var bearers = (await db.EntityNames
                .Where(n => n.HebrewStrongNumber != null && numbers.Contains(n.HebrewStrongNumber))
                .Select(n => new { Number = n.HebrewStrongNumber!, n.Entity!.Id, n.Entity.Slug, n.Entity.Kind })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.Number, StringComparer.Ordinal)
            .ToDictionary(b => b.Key, b => b.ToList(), StringComparer.Ordinal);

        var wanted = new Dictionary<int, int>();
        var ambiguous = new List<(string, IReadOnlyList<string>)>();
        var unheld = new List<string>();
        foreach (var people in peoples.Where(p => !read.Contains(p.Id)))
        {
            var stated = gentilics.Where(g => g.People == people.Id).ToList();
            if (stated.Count == 0)
            {
                continue;
            }

            var named = stated
                .SelectMany(g => bearers.GetValueOrDefault(g.OriginNumber) ?? [])
                .Where(b => b.Id != people.Id)
                .ToList();
            var persons = named.Where(b => b.Kind == EntityKind.Person).DistinctBy(b => b.Id).ToList();

            // Strong could not say whether the name is a man's or a place's; a place bearing it too
            // leaves it his doubt and not a descent.
            if (stated.All(g => g.Kind == PatronymicOrPatrial) && named.Any(b => b.Kind != EntityKind.Person))
            {
                continue;
            }

            if (persons.FirstOrDefault(b => b.Id == people.OriginEntityId) is { } origin)
            {
                wanted[people.Id] = origin.Id;
            }
            else if (persons.Count == 1)
            {
                wanted[people.Id] = persons[0].Id;
            }
            else if (persons.Count > 1)
            {
                ambiguous.Add((people.Slug, persons.Select(b => b.Slug).Order(StringComparer.Ordinal).ToList()));
            }
            else
            {
                unheld.Add(people.Slug);
            }
        }

        var standing = await db.EntityRelationships
            .Where(r => r.Type == Relation && r.Method == LinkMethod.StatedBySource && r.Source == Source)
            .ToListAsync(cancellationToken);
        var gone = standing
            .Where(r => !wanted.TryGetValue(r.FromEntityId, out var ancestor) || ancestor != r.ToEntityId)
            .ToList();
        var duplicates = standing.Except(gone)
            .GroupBy(r => r.FromEntityId)
            .SelectMany(rows => rows.OrderBy(r => r.Id).Skip(1))
            .ToList();
        gone.AddRange(duplicates);
        var held = standing.Except(gone).Select(r => r.FromEntityId).ToHashSet();
        var rows = wanted
            .Where(w => !held.Contains(w.Key))
            .Select(w => new EntityRelationship
            {
                FromEntityId = w.Key,
                ToEntityId = w.Value,
                Type = Relation,
                Category = RelationshipCategories.Read,
                Method = LinkMethod.StatedBySource,
                Confidence = null,
                Source = Source,
            })
            .ToList();

        if (rows.Count > 0 || gone.Count > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.EntityRelationships.RemoveRange(gone);
            db.EntityRelationships.AddRange(rows);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var outcome = new PeopleAncestorOutcome(
            peoples.Count,
            read.Count,
            wanted.Count,
            rows.Count,
            gone.Count,
            ambiguous,
            unheld.Order(StringComparer.Ordinal).ToList(),
            started.Elapsed);
        logger.LogInformation("Whom the peoples came from: {Outcome}", outcome);
        return outcome;
    }
}
