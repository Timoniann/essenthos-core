using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// One pair's method files made into its <c>final</c> by the stated precedence (DOC-0224 §6.4):
/// <list type="number">
/// <item>rows citing a verse whose text moved since they were made are stale and stand aside; a model's rows
/// stay out until the pair is aligned again (the owner's D1);</item>
/// <item>a pair's rulings refuse shapes or words and pin shapes;</item>
/// <item>an approved EVIDENTIA verdict that a word has no counterpart takes that word out of a Strong pairing laid from
/// another edition (<see cref="NarrowLaidStrong"/>);</item>
/// <item>rows naming the same words are one link: the top-ranked row is its settled claim, the others its claims;</item>
/// <item>links of different words competing for a word: the higher-ranked keeps it, the lower is withdrawn
/// whole, by the method that won; an absence a source states or a person decides wins its word over any
/// correspondence, an EVIDENTIA verdict's absence only over the aligner's; links of equal rank stand side
/// by side, as one source's own several links on a word do;</item>
/// <item>a machine's link across two verses no verse link joins is withdrawn (<see cref="CrossingRule"/>).</item>
/// </list>
/// Nothing is deleted: every row stays in its method file, with what the merge decided in its status. The
/// output is a function of the inputs and sorted by key, so a second merge of the same files writes the same bytes; a
/// link's id is derived from its words (<see cref="LinkIds"/>), never carried.
/// </summary>
internal static class MappingMerge
{
    /// <summary>The report's kind for rows a changed verse made stale that no producer from files makes again.</summary>
    public const string NoProducer = "no-producer";

    /// <param name="Hashes">The text hash of each cited verse when the rows were made (<see cref="VerseHashRow"/>).</param>
    /// <param name="Manifests">Each text's verse manifest as it stands, or null where the text has none yet.</param>
    /// <param name="Ordinal">The pair's ordinal in <c>mapping/pairs</c>, the top bits of its links' ids.</param>
    /// <param name="Folds">The fold of each cited verse when the rows were made (<see cref="VerseFold"/>), where it was kept.</param>
    /// <param name="FoldNow">The fold of a verse of a text as its words stand, asked only of a verse whose text hash moved.</param>
    /// <param name="Remade">The method files a producer made from the two texts' words as they stand, each with the sources whose rows it
    /// carried from an earlier file instead (<see cref="MethodFiles.KeptSources"/>): their other rows answer for no verse that moved.</param>
    /// <param name="Producers">The producer that wrote each method file, where one from files did; a moved verse's rows wait for it.</param>
    /// <param name="Verses">The two texts' frames, for <see cref="CrossingRule"/>; null leaves the rule out.</param>
    /// <param name="Yielded">The laid Strong rows the producer left to a stated link (<see cref="MappingProducts.StrongYielded"/>).</param>
    public sealed record Input(
        string Pair,
        string FromText,
        string ToText,
        IReadOnlyDictionary<string, List<MappingRow>> Methods,
        IReadOnlyList<RulingRow> Rulings,
        IReadOnlyDictionary<(string Text, VerseKey Verse), string> Hashes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<VerseKey, string>?> Manifests,
        long Ordinal,
        IReadOnlyDictionary<(string Text, VerseKey Verse), string>? Folds = null,
        Func<string, VerseKey, string?>? FoldNow = null,
        PairFrames? Verses = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? Remade = null,
        IReadOnlyDictionary<string, string>? Producers = null,
        IReadOnlyList<MappingRow>? Yielded = null);

    /// <param name="Methods">Every method file's rows with the status the merge gave them.</param>
    public sealed record Output(List<FinalRow> Final, Dictionary<string, List<MappingRow>> Methods, List<ReportRow> Report);

    private sealed class Entry(string file, int index, MappingRow row, bool laidStrong)
    {
        public string File { get; } = file;
        public int Index { get; } = index;
        public MappingRow Row { get; set; } = row;
        public string? Status { get; set; }
        public int Rank { get; } = MappingMethods.Rank(row.Method, row.Confidence, row.Evidence, laidStrong);
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
        var laid = MappingMethods.LaysStrong(input.FromText, input.ToText);
        foreach (var (file, rows) in input.Methods.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                entries.Add(new Entry(file, i, rows[i] with { Status = null }, laid));
            }
        }

        var report = new List<ReportRow>();
        var (stale, lost) = Stale(input, entries, report);
        var pinned = Rule(input, entries);
        Supersede(entries);
        if (laid)
        {
            NarrowLaidStrong(entries, pinned, input.Yielded ?? [], report);
        }

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
                : !MappingMethods.IsAbsence(candidate.Top.Row.Relation)
                    ? candidate.Top.Rank
                    : MappingMethods.StatesAbsence(candidate.Top.Row.Method)
                        ? Math.Max(MappingMethods.StatedAbsence, candidate.Top.Rank)
                        : candidate.Top.Row.Method == MappingMethods.Evidentia
                            ? MappingMethods.EvidentiaAbsence
                            : candidate.Top.Rank;
            candidate.Sort = MappingText.SortKey(candidate.Top.Row.From, candidate.Top.Row.To, input.FromText, input.ToText);
        }

        GiveWay(candidates, pinned);

        candidates.Sort((a, b) =>
        {
            var order = b.Rank.CompareTo(a.Rank);
            return order != 0 ? order : CompareBySort(a, b);
        });

        var (accepted, withdrawn, refinements) = Compete(candidates);
        var (orphans, bare, left) = KeepOrphans(accepted, withdrawn, pinned);
        accepted.Sort(CompareBySort);
        var (final, collisions) = Finalise(input, accepted);
        if (input.Verses is { } verses)
        {
            final = WithdrawCrossings(verses, accepted, final);
        }


        var methods = new Dictionary<string, List<MappingRow>>(StringComparer.Ordinal);
        foreach (var (file, rows) in input.Methods)
        {
            methods[file] = new List<MappingRow>(rows.Count);
        }

        foreach (var entry in entries.Where(e => e.Index >= 0).OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.Index))
        {
            methods[entry.File].Add(input.Methods[entry.File][entry.Index] with { Status = entry.Status });
        }

        report.Add(new ReportRow("refinements", string.Empty, string.Empty, refinements,
            "links inside a higher-ranked link on both sides, kept as links of their own"));
        report.Add(new ReportRow("orphans", string.Empty, string.Empty, orphans,
            $"links kept because the competition would leave a word of {input.FromText} with none (at least {MappingMethods.OrphanFloor} sure)"));
        report.Add(new ReportRow("bare-words", string.Empty, string.Empty, bare,
            $"words of {input.FromText} the competition alone would leave with no link"));
        report.Add(new ReportRow("bare-words-left", string.Empty, string.Empty, left,
            $"words of {input.FromText} still with no link after the kept ones: their links are under {MappingMethods.OrphanFloor}"));
        if (collisions > 0)
        {
            report.Add(new ReportRow("id-collisions", string.Empty, string.Empty, collisions,
                "links whose words hashed to an id another link of the pair had taken, numbered by hashing again"));
        }

        Report(input, entries, final, stale, lost, report);
        return new Output(final, methods, report);
    }

    /// <summary>
    /// The rows citing a verse whose text moved, or which is gone, since they were made. A verse whose text hash moved but
    /// whose words still read the same (its fold then and now agree) has not moved for its rows: only marks or punctuation
    /// changed, and every cited key still names the word it named.
    /// </summary>
    private static (HashSet<string> Drawn, Dictionary<(string File, string Method, string Source), int> Lost) Stale(
        Input input, List<Entry> entries, List<ReportRow> report)
    {
        var drawn = new HashSet<string>(StringComparer.Ordinal);
        var lost = new Dictionary<(string File, string Method, string Source), int>();
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
            return (drawn, lost);
        }

        var verdicts = new Dictionary<(bool From, string Verse), bool>();
        var readSame = 0;
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
            if (moved && manifest!.ContainsKey(verseKey) && input.Folds?.TryGetValue((text, verseKey), out var foldThen) == true
                && input.FoldNow?.Invoke(text, verseKey) == foldThen)
            {
                moved = false;
                readSame++;
            }

            verdicts[(from, verse)] = moved;
            return moved;
        }

        foreach (var entry in entries)
        {
            if (input.Remade?.TryGetValue(entry.File, out var kept) == true && !kept.Contains(entry.Row.Source))
            {
                continue;
            }

            if (MappingText.Split(entry.Row.From).Any(k => Moved(true, k)) || MappingText.Split(entry.Row.To).Any(k => Moved(false, k)))
            {
                entry.Status = MappingStatus.Stale;
                if (MappingMethods.Drawn(entry.Row.Method))
                {
                    drawn.Add(entry.Row.Method);
                }
                else
                {
                    var key = (entry.File, entry.Row.Method, entry.Row.Source);
                    lost[key] = lost.GetValueOrDefault(key) + 1;
                }
            }
        }

        if (readSame > 0)
        {
            report.Add(new ReportRow("read-same", string.Empty, string.Empty, readSame,
                "cited verses whose text moved but whose words read the same, only marks or punctuation otherwise: their rows stand"));
        }

        return (drawn, lost);
    }

    /// <summary>
    /// A newer EVIDENTIA run replaces an older one within what it covers: the books it gave verdicts in, in
    /// the tiers it gave them in (a run applied for the attached tier alone decided nothing about the safe
    /// tier). An older run's claim there is superseded; outside it, the older run stands. Runs are ordered
    /// by their number.
    /// </summary>
    private static void Supersede(List<Entry> entries)
    {
        static int Book(MappingRow row)
        {
            var key = MappingText.Split(row.From.Length > 0 ? row.From : row.To).First();
            return int.Parse(key[..key.IndexOf('.')], System.Globalization.CultureInfo.InvariantCulture);
        }

        var verdicts = entries
            .Where(e => e.Status is null && e.Row.Method == MappingMethods.Evidentia)
            .Select(e => (Entry: e, Run: MappingMethods.EvidentiaRun(e.Row.Source), Tier: MappingMethods.Tier(e.Row.Evidence) ?? string.Empty))
            .Where(v => v.Run is not null)
            .Select(v => (v.Entry, Run: v.Run!.Value, v.Tier, Book: Book(v.Entry.Row)))
            .ToList();
        var newest = new Dictionary<(string Tier, int Book), int>();
        foreach (var verdict in verdicts)
        {
            if (!newest.TryGetValue((verdict.Tier, verdict.Book), out var run) || run < verdict.Run)
            {
                newest[(verdict.Tier, verdict.Book)] = verdict.Run;
            }
        }

        foreach (var verdict in verdicts)
        {
            var run = newest[(verdict.Tier, verdict.Book)];
            if (run > verdict.Run)
            {
                verdict.Entry.Status = MappingStatus.SupersededBy(run);
            }
        }
    }

    /// <summary>
    /// An approved EVIDENTIA verdict that a word has no counterpart outranks a Strong pairing laid onto the text from another
    /// edition (the owner's order, NOT-0218: a verdict ranks below a text's own numbers and above numbers laid from elsewhere,
    /// which pair a supplied word with the phrase it stands in, "el Cristo"). The absent word is taken out of the laid link, as a
    /// ruling takes a word out of a source's rows, and the rest of the link stands as drawn; a link left with no word on that
    /// side is withdrawn by the verdict. The verdict narrows the laid link whatever else claims the word: against every other
    /// method its absence still gives way (<see cref="GiveWay"/>). A pinned shape is never narrowed. A laid row the producer left
    /// to a stated link because its words were not the link's (<paramref name="yielded"/>) is narrowed the same way and becomes a
    /// claim on that link where it then names exactly the link's words.
    /// </summary>
    private static void NarrowLaidStrong(List<Entry> entries, HashSet<string> pinned, IReadOnlyList<MappingRow> yielded, List<ReportRow> report)
    {
        var absentFrom = new HashSet<string>(StringComparer.Ordinal);
        var absentTo = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in entries.Where(e => e.Status is null && e.Row.Method == MappingMethods.Evidentia && MappingMethods.IsAbsence(e.Row.Relation))
                     .Select(e => e.Row))
        {
            if (row.Relation == "omits")
            {
                absentTo.UnionWith(MappingText.Split(row.To));
            }
            else
            {
                absentFrom.UnionWith(MappingText.Split(row.From));
            }
        }

        if (absentFrom.Count == 0 && absentTo.Count == 0)
        {
            return;
        }

        (string From, string To)? Narrowed(MappingRow row)
        {
            var from = MappingText.Split(row.From).Where(k => !absentFrom.Contains(k)).ToList();
            var to = MappingText.Split(row.To).Where(k => !absentTo.Contains(k)).ToList();
            return from.Count == 0 || to.Count == 0 ? null : (string.Join(' ', from), string.Join(' ', to));
        }

        foreach (var entry in entries.Where(e => e.Status is null && e.Row.Method == MappingMethods.Strong
                                                 && !MappingMethods.IsAbsence(e.Row.Relation) && !pinned.Contains(e.Row.Shape)).ToList())
        {
            var narrowed = Narrowed(entry.Row);
            if (narrowed is { } same && same.From == entry.Row.From && same.To == entry.Row.To)
            {
                continue;
            }

            if (narrowed is not { } left)
            {
                entry.Status = MappingStatus.WithdrawnBy(MappingMethods.Evidentia);
                continue;
            }

            entry.Status = MappingStatus.NarrowedByEvidentia;
            entries.Add(new Entry(entry.File, -1, entry.Row with
            {
                From = left.From,
                To = left.To,
                Roles = KeptRoles(entry.Row.Roles, MappingText.Split(left.From).ToHashSet(StringComparer.Ordinal)),
            }, laidStrong: true));
        }

        var stated = entries.Where(e => e.Status is null && MappingMethods.StatesAbsence(e.Row.Method) && !MappingMethods.IsAbsence(e.Row.Relation))
            .Select(e => e.Row.Shape).ToHashSet(StringComparer.Ordinal);
        var regained = 0;
        foreach (var row in yielded)
        {
            if (Narrowed(row) is { } narrowed && (narrowed.From != row.From || narrowed.To != row.To)
                && stated.Contains(narrowed.From + "|" + narrowed.To))
            {
                entries.Add(new Entry(MappingMethods.Strong, -1, row with
                {
                    From = narrowed.From,
                    To = narrowed.To,
                    Roles = KeptRoles(row.Roles, MappingText.Split(narrowed.From).ToHashSet(StringComparer.Ordinal)),
                    Evidence = Loading.Links.TaggedTextLinkLoader.CorroborationNote,
                    Status = null,
                }, laidStrong: true));
                regained++;
            }
        }

        if (regained > 0)
        {
            report.Add(new ReportRow("regained", MappingMethods.Strong, MappingMethods.Evidentia, regained,
                "laid Strong rows the producer left to a stated link, claims on it once EVIDENTIA's absent words are out"));
        }
    }

    /// <summary>The roles of a link's FROM words that name only words still in it, or null when none is left.</summary>
    private static string? KeptRoles(string? roles, HashSet<string> from)
    {
        if (roles is null)
        {
            return null;
        }

        var kept = roles.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(role =>
        {
            var equals = role.IndexOf('=');
            var at = role.LastIndexOf('@');
            return equals > 0 && from.Contains(role[..equals]) && (at < 0 || from.Contains(role[(at + 1)..]));
        }).ToList();
        return kept.Count == 0 ? null : string.Join(' ', kept);
    }

    /// <summary>
    /// An EVIDENTIA absence on a word that a correspondence claimed by anything but the aligner names gives
    /// way to it (<see cref="MappingMethods.EvidentiaAbsence"/>): on the TO side for an omission, on the FROM
    /// side for an expansion.
    /// </summary>
    private static void GiveWay(List<Candidate> candidates, HashSet<string> pinned)
    {
        var from = new HashSet<string>(StringComparer.Ordinal);
        var to = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!MappingMethods.IsAbsence(candidate.Top.Row.Relation) && !MappingMethods.OnlyTheAligner(candidate.Rows.Select(r => r.Row.Method)))
            {
                from.UnionWith(MappingText.Split(candidate.Top.Row.From));
                to.UnionWith(MappingText.Split(candidate.Top.Row.To));
            }
        }

        foreach (var candidate in candidates)
        {
            var row = candidate.Top.Row;
            if (row.Method != MappingMethods.Evidentia || !MappingMethods.IsAbsence(row.Relation) || pinned.Contains(candidate.Shape))
            {
                continue;
            }

            var contested = row.Relation == "omits" ? MappingText.Split(row.To).Any(to.Contains) : MappingText.Split(row.From).Any(from.Contains);
            if (contested)
            {
                candidate.Rank = MappingMethods.GivesWay;
            }
        }
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
                    entries.Add(new Entry(MappingMethods.Ruling, -1, new MappingRow(from, to,
                        ruling.Relation.Length == 0 ? "renders" : ruling.Relation, MappingMethods.Ruling, null,
                        "a ruling of this project: " + ruling.Reason, null, null, null), false));
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
                entries.Add(new Entry(entry.File, -1, onFrom ? entry.Row with { From = left } : entry.Row with { To = left },
                    MappingMethods.LaysStrong(input.FromText, input.ToText)));
            }
        }

        return pinned;
    }

    private static string Normal(string keys, string text) =>
        keys.Length == 0 ? string.Empty : MappingText.Keys(MappingText.Split(keys).Select(k => WordKey.Parse(k, text)));

    /// <summary>
    /// The greedy competition, highest rank first. A candidate that loses a word to a higher-ranked link
    /// but lies inside a standing link on both sides is a refinement of it and stands as a link of its own.
    /// Returns the candidates that stand, those withdrawn, and how many stood as refinements.
    /// </summary>
    private static (List<Candidate> Accepted, List<Candidate> Withdrawn, int Refinements) Compete(List<Candidate> ordered)
    {
        var standing = new Dictionary<string, List<(HashSet<string> From, HashSet<string> To)>>(StringComparer.Ordinal);
        var withdrawn = new List<Candidate>();
        var refinements = 0;
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
                if (!MappingMethods.IsAbsence(relation) && from.Count > 0 && to.Count > 0
                    && standing.TryGetValue(from[0], out var holders)
                    && holders.Any(h => from.All(h.From.Contains) && to.All(h.To.Contains)))
                {
                    refinements++;
                    accepted.Add(candidate);
                    continue;
                }

                foreach (var row in candidate.Rows)
                {
                    row.Status = MappingStatus.WithdrawnBy(best.Method);
                }

                withdrawn.Add(candidate);
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
                    var shape = (from.ToHashSet(StringComparer.Ordinal), to.ToHashSet(StringComparer.Ordinal));
                    foreach (var key in from)
                    {
                        if (!standing.TryGetValue(key, out var list))
                        {
                            standing[key] = list = [];
                        }

                        list.Add(shape);
                    }

                    break;
            }

            accepted.Add(candidate);
        }

        return (accepted, withdrawn, refinements);
    }

    /// <summary>
    /// A word of the FROM text that the competition left with no standing link keeps its best withdrawn
    /// link, highest rank then surest first, if that link is sure enough (<see cref="MappingMethods.KeepsOrphan"/>).
    /// Returns how many links were kept so, how many words were bare before, and how many still are.
    /// </summary>
    private static (int Kept, int Bare, int Left) KeepOrphans(List<Candidate> accepted, List<Candidate> withdrawn, HashSet<string> pinned)
    {
        var held = accepted.SelectMany(c => MappingText.Split(c.Top.Row.From)).ToHashSet(StringComparer.Ordinal);
        var bare = withdrawn.SelectMany(c => MappingText.Split(c.Top.Row.From)).Where(k => !held.Contains(k)).ToHashSet(StringComparer.Ordinal);
        var best = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var candidate in withdrawn)
        {
            if (!MappingMethods.KeepsOrphan(candidate.Top.Row, pinned.Contains(candidate.Shape)))
            {
                continue;
            }

            foreach (var key in MappingText.Split(candidate.Top.Row.From))
            {
                if (!held.Contains(key) && (!best.TryGetValue(key, out var other) || Better(candidate, other)))
                {
                    best[key] = candidate;
                }
            }
        }

        var kept = 0;
        foreach (var (key, candidate) in best.OrderBy(b => b.Value.Sort.First).ThenBy(b => b.Key, StringComparer.Ordinal))
        {
            if (held.Contains(key))
            {
                continue;
            }

            foreach (var row in candidate.Rows)
            {
                row.Status = null;
            }

            held.UnionWith(MappingText.Split(candidate.Top.Row.From));
            accepted.Add(candidate);
            kept++;
        }

        return (kept, bare.Count, bare.Count(k => !held.Contains(k)));

        static bool Better(Candidate a, Candidate b)
        {
            var order = a.Rank.CompareTo(b.Rank);
            if (order == 0) order = (a.Top.Row.Confidence ?? 2).CompareTo(b.Top.Row.Confidence ?? 2);
            return order > 0 || (order == 0 && CompareBySort(a, b) < 0);
        }
    }

    /// <summary>
    /// <see cref="CrossingRule"/> over the standing links, once the verse links the final makes are known: a link withdrawn
    /// here leaves its words as they are, with no other link put in its place.
    /// </summary>
    private static List<FinalRow> WithdrawCrossings(PairFrames verses, List<Candidate> accepted, List<FinalRow> final)
    {
        var joined = VerseLinks.Joins(verses, final);
        var withdrawn = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in accepted)
        {
            if (CrossingRule.Applies(candidate.Rows.Select(r => r.Row.Method))
                && CrossingRule.Withdraws(candidate.Top.Row.From, candidate.Top.Row.To, verses, joined))
            {
                foreach (var row in candidate.Rows)
                {
                    row.Status = MappingStatus.WithdrawnBy(CrossingRule.Name);
                }

                withdrawn.Add(candidate.Top.Row.From + "|" + candidate.Top.Row.To);
            }
        }

        return withdrawn.Count == 0 ? final : [.. final.Where(r => !withdrawn.Contains(r.From + "|" + r.To))];
    }

    /// <summary>The final's rows, links in key order, each numbered from its words.</summary>
    private static (List<FinalRow> Final, int Collisions) Finalise(Input input, List<Candidate> accepted)
    {
        var ids = new LinkIds.Allocator(input.Ordinal, input.Pair);
        var final = new List<FinalRow>(accepted.Count * 5 / 4);
        foreach (var candidate in accepted)
        {
            var top = candidate.Top.Row;
            var id = ids.Next(top.From, top.To);
            // The words' roles are the link's, whichever claim carries them: a source states the words and a verdict their roles.
            var roles = top.Roles ?? candidate.Rows.Select(r => r.Row.Roles).FirstOrDefault(r => r is not null);
            var seen = new HashSet<(Database.Entities.Enums.LinkMethod, string)>();
            foreach (var entry in candidate.Rows)
            {
                var row = entry.Row;
                if (!seen.Add((MappingMethods.DatabaseMethod(row.Method), row.Source)))
                {
                    continue;
                }

                final.Add(new FinalRow(id, top.From, top.To, top.Relation, roles, ReferenceEquals(entry, candidate.Top), row.Method, row.Confidence, row.Source));
            }
        }

        return (final, ids.Collisions);
    }

    private static void Report(Input input, List<Entry> entries, List<FinalRow> final, HashSet<string> drawnStale,
        Dictionary<(string File, string Method, string Source), int> lost, List<ReportRow> report)
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
                : status.StartsWith("superseded-by ", StringComparison.Ordinal)
                    ? ("superseded", status["superseded-by ".Length..])
                    : status.StartsWith("narrowed-by ", StringComparison.Ordinal)
                        ? ("narrowed", status["narrowed-by ".Length..])
                        : (status.Split(' ')[0], string.Empty);
            report.Add(new ReportRow(kind, method, by, group.Count(), status));
        }

        foreach (var method in drawnStale.Order(StringComparer.Ordinal))
        {
            report.Add(new ReportRow("needs", method, MappingMethods.Regenerates(method), 0,
                $"verses whose text moved lost their {method} rows; {input.Pair} needs {MappingMethods.Regenerates(method)} again"));
        }

        foreach (var ((file, method, source), rows) in lost.OrderBy(l => l.Key.Method, StringComparer.Ordinal).ThenBy(l => l.Key.Source, StringComparer.Ordinal))
        {
            // In a file a producer made again, the only stale rows are those of the sources it carried over and does not make.
            var producer = input.Remade?.ContainsKey(file) == true ? null : input.Producers?.GetValueOrDefault(file);
            report.Add(producer is not null
                ? new ReportRow("needs", method, producer, rows,
                    $"rows of {source} on verses whose text moved since {producer} wrote the file; {input.Pair} needs it run again")
                : new ReportRow(NoProducer, method, source, rows,
                    $"rows of verses whose text moved, which no producer makes again: {input.Pair} has no {method} rows of this source there"));
        }

        var crossed = entries.Count(e => e.Index >= 0 && e.Status == MappingStatus.WithdrawnBy(CrossingRule.Name));
        if (crossed > 0)
        {
            report.Add(new ReportRow("needs", CrossingRule.Name, MappingMethods.Regenerates(MappingMethods.Aligner), crossed,
                $"machine links joining verses no verse link joins were withdrawn; {input.Pair} needs {MappingMethods.Regenerates(MappingMethods.Aligner)} again"));
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
