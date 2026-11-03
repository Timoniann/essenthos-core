using System.Text;

namespace Essenthos.Core.Comparison;

/// <summary>
/// A row of a table as two databases are compared on it (DOC-0224 §15, the gate of every layer): its natural
/// key (words by <see cref="WordKeys"/>, records by slug, never a row id), what it says, and the row id it has,
/// which is compared apart so that a row two builds numbered differently reads as <c>id-only</c> and not as two.
/// </summary>
/// <param name="Scope">What the differences are grouped by first: a pair of texts, a text, a record type.</param>
/// <param name="Group">What they are grouped by within it: a method, a source, a kind.</param>
internal readonly record struct KeyedRow(string Scope, string Group, string Key, string Value, long? Id = null);

/// <summary>How a row differs: only in the first database, only in the second, said otherwise, or numbered otherwise.</summary>
internal enum Difference
{
    OnlyBefore,
    OnlyAfter,
    Changed,
    IdOnly,
}

/// <summary>One group of differences: a table, a scope, a group within it and a kind, how many rows, and a few of them.</summary>
internal sealed record DifferenceGroup(string Table, string Scope, string Group, Difference Kind, long Rows, List<string> Examples);

/// <summary>
/// Two databases' rows of one table compared by natural key, and the differences grouped by (table, scope, group,
/// kind), for a person to classify: what the explicit rules do that the old load did not, a debt already known, or
/// a bug. Shared by every package that rebuilds a layer from files and must show it equals live (mapping, names,
/// records, the frame): a package turns its tables into <see cref="KeyedRow"/>s, both sides the same way, and hands
/// them here a scope at a time, so memory holds one scope of one table and never the corpus.
/// </summary>
internal sealed class KeyedComparison(int examples = 12)
{
    private readonly Dictionary<(string Table, string Scope, string Group, Difference Kind), DifferenceGroup> _groups = [];
    private readonly Dictionary<string, (long Before, long After)> _counts = new(StringComparer.Ordinal);

    public IEnumerable<DifferenceGroup> Groups => _groups.Values
        .OrderBy(g => g.Table, StringComparer.Ordinal).ThenBy(g => g.Scope, StringComparer.Ordinal)
        .ThenBy(g => g.Group, StringComparer.Ordinal).ThenBy(g => g.Kind);

    public IReadOnlyDictionary<string, (long Before, long After)> Counts => _counts;

    /// <summary>
    /// Compares one table's rows (or one scope of them) of the two databases. A key held twice on one side is
    /// compared as a multiset of values, so duplicates the key does not tell apart are counted, not lost.
    /// </summary>
    public void Add(string table, IReadOnlyList<KeyedRow> before, IReadOnlyList<KeyedRow> after)
    {
        var counts = _counts.GetValueOrDefault(table);
        _counts[table] = (counts.Before + before.Count, counts.After + after.Count);

        var left = Index(before);
        var right = Index(after);
        foreach (var (key, rows) in left)
        {
            if (!right.TryGetValue(key, out var others))
            {
                foreach (var row in rows)
                {
                    Note(table, row.Scope, row.Group, Difference.OnlyBefore, $"{row.Key}  {row.Value}");
                }

                continue;
            }

            var unmatched = new List<KeyedRow>(others);
            var lost = new List<KeyedRow>();
            foreach (var row in rows)
            {
                var at = unmatched.FindIndex(o => o.Value == row.Value);
                if (at < 0)
                {
                    lost.Add(row);
                    continue;
                }

                if (row.Id != unmatched[at].Id)
                {
                    Note(table, row.Scope, row.Group, Difference.IdOnly, $"{row.Key}  {row.Id} -> {unmatched[at].Id}");
                }

                unmatched.RemoveAt(at);
            }

            var pairs = Math.Min(lost.Count, unmatched.Count);
            for (var i = 0; i < pairs; i++)
            {
                var group = lost[i].Group == unmatched[i].Group ? lost[i].Group : $"{lost[i].Group} -> {unmatched[i].Group}";
                Note(table, lost[i].Scope, group, Difference.Changed, $"{lost[i].Key}  {lost[i].Value}  =>  {unmatched[i].Value}");
            }

            foreach (var row in lost.Skip(pairs))
            {
                Note(table, row.Scope, row.Group, Difference.OnlyBefore, $"{row.Key}  {row.Value}");
            }

            foreach (var row in unmatched.Skip(pairs))
            {
                Note(table, row.Scope, row.Group, Difference.OnlyAfter, $"{row.Key}  {row.Value}");
            }
        }

        foreach (var (key, rows) in right)
        {
            if (left.ContainsKey(key))
            {
                continue;
            }

            foreach (var row in rows)
            {
                Note(table, row.Scope, row.Group, Difference.OnlyAfter, $"{row.Key}  {row.Value}");
            }
        }
    }

    /// <summary>
    /// Adds another comparison's counts and groups to this one, so that scopes can be compared apart, in parallel,
    /// and gathered. Examples are kept up to the limit, the first comparison's first.
    /// </summary>
    public void Merge(KeyedComparison other)
    {
        foreach (var (table, (before, after)) in other._counts)
        {
            var counts = _counts.GetValueOrDefault(table);
            _counts[table] = (counts.Before + before, counts.After + after);
        }

        lock (_groups)
        {
            foreach (var (key, group) in other._groups)
            {
                if (!_groups.TryGetValue(key, out var found))
                {
                    _groups[key] = group with { Examples = [.. group.Examples.Take(examples)] };
                    continue;
                }

                var kept = found.Examples;
                kept.AddRange(group.Examples.Take(Math.Max(0, examples - kept.Count)));
                _groups[key] = found with { Rows = found.Rows + group.Rows };
            }
        }
    }

    private static Dictionary<string, List<KeyedRow>> Index(IReadOnlyList<KeyedRow> rows)
    {
        var index = new Dictionary<string, List<KeyedRow>>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!index.TryGetValue(row.Key, out var list))
            {
                index[row.Key] = list = new List<KeyedRow>(1);
            }

            list.Add(row);
        }

        return index;
    }

    private void Note(string table, string scope, string group, Difference kind, string example)
    {
        lock (_groups)
        {
            if (!_groups.TryGetValue((table, scope, group, kind), out var found))
            {
                _groups[(table, scope, group, kind)] = found = new DifferenceGroup(table, scope, group, kind, 0, []);
            }

            _groups[(table, scope, group, kind)] = found = found with { Rows = found.Rows + 1 };
            if (found.Examples.Count < examples)
            {
                found.Examples.Add(example);
            }
        }
    }

    public static string Spell(Difference kind) => kind switch
    {
        Difference.OnlyBefore => "only-before",
        Difference.OnlyAfter => "only-after",
        Difference.Changed => "changed",
        Difference.IdOnly => "id-only",
        _ => kind.ToString(),
    };

    /// <summary>The groups as TSV, one line each: table, scope, group, kind, rows.</summary>
    public string Summary()
    {
        var text = new StringBuilder("table\tscope\tgroup\tkind\trows\n");
        foreach (var (table, (before, after)) in _counts.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            text.Append(table).Append("\t*\t*\tcount\t").Append(before).Append(" -> ").Append(after).Append('\n');
        }

        foreach (var group in Groups)
        {
            text.Append(group.Table).Append('\t').Append(group.Scope).Append('\t').Append(group.Group).Append('\t')
                .Append(Spell(group.Kind)).Append('\t').Append(group.Rows).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Every group with its examples, for reading.</summary>
    public string Examples()
    {
        var text = new StringBuilder();
        foreach (var group in Groups)
        {
            text.Append("## ").Append(group.Table).Append(' ').Append(group.Scope).Append(' ').Append(group.Group).Append(' ')
                .Append(Spell(group.Kind)).Append(" (").Append(group.Rows).Append(")\n");
            foreach (var example in group.Examples)
            {
                text.Append("    ").Append(example).Append('\n');
            }
        }

        return text.ToString();
    }
}
