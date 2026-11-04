using System.Globalization;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// EVIDENTIA's verdicts as a pair's method file, <c>mapping/&lt;FROM&gt;-&lt;TO&gt;/evidentia</c>, made from the ledger
/// (<see cref="EvidentiaLedger"/>) and the two texts' words and frames, with no database.
///
/// <para>
/// **Every approved or corrected verdict of every run of the pair is a row**, in the shape the verdict states: a
/// pair for a proposal or a correction, one word with nothing opposite for an absence. A tier accepted unread is
/// the rule's claim with its confidence; a verdict a person read adds a person's claim (method <c>manual</c>); a
/// correction is the person's claim alone. Which verdict stands is the merge's to decide, as for every other
/// method: a newer run supersedes an older within the tiers and books it covers, a link of a higher-ranked
/// method withdraws the row, and an absence wins its word. So a verdict the database withheld, or a run accepted
/// and never applied there, is a row here.
/// </para>
///
/// <para>
/// **A verdict about one word pair of a wider link sits on that link**, as the database writes it: where no other
/// method's row names exactly its words and one names them among more (a phrase a source states), the row takes
/// that link's words and says so in its evidence, and the merge folds it into the link as a claim instead of
/// standing it beside the link as a refinement.
/// </para>
///
/// <para>
/// **An attached word says whose it is** (<see cref="AttachedWords"/>): the run's <see cref="EvidentiaLedger.HeadsFile"/>
/// names its head. A run recorded before heads were kept names it only by surface in the rationale
/// (<c>AuxiliaryVerb of 'see'</c>), and the head is then the word of that surface in the same verse, nearest first, that
/// the same run placed on the same counterpart or that another row joins to it. The role is written on every row
/// of the link, first mark kept.
/// </para>
///
/// <para>
/// A word is found by its text, its canonical address, its ordinal among the words at that address in the text's
/// own order, and its surface; a line whose word reads otherwise now is reported and left out, never guessed at.
/// The evidence omits the decision's row id, which the ledger does not keep; the run's number, its rules' version,
/// the rule and the tier are in the source and the evidence as the database writes them.
/// </para>
/// </summary>
internal static class EvidentiaFiles
{
    public const string Producer = "forge evidentia-pairs";

    private const string AttachedKind = AttachedWords.DecisionKind;

    /// <param name="Verdicts">Approved and corrected verdicts of the runs read.</param>
    /// <param name="Rows">Rows written, after verdicts on the same words by the same source were made one.</param>
    /// <param name="Within">Rows that sit on a wider link of another method.</param>
    /// <param name="Roles">Words marked as going with another word.</param>
    /// <param name="Conflicts">Lines whose words could not be found as they read.</param>
    /// <param name="LeftOut">Runs of the ledger not read because they were asked to be left out.</param>
    public sealed record Outcome(
        string Pair,
        int Runs,
        long Verdicts,
        long Rows,
        long Within,
        long Roles,
        IReadOnlyList<string> Conflicts,
        IReadOnlyList<int> LeftOut,
        bool Current = false)
    {
        private const int Named = 10;

        public override string ToString() => Current ? $"{Pair}: current, nothing written" :
            $"{Pair}: {Runs} runs, {Verdicts:N0} verdicts, {Rows:N0} rows ({Within:N0} on a wider link), {Roles:N0} attached words"
            + (LeftOut.Count == 0 ? string.Empty : $", runs {string.Join(',', LeftOut)} left out")
            + (Conflicts.Count == 0 ? string.Empty : $", {Conflicts.Count:N0} lines not found")
            + string.Concat(Conflicts.Take(Named).Select(conflict => "\n  " + conflict))
            + (Conflicts.Count > Named ? $"\n  … and {Conflicts.Count - Named:N0} more" : string.Empty);
    }

    /// <summary>The ledger's run folders, each with what its <c>run.json</c> says, in run order.</summary>
    public static List<(string Folder, EvidentiaLedger.LedgerRun Run)> Runs(string resources)
    {
        var root = EvidentiaLedger.Folder(resources);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateDirectories(root)
                .Where(folder => File.Exists(Path.Combine(folder, EvidentiaLedger.RunFile)))
                .Select(folder => (Folder: folder, Run: EvidentiaLedger.LedgerRun.Read(Path.Combine(folder, EvidentiaLedger.RunFile))))
                .OrderBy(held => held.Run.Run ?? int.MaxValue)
                .ThenBy(held => held.Run.StartedAt)
                .ThenBy(held => held.Folder, StringComparer.Ordinal),
        ];
    }

    /// <summary>The pairs the ledger holds runs of.</summary>
    public static List<string> Pairs(string resources) =>
        [.. Runs(resources).Select(held => MappingProducts.Pair(held.Run.From, held.Run.To)).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>Whether the store holds what a pair's file is made from besides the ledger: both texts' words and frames.</summary>
    public static bool CanWrite(ProductStore store, string pair)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        return store.Exists(TextWord.ProductOf(fromText)) && store.Exists(TextWord.ProductOf(toText))
               && store.Exists(FrameFile.Product(fromText)) && store.Exists(FrameFile.Product(toText));
    }

    /// <summary>
    /// Writes a pair's <c>evidentia</c> method file from the ledger under <paramref name="ledger"/> (a Resources folder),
    /// replacing what it held.
    /// </summary>
    /// <param name="leaveOut">Run numbers not to read, for a comparison with a corpus that never applied them.</param>
    /// <summary>What a pair's file is made from: the runs' ledger folders, both texts' words and frames, the pair's other method files.</summary>
    private static List<ProductInput> Inputs(ProductStore store, string pair, IEnumerable<string> folders, IEnumerable<string> others)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        return folders.Select(folder => store.Input(Path.GetRelativePath(store.ResourcesPath, folder).Replace('\\', '/')))
            .Concat(new[] { TextWord.ProductOf(fromText), FrameFile.Product(fromText), TextWord.ProductOf(toText), FrameFile.Product(toText) }
                .Select(store.ProductInput))
            .Concat(others.Select(store.ProductInput))
            .ToList();
    }

    public static Outcome Write(ProductStore store, string ledger, string pair, IReadOnlySet<int>? leaveOut = null, bool force = false)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var runs = Runs(ledger).Where(held => held.Run.From == fromText && held.Run.To == toText).ToList();
        foreach (var (folder, run) in runs.Where(held => held.Run.Run is null))
        {
            throw new InvalidDataException(
                $"{Path.Combine(folder, EvidentiaLedger.RunFile)} names no run number, which a verdict's claim needs. " +
                "Write it again with evidentia-export from the database that holds the run.");
        }

        var leftOut = runs.Where(held => leaveOut?.Contains(held.Run.Run!.Value) == true).Select(held => held.Run.Run!.Value).ToList();
        runs = [.. runs.Where(held => leaveOut?.Contains(held.Run.Run!.Value) != true)];

        var product = MappingProducts.Of(pair, MappingMethods.Evidentia);
        if (!force && !store.NeedsBuild(product, ProductKind.Machine, Inputs(store, pair, runs.Select(held => held.Folder),
                MappingProducts.Methods(store, pair).Where(m => m is not (MappingMethods.Evidentia or PairSources.Laid)).Select(m => MappingProducts.Of(pair, m)))))
        {
            return new Outcome(pair, runs.Count, 0, 0, 0, 0, [], leftOut, Current: true);
        }

        var lines = runs.Select(held => (held.Folder, held.Run, Lines: EvidentiaLedger.BookFiles(held.Folder).SelectMany(EvidentiaLedger.LedgerLine.ReadAll)
            .Where(line => line.Verdict is EvidentiaVerdict.Approved or EvidentiaVerdict.Corrected).ToList())).ToList();
        var books = lines.SelectMany(run => run.Lines).SelectMany(line => new[] { line.At.Book, line.TargetAt.Book, line.CorrectedAt.Book }).ToHashSet();
        var fromWords = CanonicalWords.Read(store, fromText, books);
        var toWords = CanonicalWords.Read(store, toText, books);
        var others = Others.Read(store, pair);

        var conflicts = new List<string>();
        var verdicts = 0L;
        var written = new List<Written>();
        var joined = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var seen = new HashSet<(string From, string To, string Relation, string Method, string Source)>();
        foreach (var (folder, run, runLines) in lines)
        {
            var name = Path.GetFileName(folder);
            var number = run.Run!.Value;
            var placed = new Dictionary<WordKey, List<WordKey>>();
            var attached = new List<(Written Row, WordKey Word, WordKey Target, string Rationale, EvidentiaLedger.Address At, int Nth)>();
            var heads = EvidentiaLedger.LedgerHead.ReadAll(folder)?.ToDictionary(head => (head.At, head.Word));
            foreach (var line in runLines)
            {
                verdicts++;
                var source = line.Word is { } word ? fromWords.Find(line.At, word, line.Surface, name, conflicts) : null;
                var target = line.Target is { } counterpart ? toWords.Find(line.TargetAt, counterpart, line.TargetSurface, name, conflicts) : null;
                var corrected = line.Corrected is { } named ? toWords.Find(line.CorrectedAt, named, line.CorrectedSurface, name, conflicts) : null;
                if ((line.Word is not null && source is null) || (line.Target is not null && target is null)
                    || (line.Corrected is not null && corrected is null))
                {
                    continue;
                }

                var correcting = line.Verdict == EvidentiaVerdict.Corrected;
                var (relation, from, to) = correcting || line.Absence is null
                    ? ("renders", new[] { source!.Value }, new[] { corrected ?? target!.Value })
                    : line.Absence == LinkRelation.Expands
                        ? ("expands", [source!.Value], [])
                        : ("omits", Array.Empty<WordKey>(), new[] { target!.Value });
                if (source is { } placedWord && target is { } placedOn)
                {
                    if (!placed.TryGetValue(placedWord, out var list))
                    {
                        placed[placedWord] = list = [];
                    }

                    list.Add(placedOn);
                }

                var session = run.Reviews[line.Review];
                var fromKeys = MappingText.Keys(from);
                var toKeys = MappingText.Keys(to);
                var within = others.Within(relation, fromKeys, toKeys, from, to);
                var note = within is null ? null : relation == "renders" ? EvidentiaLinkWriter.WithinNote : EvidentiaLinkWriter.WithinAbsenceNote;
                var shape = within ?? (fromKeys, toKeys);

                var claims = new List<(string Method, double? Confidence, string Source, string? Evidence)>();
                if (correcting || session.Examined)
                {
                    claims.Add((MappingMethods.Manual, null, EvidentiaLinkWriter.PersonSource(session.Reviewer, number, correcting),
                        Joined(note, session.Note)));
                }

                if (!correcting)
                {
                    claims.Add((MappingMethods.Evidentia,
                        line.Confidence is { } confidence ? Math.Round((double)confidence, 4) : null,
                        EvidentiaLinkWriter.RuleSource(number, run.RuleVersion, line.Rule ?? string.Empty),
                        Joined(note, EvidentiaLinkWriter.RuleEvidence(line.Tier, line.Rationale, session.Examined, session.Reviewer))));
                }

                foreach (var claim in claims)
                {
                    if (!seen.Add((shape.From, shape.To, relation, claim.Method, claim.Source)))
                    {
                        continue;
                    }

                    var row = new Written(new MappingRow(shape.From, shape.To, relation, claim.Method, claim.Confidence, claim.Source,
                        claim.Evidence, null, null), within is not null);
                    written.Add(row);
                    foreach (var key in MappingText.Split(row.Row.From))
                    {
                        if (!joined.TryGetValue(key, out var targets))
                        {
                            joined[key] = targets = new HashSet<string>(StringComparer.Ordinal);
                        }

                        targets.UnionWith(MappingText.Split(row.Row.To));
                    }

                    if (line.Rule == AttachedKind && line.Verdict == EvidentiaVerdict.Approved && source is { } attachedWord
                        && target is { } attachedTo && line.Rationale is { } rationale && claim.Method == MappingMethods.Evidentia)
                    {
                        attached.Add((row, attachedWord, attachedTo, rationale, line.At, line.Word!.Value));
                    }
                }
            }

            foreach (var (row, word, target, rationale, at, nth) in attached)
            {
                var head = heads is null
                    ? Head(word, target, rationale, fromWords, placed, others, joined)
                    : heads.TryGetValue((at, nth), out var listed)
                        ? fromWords.Find(listed.HeadAt, listed.Head, listed.HeadSurface, name, conflicts)
                        : null;
                if (head is { } found)
                {
                    var attachment = rationale.Split(' ', 2)[0];
                    var role = AttachedWords.PhraseAttachments.Contains(attachment) ? LinkWordRole.PhraseMember : LinkWordRole.Attached;
                    row.Marks.Add((word, role, found));
                }
            }
        }

        var roles = Roles(written);
        var rows = written
            .Select(w => w.Row with { Roles = roles.GetValueOrDefault(w.Row.Shape) })
            .Select(r => (Sort: MappingText.SortKey(r.From, r.To, fromText, toText), Row: r))
            .ToList();
        rows.Sort((a, b) => MappingExport.Order(a.Sort.Side, a.Sort.Key, a.Row, b.Sort.Side, b.Sort.Key, b.Row));

        var inputs = Inputs(store, pair, runs.Select(held => held.Folder), others.Products);
        using (var writer = store.Create(product, ProductKind.Machine, MappingRow.Schema,
                   ProductHeader.DescribeProducer($"{Producer} {pair}"), inputs, header =>
                   {
                       header.Notes.Add(new("runs", string.Join(' ', runs.Select(held => held.Run.Run))));
                       if (leftOut.Count > 0)
                       {
                           header.Notes.Add(new("left-out", string.Join(' ', leftOut)));
                       }
                   }))
        {
            writer.WriteAll(rows.Select(r => r.Row));
        }

        MappingFiles.RecordHashes(store, pair, rows.Select(r => r.Row));
        return new Outcome(pair, runs.Count, verdicts, rows.Count, written.Count(w => w.Within), roles.Values.Sum(r => r.Split(' ').Length),
            conflicts, leftOut);
    }

    /// <summary>
    /// The head an attached word goes with: the word of the surface its rationale names, in its own verse, nearest
    /// first, that the same run placed on the same counterpart or that a row of the pair joins to it.
    /// </summary>
    private static WordKey? Head(
        WordKey word,
        WordKey target,
        string rationale,
        CanonicalWords words,
        Dictionary<WordKey, List<WordKey>> placed,
        Others others,
        Dictionary<string, HashSet<string>> joined)
    {
        var attachment = rationale.Split(' ', 2)[0];
        var targetKey = target.Own;
        WordKey? best = null;
        foreach (var (candidate, surface) in words.InVerse(word.Verse))
        {
            if (candidate == word || !rationale.StartsWith($"{attachment} of '{surface}'", StringComparison.Ordinal))
            {
                continue;
            }

            var onIt = (placed.TryGetValue(candidate, out var targets) && targets.Contains(target))
                       || others.Joins(candidate.Own, targetKey)
                       || (joined.TryGetValue(candidate.Own, out var rendered) && rendered.Contains(targetKey));
            if (onIt && (best is null || Math.Abs(candidate.Position - word.Position) < Math.Abs(best.Value.Position - word.Position)))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>The roles of each link's words, by its shape, first mark of a word kept: <c>key=role@head</c> in key order.</summary>
    private static Dictionary<string, string> Roles(List<Written> written)
    {
        var marks = new Dictionary<string, Dictionary<WordKey, (LinkWordRole Role, WordKey Head)>>(StringComparer.Ordinal);
        foreach (var row in written)
        {
            foreach (var (word, role, head) in row.Marks)
            {
                if (!marks.TryGetValue(row.Row.Shape, out var byWord))
                {
                    marks[row.Row.Shape] = byWord = [];
                }

                byWord.TryAdd(word, (role, head));
            }
        }

        return marks.ToDictionary(
            pair => pair.Key,
            pair => string.Join(' ', pair.Value.OrderBy(mark => mark.Key).Select(mark => $"{mark.Key.Own}={EnumSpelling.Of(mark.Value.Role)}@{mark.Value.Head.Own}")),
            StringComparer.Ordinal);
    }

    private static string? Joined(string? first, string? second) =>
        (first, second) switch
        {
            (null, _) => second,
            (_, null) => first,
            _ => $"{first}; {second}",
        };

    private sealed class Written(MappingRow row, bool within)
    {
        public MappingRow Row { get; } = row;

        public bool Within { get; } = within;

        public List<(WordKey Word, LinkWordRole Role, WordKey Head)> Marks { get; } = [];
    }

    /// <summary>
    /// The words of one text by the ledger's address: canonical book, chapter and verse of the word's verse's primary
    /// place in the frame, and the word's ordinal among the words standing there, in the edition's own order
    /// (chapter, verse number, position, id), as <see cref="EvidentiaLedger"/> numbers them.
    /// </summary>
    internal sealed class CanonicalWords
    {
        private readonly string _text;
        private readonly Dictionary<(int Book, int Chapter, int Verse, int Nth), (WordKey Key, string Surface)> _byAddress = [];
        private readonly Dictionary<VerseKey, List<(WordKey Key, string Surface)>> _byVerse = [];

        private CanonicalWords(string text) => _text = text;

        public static CanonicalWords Read(ProductStore store, string text, IReadOnlySet<int> books)
        {
            var primary = new Dictionary<VerseKey, VerseKey>();
            using (var frame = store.Open(FrameFile.Product(text), FrameRow.Schema))
            {
                foreach (var row in frame.Rows())
                {
                    foreach (var (own, canonical) in row.Verses())
                    {
                        primary[own] = canonical[0];
                    }
                }
            }

            var at = new Dictionary<(int Book, int Chapter, int Verse), List<(TextWord Word, long Order)>>();
            var words = new CanonicalWords(text);
            using (var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text)))
            {
                foreach (var word in reader.Rows())
                {
                    var place = primary.TryGetValue(word.Key.Verse, out var moved) ? moved : FrameFile.AtOwn(word.Key.Verse);
                    if (!books.Contains(place.Book) && !books.Contains(word.Key.Verse.Book))
                    {
                        continue;
                    }

                    if (!words._byVerse.TryGetValue(word.Key.Verse, out var verse))
                    {
                        words._byVerse[word.Key.Verse] = verse = [];
                    }

                    verse.Add((word.Key, word.Surface));
                    var address = (place.Book, place.Chapter, place.Verse);
                    if (!at.TryGetValue(address, out var list))
                    {
                        at[address] = list = [];
                    }

                    list.Add((word, word.Id ?? long.MaxValue));
                }
            }

            foreach (var (address, list) in at)
            {
                list.Sort((a, b) =>
                {
                    var order = a.Word.Key.Verse.Chapter.CompareTo(b.Word.Key.Verse.Chapter);
                    if (order == 0) order = a.Word.Key.Verse.Verse.CompareTo(b.Word.Key.Verse.Verse);
                    if (order == 0) order = a.Word.Key.Position.CompareTo(b.Word.Key.Position);
                    if (order == 0) order = a.Order.CompareTo(b.Order);
                    return order;
                });
                for (var n = 0; n < list.Count; n++)
                {
                    words._byAddress[(address.Book, address.Chapter, address.Verse, n + 1)] = (list[n].Word.Key, list[n].Word.Surface);
                }
            }

            return words;
        }

        public WordKey? Find(EvidentiaLedger.Address at, int nth, string surface, string run, List<string> conflicts)
        {
            if (!_byAddress.TryGetValue((at.Book, at.Chapter, at.Verse, nth), out var word))
            {
                conflicts.Add($"{run}: {_text} {at} has no word {nth.ToString(CultureInfo.InvariantCulture)} any more");
                return null;
            }

            if (!string.Equals(word.Surface, surface, StringComparison.Ordinal))
            {
                conflicts.Add($"{run}: {_text} {at} word {nth.ToString(CultureInfo.InvariantCulture)} read '{surface}' and reads '{word.Surface}' now");
                return null;
            }

            return word.Key;
        }

        public IReadOnlyList<(WordKey Key, string Surface)> InVerse(VerseKey verse) =>
            _byVerse.TryGetValue(verse, out var words) ? words : [];
    }

    /// <summary>The other methods' rows of the pair, by the words they name: the links a verdict may sit on.</summary>
    private sealed class Others
    {
        private readonly Dictionary<string, List<Shape>> _byFrom = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Shape>> _byTo = new(StringComparer.Ordinal);
        private readonly HashSet<string> _shapes = new(StringComparer.Ordinal);

        public List<string> Products { get; } = [];

        private sealed record Shape(string From, string To, HashSet<string> FromKeys, HashSet<string> ToKeys)
        {
            public int Words => FromKeys.Count + ToKeys.Count;
        }

        /// <summary>The narrowest first, then by key: the order a verdict's wider links are tried in.</summary>
        private static readonly Comparer<Shape> Narrowest = Comparer<Shape>.Create((a, b) =>
        {
            var order = a.Words.CompareTo(b.Words);
            if (order == 0) order = string.CompareOrdinal(a.From, b.From);
            if (order == 0) order = string.CompareOrdinal(a.To, b.To);
            return order;
        });

        public static Others Read(ProductStore store, string pair)
        {
            var others = new Others();
            foreach (var method in MappingProducts.Methods(store, pair).Where(m => m is not (MappingMethods.Evidentia or PairSources.Laid)))
            {
                var product = MappingProducts.Of(pair, method);
                others.Products.Add(product);
                using var reader = store.Open(product, MappingRow.Schema);
                foreach (var row in reader.Rows())
                {
                    if (!others._shapes.Add(row.Shape))
                    {
                        continue;
                    }

                    var shape = new Shape(row.From, row.To, [.. MappingText.Split(row.From)], [.. MappingText.Split(row.To)]);
                    foreach (var key in shape.FromKeys)
                    {
                        Add(others._byFrom, key, shape);
                    }

                    foreach (var key in shape.ToKeys)
                    {
                        Add(others._byTo, key, shape);
                    }
                }
            }

            return others;
        }

        private static void Add(Dictionary<string, List<Shape>> index, string key, Shape shape)
        {
            if (!index.TryGetValue(key, out var list))
            {
                index[key] = list = [];
            }

            list.Add(shape);
        }

        /// <summary>
        /// The wider link a verdict sits on: none where a row names exactly its words; otherwise the narrowest link naming its
        /// words among more on both sides (for an absence, an absence naming more words), the first by key among equals.
        /// </summary>
        public (string From, string To)? Within(string relation, string fromKeys, string toKeys, WordKey[] from, WordKey[] to)
        {
            if (_shapes.Contains(fromKeys + "|" + toKeys))
            {
                return null;
            }

            var onFrom = relation != "omits";
            var word = onFrom ? from[0].Own : to[0].Own;
            var candidates = (onFrom ? _byFrom : _byTo).GetValueOrDefault(word);
            if (candidates is null)
            {
                return null;
            }

            var wider = candidates
                .Where(shape => from.All(key => shape.FromKeys.Contains(key.Own)) && to.All(key => shape.ToKeys.Contains(key.Own)))
                .Where(shape => relation == "renders" ? shape.FromKeys.Count > 0 && shape.ToKeys.Count > 0 : onFrom ? shape.ToKeys.Count == 0 : shape.FromKeys.Count == 0)
                .Min(Narrowest);
            return wider is null ? null : (wider.From, wider.To);
        }

        public bool Joins(string from, string to) =>
            _byFrom.TryGetValue(from, out var shapes) && shapes.Any(shape => shape.ToKeys.Contains(to));
    }
}
