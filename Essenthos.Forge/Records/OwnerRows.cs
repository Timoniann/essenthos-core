using Essenthos.Core.Conversion;
using Essenthos.Core.Names;
using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

/// <summary>
/// Questions the owner's review lists asked, settled from the texts on his word and written as edits of the files the
/// build reads (NOT-0219): a name settled on a word of a reference text (<c>names/&lt;REF&gt;</c>), or a dataset's verse
/// row filed under another record of ours (<c>external/bibledata/references</c>). Each decided edit is a row of a file in
/// <see cref="Folder"/>; applying them again changes nothing, and a row the files cannot take (a word that is not there,
/// a Strong number that differs, a record or dataset row that does not exist) stops the whole apply before anything is
/// written, because it means the files are not the ones the decision was read against.
/// </summary>
internal static class OwnerRows
{
    public const string Producer = "forge owner-rows";

    /// <summary>Where the decided rows ship, under the Forge's own folder.</summary>
    public static readonly string Folder = Path.Combine("Records", "OwnerRows");

    public const string Add = "add";

    public const string Remove = "remove";

    public const string Refile = "refile";

    public const string Withdraw = "withdraw";

    private const string References = "external/bibledata/references";

    /// <summary>How a settled name says it was decided: by a person, so no confidence.</summary>
    private const string Method = "manual";

    /// <summary>Who says so, on every name row these decisions write.</summary>
    public const string Source =
        "Essenthos, the project owner's decision of 2026-11-05 on the questions made for him: each word read against its verse " +
        "in the Hebrew or the Greek, with the King James and Kulish beside it, by claude-opus-5-5 on his instruction";

    /// <param name="Entry">The review list and the entry of it the row settles (<c>objects:41</c>).</param>
    /// <param name="Product">The file the row edits.</param>
    /// <param name="Op">add or remove (a name on a word), refile or withdraw (a dataset's verse row).</param>
    /// <param name="At">The word's key in its text (names), or the verse (references).</param>
    /// <param name="Record">The record named (add, remove), filed under (refile) or whose row goes (withdraw).</param>
    /// <param name="From">For refile, the record the row is filed under now.</param>
    /// <param name="Role">For add, how the word stands for the record.</param>
    /// <param name="Strong">For a name, the word's Strong number when the decision was read: the guard that the word is the same.</param>
    /// <param name="Label">For a dataset row, the label the dataset gives it.</param>
    /// <param name="Why">The reading the decision rests on; a name row's note.</param>
    internal sealed record Row(
        string Entry, string Product, string Op, string At, string Record, string? From, string? Role, string? Strong, string? Label, string Why);

    public static readonly ProductSchema<Row> Schema = new ProductSchema<Row>()
        .Text("entry", r => r.Entry)
        .Text("product", r => r.Product)
        .Text("op", r => r.Op)
        .Text("at", r => r.At)
        .Text("record", r => r.Record)
        .OptionalText("from", r => r.From)
        .OptionalText("role", r => r.Role)
        .OptionalText("strong", r => r.Strong)
        .OptionalText("label", r => r.Label)
        .Text("why", r => r.Why)
        .Reads(row => new Row(row.Text(0), row.Text(1), row.Text(2), row.Text(3), row.Text(4), row.TextOrNull(5), row.TextOrNull(6),
            row.TextOrNull(7), row.TextOrNull(8), row.Text(9)));

    /// <param name="Applied">Rows that changed a file.</param>
    /// <param name="Already">Rows the files already showed as decided.</param>
    /// <param name="Refused">Rows the files cannot take, each with why; when any is, nothing is written.</param>
    /// <param name="Changed">Per product, the rows that changed it.</param>
    /// <param name="Written">The files written (none on a dry run or a refusal).</param>
    public sealed record Outcome(
        int Rows,
        int Applied,
        int Already,
        IReadOnlyList<string> Refused,
        IReadOnlyDictionary<string, int> Changed,
        IReadOnlyList<string> Written);

    /// <summary>The decided rows shipped with the Forge, file by file in name order.</summary>
    public static List<Row> Shipped() => Read(Directory.Exists(Path.Combine(AppContext.BaseDirectory, Folder))
        ? Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, Folder), "*.tsv").Order(StringComparer.Ordinal)
        : []);

    public static List<Row> Read(IEnumerable<string> paths)
    {
        var rows = new List<Row>();
        foreach (var path in paths)
        {
            using var reader = ProductStore.HandFormat.Open(path, Schema);
            rows.AddRange(reader.Rows());
        }

        return rows;
    }

    /// <summary>
    /// Applies the rows to the store's files. With <paramref name="dryRun"/> it says what it would do and writes nothing.
    /// </summary>
    public static Outcome Apply(ProductStore store, IReadOnlyList<Row> rows, bool dryRun, DateTimeOffset now)
    {
        var refused = new List<string>();
        var changed = new Dictionary<string, int>(StringComparer.Ordinal);
        var already = 0;
        var records = RecordSlugs(store);
        var edits = new List<IFileEdit>();

        foreach (var group in rows.GroupBy(r => r.Product, StringComparer.Ordinal))
        {
            if (!store.Exists(group.Key))
            {
                throw new FileNotFoundException(
                    $"{group.Key} is not under {store.ResourcesPath}{Path.DirectorySeparatorChar}Essenthos. The names and record files " +
                    "come from the cut-over export; apply these rows after it.");
            }

            IFileEdit edit = group.Key == References
                ? new ReferenceEdit(store)
                : group.Key.StartsWith(NameProducts.Folder + "/", StringComparison.Ordinal)
                  && NameProducts.IsReference(group.Key[(NameProducts.Folder.Length + 1)..])
                    ? new NameEdit(store, group.Key[(NameProducts.Folder.Length + 1)..], group.Where(r => r.Strong is not null).Select(r => r.At))
                    : throw new InvalidDataException($"{group.Key}: owner rows edit a reference text's names or {References}, not this file.");
            edits.Add(edit);

            foreach (var row in group)
            {
                if (records is not null && row.Op is Add or Refile && !records.Contains(row.Record))
                {
                    refused.Add($"{row.Entry} {row.Op} {row.Record} at {row.At}: no such record in records/records");
                    continue;
                }

                switch (edit.Apply(row))
                {
                    case null:
                        already++;
                        break;
                    case { Length: 0 }:
                        changed[group.Key] = changed.GetValueOrDefault(group.Key) + 1;
                        break;
                    case var why:
                        refused.Add($"{row.Entry} {row.Op} {row.Record} at {row.At}: {why}");
                        break;
                }
            }
        }

        var written = new List<string>();
        if (!dryRun && refused.Count == 0)
        {
            foreach (var edit in edits.Where(e => e.Changed))
            {
                written.Add(edit.Write(now));
            }

            store.Save();
        }

        return new Outcome(rows.Count, changed.Values.Sum(), already, refused, changed, written);
    }

    private static HashSet<string>? RecordSlugs(ProductStore store)
    {
        if (!store.Exists("records/records"))
        {
            return null;
        }

        using var reader = store.Open("records/records");
        return reader.Rows().Select(row => row[0]!).ToHashSet(StringComparer.Ordinal);
    }

    private static string OwnerNote(DateTimeOffset now) =>
        $"rows settled on the owner's review lists, applied by {Producer}, last on {now.UtcDateTime:yyyy-MM-dd}";

    private interface IFileEdit
    {
        bool Changed { get; }

        /// <returns>Empty when the row changed the file, null when the file already showed it, else why it cannot.</returns>
        string? Apply(Row row);

        string Write(DateTimeOffset now);
    }

    /// <summary>
    /// A reference text's settled names, kept as the text the file holds so every row the decisions do not touch is
    /// written back as it was; a new row goes after the last row of its key or before, so the file keeps its order.
    /// </summary>
    private sealed class NameEdit : IFileEdit
    {
        private static readonly ProductSchema<string?[]> Texts = new ProductSchema<string?[]>()
            .Text("own", r => r[0]!)
            .Text("record", r => r[1]!)
            .Text("role", r => r[2]!)
            .Text("method", r => r[3]!)
            .OptionalText("confidence", r => r[4])
            .Text("source", r => r[5]!)
            .OptionalText("note", r => r[6])
            .Reads(row => row.ToTexts());

        private readonly ProductStore _store;
        private readonly string _text;
        private readonly string _product;
        private readonly List<(WordKey Key, string?[] Row)> _rows;
        private readonly ProductHeader? _header;
        private readonly Dictionary<WordKey, string?> _words;

        public NameEdit(ProductStore store, string text, IEnumerable<string> keys)
        {
            _store = store;
            _text = text;
            _product = NameProducts.Settled(text);
            using (var reader = store.Open(_product, Texts))
            {
                _header = reader.Header;
                _rows = [.. reader.Rows().Select(row => (WordKey.Parse(row[0]!, text), row))];
            }

            var wanted = keys.Select(k => WordKey.Parse(k, text)).ToHashSet();
            _words = [];
            using var words = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
            foreach (var word in words.Rows())
            {
                if (wanted.Contains(word.Key))
                {
                    _words[word.Key] = word.Strong;
                }
            }
        }

        public bool Changed { get; private set; }

        public string? Apply(Row row)
        {
            var key = WordKey.Parse(row.At, _text);
            if (row.Strong is { } strong)
            {
                if (!_words.TryGetValue(key, out var held))
                {
                    return $"{_text} has no word {row.At}";
                }

                if (held != strong)
                {
                    return $"{_text} {row.At} carries {held ?? "no Strong number"}, not the {strong} the decision was read on";
                }
            }

            var at = _rows.FindIndex(r => r.Key == key && r.Row[1] == row.Record);
            if (row.Op == Remove)
            {
                if (at < 0)
                {
                    return null;
                }

                _rows.RemoveAll(r => r.Key == key && r.Row[1] == row.Record);
                Changed = true;
                return string.Empty;
            }

            if (row.Op != Add)
            {
                return $"a name row is added or removed, not '{row.Op}'";
            }

            var decided = new[] { key.Own, row.Record, at >= 0 ? _rows[at].Row[2] : row.Role, Method, null, Source, row.Why };
            if (decided[2] is null || !NameRoles.All.Contains(decided[2]!))
            {
                return $"'{decided[2]}' is no role of a name";
            }

            if (at >= 0)
            {
                if (_rows[at].Row.SequenceEqual(decided))
                {
                    return null;
                }

                _rows[at] = (key, decided);
            }
            else
            {
                var after = _rows.FindLastIndex(r => r.Key.CompareTo(key) <= 0);
                _rows.Insert(after + 1, (key, decided));
            }

            Changed = true;
            return string.Empty;
        }

        public string Write(DateTimeOffset now)
        {
            using var writer = _store.Create(_product, ProductKind.Hand, Texts, _header?.Producer ?? ProductHeader.ByHand, _header?.Inputs ?? [],
                annotate => Annotate(annotate, _header, now));
            foreach (var (_, row) in _rows)
            {
                writer.Write(row);
            }

            writer.Complete();
            return _product;
        }
    }

    /// <summary>
    /// The dataset's verse rows, each filed under a record of ours. A refiled row hangs on a record the dataset gave no
    /// id of its own, so its id goes as the export writes it for such a record; where that record already holds the
    /// dataset's row at the verse, the moving row is dropped, since a record holds one row per dataset and verse.
    /// </summary>
    private sealed class ReferenceEdit : IFileEdit
    {
        private const int Id = 0;
        private const int Filed = 1;
        private const int Verse = 2;
        private const int Label = 3;

        private readonly ProductStore _store;
        private readonly RecordFile _file = RecordFiles.All.Single(f => f.Product == References);
        private readonly List<string?[]> _rows;
        private readonly ProductHeader? _header;

        public ReferenceEdit(ProductStore store)
        {
            _store = store;
            using var reader = store.Open(References);
            _header = reader.Header;
            _rows = [.. reader.Rows().Select(row => (string?[])row.Clone())];
        }

        public bool Changed { get; private set; }

        public string? Apply(Row row)
        {
            bool Holds(string?[] r, string record) => r[Filed] == record && r[Verse] == row.At && (row.Label is null || r[Label] == row.Label);
            var at = _rows.FindIndex(r => Holds(r, row.Op == Refile ? row.From! : row.Record));
            switch (row.Op)
            {
                case Withdraw when at < 0:
                    return null;
                case Withdraw:
                    _rows.RemoveAt(at);
                    break;
                case Refile when at < 0:
                    return _rows.Any(r => r[Filed] == row.Record && r[Verse] == row.At)
                        ? null
                        : $"no row of the dataset under {row.From} or {row.Record} at {row.At}";
                case Refile when _rows.Any(r => r[Filed] == row.Record && r[Verse] == row.At):
                    _rows.RemoveAt(at);
                    break;
                case Refile:
                    _rows[at][Filed] = row.Record;
                    _rows[at][Id] = null;
                    break;
                default:
                    return $"a dataset row is refiled or withdrawn, not '{row.Op}'";
            }

            Changed = true;
            return string.Empty;
        }

        public string Write(DateTimeOffset now)
        {
            using var writer = _store.Create(_file.Product, _file.Kind, _file.Schema(), _header?.Producer ?? ProductHeader.ByHand, _header?.Inputs ?? [],
                annotate => Annotate(annotate, _header, now));
            writer.WriteAll(_file.Sorted(_rows, _file.Order(_file.Export)));
            return _file.Product;
        }
    }

    private static void Annotate(ProductHeader annotate, ProductHeader? header, DateTimeOffset now)
    {
        foreach (var note in header?.Notes ?? [])
        {
            if (note.Key != "owner-rows")
            {
                annotate.Notes.Add(note);
            }
        }

        annotate.Notes.Add(new("owner-rows", OwnerNote(now)));
        annotate.Comments.AddRange(header?.Comments ?? []);
    }
}
