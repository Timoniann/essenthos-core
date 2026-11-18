using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;

namespace Essenthos.Core.Build;

/// <summary>
/// What each pane of a text reads, as one key per canonical chapter: a pane whose key is the one its stored body was made
/// under is copied, not read again from the database. The key holds every product of the text's input list that a pane
/// cannot be told apart by (the text's own products, the other texts' words and kinds, the Strong candidates, the lexicons)
/// whole, and of the rest only the rows that reach the chapter: the rows of each final that name a word of the text in it,
/// the projected names on its words, the record files' rows of the records those names and its occupation words name, the
/// occupation rows of those words, and for a translation the Strong number each of its words there renders. A word is in
/// every canonical chapter its verse stands at (the frame, every address, not only the primary one), and a pane is in every
/// chapter it reads (<see cref="ChapterReading.Held"/>).
/// <para>
/// Raise <see cref="Version"/> whenever what a pane reads changes (<see cref="Panes"/>, <see cref="ChapterReading"/>,
/// <see cref="Annotations"/>): a key is a function of the products, not of the code, and a pane whose key did not move is
/// never read again.
/// </para>
/// </summary>
internal sealed class PaneInputs
{
    public const int Version = 10;

    /// <summary>The record files a pane reads, and the one column each names its record by.</summary>
    internal static readonly (string Product, string Column)[] RecordFiles =
    [
        ("records/records", "slug"), ("records/names", "slug"), ("records/facts/titles", "title"),
    ];

    internal const string Occupations = "records/facts/occupations";

    internal const string PersonOccupations = "records/facts/person-occupations";

    internal const string OccupationWords = "records/derived/occupation-words";

    /// <summary>
    /// The texts whose words refer back to others (<c>tables/word_referent</c>: MACULA Greek's on Nestle, MACULA Hebrew's on
    /// BHSA), and whose names on the words referred to a pane reads.
    /// </summary>
    internal static readonly string[] ReferringTexts = ["NESTLE1904", "BHSA"];

    internal static readonly string Referents = TableProducts.Product("word_referent");

    /// <summary>Every record file among a pane's inputs, which the key reads by row rather than whole.</summary>
    internal static readonly string[] RecordProducts = [.. RecordFiles.Select(f => f.Product), Occupations, PersonOccupations, OccupationWords];

    private static readonly byte[] Absent = [0];

    private readonly Dictionary<string, Slices> _texts = new(StringComparer.Ordinal);

    private PaneInputs()
    {
    }

    /// <summary>Whether a pane's key reads only the rows of this product that reach a chapter, rather than its whole digest.</summary>
    public static bool Sliced(string product) =>
        (product.StartsWith("mapping/", StringComparison.Ordinal) && product.EndsWith("/" + MappingProducts.Final, StringComparison.Ordinal))
        || product.StartsWith(NameProducts.Projected(""), StringComparison.Ordinal)
        || RecordProducts.Contains(product, StringComparer.Ordinal)
        || (product.StartsWith("texts/", StringComparison.Ordinal) && product == StrongNumberFiles.Product(product.Split('/')[1]));

    /// <summary>
    /// The slices of the texts named, every final of a pair holding one of them read once, and the Strong numbers of the
    /// <paramref name="numbered"/> among them (the translations, whose panes write the number each word renders).
    /// </summary>
    public static PaneInputs Read(ProductStore store, IReadOnlyCollection<string> texts, IReadOnlyList<(string From, string To)> pairs, int parallel,
        IReadOnlyCollection<string>? numbered = null)
    {
        var inputs = new PaneInputs();
        foreach (var text in texts.Order(StringComparer.Ordinal))
        {
            inputs._texts[text] = new Slices(FrameChapters.Read(store, text));
        }

        var wanted = pairs.Where(p => inputs._texts.ContainsKey(p.From) || inputs._texts.ContainsKey(p.To))
            .Where(p => store.Exists(MappingProducts.Of(MappingProducts.Pair(p.From, p.To), MappingProducts.Final)))
            .ToList();
        var sides = new ConcurrentDictionary<(string Pair, bool From), Dictionary<(int, int), byte[]>>();
        Parallel.ForEach(wanted, new ParallelOptions { MaxDegreeOfParallelism = parallel }, pair =>
        {
            var name = MappingProducts.Pair(pair.From, pair.To);
            var from = inputs._texts.GetValueOrDefault(pair.From);
            var to = inputs._texts.GetValueOrDefault(pair.To);
            var (fromHashes, toHashes) = (from is null ? null : new ChapterHashes(), to is null ? null : new ChapterHashes());
            using var reader = store.Open(MappingProducts.Of(name, MappingProducts.Final));
            var columns = reader.Columns.Select(c => c.Name).ToList();
            var (fromColumn, toColumn) = (Column(columns, "from", name), Column(columns, "to", name));
            var bytes = new RowBytes();
            var chapters = new List<(int, int)>();
            foreach (var row in reader.Rows())
            {
                var serialized = bytes.Of(row);
                if (from is not null)
                {
                    Each(from.Frame, row[fromColumn], chapters);
                    fromHashes!.Add(chapters, serialized);
                }

                if (to is not null)
                {
                    Each(to.Frame, row[toColumn], chapters);
                    toHashes!.Add(chapters, serialized);
                }
            }

            if (fromHashes is not null)
            {
                sides[(name, true)] = fromHashes.Finish();
            }

            if (toHashes is not null)
            {
                sides[(name, false)] = toHashes.Finish();
            }
        });

        foreach (var pair in wanted.OrderBy(p => MappingProducts.Pair(p.From, p.To), StringComparer.Ordinal))
        {
            var name = MappingProducts.Pair(pair.From, pair.To);
            foreach (var (text, side) in new[] { (pair.From, true), (pair.To, false) })
            {
                if (inputs._texts.TryGetValue(text, out var slices) && sides.TryGetValue((name, side), out var hashes))
                {
                    slices.AddLinks(name, hashes);
                }
            }
        }

        foreach (var slices in inputs._texts.Values)
        {
            slices.FinishLinks();
        }

        Parallel.ForEach(inputs._texts, new ParallelOptions { MaxDegreeOfParallelism = parallel }, entry =>
        {
            var (text, slices) = (entry.Key, entry.Value);
            if (!store.Exists(NameProducts.Projected(text)))
            {
                return;
            }

            using var reader = store.Open(NameProducts.Projected(text));
            var columns = reader.Columns.Select(c => c.Name).ToList();
            var (own, record) = (Column(columns, "own", text), Column(columns, "record", text));
            var hashes = new ChapterHashes();
            var bytes = new RowBytes();
            var chapters = new List<(int, int)>();
            foreach (var row in reader.Rows())
            {
                Each(slices.Frame, row[own], chapters);
                hashes.Add(chapters, bytes.Of(row));
                slices.Name(chapters, row[record]!);
            }

            slices.Names = hashes.Finish();
        });

        // Only the number counts: how a word came by it (hops, source, confidence) is not written in a pane.
        Parallel.ForEach(inputs._texts.Where(t => numbered?.Contains(t.Key) == true), new ParallelOptions { MaxDegreeOfParallelism = parallel }, entry =>
        {
            var (text, slices) = (entry.Key, entry.Value);
            if (!store.Exists(StrongNumberFiles.Product(text)))
            {
                return;
            }

            using var reader = store.Open(StrongNumberFiles.Product(text));
            var columns = reader.Columns.Select(c => c.Name).ToList();
            var (own, number) = (Column(columns, "own", text), Column(columns, "number", text));
            var hashes = new ChapterHashes();
            var bytes = new RowBytes();
            var chapters = new List<(int, int)>();
            var pair = new string?[2];
            foreach (var row in reader.Rows())
            {
                Each(slices.Frame, row[own], chapters);
                (pair[0], pair[1]) = (row[own], row[number]);
                hashes.Add(chapters, bytes.Of(pair));
            }

            slices.Numbers = hashes.Finish();
        });

        if (store.Exists(OccupationWords))
        {
            using var reader = store.Open(OccupationWords);
            var columns = reader.Columns.Select(c => c.Name).ToList();
            var (person, occupation, textColumn, own) = (Column(columns, "person", OccupationWords), Column(columns, "occupation", OccupationWords),
                Column(columns, "text", OccupationWords), Column(columns, "own", OccupationWords));
            var hashes = new Dictionary<string, ChapterHashes>(StringComparer.Ordinal);
            var bytes = new RowBytes();
            var chapters = new List<(int, int)>();
            foreach (var row in reader.Rows())
            {
                if (!inputs._texts.TryGetValue(row[textColumn]!, out var slices))
                {
                    continue;
                }

                Each(slices.Frame, row[own], chapters);
                (hashes.TryGetValue(row[textColumn]!, out var text) ? text : hashes[row[textColumn]!] = new ChapterHashes()).Add(chapters, bytes.Of(row));
                slices.Occupied(chapters, row[person]!, row[occupation]!);
            }

            foreach (var (text, textHashes) in hashes)
            {
                inputs._texts[text].OccupationWords = textHashes.Finish();
            }
        }

        inputs._records = RecordRows.Read(store);
        inputs._referents = ReferentSlices.Read(store);
        return inputs;
    }

    private RecordRows _records = RecordRows.Empty;

    private ReferentSlices _referents = ReferentSlices.Empty;

    /// <summary>
    /// The key of every pane of the text at the chapters given: the text's whole inputs that are not read by row
    /// (<paramref name="inputs"/>, the text's input list), then each chapter it reads.
    /// </summary>
    public Dictionary<(int Book, int Chapter), string> Keys(string text, IReadOnlyList<ProductInput> inputs, IEnumerable<(int Book, int Chapter)> panes)
    {
        var slices = _texts[text];
        var whole = Whole(inputs);
        var referring = inputs.Any(i => ProductOf(i.Path) == Referents);
        var numbered = inputs.Any(i => ProductOf(i.Path) == StrongNumberFiles.Product(text));
        var chapters = new Dictionary<(int, int), byte[]>();
        var keys = new Dictionary<(int, int), string>();
        Span<byte> ints = stackalloc byte[20];
        foreach (var pane in panes)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var (book, chapter, first, last, shift) in ChapterReading.Held(pane.Book, pane.Chapter))
            {
                BitConverter.TryWriteBytes(ints, book);
                BitConverter.TryWriteBytes(ints[4..], chapter);
                BitConverter.TryWriteBytes(ints[8..], first);
                BitConverter.TryWriteBytes(ints[12..], last);
                BitConverter.TryWriteBytes(ints[16..], shift);
                hash.AppendData(ints);
                if (!chapters.TryGetValue((book, chapter), out var key))
                {
                    chapters[(book, chapter)] = key = Chapter(slices, whole, (book, chapter), referring, numbered);
                }

                hash.AppendData(key);
            }

            keys[pane] = Convert.ToHexStringLower(hash.GetHashAndReset())[..32];
        }

        return keys;
    }

    /// <summary>The digest of the inputs a pane's key reads whole, with the version of what a pane reads.</summary>
    private static byte[] Whole(IReadOnlyList<ProductInput> inputs)
    {
        var text = new StringBuilder($"pane-inputs {Version}\nformat {Panes.Format}\n");
        foreach (var input in inputs.OrderBy(i => i.Path, StringComparer.Ordinal))
        {
            if (!Sliced(ProductOf(input.Path)))
            {
                text.Append(input.Path).Append('\t').Append(input.Sha256).Append('\n');
            }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>A product's name from an input path: <c>Essenthos/texts/KJV/words.essx</c> is <c>texts/KJV/words</c>.</summary>
    internal static string ProductOf(string path)
    {
        var name = path.StartsWith(ProductStore.EssenthosFolder + "/", StringComparison.Ordinal) ? path[(ProductStore.EssenthosFolder.Length + 1)..] : path;
        var dot = name.LastIndexOf('.');
        return dot > name.LastIndexOf('/') ? name[..dot] : name;
    }

    private byte[] Chapter(Slices slices, byte[] whole, (int, int) chapter, bool referring, bool numbered)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(whole);
        Part(hash, "links", slices.Links.GetValueOrDefault(chapter));
        Part(hash, "names", slices.Names.GetValueOrDefault(chapter));
        Part(hash, "occupation-words", slices.OccupationWords.GetValueOrDefault(chapter));
        if (numbered)
        {
            Part(hash, "numbers", slices.Numbers.GetValueOrDefault(chapter));
        }

        IEnumerable<string> records = slices.Records.GetValueOrDefault(chapter) ?? [];
        if (referring && _referents.Chapters.TryGetValue(chapter, out var referred))
        {
            Part(hash, "referents", referred.Digest);
            records = records.Union(referred.Records, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        }

        hash.AppendData("records\n"u8);
        foreach (var record in records)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(record + "\n"));
            foreach (var file in _records.Files)
            {
                foreach (var row in file.GetValueOrDefault(record) ?? [Absent])
                {
                    hash.AppendData(row);
                }
            }
        }

        hash.AppendData("occupations\n"u8);
        foreach (var occupation in slices.OccupationsOf.GetValueOrDefault(chapter) ?? [])
        {
            hash.AppendData(Encoding.UTF8.GetBytes(occupation + "\n"));
            hash.AppendData(_records.Occupations.GetValueOrDefault(occupation) ?? Absent);
        }

        hash.AppendData("person-occupations\n"u8);
        foreach (var held in slices.Held.GetValueOrDefault(chapter) ?? [])
        {
            hash.AppendData(Encoding.UTF8.GetBytes(held + "\n"));
            hash.AppendData(_records.PersonOccupations.GetValueOrDefault(held) ?? Absent);
        }

        return hash.GetHashAndReset();
    }

    private static void Part(IncrementalHash hash, string name, byte[]? digest)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(name + "\n"));
        hash.AppendData(digest ?? Absent);
    }

    private static int Column(List<string> columns, string name, string product) =>
        columns.IndexOf(name) is var at and >= 0 ? at : throw new InvalidDataException($"{product} has no column {name}; a pane's key reads it.");

    /// <summary>The distinct canonical chapters the words of a space-separated list of own word keys stand in.</summary>
    private static void Each(FrameChapters frame, string? keys, List<(int, int)> chapters)
    {
        chapters.Clear();
        if (string.IsNullOrEmpty(keys))
        {
            return;
        }

        foreach (var range in keys.AsSpan().Split(' '))
        {
            var key = keys.AsSpan()[range];
            if (key.IsEmpty)
            {
                continue;
            }

            foreach (var chapter in frame.Of(key[..key.LastIndexOf('.')]))
            {
                if (!chapters.Contains(chapter))
                {
                    chapters.Add(chapter);
                }
            }
        }
    }

    /// <summary>Where each verse of a text stands: the frame's addresses for the verses it moves, the verse's own chapter for the rest.</summary>
    private sealed class FrameChapters
    {
        private readonly Dictionary<string, (int, int)[]>.AlternateLookup<ReadOnlySpan<char>> _moved;

        private FrameChapters(Dictionary<string, (int, int)[]> moved) => _moved = moved.GetAlternateLookup<ReadOnlySpan<char>>();

        public static FrameChapters Read(ProductStore store, string text)
        {
            var moved = new Dictionary<string, (int, int)[]>(StringComparer.Ordinal);
            if (store.Exists(FrameFile.Product(text)))
            {
                using var reader = store.Open(FrameFile.Product(text), FrameRow.Schema);
                foreach (var row in reader.Rows())
                {
                    foreach (var (own, canonical) in row.Verses())
                    {
                        moved[own.ToString()] = [.. canonical.Select(c => (c.Book, c.Chapter)).Distinct()];
                    }
                }
            }

            return new FrameChapters(moved);
        }

        public (int, int)[] Of(ReadOnlySpan<char> verse)
        {
            if (_moved.TryGetValue(verse, out var moved))
            {
                return moved;
            }

            var key = VerseKey.Parse(verse);
            return [(key.Book, key.Chapter)];
        }
    }

    /// <summary>One text's slices: per canonical chapter, the digests of its rows and the records and occupations they name.</summary>
    private sealed class Slices(FrameChapters frame)
    {
        public FrameChapters Frame { get; } = frame;

        private readonly Dictionary<(int, int), IncrementalHash> _links = [];

        public Dictionary<(int, int), byte[]> Links { get; private set; } = [];

        public Dictionary<(int, int), byte[]> Names { get; set; } = [];

        public Dictionary<(int, int), byte[]> OccupationWords { get; set; } = [];

        public Dictionary<(int, int), byte[]> Numbers { get; set; } = [];

        public Dictionary<(int, int), SortedSet<string>> Records { get; } = [];

        public Dictionary<(int, int), SortedSet<string>> OccupationsOf { get; } = [];

        public Dictionary<(int, int), SortedSet<string>> Held { get; } = [];

        /// <summary>A pair's digests of the chapters, added in pair order; <see cref="Links"/> holds them once <see cref="FinishLinks"/> ran.</summary>
        public void AddLinks(string pair, Dictionary<(int, int), byte[]> hashes)
        {
            foreach (var (chapter, digest) in hashes)
            {
                if (!_links.TryGetValue(chapter, out var hash))
                {
                    _links[chapter] = hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                }

                hash.AppendData(Encoding.UTF8.GetBytes(pair + "\n"));
                hash.AppendData(digest);
            }
        }

        public void FinishLinks()
        {
            Links = _links.ToDictionary(l => l.Key, l => l.Value.GetHashAndReset());
            foreach (var hash in _links.Values)
            {
                hash.Dispose();
            }

            _links.Clear();
        }

        public void Name(List<(int, int)> chapters, string record)
        {
            foreach (var chapter in chapters)
            {
                (Records.TryGetValue(chapter, out var set) ? set : Records[chapter] = new SortedSet<string>(StringComparer.Ordinal)).Add(record);
            }
        }

        public void Occupied(List<(int, int)> chapters, string person, string occupation)
        {
            lock (this)
            {
                Name(chapters, person);
                foreach (var chapter in chapters)
                {
                    (OccupationsOf.TryGetValue(chapter, out var set) ? set : OccupationsOf[chapter] = new SortedSet<string>(StringComparer.Ordinal)).Add(occupation);
                    (Held.TryGetValue(chapter, out var held) ? held : Held[chapter] = new SortedSet<string>(StringComparer.Ordinal)).Add(person + "\t" + occupation);
                }
            }
        }
    }

    /// <summary>A SHA-256 per chapter, each row's bytes appended to the chapters it reaches in the order the file holds them.</summary>
    private sealed class ChapterHashes
    {
        private readonly Dictionary<(int, int), IncrementalHash> _hashes = [];

        public void Add(List<(int, int)> chapters, ReadOnlySpan<byte> row)
        {
            foreach (var chapter in chapters)
            {
                if (!_hashes.TryGetValue(chapter, out var hash))
                {
                    _hashes[chapter] = hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                }

                hash.AppendData(row);
            }
        }

        public Dictionary<(int, int), byte[]> Finish()
        {
            var digests = _hashes.ToDictionary(h => h.Key, h => h.Value.GetHashAndReset());
            foreach (var hash in _hashes.Values)
            {
                hash.Dispose();
            }

            return digests;
        }
    }

    /// <summary>A row as bytes: each field in UTF-8 followed by 0x1F, a null as 0x00, the row closed by 0x1E.</summary>
    private sealed class RowBytes
    {
        private byte[] _buffer = new byte[4096];

        public ReadOnlySpan<byte> Of(string?[] row)
        {
            var length = 0;
            foreach (var field in row)
            {
                var need = field is null ? 2 : Encoding.UTF8.GetMaxByteCount(field.Length) + 1;
                if (length + need + 1 > _buffer.Length)
                {
                    Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, length + need + 1));
                }

                if (field is null)
                {
                    _buffer[length++] = 0;
                }
                else
                {
                    length += Encoding.UTF8.GetBytes(field, _buffer.AsSpan(length));
                }

                _buffer[length++] = 0x1F;
            }

            _buffer[length++] = 0x1E;
            return _buffer.AsSpan(0, length);
        }

        public byte[] Copy(string?[] row) => Of(row).ToArray();
    }

    /// <summary>
    /// Per canonical chapter, what the references back of <see cref="ReferringTexts"/>' words standing there reach: each referring
    /// word, every word its chain of references reaches (<see cref="ReferenceSteps"/>, whether or not the word names anybody,
    /// so a name newly given to a word on the way moves the key), and the projected names on those words, with the records they
    /// name. A chapter of a text paired with that text reads them, as <see cref="ChapterReading"/> does through its links.
    /// </summary>
    private sealed record ReferentSlices(Dictionary<(int, int), (byte[] Digest, SortedSet<string> Records)> Chapters)
    {
        private const int ReferenceSteps = 8;

        public static readonly ReferentSlices Empty = new([]);

        public static ReferentSlices Read(ProductStore store)
        {
            var texts = ReferringTexts
                .Where(text => store.Exists(NameProducts.Projected(text)) && store.Exists(TextWord.ProductOf(text)))
                .ToList();
            if (!store.Exists(Referents) || texts.Count == 0)
            {
                return Empty;
            }

            var table = WordIdTable.For(store);
            var keys = new Dictionary<long, WordKey>();
            foreach (var key in texts.SelectMany(text => WordIdCollisions.Keys(store, text)))
            {
                keys[table.Of(key)] = key;
            }

            var direct = new Dictionary<WordKey, List<WordKey>>();
            using (var reader = store.Open(Referents))
            {
                var columns = reader.Columns.Select(c => c.Name).ToList();
                var (word, referent) = (Column(columns, "word_id", Referents), Column(columns, "referent_word_id", Referents));
                foreach (var row in reader.Rows())
                {
                    if (keys.TryGetValue(long.Parse(row[word]!), out var from) && keys.TryGetValue(long.Parse(row[referent]!), out var to))
                    {
                        (direct.TryGetValue(from, out var list) ? list : direct[from] = []).Add(to);
                    }
                }
            }

            var names = new Dictionary<(string Text, string Own), (List<byte[]> Rows, List<string> Records)>();
            var byChapter = new Dictionary<(int, int), List<WordKey>>();
            foreach (var text in texts)
            {
                using (var reader = store.Open(NameProducts.Projected(text)))
                {
                    var columns = reader.Columns.Select(c => c.Name).ToList();
                    var (own, record) = (Column(columns, "own", text), Column(columns, "record", text));
                    var bytes = new RowBytes();
                    foreach (var row in reader.Rows())
                    {
                        var entry = names.TryGetValue((text, row[own]!), out var held) ? held : names[(text, row[own]!)] = ([], []);
                        entry.Rows.Add(bytes.Copy(row));
                        entry.Records.Add(row[record]!);
                    }
                }

                var frame = FrameChapters.Read(store, text);
                foreach (var from in direct.Keys.Where(key => key.Text == text))
                {
                    foreach (var chapter in frame.Of(from.Verse.ToString()))
                    {
                        (byChapter.TryGetValue(chapter, out var list) ? list : byChapter[chapter] = []).Add(from);
                    }
                }
            }

            var chapters = new Dictionary<(int, int), (byte[], SortedSet<string>)>();
            foreach (var (chapter, referring) in byChapter)
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var records = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var from in referring.Order())
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(from.Own + "\n"));
                    foreach (var reached in Reached(direct, from))
                    {
                        hash.AppendData(Encoding.UTF8.GetBytes(reached.Own + "\t"));
                        if (names.TryGetValue((reached.Text, reached.Own), out var named))
                        {
                            named.Rows.ForEach(hash.AppendData);
                            records.UnionWith(named.Records);
                        }
                        else
                        {
                            hash.AppendData(Absent);
                        }
                    }
                }

                chapters[chapter] = (hash.GetHashAndReset(), records);
            }

            return new ReferentSlices(chapters);
        }

        /// <summary>Every word one word's chain of references reaches, in the order the chain meets them.</summary>
        private static List<WordKey> Reached(Dictionary<WordKey, List<WordKey>> direct, WordKey word)
        {
            var seen = new HashSet<WordKey> { word };
            var reached = new List<WordKey>();
            var frontier = new List<WordKey> { word };
            for (var step = 0; step < ReferenceSteps && frontier.Count > 0; step++)
            {
                var next = new List<WordKey>();
                foreach (var at in frontier)
                {
                    foreach (var referent in direct.GetValueOrDefault(at) ?? [])
                    {
                        if (seen.Add(referent))
                        {
                            reached.Add(referent);
                            next.Add(referent);
                        }
                    }
                }

                frontier = next;
            }

            return reached;
        }
    }

    /// <summary>The record files' rows by the record they name, and the occupation files' rows by occupation and by holder.</summary>
    private sealed record RecordRows(
        IReadOnlyList<Dictionary<string, List<byte[]>>> Files,
        Dictionary<string, byte[]> Occupations,
        Dictionary<string, byte[]> PersonOccupations)
    {
        public static readonly RecordRows Empty = new([], [], []);

        public static RecordRows Read(ProductStore store)
        {
            var files = new List<Dictionary<string, List<byte[]>>>();
            foreach (var (product, column) in RecordFiles)
            {
                var rows = new Dictionary<string, List<byte[]>>(StringComparer.Ordinal);
                if (store.Exists(product))
                {
                    using var reader = store.Open(product);
                    var at = Column(reader.Columns.Select(c => c.Name).ToList(), column, product);
                    var bytes = new RowBytes();
                    foreach (var row in reader.Rows())
                    {
                        (rows.TryGetValue(row[at]!, out var list) ? list : rows[row[at]!] = []).Add(bytes.Copy(row));
                    }
                }

                files.Add(rows);
            }

            return new RecordRows(files, Keyed(store, PaneInputs.Occupations, ["slug"]), Keyed(store, PaneInputs.PersonOccupations, ["person", "occupation"]));
        }

        private static Dictionary<string, byte[]> Keyed(ProductStore store, string product, string[] key)
        {
            var rows = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (!store.Exists(product))
            {
                return rows;
            }

            using var reader = store.Open(product);
            var columns = reader.Columns.Select(c => c.Name).ToList();
            var at = key.Select(k => Column(columns, k, product)).ToArray();
            var bytes = new RowBytes();
            foreach (var row in reader.Rows())
            {
                var name = string.Join('\t', at.Select(i => row[i]));
                rows[name] = rows.TryGetValue(name, out var before) ? [.. before, .. bytes.Of(row)] : bytes.Copy(row);
            }

            return rows;
        }
    }
}
