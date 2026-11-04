using System.Text.Json;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Names;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The names of a translation placed on the words of the original that name the same person, place or people
/// (<see cref="NamesFirstPass"/>, <see cref="NamePlacement"/>), read from files: both texts' words by frame address
/// (<see cref="NamedWords"/>), whom the original's words name (its settled names, <c>names/&lt;REF&gt;</c>, and the records
/// with their other names), whom the translation's verses say its words name, and the pair's earlier method files, which
/// say which words a name may no longer be placed on.
///
/// <para>
/// <strong>Whom the translation's verses say a word names</strong> is the name-consensus pass's reading of the text
/// (<see cref="Encyclopedia.NameConsensusPass"/>). It is read from the text's names as the database held them
/// (<c>names/live/&lt;SLUG&gt;</c>, the rows of that pass's source): the pass reads every text of a language at once and
/// the names every other method gave the text, which the names projection makes from the finals this file goes into, so
/// it cannot be made here without the graph reading itself. A text with no such file is placed on the other two signals.
/// </para>
///
/// <para>
/// <strong>A name is placed only where neither word is linked yet</strong>, as the database's pass wrote it after every
/// other step of the recipe: by the pair's stated, Strong, aligner, composed, lexical, name-list and possessive files.
/// EVIDENTIA's file and the laid stated rows read this one, so they are not read here; the merge ranks EVIDENTIA above the
/// names where both claim a word. A ruling's refused rendering is the merge's to suppress, as for every method.
/// </para>
/// </summary>
internal sealed class NamesFirstSource : IPairSource
{
    /// <summary>The pairs the recipe placed the names of (<c>names-first</c>, 2026-10-15).</summary>
    internal static readonly IReadOnlySet<string> Placed = new HashSet<string>(StringComparer.Ordinal)
    {
        "ALEX-BHSA", "ALEX-NESTLE1904", "ALM1911-BHSA", "ALM1911-NESTLE1904", "ASV-BHSA", "ASV-NESTLE1904", "AVD1865-BHSA",
        "AVD1865-NESTLE1904", "BBE-BHSA", "BBE-NESTLE1904", "BRENTON-BHSA", "BRENTON-NESTLE1904", "BSB-BHSA", "BSB-NESTLE1904",
        "CSLELIZABETH-BHSA", "CSLELIZABETH-NESTLE1904", "CUV-BHSA", "CUV-NESTLE1904", "CUVS-BHSA", "CUVS-NESTLE1904", "DRA-BHSA",
        "DRA-NESTLE1904", "ELB1905-BHSA", "ELB1905-NESTLE1904", "GEEZ81-BHSA", "GEEZ81-NESTLE1904", "GNV1599-BHSA",
        "GNV1599-NESTLE1904", "GRCBRENT-BHSA", "GRCBRENT-NESTLE1904", "IRV2019-BHSA", "IRV2019-NESTLE1904", "IRVASM-BHSA",
        "IRVASM-NESTLE1904", "IRVBEN-BHSA", "IRVBEN-NESTLE1904", "IRVGUJ-NESTLE1904", "IRVKAN-NESTLE1904", "IRVMAL-BHSA",
        "IRVMAL-NESTLE1904", "IRVMAR-NESTLE1904", "IRVPAN-NESTLE1904", "IRVTAM-BHSA", "IRVTAM-NESTLE1904", "IRVTEL-NESTLE1904",
        "IRVURD-BHSA", "IRVURD-NESTLE1904", "JND2024-BHSA", "JND2024-NESTLE1904", "JPS1917-BHSA", "KJV-BHSA", "KJV-NESTLE1904",
        "KRV-BHSA", "KRV-NESTLE1904", "LSG1910-BHSA", "LSG1910-NESTLE1904", "LUTH1912-BHSA", "LUTH1912-NESTLE1904", "NPU2022-BHSA",
        "NPU2022-NESTLE1904", "OHCB-BHSA", "OHCB-NESTLE1904", "OTTLEY-BHSA", "RLT2018-BHSA", "RLT2018-NESTLE1904",
        "RP2018-NESTLE1904", "RUSV-BHSA", "RUSV-NESTLE1904", "RV1909-BHSA", "RV1909-NESTLE1904", "SBLGNT-NESTLE1904",
        "SCH1951-BHSA", "SCH1951-NESTLE1904", "SIN-NESTLE1904", "SP-BHSA", "SWETE-BHSA", "SWETE-NESTLE1904", "SWETEOG-BHSA",
        "SWETEOG-NESTLE1904", "TISCH-NESTLE1904", "TR1550-NESTLE1904", "TR1894-NESTLE1904", "TYN1534-NESTLE1904", "UBIO-BHSA",
        "UBIO-NESTLE1904", "UKR1871-BHSA", "UKR1871-NESTLE1904", "ULT-BHSA", "ULT-NESTLE1904", "UST-BHSA", "UST-NESTLE1904",
        "VAT-NESTLE1904", "VIGLT-BHSA", "VIGLT-NESTLE1904", "VULGCLEM-BHSA", "VULGCLEM-NESTLE1904", "WEB-BHSA", "WEB-NESTLE1904",
        "WH1881-NESTLE1904", "YLT-BHSA", "YLT-NESTLE1904",
    };

    /// <summary>The methods whose links a name is never placed across: every method file of the pair that does not read this one.</summary>
    internal static readonly IReadOnlyList<string> Before =
    [
        MappingMethods.Stated, MappingMethods.Strong, MappingMethods.Aligner, MappingMethods.Composed, MappingMethods.Lexical,
        MappingMethods.NameList, MappingMethods.Possessive,
    ];

    public const string RecordsProduct = "records/records";

    private static readonly IReadOnlySet<string> Kinds = new HashSet<string>(
        [EnumSpelling.Of(EntityKind.Person), EnumSpelling.Of(EntityKind.Place), EnumSpelling.Of(EntityKind.People)], StringComparer.Ordinal);

    private static readonly IReadOnlySet<int> Nobody = new HashSet<int>();

    public string Method => MappingMethods.Names;

    public string Name => "names-first";

    public bool States(string pair) => Placed.Contains(pair);

    public bool Makes(string source) => source == NamesFirstPass.Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        string[] products =
        [
            NameProducts.Settled(toText), NameProducts.Live(fromText), RecordsProduct,
            .. Before.Select(method => MappingProducts.Of(pair, method)),
        ];
        return products.Select(store.Find).OfType<string>();
    }

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var from = texts[fromText];
        var to = texts[toText];
        var store = texts.Store;
        var policy = NamePolicy.Default.For(from.Language);
        var lettered = Versification(store, fromText) == Versification(store, toText);
        var source = NamedWords.Read(from, lettered);
        var target = NamedWords.Read(to, lettered);
        var records = Records.Read(store);
        var originals = Originals(store, to, records);
        var named = Named(store, from, records);
        var (linkedSources, linkedTargets) = Linked(store, pair, from, to);
        var capitalised = Capitalised(from);

        var verses = Verses(source, target, from, to, originals, named, capitalised);
        var placings = NamePlacement.Place(verses);
        var rows = Placings(placings, policy, linkedSources, linkedTargets)
            .Select(group => new MappingRow(from.Key(group.Source).Own.ToString(),
                string.Join(' ', group.Targets.Order().Select(id => to.Key(id).Own.ToString())), "renders", Method,
                NamesFirstPass.Confidence, NamesFirstPass.Source, group.Evidence, null, null))
            .ToList();
        log($"{verses.Count} verses where the original names someone, {named.Count} words its verses say name someone " +
            $"({(store.Exists(NameProducts.Live(fromText)) ? "the text's names" : "no names file")}), " +
            $"{placings.Count(policy.Places)} pairs placed, {rows.Count} source words no link of the pair joins yet");
        return rows;
    }

    /// <summary>
    /// The placed pairs as the database's pass wrote them: only where neither word is linked in the pair yet, one link per
    /// word of the translation with every original word placed on it, named by the first placing's signals.
    /// </summary>
    internal static IEnumerable<(long Source, IReadOnlyList<long> Targets, string Evidence)> Placings(
        IReadOnlyList<NamePlacing> placings, NamePolicy policy, IReadOnlySet<long> linkedSources, IReadOnlySet<long> linkedTargets) =>
        placings
            .Where(policy.Places)
            .Where(placing => !linkedSources.Contains(placing.Source) && !linkedTargets.Contains(placing.Target))
            .GroupBy(placing => placing.Source)
            .Select(group => (group.Key, (IReadOnlyList<long>)[.. group.Select(placing => placing.Target).Distinct()],
                NamesFirstPass.Combination(group.First().Signals)));

    /// <summary>The verses where both texts stand and the original names someone, as the database's pass built them.</summary>
    internal static List<NameVerse> Verses(
        Dictionary<(int, int, int), List<AlignmentPipeline.Word>> source,
        Dictionary<(int, int, int), List<AlignmentPipeline.Word>> target,
        ProductText from,
        ProductText to,
        IReadOnlyDictionary<long, Original> originals,
        IReadOnlyDictionary<long, IReadOnlySet<int>> named,
        IReadOnlySet<long> capitalised)
    {
        var verses = new List<NameVerse>();
        foreach (var address in source.Keys.Intersect(target.Keys).Order())
        {
            var original = target[address];
            var targets = new List<NameTargetWord>();
            for (var index = 0; index < original.Count; index++)
            {
                if (originals.TryGetValue(original[index].Id, out var annotated))
                {
                    targets.Add(new NameTargetWord(original[index].Id, index, original[index].Letters, annotated.Families, annotated.Kind,
                        NamesFirstPass.Spelling(original[index].Text, to.Language), annotated.Proper));
                }
            }

            if (targets.Count == 0)
            {
                continue;
            }

            verses.Add(new NameVerse(
                address,
                [
                    .. source[address].Select(word => new NameSourceWord(
                        word.Id,
                        word.Text,
                        NameLetters.Of(word.Text, from.Language),
                        NameLetters.Form(word.Text, from.Language),
                        named.GetValueOrDefault(word.Id) ?? Nobody,
                        word.Name is not null || capitalised.Contains(word.Id),
                        word.Name is not null)),
                ],
                targets,
                original.Count));
        }

        return verses;
    }

    /// <param name="Families">The records it names, each by its family.</param>
    /// <param name="Kind">The kind of the first of them in the records' order.</param>
    /// <param name="Proper">Whether it is a name and not a common noun annotated with whom it means.</param>
    internal sealed record Original(IReadOnlySet<int> Families, EntityKind Kind, bool Proper);

    /// <summary>Each record by slug as a number, its kind, and its other names as families (<see cref="NameFamilies"/>).</summary>
    internal sealed record Records(IReadOnlyDictionary<string, int> Ids, IReadOnlyList<string> Kinds, NameFamilies Families)
    {
        public static Records Read(ProductStore store)
        {
            var ids = new Dictionary<string, int>(StringComparer.Ordinal);
            var kinds = new List<string>();
            var aliases = new List<(string Slug, string Other)>();
            if (store.Exists(RecordsProduct))
            {
                using var reader = store.Open(RecordsProduct);
                var columns = reader.Columns.Select(column => column.Name).ToList();
                var (slug, kind, other) = (columns.IndexOf("slug"), columns.IndexOf("kind"), columns.IndexOf("another_name_for"));
                foreach (var row in reader.Rows())
                {
                    ids[row[slug]!] = kinds.Count;
                    kinds.Add(row[kind]!);
                    if (row[other] is { Length: > 0 } another)
                    {
                        aliases.Add((row[slug]!, another));
                    }
                }
            }

            var families = new NameFamilies(aliases
                .Where(alias => ids.ContainsKey(alias.Other))
                .Select(alias => (ids[alias.Slug], ids[alias.Other])));
            return new Records(ids, kinds, families);
        }
    }

    /// <summary>
    /// The original's words that name a person, a place or a people, from its settled names (<see cref="NamesFirstPass"/> has
    /// why a common noun and the אֲדֹנָי beside יְהוִה are weighed but never placed).
    /// </summary>
    internal static Dictionary<long, Original> Originals(ProductStore store, ProductText to, Records records)
    {
        var byWord = new Dictionary<long, List<int>>();
        var ids = PairLinkFiles.Ids(to);
        if (store.Exists(NameProducts.Settled(to.Slug)))
        {
            using var reader = store.Open(NameProducts.Settled(to.Slug), SettledName.Schema(to.Slug));
            foreach (var row in reader.Rows())
            {
                if (records.Ids.TryGetValue(row.Record, out var record) && Kinds.Contains(records.Kinds[record])
                    && ids.TryGetValue(row.Own.Own, out var word))
                {
                    if (!byWord.TryGetValue(word, out var named))
                    {
                        byWord[word] = named = [];
                    }

                    if (!named.Contains(record))
                    {
                        named.Add(record);
                    }
                }
            }
        }

        var originals = new Dictionary<long, Original>(byWord.Count);
        foreach (var (word, named) in byWord)
        {
            named.Sort();
            originals[word] = new Original(records.Families.Of(named), EnumSpelling.ToEntityKind(records.Kinds[named[0]]),
                Proper(to, (int)word));
        }

        return originals;
    }

    /// <summary>
    /// Whether the original's word is a name: a proper noun or a gentilic, or a Greek word whose lemma is capitalised, and not
    /// אֲדֹנָי standing beside יְהוָה either way round.
    /// </summary>
    internal static bool Proper(ProductText text, int id)
    {
        var word = text.Words[id];
        var pos = Pos(word.Morphology);
        var proper = pos is "nmpr" or "adjv"
                     || (word.Lemma is { Length: > 0 } lemma && lemma[..1] != lemma[..1].ToLowerInvariant());
        return proper && !Beside(text, id, id - 1) && !Beside(text, id, id + 1);
    }

    private static bool Beside(ProductText text, int id, int other)
    {
        if (other < 0 || other >= text.Words.Count)
        {
            return false;
        }

        var (word, next) = (text.Words[id], text.Words[other]);
        return word.Key.Verse == next.Key.Verse && Math.Abs(word.Key.Position - next.Key.Position) == 1
               && ((word.Lemma == NamesFirstPass.Adonai && next.Lemma == NamesFirstPass.Yhwh)
                   || (word.Lemma == NamesFirstPass.Yhwh && next.Lemma == NamesFirstPass.Adonai));
    }

    private static string? Pos(string? morphology)
    {
        if (morphology is null)
        {
            return null;
        }

        using var json = JsonDocument.Parse(morphology);
        return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("pos", out var pos)
               && pos.ValueKind == JsonValueKind.String
            ? pos.GetString()
            : null;
    }

    /// <summary>The translation's words its verses say name someone: the rows of the name-consensus pass among its names.</summary>
    internal static Dictionary<long, IReadOnlySet<int>> Named(ProductStore store, ProductText from, Records records)
    {
        var byWord = new Dictionary<long, List<int>>();
        if (store.Exists(NameProducts.Live(from.Slug)))
        {
            var ids = PairLinkFiles.Ids(from);
            using var reader = store.Open(NameProducts.Live(from.Slug), LiveName.Schema(from.Slug));
            foreach (var row in reader.Rows())
            {
                if (row.Source == Annotations.Consensus && records.Ids.TryGetValue(row.Record, out var record)
                    && ids.TryGetValue(row.Own.Own, out var word))
                {
                    if (!byWord.TryGetValue(word, out var named))
                    {
                        byWord[word] = named = [];
                    }

                    named.Add(record);
                }
            }
        }

        return byWord.ToDictionary(entry => entry.Key, entry => records.Families.Of(entry.Value));
    }

    /// <summary>The words of either text some link of the pair's earlier method files names.</summary>
    private static (HashSet<long> Sources, HashSet<long> Targets) Linked(ProductStore store, string pair, ProductText from, ProductText to)
    {
        var fromIds = PairLinkFiles.Ids(from);
        var toIds = PairLinkFiles.Ids(to);
        var (sources, targets) = (new HashSet<long>(), new HashSet<long>());
        foreach (var method in Before)
        {
            var product = MappingProducts.Of(pair, method);
            if (!store.Exists(product))
            {
                continue;
            }

            using var reader = store.Open(product, MappingRow.Schema);
            foreach (var row in reader.Rows())
            {
                sources.UnionWith(PairLinkFiles.Words(row.From, fromIds).Select(id => (long)id));
                targets.UnionWith(PairLinkFiles.Words(row.To, toIds).Select(id => (long)id));
            }
        }

        return (sources, targets);
    }

    /// <summary>
    /// The words a text writes with a capital inside its verse, whatever it writes elsewhere (<see cref="NamesFirstPass"/>):
    /// <em>Lot</em> is still Lot where the text writes <em>lots</em>.
    /// </summary>
    internal static HashSet<long> Capitalised(ProductText text)
    {
        var capitalised = new HashSet<long>();
        for (var id = 0; id < text.Words.Count; id++)
        {
            var surface = text.Words[id].Surface;
            if (text.Words[id].Key.Position <= 1 || surface == surface.ToLowerInvariant() || surface == surface.ToUpperInvariant())
            {
                continue;
            }

            var trimmed = surface.TrimStart('(', '[', '"', '\'', '“', '‘');
            if (trimmed.Length > 0 && char.IsUpper(trimmed[0]))
            {
                capitalised.Add(id);
            }
        }

        return capitalised;
    }

    private static string? Versification(ProductStore store, string slug)
    {
        using var reader = store.Open(TextField.ProductOf(slug), TextField.Schema);
        return reader.Rows().FirstOrDefault(field => field.Field == "versification")?.Value;
    }
}
