using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// One pair's method files made into its <c>final</c> by the stated precedence (DOC-0224 §6.4):
/// <list type="number">
/// <item>rows citing a verse whose text moved since they were made are stale and stand aside; a model's rows
/// stay out until the pair is aligned again (the owner's D1);</item>
/// <item>a pair's rulings refuse shapes or words and pin shapes;</item>
/// <item>rows naming the same words are one link: the top-ranked row is its settled claim, the others its claims;</item>
/// <item>links of different words competing for a word: the higher-ranked keeps it, the lower is withdrawn
/// whole, by the method that won; an absence a source states or a verdict decides wins its word over any
/// correspondence; links of equal rank stand side by side, as one source's own several links on a word do.</item>
/// </list>
/// Nothing is deleted: every row stays in its method file, with what the merge decided in its status. The
/// output is a function of the inputs and sorted by key, so a second merge of the same files writes the same bytes.
/// </summary>
internal static class MappingMerge
{
    /// <param name="Hashes">The text hash of each cited verse when the rows were made (<see cref="VerseHashRow"/>).</param>
    /// <param name="Manifests">Each text's verse manifest as it stands, or null where the text has none yet.</param>
    /// <param name="PreviousIds">The ids an earlier final gave its links, by shape, so a link keeps its id.</param>
    public sealed record Input(
        string Pair,
        string FromText,
        string ToText,
        IReadOnlyDictionary<string, List<MappingRow>> Methods,
        IReadOnlyList<RulingRow> Rulings,
        IReadOnlyDictionary<(string Text, VerseKey Verse), string> Hashes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<VerseKey, string>?> Manifests,
        IReadOnlyDictionary<string, long> PreviousIds,
        long IdBase);

    /// <param name="Methods">Every method file's rows with the status the merge gave them.</param>
    public sealed record Output(List<FinalRow> Final, Dictionary<string, List<MappingRow>> Methods, List<ReportRow> Report);

    private sealed class Entry(string file, int index, MappingRow row)
    {
        public string File { get; } = file;
        public int Index { get; } = index;
        public MappingRow Row { get; set; } = row;
        public string? Status { get; set; }
        public int Rank { get; } = MappingMethods.Rank(row.Method, row.Confidence, row.Evidence);
    }

    private sealed class Candidate(string shape, List<Entry> rows, Entry top)
    {
        public string Shape { get; } = shape;
        public List<Entry> Rows { get; } = rows;
        public Entry Top { get; } = top;
        public int Rank { get; set; }
        public (int Side, WordKey First) Sort { get; set; }
    }

    public static Output Merge(Input input)
    {
        var entries = new List<Entry>();
        foreach (var (file, rows) in input.Methods.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                entries.Add(new Entry(file, i, rows[i] with { Status = null }));
            }
        }

        var report = new List<ReportRow>();
        var stale = Stale(input, entries, report);
        var pinned = Rule(input, entries);

        var active = entries.Where(e => e.Status is null).ToList();
        var candidates = active
            .GroupBy(e => e.Row.Shape, StringComparer.Ordinal)
            .Select(group =>
            {
                var rows = group.OrderBy(e => e, TopFirst).ToList();
                return new Candidate(group.Key, rows, rows[0]);
            })
            .ToList();

        foreach (var candidate in candidates)
        {
            foreach (var row in candidate.Rows.Where(r => r.Row.Relation != candidate.Top.Row.Relation))
            {
                row.Status = MappingStatus.WithdrawnBy(candidate.Top.Row.Method);
            }

            candidate.Rows.RemoveAll(r => r.Status is not null);
            candidate.Rank = pinned.Contains(candidate.Shape)
                ? MappingMethods.Pinned
                : MappingMethods.IsAbsence(candidate.Top.Row.Relation) && MappingMethods.StatesAbsence(candidate.Top.Row.Method)
                    ? Math.Max(MappingMethods.StatedAbsence, candidate.Top.Rank)
                    : candidate.Top.Rank;
            candidate.Sort = MappingText.SortKey(candidate.Top.Row.From, candidate.Top.Row.To, input.FromText, input.ToText);
        }

        candidates.Sort((a, b) =>
        {
            var order = b.Rank.CompareTo(a.Rank);
            return order != 0 ? order : CompareBySort(a, b);
        });

        var accepted = Compete(candidates);
        accepted.Sort(CompareBySort);
        var final = Finalise(input, accepted);

        var methods = new Dictionary<string, List<MappingRow>>(StringComparer.Ordinal);
        foreach (var (file, rows) in input.Methods)
        {
            methods[file] = new List<MappingRow>(rows.Count);
        }

        foreach (var entry in entries.Where(e => e.Index >= 0).OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.Index))
        {
            methods[entry.File].Add(input.Methods[entry.File][entry.Index] with { Status = entry.Status });
        }

        Report(input, entries, final, stale, report);
        return new Output(final, methods, report);
    }

    /// <summary>The rows citing a verse whose text moved, or which is gone, since they were made.</summary>
    private static HashSet<string> Stale(Input input, List<Entry> entries, List<ReportRow> report)
    {
        var drawn = new HashSet<string>(StringComparer.Ordinal);
        var fromManifest = input.Manifests.GetValueOrDefault(input.FromText);
        var toManifest = input.Manifests.GetValueOrDefault(input.ToText);
        foreach (var (text, manifest) in new[] { (input.FromText, fromManifest), (input.ToText, toManifest) })
        {
            if (manifest is null)
            {
                report.Add(new ReportRow("unchecked", string.Empty, string.Empty, 0, $"{text} has no verse manifest yet: its changed verses cannot be told"));
            }
        }

        if (fromManifest is null && toManifest is null)
        {
            return drawn;
        }

        var verdicts = new Dictionary<(bool From, string Verse), bool>();
        bool Moved(bool from, string key)
        {
            var verse = key[..key.LastIndexOf('.')];
            if (verdicts.TryGetValue((from, verse), out var moved))
            {
                return moved;
            }

            var (text, manifest) = from ? (input.FromText, fromManifest) : (input.ToText, toManifest);
            var verseKey = VerseKey.Parse(verse);
            moved = manifest is not null
                    && (!manifest.TryGetValue(verseKey, out var now)
                        || (input.Hashes.TryGetValue((text, verseKey), out var then) && then != now));
            verdicts[(from, verse)] = moved;
            return moved;
        }

        foreach (var entry in entries)
        {
            if (MappingText.Split(entry.Row.From).Any(k => Moved(true, k)) || MappingText.Split(entry.Row.To).Any(k => Moved(false, k)))
            {
                entry.Status = MappingStatus.Stale;
                if (MappingMethods.Drawn(entry.Row.Method))
                {
                    drawn.Add(entry.Row.Method);
                }
            }
        }

        return drawn;
    }

    /// <summary>Applies the rulings; returns the pinned shapes.</summary>
    private static HashSet<string> Rule(Input input, List<Entry> entries)
    {
        var pinned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ruling in input.Rulings)
        {
            var from = Normal(ruling.From, input.FromText);
            var to = Normal(ruling.To, input.ToText);
            bool Sourced(Entry e) => ruling.Source.Length == 0 || e.Row.Source.StartsWith(ruling.Source, StringComparison.Ordinal);
            bool Related(Entry e) => ruling.Relation.Length == 0 || e.Row.Relation == ruling.Relation;
            if (ruling.Action == RulingRow.Accept)
            {
                var shape = from + "|" + to;
                pinned.Add(shape);
                if (!entries.Any(e => e.Status is null && e.Row.Shape == shape))
                {
                    entries.Add(new Entry(MappingMethods.Ruling, -1, new MappingRow(null, from, to,
                        ruling.Relation.Length == 0 ? "renders" : ruling.Relation, MappingMethods.Ruling, null,
                        "a ruling of this project: " + ruling.Reason, null, null, null)));
                }

                continue;
            }

            if (ruling.Action != RulingRow.Refuse)
            {
                throw new InvalidDataException($"{input.Pair}: a ruling is refuse or accept, not '{ruling.Action}'.");
            }

            if (from.Length > 0 && to.Length > 0)
            {
                foreach (var entry in entries.Where(e => e.Status is null && e.Row.From == from && e.Row.To == to && Sourced(e) && Related(e)))
                {
                    entry.Status = MappingStatus.RefusedByRuling;
                }

                continue;
            }

            if (ruling.Source.Length == 0)
            {
                throw new InvalidDataException($"{input.Pair}: a ruling on one side's words names the source whose rows lose them.");
            }

            var onFrom = from.Length > 0;
            var words = MappingText.Split(onFrom ? from : to).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in entries.Where(e => e.Status is null && Sourced(e) && Related(e)).ToList())
            {
                var side = MappingText.Split(onFrom ? entry.Row.From : entry.Row.To).ToList();
                if (!side.Any(words.Contains))
                {
                    continue;
                }

                var left = string.Join(' ', side.Where(k => !words.Contains(k)));
                if (left.Length == 0)
                {
                    entry.Status = MappingStatus.RefusedByRuling;
                    continue;
                }

                entry.Status = MappingStatus.NarrowedByRuling;
                entries.Add(new Entry(entry.File, -1, onFrom ? entry.Row with { From = left } : entry.Row with { To = left }));
            }
        }

        return pinned;
    }

    private static string Normal(string keys, string text) =>
        keys.Length == 0 ? string.Empty : MappingText.Keys(MappingText.Split(keys).Select(k => WordKey.Parse(k, text)));

    /// <summary>The greedy competition, highest rank first; returns the candidates that stand.</summary>
    private static List<Candidate> Compete(List<Candidate> ordered)
    {
        var fromHeld = new Dictionary<string, (int Rank, string Method)>(StringComparer.Ordinal);
        var toRendered = new Dictionary<string, (int Rank, string Method)>(StringComparer.Ordinal);
        var toAbsent = new Dictionary<string, (int Rank, string Method)>(StringComparer.Ordinal);
        var accepted = new List<Candidate>(ordered.Count);

        static void Strongest(Dictionary<string, (int Rank, string Method)> held, IEnumerable<string> keys, ref (int Rank, string Method) best)
        {
            foreach (var key in keys)
            {
                if (held.TryGetValue(key, out var holder) && holder.Rank > best.Rank)
                {
                    best = holder;
                }
            }
        }

        static void Hold(Dictionary<string, (int Rank, string Method)> held, IEnumerable<string> keys, int rank, string method)
        {
            foreach (var key in keys)
            {
                if (!held.TryGetValue(key, out var holder) || holder.Rank < rank)
                {
                    held[key] = (rank, method);
                }
            }
        }

        foreach (var candidate in ordered)
        {
            var from = MappingText.Split(candidate.Top.Row.From).ToList();
            var to = MappingText.Split(candidate.Top.Row.To).ToList();
            var relation = candidate.Top.Row.Relation;
            var best = (Rank: int.MinValue, Method: string.Empty);
            switch (relation)
            {
                case "omits":
                    Strongest(toRendered, to, ref best);
                    Strongest(toAbsent, to, ref best);
                    break;
                case "expands":
                    Strongest(fromHeld, from, ref best);
                    break;
                default:
                    Strongest(fromHeld, from, ref best);
                    Strongest(toAbsent, to, ref best);
                    break;
            }

            if (best.Rank > candidate.Rank)
            {
                foreach (var row in candidate.Rows)
                {
                    row.Status = MappingStatus.WithdrawnBy(best.Method);
                }

                continue;
            }

            var method = candidate.Top.Row.Method;
            switch (relation)
            {
                case "omits":
                    Hold(toAbsent, to, candidate.Rank, method);
                    break;
                case "expands":
                    Hold(fromHeld, from, candidate.Rank, method);
                    break;
                default:
                    Hold(fromHeld, from, candidate.Rank, method);
                    Hold(toRendered, to, candidate.Rank, method);
                    break;
            }

            accepted.Add(candidate);
        }

        return accepted;
    }

    private static List<FinalRow> Finalise(Input input, List<Candidate> accepted)
    {
        var used = new HashSet<long>();
        var next = input.PreviousIds.Values.Where(id => id >= input.IdBase && id < input.IdBase + PairOrdinals.Range).DefaultIfEmpty(input.IdBase - 1).Max() + 1;
        var final = new List<FinalRow>(accepted.Count * 5 / 4);
        foreach (var candidate in accepted)
        {
            var id = candidate.Rows.Select(r => r.Row.Id).Where(i => i is not null && !used.Contains(i.Value)).Min()
                     ?? (input.PreviousIds.TryGetValue(candidate.Shape, out var previous) && !used.Contains(previous) ? previous : next++);
            used.Add(id);
            var top = candidate.Top.Row;
            var seen = new HashSet<(Database.Entities.Enums.LinkMethod, string)>();
            foreach (var entry in candidate.Rows)
            {
                var row = entry.Row;
                if (!seen.Add((MappingMethods.DatabaseMethod(row.Method), row.Source)))
                {
                    continue;
                }

                final.Add(new FinalRow(id, top.From, top.To, top.Relation, top.Roles, ReferenceEquals(entry, candidate.Top), row.Method, row.Confidence, row.Source));
            }
        }

        return final;
    }

    private static void Report(Input input, List<Entry> entries, List<FinalRow> final, HashSet<string> drawnStale, List<ReportRow> report)
    {
        foreach (var group in entries.Where(e => e.Index >= 0).GroupBy(e => e.File).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            report.Add(new ReportRow("rows", group.Key, string.Empty, group.Count(), "rows in the method file"));
            report.Add(new ReportRow("standing", group.Key, string.Empty, group.Count(e => e.Status is null), "rows that are claims of the final"));
        }

        foreach (var group in entries.Where(e => e.Status is not null && e.Index >= 0)
                     .GroupBy(e => (e.Status!, e.Row.Method))
                     .OrderBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => g.Key.Method, StringComparer.Ordinal))
        {
            var (status, method) = group.Key;
            var (kind, by) = status.StartsWith("withdrawn-by ", StringComparison.Ordinal)
                ? ("withdrawn", status["withdrawn-by ".Length..])
                : (status.Split(' ')[0], string.Empty);
            report.Add(new ReportRow(kind, method, by, group.Count(), status));
        }

        foreach (var method in drawnStale.Order(StringComparer.Ordinal))
        {
            report.Add(new ReportRow("needs", method, MappingMethods.Regenerates(method), 0,
                $"verses whose text moved lost their {method} rows; {input.Pair} needs {MappingMethods.Regenerates(method)} again"));
        }

        report.Add(new ReportRow("links", string.Empty, string.Empty, final.Count(r => r.Settled), "links of the final"));
        report.Add(new ReportRow("claims", string.Empty, string.Empty, final.Count, "claims of the final"));
        foreach (var group in final.Where(r => r.Settled).GroupBy(r => r.Method).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            report.Add(new ReportRow("settled", group.Key, string.Empty, group.Count(), "links whose own claim is this method's"));
        }

        if (input.Manifests.GetValueOrDefault(input.FromText) is { } manifest)
        {
            var linked = final.SelectMany(r => MappingText.Split(r.From)).Select(k => k[..k.LastIndexOf('.')]).ToHashSet(StringComparer.Ordinal);
            var unlinked = manifest.Keys.Count(v => !linked.Contains(v.ToString()));
            report.Add(new ReportRow("unlinked-verses", string.Empty, string.Empty, unlinked, $"verses of {input.FromText} no link of the final names a word of"));
        }
    }

    private static int CompareBySort(Candidate a, Candidate b)
    {
        var order = a.Sort.Side.CompareTo(b.Sort.Side);
        if (order == 0) order = a.Sort.First.CompareTo(b.Sort.First);
        if (order == 0) order = string.CompareOrdinal(a.Top.Row.From, b.Top.Row.From);
        if (order == 0) order = string.CompareOrdinal(a.Top.Row.To, b.Top.Row.To);
        return order;
    }

    /// <summary>Rank, then <see cref="MappingMethods.SettlesBefore"/> between equals: the order a link's claims stand in.</summary>
    private static readonly Comparer<Entry> TopFirst = Comparer<Entry>.Create((a, b) =>
    {
        var order = b.Rank.CompareTo(a.Rank);
        if (order == 0) order = MappingMethods.SettlesBefore(a.Row, b.Row);
        if (order == 0) order = string.CompareOrdinal(a.File, b.File);
        if (order == 0) order = a.Index.CompareTo(b.Index);
        return order;
    });
}
