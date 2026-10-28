using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Which marriage a brother- or sister-in-law is one through, where the line can tell.
///
/// <para>
/// <c>brother-in-law-of</c> does not say whose sibling the subject is, and English, German and Spanish
/// need not say it: <em>brother-in-law</em>, <em>Schwager</em>, <em>cuñado</em>. Ukrainian has a word
/// for each side and none for both: <em>шурин</em> is a wife's brother, <em>дівер</em> a husband's,
/// <em>зовиця</em> a husband's sister; <em>свояк</em> and <em>своячка</em> stand where the side is
/// not known. Hobab is Zipporah's brother and Zipporah is Moses' wife (NUM 10:29; EXO 2:21), so
/// Hobab is Moses' <em>шурин</em>.
/// </para>
///
/// <para>
/// The side is read from what the line already holds and nothing else: a brother or sister of the
/// subject's, stated or worked out from the parents, who is the target's wife or husband by a clause
/// of either of the two. A tie through the subject's own spouse — the husband of the target's sister
/// — is not looked for, and keeps the word that does not choose.
/// </para>
/// </summary>
internal static class InLaws
{
    /// <summary>The subject is a brother or sister of the target's wife.</summary>
    public const string Wife = "wife";

    /// <summary>The subject is a brother or sister of the target's husband.</summary>
    public const string Husband = "husband";

    /// <summary>
    /// For each brother- or sister-in-law clause among <paramref name="clauses"/> whose side the
    /// line can tell, keyed by its subject and target: <see cref="Wife"/> or <see cref="Husband"/>.
    /// </summary>
    public static async Task<Dictionary<(int Subject, int Target), string>> Through(
        AppDbContext db,
        IReadOnlyList<Descriptors.Clause> clauses,
        CancellationToken cancellationToken)
    {
        var inLaws = clauses
            .Where(c => c.Relation is DescriptorRelations.BrotherInLawOf or DescriptorRelations.SisterInLawOf)
            .ToList();
        if (inLaws.Count == 0)
        {
            return [];
        }

        var targets = inLaws.Select(c => c.TargetEntityId).Distinct().ToList();
        var married = await db.EntityDescriptors
            .Where(d => (d.Relation == DescriptorRelations.HusbandOf || d.Relation == DescriptorRelations.WifeOf)
                        && (targets.Contains(d.EntityId) || targets.Contains(d.TargetEntityId)))
            .Select(d => new { d.EntityId, d.TargetEntityId, d.Relation })
            .ToListAsync(cancellationToken);

        // The target's spouses, each as the target's wife or husband.
        var spouses = new HashSet<(int Target, int Spouse, string Side)>();
        foreach (var row in married)
        {
            // X husband-of Y: Y is X's wife, X is Y's husband; X wife-of Y the other way about.
            var wifeOfTheWriter = row.Relation == DescriptorRelations.HusbandOf;
            spouses.Add((row.EntityId, row.TargetEntityId, wifeOfTheWriter ? Wife : Husband));
            spouses.Add((row.TargetEntityId, row.EntityId, wifeOfTheWriter ? Husband : Wife));
        }

        var through = new Dictionary<(int Subject, int Target), string>();
        foreach (var clause in inLaws)
        {
            var siblings = clauses
                .Where(c => c.SubjectId == clause.SubjectId && FamilyTies.Siblings.Contains(c.Relation))
                .Select(c => c.TargetEntityId)
                .ToHashSet();
            var sides = spouses
                .Where(s => s.Target == clause.TargetEntityId && siblings.Contains(s.Spouse))
                .Select(s => s.Side)
                .Distinct()
                .ToList();
            if (sides.Count == 1)
            {
                through[(clause.SubjectId, clause.TargetEntityId)] = sides[0];
            }
        }

        return through;
    }
}
