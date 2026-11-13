using System.Diagnostics;
using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

namespace Essenthos.Core.Indexes;

/// <summary>
/// What stands behind each spelling of a text in every original it is linked to, counted once at build so that the
/// concordance read backwards (<c>/v1/renderings</c>) reads a few rows by spelling instead of walking every occurrence's
/// links: <c>texts/&lt;SLUG&gt;/index/renderings</c> (per spelling, edition and original: the words it stands behind, the
/// links, their methods and every place) and <c>texts/&lt;SLUG&gt;/index/rendered</c> (per spelling and edition: the
/// occurrences in the books the edition holds, how many are reached, the links, their methods and sources).
///
/// <para>
/// A request gathers every spelling sharing a stem and adds their rows. A word prints one spelling, so the words and the
/// places of one spelling are never another's; a link can hold words of two spellings (a phrase linked whole), and such a
/// link is named in <c>shared</c> of every row it is counted in where the two spellings can be asked for together
/// (<see cref="FormIndex.Together"/>), so the sum can count it once.
/// </para>
/// </summary>
internal static class RenderingIndex
{
    private const string Producer = "forge indexes";

    private sealed class Behind
    {
        public readonly HashSet<int> Hits = [];
        public readonly Dictionary<long, string> Links = [];
        public readonly Dictionary<long, string> Shared = [];
        public readonly SortedDictionary<(int Book, int Chapter, int Verse), (int Position, int Word, string Surface)> Places = [];
        public (int Book, int Chapter, int Verse, int Word) FirstAt = (int.MaxValue, 0, 0, 0);
        public string? FirstLemma;
    }

    private sealed class Reach
    {
        public readonly HashSet<int> Hits = [];
        public readonly Dictionary<long, string> Links = [];
        public readonly Dictionary<long, string> Shared = [];
        public readonly SortedSet<string> Sources = new(StringComparer.Ordinal);
    }

    private sealed record Word(string Form, int Book, int Chapter, int Verse, int Position, string Surface);

    public static IReadOnlyList<ProductInput> Inputs(IndexContext context, string text, IReadOnlyList<(string Pair, string Other, bool First)> others)
    {
        var store = context.Store;
        var inputs = new List<ProductInput>
        {
            store.ProductInput(TextField.ProductOf(text)),
            store.ProductInput(TextWord.ProductOf(text)),
        };
        if (store.Exists(FrameFile.Product(text)))
        {
            inputs.Add(store.ProductInput(FrameFile.Product(text)));
        }

        foreach (var other in others.Select(p => p.Other).Distinct().Order(StringComparer.Ordinal))
        {
            inputs.Add(store.ProductInput(TextField.ProductOf(other)));
            inputs.Add(store.ProductInput(TextWord.ProductOf(other)));
            inputs.Add(store.ProductInput(TextBook.ProductOf(other)));
        }

        inputs.AddRange(others.Select(p => store.ProductInput(MappingProducts.Of(p.Pair, MappingProducts.Final))));
        if (store.Exists(TableProducts.Product("word_strong")))
        {
            inputs.Add(store.ProductInput(TableProducts.Product("word_strong")));
            inputs.Add(store.ProductInput(WordIdCollision.Product));
        }

        return inputs;
    }

    public static IndexOutcome? Make(IndexContext context, string text, bool force)
    {
        var clock = Stopwatch.StartNew();
        var others = context.PairsOf(text).Where(p => context.Texts[p.Other].Kind != TextKind.Translation).ToList();
        if (others.Count == 0)
        {
            return null;
        }

        var store = context.Store;
        var renderings = IndexProducts.Of(text, IndexProducts.Renderings);
        var rendered = IndexProducts.Of(text, IndexProducts.Rendered);
        var inputs = Inputs(context, text, others);
        if (!force && !context.NeedsBuild(renderings, inputs) && !context.NeedsBuild(rendered, inputs))
        {
            return new IndexOutcome(text, IndexProducts.Renderings, false, 0, clock.Elapsed);
        }

        var words = Words(store, text, out var perBook);
        var language = context.Texts[text].Language;

        var links = new Dictionary<string, int>(StringComparer.Ordinal);
        var behind = new Dictionary<(string Form, string Witness, string? Number, string? Lemma), Behind>();
        var reach = new Dictionary<(string Form, string Witness), Reach>();
        var hitForms = new List<string>();
        var hits = new List<int>();
        foreach (var (pair, other, first) in others)
        {
            var originals = context.OriginalsOf(other);
            var count = 0;
            using var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema);
            foreach (var row in reader.Rows().Where(row => row.Settled))
            {
                count++;
                if (row.Relation is not ("renders" or "equals"))
                {
                    continue;
                }

                hits.Clear();
                foreach (var own in MappingText.Split(first ? row.From : row.To))
                {
                    if (words.Keys.TryGetValue(own, out var hit) && words.Rows[hit] is not null)
                    {
                        hits.Add(hit);
                    }
                }

                var theirs = MappingText.Split(first ? row.To : row.From).ToList();
                if (hits.Count == 0 || theirs.Count == 0)
                {
                    continue;
                }

                var method = EnumSpelling.Of(MappingMethods.DatabaseMethod(row.Method));
                hitForms.Clear();
                hitForms.AddRange(hits.Select(h => words.Rows[h]!.Form).Distinct(StringComparer.Ordinal));
                var shared = hitForms.Count > 1 && FormIndex.Together(language, hitForms);
                var keys = theirs.Select(their => originals.TryGetValue(their, out var key) ? key : (null, null)).Distinct().ToList();
                foreach (var hit in hits)
                {
                    var word = words.Rows[hit]!;
                    if (!reach.TryGetValue((word.Form, other), out var reached))
                    {
                        reach[(word.Form, other)] = reached = new Reach();
                    }

                    reached.Hits.Add(hit);
                    reached.Links[row.Id] = method;
                    reached.Sources.Add(row.Source);
                    if (shared)
                    {
                        reached.Shared[row.Id] = method;
                    }

                    foreach (var (number, lemma) in keys)
                    {
                        var key = (word.Form, other, number, number is null ? lemma : null);
                        if (!behind.TryGetValue(key, out var original))
                        {
                            behind[key] = original = new Behind();
                        }

                        original.Hits.Add(hit);
                        original.Links[row.Id] = method;
                        if (shared)
                        {
                            original.Shared[row.Id] = method;
                        }

                        var at = (word.Book, word.Chapter, word.Verse);
                        if (!original.Places.TryGetValue(at, out var placed) || word.Position < placed.Position)
                        {
                            original.Places[at] = (word.Position, hit, word.Surface);
                        }

                        var earliest = (word.Book, word.Chapter, word.Verse, hit);
                        if (earliest.CompareTo(original.FirstAt) < 0
                            || (earliest == original.FirstAt && string.CompareOrdinal(lemma, original.FirstLemma) < 0))
                        {
                            original.FirstAt = earliest;
                            original.FirstLemma = lemma;
                        }
                    }
                }
            }

            links[other] = links.GetValueOrDefault(other) + count;
        }

        var ordinal = 0;
        var ranked = LinkedOriginals.Ordered(links.Keys.Order(StringComparer.Ordinal).Select(other =>
                new LinkedOriginal(++ordinal, other, other, context.Texts[other].Language, context.Texts[other].Kind, links[other])))
            .Select((original, rank) => (original.Slug, rank))
            .ToDictionary(o => o.Slug, o => o.rank, StringComparer.Ordinal);

        long rows;
        using (var writer = Create(store, renderings, RenderingRow.Schema, inputs))
        {
            foreach (var ((form, witness, number, lemma), original) in behind
                         .OrderBy(b => b.Key.Form, StringComparer.Ordinal)
                         .ThenBy(b => ranked[b.Key.Witness])
                         .ThenBy(b => b.Key.Number, StringComparer.Ordinal)
                         .ThenBy(b => b.Key.Lemma, StringComparer.Ordinal))
            {
                writer.Write(new RenderingRow(
                    form,
                    witness,
                    number,
                    number is null ? lemma : original.FirstLemma,
                    original.Hits.Count,
                    original.Links.Count,
                    RenderingPlaces.WriteMethods(Methods(original.Links)),
                    RenderingPlaces.WriteLinks(original.Shared.Select(l => (l.Key, l.Value))),
                    RenderingPlaces.Write(original.Places.Select(p =>
                        new RenderingPlace(p.Key.Book, p.Key.Chapter, p.Key.Verse, p.Value.Position, p.Value.Surface)))));
            }

            writer.Complete();
            rows = writer.Rows;
        }

        var sources = reach.Values.SelectMany(r => r.Sources).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select((source, at) => (source, at)).ToDictionary(s => s.source, s => s.at + 1, StringComparer.Ordinal);
        using (var writer = Create(store, rendered, RenderedRow.Schema, inputs,
                   [.. sources.OrderBy(s => s.Value).Select(s => (SourceNote, $"{s.Value} {s.Key}"))]))
        {
            var held = ranked.Keys.ToDictionary(w => w, w => context.BooksOf(w), StringComparer.Ordinal);
            var forms = perBook.Keys.Concat(reach.Keys.Select(r => r.Form)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            foreach (var form in forms)
            {
                var books = perBook.GetValueOrDefault(form);
                foreach (var witness in ranked.OrderBy(r => r.Value).Select(r => r.Key))
                {
                    var occurrences = books is null ? 0 : held[witness].Sum(book => books.GetValueOrDefault(book));
                    var reached = reach.GetValueOrDefault((form, witness));
                    if (occurrences == 0 && reached is null)
                    {
                        continue;
                    }

                    writer.Write(new RenderedRow(
                        form,
                        witness,
                        ranked[witness],
                        occurrences,
                        reached?.Hits.Count ?? 0,
                        reached?.Links.Count ?? 0,
                        reached is null ? string.Empty : RenderingPlaces.WriteMethods(Methods(reached.Links)),
                        reached is null ? null : RenderingPlaces.WriteLinks(reached.Shared.Select(l => (l.Key, l.Value))),
                        reached is null ? string.Empty : string.Join(' ', reached.Sources.Select(source => sources[source]).Order())));
                }
            }

            writer.Complete();
            rows += writer.Rows;
        }

        return new IndexOutcome(text, IndexProducts.Renderings, true, rows, clock.Elapsed, $"over {string.Join(", ", ranked.Keys.Order(StringComparer.Ordinal))}");
    }

    private static IEnumerable<(string Method, int Links)> Methods(Dictionary<long, string> links) =>
        links.Values.GroupBy(m => m, StringComparer.Ordinal).Select(g => (g.Key, g.Count()));

    private static IProductWriter<T> Create<T>(ProductStore store, string product, ProductSchema<T> schema, IReadOnlyList<ProductInput> inputs,
        params (string Key, string Value)[] notes) =>
        store.Create(product, ProductKind.Machine, schema, ProductHeader.DescribeProducer(Producer), inputs, header =>
        {
            header.Notes.Add(new(IndexContext.VersionNote, IndexContext.Version));
            foreach (var (key, value) in notes)
            {
                header.Notes.Add(new(key, value));
            }
        });

    /// <summary>The header note of <c>rendered</c> numbering each source its rows name: <c>source  &lt;n&gt; &lt;source&gt;</c>.</summary>
    public const string SourceNote = "source";

    /// <summary>The sources a <c>rendered</c> product's notes number, by number.</summary>
    public static Dictionary<int, string> Sources(ProductHeader? header) =>
        (header?.Notes ?? []).Where(note => note.Key == SourceNote)
        .Select(note => note.Value.Split(' ', 2))
        .ToDictionary(parts => int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), parts => parts[1]);

    private sealed record TextWords(Dictionary<string, int> Keys, List<Word?> Rows);

    /// <summary>
    /// The text's words by own key, each with its spelling and where it stands in the frame; null for a word printing no
    /// spelling or elided, which no count reads. <paramref name="perBook"/>: how many words print each spelling in each book.
    /// </summary>
    private static TextWords Words(ProductStore store, string text, out Dictionary<string, Dictionary<int, int>> perBook)
    {
        var primary = new Dictionary<VerseKey, VerseKey>();
        if (store.Exists(FrameFile.Product(text)))
        {
            using var frame = store.Open(FrameFile.Product(text), FrameRow.Schema);
            foreach (var (own, canonical) in frame.Rows().SelectMany(row => row.Verses()))
            {
                primary[own] = canonical[0];
            }
        }

        perBook = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new List<Word?>();
        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        foreach (var word in reader.Rows())
        {
            keys[word.Key.Own] = rows.Count;
            if (word.Elided || word.Normalised is not { Length: > 0 } form)
            {
                rows.Add(null);
                continue;
            }

            var verse = word.Key.Verse;
            var at = primary.TryGetValue(verse, out var moved) ? moved : FrameFile.AtOwn(verse);
            rows.Add(new Word(form, at.Book, at.Chapter, at.Verse, word.Key.Position, word.Surface));
            if (!perBook.TryGetValue(form, out var books))
            {
                perBook[form] = books = [];
            }

            books[verse.Book] = books.GetValueOrDefault(verse.Book) + 1;
        }

        return new TextWords(keys, rows);
    }
}
