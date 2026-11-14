using System.Text;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Publishing;

/// <summary>
/// What a difference does to the schema when two releases' schemas differ: the tables whose definition
/// changed and the new ones go whole (dropped, created as the release creates them, every row copied,
/// then their keys, indexes, triggers and the foreign keys that point at them), and the tables the
/// release no longer has are dropped. A change to anything else (a function, an extension, a sequence of
/// its own, an index or key of a table that is otherwise the same) is not patched: the release is
/// restored whole, which is always right.
///
/// Whole tables rather than <c>ALTER TABLE</c>: a migration's own steps are not in the dumps, only their
/// result, and a table rewritten by a restore is what the sums are held to. The tables a migration
/// touches are few, so sending them whole costs little beside the rest of the release.
/// </summary>
/// <param name="Whole">The tables created from nothing, with every row of the release.</param>
/// <param name="Dropped">The tables of the base the script drops: the release's whole tables it held, and those the release lacks.</param>
/// <param name="Create">The SQL creating the whole tables, their sequences and defaults, before any row.</param>
/// <param name="Finish">The SQL of their keys, indexes, triggers, comments and foreign keys, after every row.</param>
internal sealed partial record SchemaChange(
    IReadOnlySet<string> Whole, IReadOnlyList<string> Dropped, string Create, string Finish)
{
    public static readonly SchemaChange None = new(new HashSet<string>(), [], string.Empty, string.Empty);

    public bool IsNone => Whole.Count == 0 && Dropped.Count == 0;

    /// <summary>
    /// The change from <paramref name="before"/>'s schema to <paramref name="after"/>'s, or the reason it
    /// cannot be made by whole tables, in which case the release is restored whole.
    /// </summary>
    /// <param name="alsoWhole">Tables of the same definition in both that go whole all the same: most of their rows changed.</param>
    public static (SchemaChange? Change, string? Refusal) Of(
        IReadOnlyList<DumpArchive.Entry> before, IReadOnlyList<DumpArchive.Entry> after, IReadOnlyCollection<string>? alsoWhole = null)
    {
        var earlier = Keyed(before);
        var later = Keyed(after);
        var tablesBefore = before.Where(entry => entry.Kind == "TABLE").Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var tablesAfter = after.Where(entry => entry.Kind == "TABLE").Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> whole = after.Where(entry => entry.Kind == "TABLE"
                && (!earlier.TryGetValue(Key(entry), out var was) || Normal(was.Definition) != Normal(entry.Definition)))
            .Select(entry => entry.Name).Concat((alsoWhole ?? []).Where(tablesAfter.Contains)).ToHashSet(StringComparer.Ordinal);
        var gone = tablesBefore.Where(table => !tablesAfter.Contains(table)).Order(StringComparer.Ordinal).ToList();
        IReadOnlySet<string> replaced = whole.Concat(gone).ToHashSet(StringComparer.Ordinal);

        var ownersBefore = Owners(before);
        var ownersAfter = Owners(after);
        foreach (var key in earlier.Keys.Union(later.Keys).Order(StringComparer.Ordinal))
        {
            var was = earlier.GetValueOrDefault(key);
            var now = later.GetValueOrDefault(key);
            if (was is not null && now is not null && Normal(was.Definition) == Normal(now.Definition))
            {
                continue;
            }

            var owner = now is not null ? ownersAfter.GetValueOrDefault(now.DumpId) : ownersBefore.GetValueOrDefault(was!.DumpId);
            if ((owner is null || !replaced.Contains(owner)) && !PointsAt(now ?? was!, replaced))
            {
                var entry = now ?? was!;
                return (null, $"{entry.Kind} {entry.Name} {(was is null ? "is new" : now is null ? "is gone" : "changed")}, and it is not part of a table sent whole");
            }
        }

        if (whole.Count == 0 && gone.Count == 0)
        {
            return (None, null);
        }

        var create = new StringBuilder();
        var finish = new StringBuilder();
        foreach (var entry in after)
        {
            var owner = ownersAfter.GetValueOrDefault(entry.DumpId);
            var mine = owner is not null && whole.Contains(owner);
            var pointing = PointsAt(entry, whole);
            if (!(mine || pointing) || entry.Kind is "TABLE DATA" or "SEQUENCE SET" || entry.Definition.Length == 0)
            {
                continue;
            }

            (entry.Section == DumpArchive.PostData || entry.Kind is "COMMENT" or "ACL" ? finish : create)
                .Append(entry.Definition.TrimEnd()).Append('\n');
        }

        var dropped = gone.Concat(whole.Where(tablesBefore.Contains)).Order(StringComparer.Ordinal).ToList();
        return (new SchemaChange(whole, dropped, create.ToString(), finish.ToString()), null);
    }

    /// <summary>The statements dropping <see cref="Dropped"/>, foreign keys pointing at them included.</summary>
    public string Drop => string.Concat(Dropped.Select(table => $"DROP TABLE IF EXISTS public.{TargetHost.Identifier(table)} CASCADE;\n"));

    /// <summary>The tables some other table's foreign key points at.</summary>
    public static IReadOnlySet<string> Referenced(IReadOnlyList<DumpArchive.Entry> entries) =>
        entries.Where(entry => entry.Kind == "FK CONSTRAINT")
            .Select(entry => (Owner: FirstObject().Match(entry.Definition), Target: References().Match(entry.Definition)))
            .Where(pair => pair.Owner.Success && pair.Target.Success
                && Unquote(pair.Owner.Groups["name"].Value) != Unquote(pair.Target.Groups["table"].Value))
            .Select(pair => Unquote(pair.Target.Groups["table"].Value)).ToHashSet(StringComparer.Ordinal);

    /// <summary>A foreign key to one of <paramref name="tables"/>: dropped with it, and created again after its rows.</summary>
    private static bool PointsAt(DumpArchive.Entry entry, IReadOnlySet<string> tables) =>
        entry.Kind == "FK CONSTRAINT" && References().Match(entry.Definition) is { Success: true } reference
        && tables.Contains(Unquote(reference.Groups["table"].Value));

    private static Dictionary<string, DumpArchive.Entry> Keyed(IReadOnlyList<DumpArchive.Entry> entries)
    {
        var keyed = new Dictionary<string, DumpArchive.Entry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            keyed[Key(entry)] = entry;
        }

        return keyed;
    }

    private static string Key(DumpArchive.Entry entry) => $"{entry.Kind}\u0001{entry.Schema}\u0001{entry.Name}";

    private static string Normal(string definition) =>
        string.Join('\n', definition.Split('\n').Select(line => line.TrimEnd()));

    /// <summary>
    /// The table each entry belongs to: the first object of schema public its SQL names, which for every
    /// kind pg_dump writes (CREATE TABLE, ALTER TABLE … ADD CONSTRAINT, CREATE INDEX … ON, CREATE TRIGGER
    /// … ON, ALTER TABLE … ADD GENERATED … AS IDENTITY, COMMENT ON TABLE|COLUMN) is the table. A sequence
    /// <c>OWNED BY</c> a table's column belongs to that table, and so do its other entries.
    /// </summary>
    private static Dictionary<int, string> Owners(IReadOnlyList<DumpArchive.Entry> entries)
    {
        var owners = new Dictionary<int, string>();
        var sequences = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (FirstObject().Match(entry.Definition) is { Success: true } first)
            {
                owners[entry.DumpId] = Unquote(first.Groups["name"].Value);
            }

            if (OwnedBy().Match(entry.Definition) is { Success: true } owned)
            {
                sequences[Unquote(owned.Groups["sequence"].Value)] = Unquote(owned.Groups["table"].Value);
            }
        }

        foreach (var (id, owner) in owners.ToList())
        {
            if (sequences.TryGetValue(owner, out var table))
            {
                owners[id] = table;
            }
        }

        return owners;
    }

    private static string Unquote(string name) => name.Length > 1 && name[0] == '"' ? name[1..^1].Replace("\"\"", "\"") : name;

    [GeneratedRegex(@"\bpublic\.(?<name>""(?:[^""]|"""")+""|[A-Za-z_][A-Za-z0-9_$]*)")]
    private static partial Regex FirstObject();

    [GeneratedRegex(@"ALTER SEQUENCE public\.(?<sequence>""(?:[^""]|"""")+""|[A-Za-z_][A-Za-z0-9_$]*) OWNED BY public\.(?<table>""(?:[^""]|"""")+""|[A-Za-z_][A-Za-z0-9_$]*)\.")]
    private static partial Regex OwnedBy();

    [GeneratedRegex(@"REFERENCES public\.(?<table>""(?:[^""]|"""")+""|[A-Za-z_][A-Za-z0-9_$]*)\(")]
    private static partial Regex References();
}
