using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a person's tribe names, and who belongs to a tribe or a people. A record holds its tribe as
/// the datasets write it (<em>Levi</em>, <em>Edomite</em>); an Israelite tribe is its patriarch, and
/// anything else is a people, written as one of its members, as the people or as the land or father it
/// is named after. A people's page lists everyone whose tribe names it or its patriarch, and everyone a
/// clause of ours says is of it.
/// </summary>
internal static class Tribes
{
    /// <param name="Patriarch">True where the value names the father of an Israelite tribe.</param>
    internal sealed record Named(int Id, string Slug, EntityKind Kind, string Name, bool Patriarch);

    /// <summary>Who belongs to a people: the tribe values that name it and the records a clause may name.</summary>
    /// <param name="Tribe">True where it is an Israelite tribe, named through its patriarch.</param>
    internal sealed record Membership(IReadOnlyList<string> Values, IReadOnlyList<int> Targets, bool Tribe);

    internal static readonly string[] Relations = ["of-tribe", "of-people"];

    /// <summary>
    /// The record each tribe value names: the one patriarch of that name, else the one people spelled so
    /// (as the value, with <em>s</em> or with <em>ites</em>). A value two patriarchs answer, or nothing,
    /// names no record.
    /// </summary>
    internal static async Task<Dictionary<string, Named>> Resolve(
        AppDbContext db, IReadOnlyCollection<string> values, CancellationToken cancellationToken)
    {
        var named = new Dictionary<string, Named>(StringComparer.Ordinal);
        if (values.Count == 0) return named;

        var tribalIds = db.EntityDescriptors.Where(d => d.Relation == "of-tribe").Select(d => d.TargetEntityId);
        var patriarchIds = db.EntityDescriptors.Where(d => tribalIds.Contains(d.EntityId)
            && d.Relation == "descendants-of" && d.Target!.Kind == EntityKind.Person).Select(d => d.TargetEntityId);
        var patriarchs = await db.Entities.Where(e => values.Contains(e.Name) && e.Kind == EntityKind.Person
                && (tribalIds.Contains(e.Id) || patriarchIds.Contains(e.Id)))
            .Select(e => new { e.Id, e.Slug, e.Kind, e.Name })
            .ToListAsync(cancellationToken);
        foreach (var value in patriarchs.GroupBy(p => p.Name).Where(g => g.Count() == 1).Select(g => g.Single()))
        {
            named[value.Name] = new Named(value.Id, value.Slug, value.Kind, value.Name, true);
        }

        var unanswered = values.Where(v => patriarchs.All(p => p.Name != v)).ToList();
        if (unanswered.Count == 0) return named;

        var spellings = unanswered.SelectMany(Spellings).Distinct().ToList();
        var peoples = await db.Entities
            .Where(e => e.Kind == EntityKind.People
                && (spellings.Contains(e.Name) || e.Names.Any(n => spellings.Contains(n.Label))))
            .Select(e => new { e.Id, e.Slug, e.Kind, e.Name, Labels = e.Names.Where(n => spellings.Contains(n.Label)).Select(n => n.Label).ToList() })
            .ToListAsync(cancellationToken);
        foreach (var value in unanswered)
        {
            var spelled = Spellings(value).ToHashSet(StringComparer.Ordinal);
            var answers = peoples.Where(p => spelled.Contains(p.Name) || p.Labels.Any(spelled.Contains)).ToList();
            if (answers.Count == 1)
            {
                named[value] = new Named(answers[0].Id, answers[0].Slug, answers[0].Kind, answers[0].Name, false);
            }
        }

        return named;
    }

    private static string[] Spellings(string value) => [value, value + "s", value + "ites"];

    /// <summary>Who belongs to the people <paramref name="people"/>: every tribe value naming it or its patriarch.</summary>
    internal static async Task<Membership> Of(AppDbContext db, int people, int? origin, CancellationToken cancellationToken)
    {
        var values = await db.Entities.Where(e => e.Kind == EntityKind.Person && e.Tribe != null)
            .Select(e => e.Tribe!).Distinct().ToListAsync(cancellationToken);
        var named = await Resolve(db, values, cancellationToken);
        var naming = named.Where(n => n.Value.Id == people || n.Value.Id == origin).ToList();
        return new Membership(
            [.. naming.Select(n => n.Key).Order(StringComparer.Ordinal)],
            origin is { } father ? [people, father] : [people],
            naming.Any(n => n.Value.Patriarch && n.Value.Id == origin));
    }

    /// <summary>The persons of a people: by the tribe on their record, or by a clause saying they are of it.</summary>
    internal static IQueryable<Entity> Members(AppDbContext db, IQueryable<Entity> entities, Membership membership)
    {
        var values = membership.Values;
        var targets = membership.Targets;
        var stated = db.EntityDescriptors
            .Where(d => Relations.Contains(d.Relation) && targets.Contains(d.TargetEntityId))
            .Select(d => d.EntityId);
        return entities.Where(e => e.Kind == EntityKind.Person
            && ((e.Tribe != null && values.Contains(e.Tribe)) || stated.Contains(e.Id)));
    }

    /// <summary>
    /// The Israelite tribes, each as its people with how many persons belong to it: a people whose
    /// patriarch some person's tribe names.
    /// </summary>
    internal static async Task<IReadOnlyList<(int Id, string Slug, Membership Membership)>> Israel(
        AppDbContext db, CancellationToken cancellationToken)
    {
        var values = await db.Entities.Where(e => e.Kind == EntityKind.Person && e.Tribe != null)
            .Select(e => e.Tribe!).Distinct().ToListAsync(cancellationToken);
        var named = await Resolve(db, values, cancellationToken);
        var fathers = named.Values.Where(n => n.Patriarch).Select(n => n.Id).Distinct().ToList();
        var peoples = await db.Entities
            .Where(e => e.Kind == EntityKind.People && e.OriginEntityId != null && fathers.Contains(e.OriginEntityId.Value))
            .Select(e => new { e.Id, e.Slug, Origin = e.OriginEntityId!.Value })
            .ToListAsync(cancellationToken);
        return
        [
            .. peoples
                .GroupBy(p => p.Origin)
                .Where(g => g.Count() == 1)
                .Select(g => g.Single())
                .Select(p => (p.Id, p.Slug, new Membership(
                    [.. named.Where(n => n.Value.Id == p.Origin).Select(n => n.Key).Order(StringComparer.Ordinal)],
                    [p.Id, p.Origin],
                    true))),
        ];
    }
}
