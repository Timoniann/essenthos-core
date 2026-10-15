using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The family clauses of a person's line: which are read from the other person's record as well,
/// how one is turned to this side, and the order the line says them in.
/// </summary>
internal static class FamilyTies
{
    private static readonly string[] Parents = [DescriptorRelations.SonOf, DescriptorRelations.DaughterOf];

    private static readonly string[] Spouses =
        [DescriptorRelations.HusbandOf, DescriptorRelations.WifeOf, DescriptorRelations.ConcubineOf];

    private static readonly string[] Children =
        [DescriptorRelations.FatherOf, DescriptorRelations.MotherOf, DescriptorRelations.AdoptiveMotherOf];

    public static readonly IReadOnlySet<string> Siblings = new HashSet<string>(StringComparer.Ordinal)
    {
        DescriptorRelations.BrotherOf, DescriptorRelations.SisterOf,
        DescriptorRelations.HalfBrotherOf, DescriptorRelations.HalfSisterOf,
    };

    /// <summary>
    /// The relations a clause written on the other person is read for: a parent, a spouse, a child or
    /// a sibling. An array, so a query can send it as one parameter.
    /// </summary>
    public static readonly string[] Turnable =
    [
        DescriptorRelations.SonOf, DescriptorRelations.DaughterOf,
        DescriptorRelations.FatherOf, DescriptorRelations.MotherOf,
        DescriptorRelations.HusbandOf, DescriptorRelations.WifeOf,
        DescriptorRelations.BrotherOf, DescriptorRelations.SisterOf,
        DescriptorRelations.HalfBrotherOf, DescriptorRelations.HalfSisterOf,
    ];

    private static readonly IReadOnlySet<string> SaidOfAMan = new HashSet<string>(StringComparer.Ordinal)
    {
        DescriptorRelations.SonOf, DescriptorRelations.FatherOf, DescriptorRelations.HusbandOf,
        DescriptorRelations.BrotherOf, DescriptorRelations.HalfBrotherOf,
    };

    private static readonly IReadOnlySet<string> SaidOfAWoman = new HashSet<string>(StringComparer.Ordinal)
    {
        DescriptorRelations.DaughterOf, DescriptorRelations.MotherOf, DescriptorRelations.WifeOf,
        DescriptorRelations.SisterOf, DescriptorRelations.HalfSisterOf,
    };

    /// <summary>Whether a run of this relation is counted once it names more than a few: children and siblings.</summary>
    public static bool Counts(string relation) => Children.Contains(relation) || Siblings.Contains(relation);

    /// <summary>
    /// A clause written on the other person, said from this side: <c>son-of</c> on Reuben's record is
    /// <c>father-of</c> on Jacob's. Where the turned relation depends on this person's sex —
    /// <em>father</em> or <em>mother</em>, <em>brother</em> or <em>sister</em> — and the sex is not
    /// known, null.
    /// </summary>
    public static string? Turn(string relation, string? sex)
    {
        var options = RelationshipVocabulary.Reversed(relation);
        if (options.Count == 1)
        {
            return options.First();
        }

        var said = sex switch
        {
            SharedParents.Male => SaidOfAMan,
            SharedParents.Female => SaidOfAWoman,
            _ => null,
        };
        return said is null ? null : options.FirstOrDefault(said.Contains);
    }

    /// <summary>The sex a family relation states of the person it is written on: a son is male.</summary>
    public static string? SexOfTheWriter(string relation) =>
        SaidOfAMan.Contains(relation) ? SharedParents.Male
        : SaidOfAWoman.Contains(relation) ? SharedParents.Female
        : null;

    /// <summary>
    /// The turned clauses that say something the person's own clauses do not. One that repeats an own
    /// clause stays, as more evidence for it. One that gives the same pair another answer to the same
    /// question — <em>brother</em> where the own record says <em>half-brother</em> — gives way to the
    /// record's own, unless it is the closer of the two: <em>father</em> over an own
    /// <em>ancestor</em>, which it then replaces.
    /// </summary>
    public static List<Descriptors.Clause> Folded(IReadOnlyList<Descriptors.Clause> clauses)
    {
        var dropped = new HashSet<Descriptors.Clause>(ReferenceEqualityComparer.Instance);
        foreach (var pair in clauses.GroupBy(c => (c.Slug, c.TargetEntityId)))
        {
            var own = pair.Where(c => c.Stated is null).ToList();
            var answers = own.Select(c => c.Relation).ToHashSet(StringComparer.Ordinal);
            foreach (var turned in pair.Where(c => c.Stated is not null))
            {
                if (answers.Contains(turned.Relation))
                {
                    continue;
                }

                var rivals = answers
                    .Where(answer => RelationshipVocabulary.Branch(answer) == RelationshipVocabulary.Branch(turned.Relation))
                    .ToList();
                if (rivals.Count == 0)
                {
                    continue;
                }

                // The owner's own word on a tie, written on the other person, outranks a reading of
                // this person's record: Simeon's record says half-brother of Benjamin by his ruling of
                // 2026-09-12, and Benjamin's own record still said brother.
                var ruled = turned.Method == LinkMethod.Manual
                            && own.Where(c => rivals.Contains(c.Relation)).All(c => c.Method != LinkMethod.Manual);
                if (ruled || rivals.All(rival => RelationshipVocabulary.Implies(turned.Relation, rival)))
                {
                    dropped.UnionWith(own.Where(c => rivals.Contains(c.Relation)));
                }
                else
                {
                    dropped.Add(turned);
                }
            }
        }

        return [.. clauses.Where(c => !dropped.Contains(c))];
    }

    /// <summary>
    /// The offices a person held, which the line says before anything else: <em>king of the
    /// Israelites, son of Jesse</em>, as the owner asked on 2026-10-15. Among themselves they keep the
    /// order they were written in.
    /// </summary>
    private static readonly IReadOnlySet<string> Offices = new HashSet<string>(StringComparer.Ordinal)
    {
        DescriptorRelations.KingOf, DescriptorRelations.QueenOf, DescriptorRelations.JudgeOf, DescriptorRelations.LeaderOf,
        DescriptorRelations.ProphetTo, DescriptorRelations.HighPriestOf, DescriptorRelations.PriestOf,
        DescriptorRelations.ApostleOf, DescriptorRelations.GovernorOf, DescriptorRelations.TetrarchOf,
        DescriptorRelations.CommanderOf, DescriptorRelations.ScribeOf,
    };

    /// <summary>
    /// The order the line says its clauses in: offices, parents, spouses, children, siblings, then the rest.
    /// Within each family group a relation's clauses stand together, the person's own first and in the
    /// order written, the other records' after them in the order of their verses; the rest keep the
    /// order they were written in.
    /// </summary>
    public static IEnumerable<Descriptors.Clause> Ordered(IReadOnlyList<Descriptors.Clause> clauses)
    {
        var placed = clauses
            .Select((clause, position) => (Clause: clause, Position: position + (clause.Stated is null ? 0 : clauses.Count)))
            .ToList();
        var first = placed
            .GroupBy(p => p.Clause.Relation, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Min(p => p.Position), StringComparer.Ordinal);
        return placed
            .OrderBy(p => Rank(p.Clause.Relation))
            .ThenBy(p => Rank(p.Clause.Relation) is Office || (Rank(p.Clause.Relation) == Rest && p.Clause.Relation != DescriptorRelations.AncestorOf)
                ? p.Position
                : first[p.Clause.Relation])
            .ThenBy(p => p.Position)
            .Select(p => p.Clause);
    }

    /// <summary>Where a relation stands in the line: offices, parents, spouses, children, siblings, the rest.</summary>
    public static int Rank(string relation) =>
        Offices.Contains(relation) ? Office
        : Parents.Contains(relation) ? 0
        : Spouses.Contains(relation) ? 1
        : Children.Contains(relation) ? ChildrenRank
        : Siblings.Contains(relation) ? 3
        : Rest;

    /// <summary>Everything that is not a parent, spouse, child or sibling, which keeps the order it was written in.</summary>
    private const int Rest = 4;

    /// <summary>The children's place in the line, which a count of children takes where no child is named.</summary>
    public const int ChildrenRank = 2;

    /// <summary>The offices, before the family.</summary>
    private const int Office = -1;
}
