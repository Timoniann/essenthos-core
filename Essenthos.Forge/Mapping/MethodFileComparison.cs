using System.Globalization;
using System.Text;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Two stores' method files of a pair compared by key: a row is its words, relation and source; its value is its
/// confidence, evidence and roles; its id and the merge's status are not compared. How a producer's file is held to the
/// file exported from a database.
/// </summary>
internal static class MethodFileComparison
{
    public sealed record Group(string Pair, string Method, string Source, int Equal, int IdOnly, int Changed, int OnlyBefore, int OnlyAfter)
    {
        public bool Same => Changed == 0 && OnlyBefore == 0 && OnlyAfter == 0;
    }

    public sealed record Outcome(List<Group> Groups, List<string> Examples);

    private readonly record struct Value(long? Id, string? Confidence, string? Evidence, string? Roles);

    public static Outcome Compare(ProductStore before, ProductStore after, string pair, string method, int examples = 4)
    {
        var product = MappingProducts.Of(pair, method);
        var old = Read(before, product);
        var made = Read(after, product);
        var groups = new List<Group>();
        var lines = new List<string>();
        foreach (var source in old.Keys.Concat(made.Keys).Select(k => k.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            int equal = 0, idOnly = 0, changed = 0, onlyBefore = 0, onlyAfter = 0;
            var shown = new Dictionary<string, int>(StringComparer.Ordinal);
            void Show(string kind, string text)
            {
                if (shown.GetValueOrDefault(kind) < examples)
                {
                    shown[kind] = shown.GetValueOrDefault(kind) + 1;
                    lines.Add($"{pair}\t{method}\t{Short(source)}\t{kind}\t{text}");
                }
            }

            foreach (var key in old.Keys.Where(k => k.Source == source).Concat(made.Keys.Where(k => k.Source == source)).Distinct())
            {
                var a = old.GetValueOrDefault(key) ?? [];
                var b = made.GetValueOrDefault(key) ?? [];
                var pairs = Math.Min(a.Count, b.Count);
                for (var i = 0; i < pairs; i++)
                {
                    var (x, y) = (a[i], b[i]);
                    if (x with { Id = null } != y with { Id = null })
                    {
                        changed++;
                        Show("changed", $"{key.From}|{key.To} {key.Relation}: {Describe(x)} -> {Describe(y)}");
                    }
                    else if (x.Id != y.Id && y.Id is not null)
                    {
                        idOnly++;
                    }
                    else
                    {
                        equal++;
                    }
                }

                for (var i = pairs; i < a.Count; i++)
                {
                    onlyBefore++;
                    Show("only-before", $"{key.From}|{key.To} {key.Relation} {Describe(a[i])}");
                }

                for (var i = pairs; i < b.Count; i++)
                {
                    onlyAfter++;
                    Show("only-after", $"{key.From}|{key.To} {key.Relation} {Describe(b[i])}");
                }
            }

            groups.Add(new Group(pair, method, source, equal, idOnly, changed, onlyBefore, onlyAfter));
        }

        return new Outcome(groups, lines);
    }

    public static string Summary(IEnumerable<Group> groups)
    {
        var text = new StringBuilder("pair\tmethod\tequal\tid-only\tchanged\tonly-before\tonly-after\tsource\n");
        foreach (var g in groups)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{g.Pair}\t{g.Method}\t{g.Equal}\t{g.IdOnly}\t{g.Changed}\t{g.OnlyBefore}\t{g.OnlyAfter}\t{g.Source}\n");
        }

        return text.ToString();
    }

    private static string Short(string source) => source.Length <= 60 ? source : source[..60] + "…";

    private static string Describe(Value value) =>
        $"[{value.Confidence ?? "-"}] {value.Evidence ?? "-"} {value.Roles ?? ""}".TrimEnd();

    private static Dictionary<(string From, string To, string Relation, string Source), List<Value>> Read(ProductStore store, string product)
    {
        var rows = new Dictionary<(string, string, string, string), List<Value>>();
        if (!store.Exists(product))
        {
            return rows;
        }

        using var reader = store.Open(product, MappingRow.Schema);
        foreach (var row in reader.Rows())
        {
            var key = (row.From, row.To, row.Relation, row.Source);
            if (!rows.TryGetValue(key, out var values))
            {
                rows[key] = values = [];
            }

            values.Add(new Value(row.Id, MappingText.Confidence(row.Confidence), row.Evidence, row.Roles));
        }

        foreach (var values in rows.Values)
        {
            values.Sort((a, b) => string.CompareOrdinal($"{a.Confidence}|{a.Evidence}|{a.Roles}", $"{b.Confidence}|{b.Evidence}|{b.Roles}"));
        }

        return rows;
    }
}
