using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Indexes;

/// <summary>A text as its declaration states it: what the indexes need of it.</summary>
internal sealed record IndexedText(string Slug, string Language, TextKind Kind);

/// <summary>One index of one text, made or found current, and how long it took.</summary>
internal sealed record IndexOutcome(string Text, string Index, bool Made, long Rows, TimeSpan Elapsed, string? Note = null);

/// <summary>
/// What every text's indexes are made from, read once per run and shared by the texts made in parallel: the texts'
/// declarations, the mapping's pairs, and the words of the editions every translation is counted over.
/// </summary>
internal sealed class IndexContext
{
    /// <summary>
    /// Raised with any change to what an index holds or how it is counted; an index made by another version is made again
    /// whatever its inputs say.
    /// </summary>
    public const string Version = "1";

    public const string VersionNote = "index-version";

    private readonly ConcurrentDictionary<string, Lazy<Edition>> _editions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, List<string>>>> _names = new(StringComparer.Ordinal);

    public IndexContext(ProductStore store)
    {
        Store = store;
        Texts = Directory.GetDirectories(Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, "texts"))
            .Select(Path.GetFileName).OfType<string>()
            .Where(slug => store.Exists(TextField.ProductOf(slug)) && store.Exists(TextWord.ProductOf(slug)))
            .Select(slug => Declared(store, slug))
            .ToDictionary(text => text.Slug, StringComparer.Ordinal);
        Pairs = [.. MappingProducts.Pairs(store).Where(pair => store.Exists(MappingProducts.Of(pair, MappingProducts.Final)))];
    }

    public ProductStore Store { get; }

    public IReadOnlyDictionary<string, IndexedText> Texts { get; }

    public IReadOnlyList<string> Pairs { get; }

    /// <summary>The pairs a text stands in, with the other text and whether the text is the pair's first.</summary>
    public IEnumerable<(string Pair, string Other, bool First)> PairsOf(string text)
    {
        foreach (var pair in Pairs)
        {
            var (from, to) = MappingProducts.Split(pair);
            if (from == text && Texts.ContainsKey(to))
            {
                yield return (pair, to, true);
            }
            else if (to == text && Texts.ContainsKey(from))
            {
                yield return (pair, from, false);
            }
        }
    }

    /// <summary>The words of a text as an edition is counted by: the numbers it may serve and its lemmas, by own key.</summary>
    public Edition EditionOf(string slug) =>
        _editions.GetOrAdd(slug, s => new Lazy<Edition>(() => Edition.Read(Store, s))).Value;

    /// <summary>The records each word of a text is named as, by own key, from the exported names; empty where there are none.</summary>
    public IReadOnlyDictionary<string, List<string>> NamesOf(string slug) =>
        _names.GetOrAdd(slug, s => new Lazy<IReadOnlyDictionary<string, List<string>>>(() =>
        {
            var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (!Store.Exists(IndexProducts.ExportedNames(s)))
            {
                return names;
            }

            using var reader = Store.Open(IndexProducts.ExportedNames(s), ExportedName.Schema(s));
            foreach (var row in reader.Rows())
            {
                var own = row.Own.Own;
                if (!names.TryGetValue(own, out var records))
                {
                    names[own] = records = [];
                }

                records.Add(row.Record);
            }

            return names;
        })).Value;

    /// <summary>Whether the product is missing, an input moved, or another version of the indexes made it.</summary>
    public bool NeedsBuild(string product, IReadOnlyList<ProductInput> inputs)
    {
        if (Store.NeedsBuild(product, ProductKind.Machine, inputs))
        {
            return true;
        }

        using var reader = Store.Open(product);
        return reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value != Version;
    }

    public static IndexedText Declared(ProductStore store, string slug)
    {
        using var reader = store.Open(TextField.ProductOf(slug), TextField.Schema);
        var fields = reader.Rows().GroupBy(f => f.Field).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        return new IndexedText(
            slug,
            fields.GetValueOrDefault("language") ?? throw new InvalidDataException($"texts/{slug}/text declares no language."),
            EnumSpelling.ToTextKind(fields.GetValueOrDefault("kind") ?? throw new InvalidDataException($"texts/{slug}/text declares no kind.")));
    }
}

/// <summary>An edition's words as the indexes read them.</summary>
/// <param name="Numbers">The Strong number of every numbered word, where the words product lets it be served.</param>
/// <param name="Lemmas">The lemma of every word that has one.</param>
internal sealed record Edition(IReadOnlyDictionary<string, string> Numbers, IReadOnlyDictionary<string, string> Lemmas)
{
    public static Edition Read(ProductStore store, string slug)
    {
        var numbers = new Dictionary<string, string>(StringComparer.Ordinal);
        var lemmas = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
        var served = StrongTerms.MayServe(reader.Header);
        foreach (var word in reader.Rows())
        {
            var own = word.Key.Own;
            if (served && word.Strong is { } strong)
            {
                numbers[own] = strong;
            }

            if (word.Lemma is { } lemma)
            {
                lemmas[own] = lemma;
            }
        }

        return new Edition(numbers, lemmas);
    }
}

/// <summary>
/// The per-text indexes of DOC-0224 §10, each a pure function of the text's words, the finals of its pairs and the names
/// where it needs them, made only when one of those moved: <c>texts/&lt;SLUG&gt;/index/strong</c> (how the text renders
/// each numbered word of the editions it is counted over, today's <c>lexicon-phrases</c>), <c>occurrences</c> (an
/// edition's numbers by book and part of speech), <c>forms</c> (the search's forms, today's <c>search-forms</c>) and
/// <c>spellings</c> (the words naming records, which <c>entity_rendering</c> and <c>entity_search_spelling</c> are
/// counted from, today's <c>spellings</c>).
/// </summary>
internal static class TextIndexes
{
    private const string Producer = "forge indexes";

    /// <summary>Every index of every text named, the texts in parallel.</summary>
    public static List<IndexOutcome> Make(IndexContext context, IReadOnlyCollection<string> texts, IReadOnlySet<string>? indexes, bool force, int parallel)
    {
        var outcomes = new ConcurrentBag<IndexOutcome>();
        Parallel.ForEach(texts, new ParallelOptions { MaxDegreeOfParallelism = parallel }, text =>
        {
            foreach (var index in IndexProducts.All.Where(index => indexes is null || indexes.Contains(index)))
            {
                var outcome = index switch
                {
                    IndexProducts.Strong => Strong(context, text, force),
                    IndexProducts.Occurrences => Occurrences(context, text, force),
                    IndexProducts.Forms => Forms(context, text, force),
                    IndexProducts.Spellings => Spellings(context, text, force),
                    _ => throw new ArgumentOutOfRangeException(nameof(indexes), index, null),
                };
                if (outcome is not null)
                {
                    outcomes.Add(outcome);
                }
            }
        });

        context.Store.Save();
        return [.. outcomes.OrderBy(o => o.Text, StringComparer.Ordinal).ThenBy(o => Array.IndexOf(IndexProducts.All, o.Index))];
    }

    private static ProductInput Product(IndexContext context, string product) => context.Store.ProductInput(product);

    private static IProductWriter<T> Create<T>(IndexContext context, string product, ProductSchema<T> schema, IReadOnlyList<ProductInput> inputs, params (string Key, string Value)[] notes) =>
        context.Store.Create(product, ProductKind.Machine, schema, ProductHeader.DescribeProducer(Producer), inputs, header =>
        {
            header.Notes.Add(new(IndexContext.VersionNote, IndexContext.Version));
            foreach (var (key, value) in notes)
            {
                header.Notes.Add(new(key, value));
            }
        });

    /// <summary>
    /// How the text renders every numbered word of the editions it is counted over: one row per word of the editions that a
    /// rendering link of the text holds. The editions are <see cref="LinkedOriginals.Primary"/> of the text's links to
    /// texts that are not translations, counted from the finals as the database counts its links; the counted edition of
    /// each language goes in the header (<c>hebrew</c>, <c>greek</c>) for the loader, which counts every number standing
    /// in it whether the text reaches it or not.
    /// </summary>
    public static IndexOutcome Strong(IndexContext context, string text, bool force)
    {
        var clock = Stopwatch.StartNew();
        var product = IndexProducts.Of(text, IndexProducts.Strong);
        var others = context.PairsOf(text).Where(p => context.Texts[p.Other].Kind != TextKind.Translation).ToList();
        var inputs = new List<ProductInput> { Product(context, TextWord.ProductOf(text)) };
        foreach (var other in others.Select(p => p.Other).Distinct().Order(StringComparer.Ordinal))
        {
            inputs.Add(Product(context, TextField.ProductOf(other)));
            inputs.Add(Product(context, TextWord.ProductOf(other)));
        }

        inputs.AddRange(others.Select(p => Product(context, MappingProducts.Of(p.Pair, MappingProducts.Final))));
        if (!force && !context.NeedsBuild(product, inputs))
        {
            return new IndexOutcome(text, IndexProducts.Strong, false, 0, clock.Elapsed);
        }

        // Every link between the text and each edition counts towards which edition it is counted over; the rendering
        // links of the ones chosen are what it is counted from.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var rendering = new Dictionary<string, List<(long Id, string Method, string[] Mine, string[] Theirs)>>(StringComparer.Ordinal);
        foreach (var (pair, other, first) in others)
        {
            var links = rendering.TryGetValue(other, out var held) ? held : rendering[other] = [];
            var count = 0;
            using var reader = context.Store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
            foreach (var row in reader.Rows().Where(row => row.Settled))
            {
                count++;
                if (row.Relation is "renders" or "equals")
                {
                    var mine = MappingText.Split(first ? row.From : row.To).ToArray();
                    var theirs = MappingText.Split(first ? row.To : row.From).ToArray();
                    links.Add((row.Id, EnumSpelling.Of(MappingMethods.DatabaseMethod(row.Method)), mine, theirs));
                }
            }

            counts[other] = counts.GetValueOrDefault(other) + count;
        }

        var ordinal = 0;
        var originals = LinkedOriginals.Ordered(counts.Keys.Order(StringComparer.Ordinal).Select(other =>
            new LinkedOriginal(++ordinal, other, other, context.Texts[other].Language, context.Texts[other].Kind, counts[other])
            {
                Numbered = context.EditionOf(other).Numbers.Count > 0,
            }));
        var primary = LinkedOriginals.Primary(originals);

        var words = new Dictionary<string, (int Verse, int Position, long Id, string Lower)>(StringComparer.Ordinal);
        if (primary.Count > 0)
        {
            using var reader = context.Store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
            foreach (var word in reader.Rows())
            {
                words[word.Key.Own] = (word.Key.Verse.Verse, word.Key.Position, word.Id ?? long.MaxValue, word.Surface.ToLowerInvariant());
            }
        }

        var reached = new Dictionary<(string Number, string Witness, string Original), (HashSet<string> Mine, SortedDictionary<long, string> Links)>();
        foreach (var witness in primary)
        {
            var numbers = context.EditionOf(witness.Slug).Numbers;
            foreach (var (id, method, mine, theirs) in rendering[witness.Slug])
            {
                foreach (var original in theirs)
                {
                    if (!numbers.TryGetValue(original, out var number))
                    {
                        continue;
                    }

                    var key = (number, witness.Slug, original);
                    if (!reached.TryGetValue(key, out var found))
                    {
                        reached[key] = found = (new HashSet<string>(StringComparer.Ordinal), new SortedDictionary<long, string>());
                    }

                    found.Links[id] = method;
                    found.Mine.UnionWith(mine.Where(words.ContainsKey));
                }
            }
        }

        var witnessOrder = primary.Select((w, i) => (w.Slug, i)).ToDictionary(w => w.Slug, w => w.i, StringComparer.Ordinal);
        var hebrew = LinkedOriginals.WitnessFor(primary, "H1")?.Slug;
        var greek = LinkedOriginals.WitnessFor(primary, "G1")?.Slug;
        long rows;
        using (var writer = Create(context, product, StrongIndexRow.Schema, inputs,
                   ("primary", string.Join(' ', primary.Select(w => w.Slug))),
                   ("hebrew", hebrew ?? "-"),
                   ("greek", greek ?? "-")))
        {
            foreach (var ((number, witness, original), (mine, links)) in reached
                         .OrderBy(r => witnessOrder[r.Key.Witness])
                         .ThenBy(r => WordKey.Parse(r.Key.Original, r.Key.Witness))
                         .ThenBy(r => r.Key.Number, StringComparer.Ordinal))
            {
                var ordered = mine.Select(own => (Own: own, Word: words[own]))
                    .OrderBy(w => w.Word.Verse).ThenBy(w => w.Word.Position).ThenBy(w => w.Word.Id)
                    .ToList();
                writer.Write(new StrongIndexRow(
                    number,
                    witness,
                    original,
                    string.Join(' ', ordered.Select(w => w.Own)),
                    ordered.Count == 0 ? null : string.Join(' ', ordered.Select(w => w.Word.Lower)),
                    string.Join(' ', links.Keys),
                    string.Join(' ', links.Values)));
            }

            writer.Complete();
            rows = writer.Rows;
        }

        return new IndexOutcome(text, IndexProducts.Strong, true, rows, clock.Elapsed, $"over {string.Join(", ", primary.Select(w => w.Slug))}");
    }

    /// <summary>
    /// How many words of an edition carry each Strong number in each book, by the part of speech its morphology states: the
    /// occurrences a text's reach is counted against, the edition's own counts by book, and the parts of speech a number is
    /// read as. Made only for a text of an original language that is not a translation and whose numbers may be served.
    /// </summary>
    public static IndexOutcome? Occurrences(IndexContext context, string text, bool force)
    {
        var clock = Stopwatch.StartNew();
        var declared = context.Texts[text];
        if (declared.Kind == TextKind.Translation || !Renderings.Original.Contains(declared.Language))
        {
            return null;
        }

        var product = IndexProducts.Of(text, IndexProducts.Occurrences);
        IReadOnlyList<ProductInput> inputs = [Product(context, TextField.ProductOf(text)), Product(context, TextWord.ProductOf(text))];
        if (!force && !context.NeedsBuild(product, inputs))
        {
            return new IndexOutcome(text, IndexProducts.Occurrences, false, 0, clock.Elapsed);
        }

        var counted = new Dictionary<(string Number, int Book, string? Pos), int>();
        using (var reader = context.Store.Open(TextWord.ProductOf(text), TextWord.Schema(text)))
        {
            var served = StrongTerms.MayServe(reader.Header);
            foreach (var word in reader.Rows())
            {
                if (!served || word.Strong is not { } number)
                {
                    continue;
                }

                var key = (number, word.Key.Verse.Book, Pos(word.Morphology));
                counted[key] = counted.GetValueOrDefault(key) + 1;
            }
        }

        if (counted.Count == 0)
        {
            return null;
        }

        long rows;
        using (var writer = Create(context, product, OccurrenceRow.Schema, inputs))
        {
            foreach (var ((number, book, pos), words) in counted
                         .OrderBy(c => c.Key.Number, StringComparer.Ordinal).ThenBy(c => c.Key.Book).ThenBy(c => c.Key.Pos, StringComparer.Ordinal))
            {
                writer.Write(new OccurrenceRow(number, book, pos, words));
            }

            writer.Complete();
            rows = writer.Rows;
        }

        return new IndexOutcome(text, IndexProducts.Occurrences, true, rows, clock.Elapsed);
    }

    /// <summary>The morphology's <c>pos</c> as <c>morphology -&gt;&gt; 'pos'</c> reads it, where the object has one.</summary>
    internal static string? Pos(string? morphology)
    {
        if (morphology is null || !morphology.Contains("\"pos\"", StringComparison.Ordinal))
        {
            return null;
        }

        using var document = JsonDocument.Parse(morphology);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("pos", out var pos))
        {
            return null;
        }

        return pos.ValueKind switch
        {
            JsonValueKind.String => pos.GetString(),
            JsonValueKind.Null => null,
            _ => pos.GetRawText(),
        };
    }

    /// <summary>
    /// Every form the text prints, as <c>normalised</c> holds it, with how often and its stem (<see cref="FormRow"/>): the
    /// rows of <c>text_form</c>, which the search turns a typed word into the text's forms through.
    /// </summary>
    public static IndexOutcome Forms(IndexContext context, string text, bool force)
    {
        var clock = Stopwatch.StartNew();
        var product = IndexProducts.Of(text, IndexProducts.Forms);
        IReadOnlyList<ProductInput> inputs = [Product(context, TextField.ProductOf(text)), Product(context, TextWord.ProductOf(text))];
        if (!force && !context.NeedsBuild(product, inputs))
        {
            return new IndexOutcome(text, IndexProducts.Forms, false, 0, clock.Elapsed);
        }

        var language = context.Texts[text].Language;
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var reader = context.Store.Open(TextWord.ProductOf(text), TextWord.Schema(text)))
        {
            foreach (var word in reader.Rows())
            {
                if (word.Normalised is { Length: > 0 } form)
                {
                    counted[form] = counted.GetValueOrDefault(form) + 1;
                }
            }
        }

        long rows;
        using (var writer = Create(context, product, FormRow.Schema, inputs, ("stems", Stems(language))))
        {
            foreach (var (form, occurrences) in counted.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                writer.Write(new FormRow(form, occurrences, Stem(form, language)));
            }

            writer.Complete();
            rows = writer.Rows;
        }

        return new IndexOutcome(text, IndexProducts.Forms, true, rows, clock.Elapsed);
    }

    private static string Stems(string language) =>
        SearchWords.StemmedHere(language) ? "here"
        : SearchWords.Dictionary(language) is { } dictionary ? $"at load, by the serving database's {dictionary}"
        : "none: a form is its own stem";

    /// <summary>The stem a form is written with: here, itself, or null for the serving database's dictionary to give.</summary>
    internal static string? Stem(string form, string language) =>
        SearchWords.StemmedHere(language) ? SearchWords.StemHere(form, language) ?? form
        : SearchWords.Dictionary(language) is not null ? null
        : form;

    /// <summary>
    /// Every word of the text that names a record and can spell a name, with what the spellings are counted by: its letters,
    /// the lemma of a word of an original a link pairs it with that names the same record (read through the finals of the
    /// text's pairs with the originals, as <c>EntityRenderingLoader</c> reads it through the links), and how many words of the
    /// text print its form. The names come from <c>names/exported</c> until the names layer writes them from files.
    /// </summary>
    public static IndexOutcome? Spellings(IndexContext context, string text, bool force)
    {
        var clock = Stopwatch.StartNew();
        if (!context.Store.Exists(IndexProducts.ExportedNames(text)))
        {
            return null;
        }

        var product = IndexProducts.Of(text, IndexProducts.Spellings);
        var language = context.Texts[text].Language;
        var original = Renderings.Original.Contains(language);
        var pairs = original
            ? []
            : context.PairsOf(text)
                .Where(p => Renderings.Original.Contains(context.Texts[p.Other].Language) && context.Store.Exists(IndexProducts.ExportedNames(p.Other)))
                .ToList();
        var inputs = new List<ProductInput>
        {
            Product(context, TextField.ProductOf(text)),
            Product(context, TextWord.ProductOf(text)),
            Product(context, IndexProducts.ExportedNames(text)),
        };
        foreach (var other in pairs.Select(p => p.Other).Distinct().Order(StringComparer.Ordinal))
        {
            inputs.Add(Product(context, TextField.ProductOf(other)));
            inputs.Add(Product(context, TextWord.ProductOf(other)));
            inputs.Add(Product(context, IndexProducts.ExportedNames(other)));
        }

        inputs.AddRange(pairs.Select(p => Product(context, MappingProducts.Of(p.Pair, MappingProducts.Final))));
        if (!force && !context.NeedsBuild(product, inputs))
        {
            return new IndexOutcome(text, IndexProducts.Spellings, false, 0, clock.Elapsed);
        }

        var names = new List<ExportedName>();
        using (var reader = context.Store.Open(IndexProducts.ExportedNames(text), ExportedName.Schema(text)))
        {
            names.AddRange(reader.Rows());
        }

        var named = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var name in names.Where(n => n.Spellable))
        {
            if (!named.TryGetValue(name.Own.Own, out var records))
            {
                named[name.Own.Own] = records = new HashSet<string>(StringComparer.Ordinal);
            }

            records.Add(name.Record);
        }

        // Which name of the original a word translates: the least lemma, as the database's min() gives it, of a word of an
        // original on any link of the word that names the same record.
        var renders = new Dictionary<(string Own, string Record), string>();
        foreach (var (pair, other, first) in pairs)
        {
            var theirNames = context.NamesOf(other);
            var lemmas = context.EditionOf(other).Lemmas;
            using var reader = context.Store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
            foreach (var row in reader.Rows().Where(row => row.Settled))
            {
                var mine = first ? row.From : row.To;
                var theirs = first ? row.To : row.From;
                foreach (var own in MappingText.Split(mine))
                {
                    if (!named.TryGetValue(own, out var records))
                    {
                        continue;
                    }

                    foreach (var their in MappingText.Split(theirs))
                    {
                        if (!theirNames.TryGetValue(their, out var theirRecords) || !lemmas.TryGetValue(their, out var lemma))
                        {
                            continue;
                        }

                        foreach (var record in theirRecords.Where(records.Contains))
                        {
                            if (!renders.TryGetValue((own, record), out var least) || Collated(lemma, least) < 0)
                            {
                                renders[(own, record)] = lemma;
                            }
                        }
                    }
                }
            }
        }

        var words = new Dictionary<string, TextWord>(StringComparer.Ordinal);
        var printed = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var reader = context.Store.Open(TextWord.ProductOf(text), TextWord.Schema(text)))
        {
            foreach (var word in reader.Rows())
            {
                if (named.ContainsKey(word.Key.Own))
                {
                    words[word.Key.Own] = word;
                }

                if (word.Normalised is { } normalised)
                {
                    printed[normalised] = printed.GetValueOrDefault(normalised) + 1;
                }
            }
        }

        long rows;
        var missing = 0;
        using (var writer = Create(context, product, SpellingRow.Schema(text), inputs,
                   ("names", "exported from the database (names/exported) until the names layer writes them from files")))
        {
            foreach (var name in names.Where(n => n.Spellable).OrderBy(n => n.Own).ThenBy(n => n.Record, StringComparer.Ordinal))
            {
                if (!words.TryGetValue(name.Own.Own, out var word))
                {
                    missing++;
                    continue;
                }

                writer.Write(new SpellingRow(
                    name.Own,
                    name.Record,
                    word.Surface,
                    word.Trailer,
                    word.Lemma,
                    word.Normalised,
                    original ? null : renders.GetValueOrDefault((name.Own.Own, name.Record)),
                    name.Guess,
                    name.Thing,
                    name.Firm,
                    word.Normalised is { } normalised ? printed.GetValueOrDefault(normalised) : 0));
            }

            writer.Complete();
            rows = writer.Rows;
        }

        return new IndexOutcome(text, IndexProducts.Spellings, true, rows, clock.Elapsed, missing > 0 ? $"{missing} named words the words product lacks" : null);
    }

    /// <summary>
    /// Lemmas compared as the database's <c>min()</c> compares them under its collation (<c>en_US.utf8</c>), not by code
    /// point: a word paired with both the Hebrew יְרֻבַּעַל and the Greek Ἱεροβάαλ takes the Greek, which the collation
    /// sorts first (Latin, Greek, Cyrillic, Hebrew, then the rest) and the code points last (U+05D9 before U+1F39). Within
    /// a script, by the letters without their marks and case, then by code point. The Forge runs without a culture's
    /// collation (invariant globalisation), so the order the lemmas of the originals need is written out here.
    /// </summary>
    internal static int Collated(string left, string right)
    {
        var script = Script(left).CompareTo(Script(right));
        if (script != 0)
        {
            return script;
        }

        var letters = string.CompareOrdinal(Letters(left), Letters(right));
        return letters != 0 ? letters : string.CompareOrdinal(left, right);
    }

    private static int Script(string word)
    {
        foreach (var c in word)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            return c switch
            {
                <= 'ɏ' => 1,
                >= 'Ͱ' and <= 'Ͽ' or >= 'ἀ' and <= '῿' => 2,
                >= 'Ѐ' and <= 'ԯ' => 3,
                >= '֐' and <= '׿' => 4,
                _ => 5,
            };
        }

        return 0;
    }

    private static string Letters(string word)
    {
        var decomposed = word.Normalize(System.Text.NormalizationForm.FormD);
        var letters = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                letters.Append(char.ToLowerInvariant(c));
            }
        }

        return letters.ToString();
    }
}
