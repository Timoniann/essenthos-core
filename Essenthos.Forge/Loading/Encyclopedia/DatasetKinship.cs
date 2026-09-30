namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The dataset's own statements of who is whose parent, child, brother or grandparent, read from
/// its file and kept nowhere.
///
/// <para>
/// **Not a relationship this corpus holds.** Every relationship the encyclopedia states is read from
/// the text by this project, and the dataset's edge list is not loaded. What is read here decides one
/// thing about the dataset's own verse lists: whether a verse it files under a man a people is named
/// after reads as his genealogy — <em>these are the sons of Israel; Reuben, Simeon, Levi, and
/// Judah</em> — and so stays with him (<see cref="MisfiledVerseLoader"/>). It is read where the verse
/// lists are read, before this corpus has described anybody, which is why it cannot be asked of our
/// own clauses: a clause may cite only a verse its record is named in, and the verse lists are what
/// say so.
/// </para>
/// </summary>
internal static class DatasetKinship
{
    public const string FileName = "BibleData-PersonRelationship.csv";

    /// <summary>The relation words a row reading from a parent to the child is written with.</summary>
    private static readonly HashSet<string> Parents = new(["father", "mother"], StringComparer.Ordinal);

    /// <summary>The relation words a row reading from a child to the parent is written with.</summary>
    private static readonly HashSet<string> Offspring = new(["son", "daughter"], StringComparer.Ordinal);

    /// <summary>The other relation words that make somebody close kin, as the dataset writes them.</summary>
    private static readonly HashSet<string> Kin = new(
    [
        "brother", "sister", "half-brother", "half-sister", "grandfather", "grandmother", "grandson",
        "granddaughter",
    ], StringComparer.Ordinal);

    /// <summary>
    /// Levites of Hezekiah's and Nehemiah's day the dataset writes as sons of Levi, by its person ids.
    /// Its own notes hold them as his descendants, centuries after Jacob's son, so none is his child
    /// here (2CH 31:15; NEH 13:13).
    /// </summary>
    private static readonly HashSet<string> DescendantsWrittenAsSonsOfLevi = new(
        ["Miniamin_1", "Jeshua_2", "Shemaiah_12", "Amariah_4", "Shecaniah_3", "Mattaniah_10"],
        StringComparer.Ordinal);

    private const string Levi = "Levi_1";

    /// <param name="Parent">The dataset's person id of the parent.</param>
    /// <param name="Child">The dataset's person id of the child.</param>
    internal readonly record struct Descent(string Parent, string Child);

    /// <param name="Descents">Each parent and child the file states, once, from whichever end it states it.</param>
    /// <param name="Others">Each pair it states as siblings or as grandparent and grandchild.</param>
    internal sealed record Statements(IReadOnlySet<Descent> Descents, IReadOnlySet<(string One, string Other)> Others)
    {
        public static readonly Statements None = new(new HashSet<Descent>(), new HashSet<(string, string)>());

        /// <summary>Every person id the statements name.</summary>
        public IEnumerable<string> People =>
            Descents.SelectMany(d => new[] { d.Parent, d.Child }).Concat(Others.SelectMany(o => new[] { o.One, o.Other }));
    }

    /// <summary>What the file in this folder states, or nothing where the folder holds no such file.</summary>
    public static Statements Read(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            return Statements.None;
        }

        var descents = new HashSet<Descent>();
        var others = new HashSet<(string, string)>();
        foreach (var row in Csv.Read(path))
        {
            var (one, type, other) = (row["person_id_1"], row["relationship_type"], row["person_id_2"]);
            if (Parents.Contains(type) && !IsALeviteOfALaterDay(one, other))
            {
                descents.Add(new Descent(one, other));
            }
            else if (Offspring.Contains(type) && !IsALeviteOfALaterDay(other, one))
            {
                descents.Add(new Descent(other, one));
            }
            else if (Kin.Contains(type))
            {
                others.Add((one, other));
            }
        }

        return new Statements(descents, others);
    }

    private static bool IsALeviteOfALaterDay(string parent, string child) =>
        parent == Levi && DescendantsWrittenAsSonsOfLevi.Contains(child);

    /// <summary>
    /// Each of these people's parents, children and siblings, by the records the dataset's ids name.
    /// The dataset writes a tie between parent and child from both ends, and siblings mostly not at
    /// all, so brothers are also read as the other children of a parent: <em>the sons of Levi;
    /// Gershon, Kohath, and Merari</em> is a genealogy for each of the three.
    /// </summary>
    /// <param name="records">The record each of the dataset's person ids is held as; an id it does not hold names nobody.</param>
    public static Dictionary<int, MisfiledVerseLoader.Family> Families(
        Statements statements,
        IReadOnlyDictionary<string, int> records,
        IReadOnlyCollection<int> people)
    {
        var kin = people.ToDictionary(id => id, _ => new HashSet<int>());
        var children = people.ToDictionary(id => id, _ => new HashSet<int>());

        var descents = statements.Descents
            .Select(d => (Parent: records.GetValueOrDefault(d.Parent, NoRecord), Child: records.GetValueOrDefault(d.Child, NoRecord)))
            .Where(d => d.Parent != NoRecord && d.Child != NoRecord && d.Parent != d.Child)
            .Distinct()
            .ToList();
        var offspring = descents.ToLookup(d => d.Parent, d => d.Child);

        foreach (var (parent, child) in descents)
        {
            if (kin.TryGetValue(child, out var ofTheChild))
            {
                ofTheChild.Add(parent);
                ofTheChild.UnionWith(offspring[parent].Where(sibling => sibling != child));
            }

            if (kin.TryGetValue(parent, out var ofTheParent))
            {
                ofTheParent.Add(child);
                children[parent].Add(child);
            }
        }

        foreach (var (one, other) in statements.Others)
        {
            var (a, b) = (records.GetValueOrDefault(one, NoRecord), records.GetValueOrDefault(other, NoRecord));
            if (a == NoRecord || b == NoRecord || a == b)
            {
                continue;
            }

            if (kin.TryGetValue(a, out var ofOne))
            {
                ofOne.Add(b);
            }

            if (kin.TryGetValue(b, out var ofTheOther))
            {
                ofTheOther.Add(a);
            }
        }

        return people.ToDictionary(id => id, id => new MisfiledVerseLoader.Family(kin[id], children[id]));
    }

    private const int NoRecord = 0;
}
