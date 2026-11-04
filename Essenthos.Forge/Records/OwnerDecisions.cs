using System.Globalization;
using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

/// <summary>
/// The owner's console decisions that take effect as edits of the files the build reads (DOC-0224 §8.4):
/// each is a line of <c>owner-changes.jsonl</c> waiting on <see cref="OwnerChanges.Files"/>, applied here
/// to the file it concerns with the owner's stamp on the row, after which one <c>apply</c> line clears them.
/// The review lists are where a decision's question and proposal are read from, never what the build reads.
/// </summary>
internal static class OwnerDecisions
{
    public const string Producer = "forge owner-apply";

    /// <summary>What a known-for line of the log is filed under, as the console writes it.</summary>
    public const string KnownForSection = "known-for";

    private const string Approved = KnownForReviewList.ApprovedAnswer;

    private const string KnownFor = "records/facts/known-for";

    private const string KnownForReferences = "records/facts/known-for-references";

    /// <param name="Waiting">The log's lines that waited on the step.</param>
    /// <param name="Records">The records whose decision was applied, each once whatever its number of lines.</param>
    /// <param name="Added">Records whose clauses were written into the files.</param>
    /// <param name="Removed">Records whose clauses were taken out of them.</param>
    /// <param name="Already">Records the files already showed as the owner decided.</param>
    /// <param name="Held">Records KnownFor.json holds, which no answer in the console overrides.</param>
    /// <param name="Unplaced">Approved records the files no longer hold (folded into another, or gone), each with why.</param>
    /// <param name="Unhandled">Waiting lines of a section this step does not apply, as <c>line section target</c>.</param>
    /// <param name="Recorded">Whether the apply line was appended to the log.</param>
    public sealed record Outcome(
        int Waiting,
        int Records,
        int Added,
        int Removed,
        int Already,
        IReadOnlyList<string> Held,
        IReadOnlyList<string> Unplaced,
        IReadOnlyList<string> Unhandled,
        IReadOnlyList<string> Written,
        bool Recorded);

    /// <summary>
    /// Applies every decision waiting on the files step to the store's files. With <paramref name="dryRun"/>
    /// it says what it would do and writes nothing. The apply line is appended only when every waiting line
    /// was one it applies, so a line it cannot apply keeps waiting.
    /// </summary>
    public static Outcome Apply(ProductStore store, bool dryRun, DateTimeOffset now)
    {
        var log = Path.Combine(store.ResourcesPath, OwnerChanges.DefaultPath);
        var waiting = OwnerChanges.WaitingOn(log, OwnerChanges.Files);
        var unhandled = waiting
            .Where(w => OwnerChanges.Text(w.Change, "section") != KnownForSection)
            .Select(w => $"{w.Line} {OwnerChanges.Text(w.Change, "section")} {OwnerChanges.Text(w.Change, "target")}")
            .ToList();

        // The last word on each record is its decision; one taken back is no approval.
        var decisions = waiting
            .Where(w => OwnerChanges.Text(w.Change, "section") == KnownForSection)
            .GroupBy(w => SlugOf(OwnerChanges.Text(w.Change, "target")), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (Answer: OwnerChanges.Text(g.Last().Change, "after"), At: OwnerChanges.Text(g.Last().Change, "at") ?? string.Empty),
                StringComparer.Ordinal);

        var knownFor = new KnownForEdit(store);
        if (decisions.Count > 0)
        {
            knownFor.Apply(decisions, KnownForReviewList.PathUnder(store.ResourcesPath));
        }

        IReadOnlyList<string> written = dryRun ? [] : knownFor.Write(now);
        var recorded = false;
        if (!dryRun && waiting.Count > 0 && unhandled.Count == 0)
        {
            OwnerChanges.Applied(log, OwnerChanges.Files,
                $"forge owner-apply: {decisions.Count} known-for decisions applied to {KnownFor} and its references and languages " +
                $"({knownFor.Added.Count} written, {knownFor.Removed.Count} taken out, {knownFor.Already.Count} already so" +
                (knownFor.Held.Count > 0 ? $", {knownFor.Held.Count} held by KnownFor.json and left" : string.Empty) +
                (knownFor.Unplaced.Count > 0 ? $", {knownFor.Unplaced.Count} no longer records of their own: {string.Join("; ", knownFor.Unplaced)}" : string.Empty) + ")",
                now);
            recorded = true;
        }

        if (!dryRun)
        {
            store.Save();
        }

        return new Outcome(waiting.Count, decisions.Count, knownFor.Added.Count, knownFor.Removed.Count, knownFor.Already.Count,
            knownFor.Held, knownFor.Unplaced, unhandled, written, recorded);
    }

    /// <summary><c>place/sodom</c> → <c>sodom</c>.</summary>
    private static string SlugOf(string? target) => target is null ? string.Empty : target[(target.IndexOf('/') + 1)..];

    /// <summary>The stamp on a row the owner's approval wrote.</summary>
    public static string ApprovedBy(string source, string at) =>
        $"{source}; approved by the project owner in his console on {(at.Length >= 10 ? at[..10] : at)}";

    /// <summary>
    /// What a record is known for, in the files the build reads: one row per clause in
    /// <c>records/facts/known-for</c>, its verses in <c>known-for-references</c>, its words in each
    /// language's <c>records/i18n/&lt;lang&gt;/known-for</c>. An approval writes the list's clauses for a
    /// record the files do not show yet; anything else takes the record's clauses out. A record already
    /// shown keeps its rows, so a correction made in the files after the approval stands.
    /// </summary>
    private sealed class KnownForEdit(ProductStore store)
    {
        private readonly Dictionary<string, (RecordFile File, List<string?[]> Rows, ProductHeader? Header)> _files = new(StringComparer.Ordinal);

        private readonly HashSet<string> _changed = new(StringComparer.Ordinal);

        public List<string> Added { get; } = [];

        public List<string> Removed { get; } = [];

        public List<string> Already { get; } = [];

        public List<string> Held { get; } = [];

        public List<string> Unplaced { get; } = [];

        /// <summary>The records the files hold and the old slugs folded into one, or no records where the store has none.</summary>
        private (HashSet<string>? Records, Dictionary<string, string> Folds) Records()
        {
            var folds = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!store.Exists("records/records"))
            {
                return (null, folds);
            }

            if (store.Exists("records/folds"))
            {
                using var reader = store.Open("records/folds");
                foreach (var row in reader.Rows())
                {
                    folds[row[0]!] = row[1]!;
                }
            }

            using var records = store.Open("records/records");
            return (records.Rows().Select(row => row[0]!).ToHashSet(StringComparer.Ordinal), folds);
        }

        private static IEnumerable<RecordFile> Files =>
            RecordFiles.All.Where(f => f.Product is KnownFor or KnownForReferences
                                       || (f.Product.StartsWith("records/i18n/", StringComparison.Ordinal)
                                           && f.Product.EndsWith("/known-for", StringComparison.Ordinal)));

        public void Apply(IReadOnlyDictionary<string, (string? Answer, string At)> decisions, string listPath)
        {
            foreach (var file in Files)
            {
                if (!store.Exists(file.Product))
                {
                    throw new FileNotFoundException(
                        $"{file.Product} is not under {store.ResourcesPath}{Path.DirectorySeparatorChar}Essenthos. The build's record files " +
                        "come from the cut-over export (forge records-export); until it is taken, a known-for decision reaches the site " +
                        "through the old load (core-load --set only=known-for), and this step waits.");
                }

                using var reader = store.Open(file.Product);
                _files[file.Product] = (file, [.. reader.Rows().Select(row => (string?[])row.Clone())], reader.Header);
            }

            var held = KnownForLoader.Read().Records.Select(r => r.Slug).ToHashSet(StringComparer.Ordinal);
            var (records, folds) = Records();
            var list = File.Exists(listPath) ? KnownForReviewList.Read(listPath) : null;
            var proposals = (list?.Records ?? []).ToDictionary(r => r.Slug, StringComparer.Ordinal);
            foreach (var (slug, (answer, at)) in decisions.OrderBy(d => d.Key, StringComparer.Ordinal))
            {
                if (held.Contains(slug))
                {
                    Held.Add(slug);
                    continue;
                }

                var shown = _files[KnownFor].Rows.Any(r => r[0] == slug);
                if (answer == Approved)
                {
                    if (shown)
                    {
                        Already.Add(slug);
                        continue;
                    }

                    // A record folded into another since the list was written has no rows to take: the record it
                    // went into keeps its own line, and rows under the old slug would fail the load.
                    if (records is not null && !records.Contains(slug))
                    {
                        Unplaced.Add(folds.TryGetValue(slug, out var into) ? $"{slug}: folded into {into}, whose own line stands" : $"{slug}: no such record");
                        continue;
                    }

                    if (!proposals.TryGetValue(slug, out var proposal) || proposal.Clauses.Count == 0)
                    {
                        throw new InvalidDataException(
                            $"The owner approved what \"{slug}\" is known for, and {listPath} holds no clauses for it to write. " +
                            "Restore the entry in the list from git, then apply again.");
                    }

                    Write(slug, proposal.Clauses, ApprovedBy(list!.Source ?? string.Empty, at));
                    Added.Add(slug);
                }
                else if (shown)
                {
                    foreach (var product in _files.Keys)
                    {
                        if (_files[product].Rows.RemoveAll(r => r[0] == slug) > 0)
                        {
                            _changed.Add(product);
                        }
                    }

                    Removed.Add(slug);
                }
                else
                {
                    Already.Add(slug);
                }
            }
        }

        private void Write(string slug, IReadOnlyList<KnownForClause> clauses, string by)
        {
            for (var ordinal = 0; ordinal < clauses.Count; ordinal++)
            {
                var clause = clauses[ordinal];
                var number = ordinal.ToString(CultureInfo.InvariantCulture);
                Add(KnownFor, [slug, number, by]);
                Add("records/i18n/eng/known-for", [slug, number, clause.English]);
                foreach (var (language, text) in clause.Languages())
                {
                    if (_files.ContainsKey($"records/i18n/{language}/known-for"))
                    {
                        Add($"records/i18n/{language}/known-for", [slug, number, text]);
                    }
                }

                for (var position = 0; position < clause.References.Count; position++)
                {
                    var written = clause.References[position];
                    if (ScriptureSpan.TryParse(written) is not { FromVerse: { } verse, ToVerse: { } last, Nth: null } span)
                    {
                        throw new InvalidDataException(
                            $"\"{written}\", cited by \"{slug}\" in the known-for list, is not a run of verses. Correct the list, then apply again.");
                    }

                    Add(KnownForReferences,
                    [
                        slug, number, position.ToString(CultureInfo.InvariantCulture),
                        new VerseKey(span.Book, span.FromChapter, verse).ToString(),
                        span.ToChapter.ToString(CultureInfo.InvariantCulture), last.ToString(CultureInfo.InvariantCulture),
                    ]);
                }
            }
        }

        private void Add(string product, string?[] row)
        {
            _files[product].Rows.Add(row);
            _changed.Add(product);
        }

        /// <summary>Writes the files that changed, each in its own order, keeping its header and saying who edited it.</summary>
        public List<string> Write(DateTimeOffset now)
        {
            var written = new List<string>();
            foreach (var product in _changed.Order(StringComparer.Ordinal))
            {
                var (file, rows, header) = _files[product];
                using var writer = store.Create(file.Product, file.Kind, file.Schema(), header?.Producer ?? ProductHeader.ByHand,
                    header?.Inputs ?? [], annotate =>
                    {
                        foreach (var note in header?.Notes ?? [])
                        {
                            if (note.Key != "owner")
                            {
                                annotate.Notes.Add(note);
                            }
                        }

                        annotate.Notes.Add(new("owner",
                            $"the owner's decisions of {OwnerChanges.FileName} applied by {Producer}, last on {now.UtcDateTime:yyyy-MM-dd}"));
                        annotate.Comments.AddRange(header?.Comments ?? []);
                    });
                writer.WriteAll(file.Sorted(rows, file.Order(file.Export)));
                written.Add(product);
            }

            return written;
        }
    }
}
