using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// How another witness's relation names line up with this corpus's own, and which of them are
/// answers to the same question.
///
/// BibleData writes <c>son</c> where <see cref="DescriptorRelations"/> writes <c>son-of</c>, states
/// <em>Bani is the ancestor of Adaiah</em> where the encyclopedia says <em>Adaiah is a descendant
/// of Bani</em>, and names twenty-three relations the vocabulary has no word for at all. None of
/// that can be settled by string comparison, and the descriptor pass already had to answer it to
/// score itself against the same rows — so the table here is that scorer's, moved to where the
/// loader can use it too. One answer to <em>do these two witnesses say the same thing</em>, not two.
/// </summary>
internal static class RelationshipVocabulary
{
    /// <summary>
    /// BibleData's type names against the vocabulary, in the same direction: <c>isaac son abraham</c>
    /// is Isaac the son of Abraham.
    ///
    /// <para>
    /// What is absent is absent from the vocabulary rather than from this table, and a type with no
    /// entry is a claim this corpus cannot compare itself with — <c>cousin</c>, <c>rabbi</c>,
    /// <c>concubinator</c>, <c>Creator</c>. Those neither corroborate nor contradict anything;
    /// they are counted, and what we say about the same pair stands on its own.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Says =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["son"] = DescriptorRelations.SonOf,
            ["daughter"] = DescriptorRelations.DaughterOf,
            ["father"] = DescriptorRelations.FatherOf,
            ["mother"] = DescriptorRelations.MotherOf,
            ["brother"] = DescriptorRelations.BrotherOf,
            ["sister"] = DescriptorRelations.SisterOf,
            ["husband"] = DescriptorRelations.HusbandOf,
            ["wife"] = DescriptorRelations.WifeOf,
            ["half-brother"] = DescriptorRelations.HalfBrotherOf,
            ["half-sister"] = DescriptorRelations.HalfSisterOf,
            ["grandfather"] = DescriptorRelations.GrandfatherOf,
            ["grandmother"] = DescriptorRelations.GrandmotherOf,
            ["grandson"] = DescriptorRelations.GrandsonOf,
            ["granddaughter"] = DescriptorRelations.GranddaughterOf,
            ["uncle"] = DescriptorRelations.UncleOf,
            ["aunt"] = DescriptorRelations.AuntOf,
            ["nephew"] = DescriptorRelations.NephewOf,
            ["ancestor"] = DescriptorRelations.AncestorOf,
            ["descendant"] = DescriptorRelations.DescendantOf,
            ["father-in-law"] = DescriptorRelations.FatherInLawOf,
            ["mother-in-law"] = DescriptorRelations.MotherInLawOf,
            ["son-in-law"] = DescriptorRelations.SonInLawOf,
            ["daughter-in-law"] = DescriptorRelations.DaughterInLawOf,
            ["brother-in-law"] = DescriptorRelations.BrotherInLawOf,
            ["sister-in-law"] = DescriptorRelations.SisterInLawOf,
            ["concubine"] = DescriptorRelations.ConcubineOf,
            ["servant"] = DescriptorRelations.ServantOf,
            ["master"] = DescriptorRelations.MasterOf,
            ["disciple"] = DescriptorRelations.DiscipleOf,
            ["apostle"] = DescriptorRelations.ApostleOf,
            ["king"] = DescriptorRelations.KingOf,
            ["killer"] = DescriptorRelations.KillerOf,
            ["killed by"] = DescriptorRelations.KilledBy,
            ["army commander"] = DescriptorRelations.CommanderOf,

            // The same relations under the dataset's other names for them, which a reading of the
            // verse gives in this vocabulary's words: a captain commands, an officer or chamberlain
            // serves, a lady is the mistress of her maid.
            ["army captain"] = DescriptorRelations.CommanderOf,
            ["officer"] = DescriptorRelations.ServantOf,
            ["chamberlain"] = DescriptorRelations.ServantOf,
            ["chief official"] = DescriptorRelations.ServantOf,
            ["lieutenant"] = DescriptorRelations.ServantOf,
            ["chief"] = DescriptorRelations.MasterOf,
            ["lady"] = DescriptorRelations.MasterOf,
            ["born by"] = DescriptorRelations.SonOf,
            ["bearer"] = DescriptorRelations.MotherOf,
            ["cousin"] = DescriptorRelations.CousinOf,
            ["rabbi"] = DescriptorRelations.TeacherOf,
            ["ally"] = DescriptorRelations.AllyOf,
            ["raper"] = DescriptorRelations.RaperOf,

            // Deliberately absent: "victim". The dataset files the dead and two raped women under
            // that one word -- Dinah and Tamar beside Zimri and Jael's Sisera -- so no single
            // relation is its meaning, and mapping it to killed-by would have a page read "Dinah,
            // killed by Shechem". Which one each row is, is read from the verse (TSK-0489).
            // "concubinator" is absent because its fact is already the dataset's concubine row read
            // from the woman's side, and the vocabulary has no word for the man's.
        };

    /// <summary>
    /// The same fact said from the other end. A descriptor is written from its own subject's side,
    /// so BibleData's <c>bani-4 ancestor adaiah-6</c> is answered by the claim on Adaiah and not by
    /// one on Bani. Read one-directionally that is a disagreement, and every one of them would be
    /// the encyclopedia saying the thing correctly in the only place a reader would look for it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Inverse =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [DescriptorRelations.SonOf] = Set(DescriptorRelations.FatherOf, DescriptorRelations.MotherOf),
            [DescriptorRelations.DaughterOf] = Set(DescriptorRelations.FatherOf, DescriptorRelations.MotherOf),
            [DescriptorRelations.FatherOf] = Set(DescriptorRelations.SonOf, DescriptorRelations.DaughterOf),
            [DescriptorRelations.MotherOf] = Set(DescriptorRelations.SonOf, DescriptorRelations.DaughterOf),
            [DescriptorRelations.BrotherOf] = Set(DescriptorRelations.BrotherOf, DescriptorRelations.SisterOf),
            [DescriptorRelations.SisterOf] = Set(DescriptorRelations.BrotherOf, DescriptorRelations.SisterOf),
            [DescriptorRelations.HalfBrotherOf] =
                Set(DescriptorRelations.HalfBrotherOf, DescriptorRelations.HalfSisterOf),
            [DescriptorRelations.HalfSisterOf] =
                Set(DescriptorRelations.HalfBrotherOf, DescriptorRelations.HalfSisterOf),
            [DescriptorRelations.HusbandOf] = Set(DescriptorRelations.WifeOf),
            [DescriptorRelations.WifeOf] = Set(DescriptorRelations.HusbandOf),
            [DescriptorRelations.AncestorOf] = Set(DescriptorRelations.DescendantOf),
            [DescriptorRelations.DescendantOf] = Set(DescriptorRelations.AncestorOf),
            [DescriptorRelations.GrandfatherOf] =
                Set(DescriptorRelations.GrandsonOf, DescriptorRelations.GranddaughterOf),
            [DescriptorRelations.GrandmotherOf] =
                Set(DescriptorRelations.GrandsonOf, DescriptorRelations.GranddaughterOf),
            [DescriptorRelations.GrandsonOf] =
                Set(DescriptorRelations.GrandfatherOf, DescriptorRelations.GrandmotherOf),
            [DescriptorRelations.GranddaughterOf] =
                Set(DescriptorRelations.GrandfatherOf, DescriptorRelations.GrandmotherOf),
            [DescriptorRelations.UncleOf] = Set(DescriptorRelations.NephewOf, DescriptorRelations.NieceOf),
            [DescriptorRelations.AuntOf] = Set(DescriptorRelations.NephewOf, DescriptorRelations.NieceOf),
            [DescriptorRelations.NephewOf] = Set(DescriptorRelations.UncleOf, DescriptorRelations.AuntOf),
            [DescriptorRelations.NieceOf] = Set(DescriptorRelations.UncleOf, DescriptorRelations.AuntOf),
            [DescriptorRelations.FatherInLawOf] =
                Set(DescriptorRelations.SonInLawOf, DescriptorRelations.DaughterInLawOf),
            [DescriptorRelations.MotherInLawOf] =
                Set(DescriptorRelations.SonInLawOf, DescriptorRelations.DaughterInLawOf),
            [DescriptorRelations.SonInLawOf] =
                Set(DescriptorRelations.FatherInLawOf, DescriptorRelations.MotherInLawOf),
            [DescriptorRelations.DaughterInLawOf] =
                Set(DescriptorRelations.FatherInLawOf, DescriptorRelations.MotherInLawOf),
            [DescriptorRelations.BrotherInLawOf] =
                Set(DescriptorRelations.BrotherInLawOf, DescriptorRelations.SisterInLawOf),
            [DescriptorRelations.SisterInLawOf] =
                Set(DescriptorRelations.BrotherInLawOf, DescriptorRelations.SisterInLawOf),
            [DescriptorRelations.KillerOf] = Set(DescriptorRelations.KilledBy),
            [DescriptorRelations.KilledBy] = Set(DescriptorRelations.KillerOf),
            [DescriptorRelations.MasterOf] = Set(DescriptorRelations.ServantOf),
            [DescriptorRelations.ServantOf] = Set(DescriptorRelations.MasterOf),
            [DescriptorRelations.CousinOf] = Set(DescriptorRelations.CousinOf),
            [DescriptorRelations.DiscipleOf] = Set(DescriptorRelations.TeacherOf),
            [DescriptorRelations.TeacherOf] = Set(DescriptorRelations.DiscipleOf),
            [DescriptorRelations.AllyOf] = Set(DescriptorRelations.AllyOf),
            [DescriptorRelations.RaperOf] = Set(DescriptorRelations.RapedBy),
            [DescriptorRelations.RapedBy] = Set(DescriptorRelations.RaperOf),
        };

    /// <summary>
    /// Relations that are answers to the same question, so that stating two of them about one pair
    /// is stating two different things about it.
    ///
    /// **Kinship by descent is one branch and marriage is another**, and the split is not tidiness:
    /// a man is somebody's father or his grandfather, his son or his descendant, his brother or his
    /// uncle, and never both — but he may perfectly well be her husband <em>and</em> her
    /// half-brother, which is what BibleData states of Abram and Sarai from GEN 11:29 and what the
    /// text says. Reading marriage as an answer to the same question as descent withholds the very
    /// clause the witness agrees with.
    ///
    /// <para>
    /// Nothing outside kinship is an answer to anyone else's question. The commander of Judah is
    /// also of the tribe of Judah, and a king of a place is also from it; reading those as
    /// contradictions would withhold eleven true clauses measured on the corpus as loaded, and
    /// would be this corpus inventing a rule rather than applying one.
    /// </para>
    ///
    /// <para>
    /// The branch is still coarser than the fact: <em>daughter-in-law of Terah</em> and
    /// <em>daughter of Terah</em> are both true of Sarai and both kinship, so the weaker of the two
    /// is withheld. Two such pairs exist in the corpus as loaded and both are named in PRB-0445.
    /// The alternative is a table of which relations exclude which, which is a rule nobody has
    /// written and this task was told not to invent.
    /// </para>
    /// </summary>
    private static readonly IReadOnlySet<string> Kinship = new HashSet<string>(StringComparer.Ordinal)
    {
        DescriptorRelations.SonOf, DescriptorRelations.DaughterOf,
        DescriptorRelations.FatherOf, DescriptorRelations.MotherOf,
        DescriptorRelations.BrotherOf, DescriptorRelations.SisterOf,
        DescriptorRelations.HalfBrotherOf, DescriptorRelations.HalfSisterOf,
        DescriptorRelations.GrandfatherOf, DescriptorRelations.GrandmotherOf,
        DescriptorRelations.GrandsonOf, DescriptorRelations.GranddaughterOf,
        DescriptorRelations.UncleOf, DescriptorRelations.AuntOf,
        DescriptorRelations.NephewOf, DescriptorRelations.NieceOf,
        DescriptorRelations.AncestorOf, DescriptorRelations.DescendantOf,
        DescriptorRelations.FatherInLawOf, DescriptorRelations.MotherInLawOf,
        DescriptorRelations.SonInLawOf, DescriptorRelations.DaughterInLawOf,
        DescriptorRelations.BrotherInLawOf, DescriptorRelations.SisterInLawOf,
        DescriptorRelations.CousinOf,
    };

    /// <summary>Whom a person is married to, or kept as a concubine — one question, three answers.</summary>
    private static readonly IReadOnlySet<string> Marriage = new HashSet<string>(StringComparer.Ordinal)
    {
        DescriptorRelations.HusbandOf, DescriptorRelations.WifeOf, DescriptorRelations.ConcubineOf,
    };

    private const string KinshipBranch = "kinship";

    private const string MarriageBranch = "marriage";

    private const string ViolenceBranch = "violence";

    /// <summary>
    /// Which question a relation answers. Two relations in the same branch are two answers to it;
    /// two in different branches are two facts.
    /// </summary>
    public static string Branch(string relation) =>
        Kinship.Contains(relation) ? KinshipBranch
        : Marriage.Contains(relation) ? MarriageBranch
        : relation is DescriptorRelations.KillerOf or DescriptorRelations.KilledBy ? ViolenceBranch
        : relation;

    /// <summary>
    /// The wider tie a closer one already states. A son is a descendant and a grandfather an
    /// ancestor, so <em>son of</em> and <em>descendant of</em> over one pair are one answer said
    /// closely and loosely, not two answers — and a witness saying the loose one has not
    /// contradicted a reading that says the close one.
    ///
    /// <para>
    /// One way only. A descendant is not thereby a son, so a reading of <em>descendant of</em>
    /// against a witness's <em>son</em> still says less than the witness and still gives way to it.
    /// Nothing else is here because nothing else is entailed: a half-brother is not a brother in
    /// the sense the dataset means one.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Broader =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DescriptorRelations.SonOf] = DescriptorRelations.DescendantOf,
            [DescriptorRelations.DaughterOf] = DescriptorRelations.DescendantOf,
            [DescriptorRelations.GrandsonOf] = DescriptorRelations.DescendantOf,
            [DescriptorRelations.GranddaughterOf] = DescriptorRelations.DescendantOf,
            [DescriptorRelations.FatherOf] = DescriptorRelations.AncestorOf,
            [DescriptorRelations.MotherOf] = DescriptorRelations.AncestorOf,
            [DescriptorRelations.GrandfatherOf] = DescriptorRelations.AncestorOf,
            [DescriptorRelations.GrandmotherOf] = DescriptorRelations.AncestorOf,
        };

    /// <summary>Whether stating <paramref name="closer"/> already states the wider <paramref name="wider"/>.</summary>
    public static bool Implies(string closer, string wider) =>
        Broader.TryGetValue(closer, out var broader) && broader == wider;

    /// <summary>
    /// Whether a claim says something the settled relations of a pair do not, about the same thing
    /// they are about — which is the only disagreement there is here. A settled relation the claim
    /// already implies is not one it disagrees with.
    /// </summary>
    public static bool Contradicts(string relation, IReadOnlySet<string> settled) =>
        !settled.Contains(relation)
        && settled.Any(other => Branch(other) == Branch(relation) && !Implies(relation, other));

    /// <summary>What a witness stating this relation about a pair says about the reverse pair.</summary>
    public static IReadOnlySet<string> Reversed(string relation) =>
        Inverse.GetValueOrDefault(relation, Empty);

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    private static IReadOnlySet<string> Set(params string[] relations) =>
        new HashSet<string>(relations, StringComparer.Ordinal);
}
