using Essenthos.Core.Comparison;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Npgsql;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The links a generator reads, from the mapping's finals instead of the database (DOC-0224 §6, generators read
/// files): each pair's final, its keys resolved to the word ids of the database the generator's words still come
/// from, so that the generator's own code is unchanged. Once the texts are products (TSK-1443) the ids come from
/// <c>texts/&lt;SLUG&gt;/ids</c> and no database is read at all. What a generator writes goes back to a method file
/// (<see cref="WriteComposed"/>), and the merge decides what of it becomes a link.
/// </summary>
internal sealed class MappingLinks(ProductStore store, WordKeys keys, IReadOnlyDictionary<int, string?> languages)
{
    /// <summary>One link of a final: its words by id and its settled claim.</summary>
    internal sealed record FinalLink(long Id, string Relation, LinkMethod Method, double? Confidence, long[] From, long[] To);

    private readonly Dictionary<string, List<FinalLink>> _finals = new(StringComparer.Ordinal);

    public ProductStore Store => store;

    public WordKeys Keys => keys;

    public static async Task<MappingLinks> Open(ProductStore store, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var keys = await WordKeys.Load(connection, reverse: true, cancellationToken);
        var languages = new Dictionary<int, string?>();
        await using var command = new NpgsqlCommand("SELECT id, language FROM text", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            languages[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return new MappingLinks(store, keys, languages);
    }

    /// <summary>The links of a pair's final; empty where the pair has none.</summary>
    public List<FinalLink> Links(int fromTextId, int toTextId)
    {
        var pair = MappingProducts.Pair(keys.Slug(fromTextId), keys.Slug(toTextId));
        lock (_finals)
        {
            if (_finals.TryGetValue(pair, out var cached))
            {
                return cached;
            }
        }

        var links = new List<FinalLink>();
        var product = MappingProducts.Of(pair, MappingProducts.Final);
        if (store.Exists(product))
        {
            using var reader = store.Open(product, FinalRow.Schema);
            foreach (var row in reader.Rows().Where(r => r.Settled))
            {
                var from = Ids(row.From, fromTextId);
                var to = Ids(row.To, toTextId);
                if (from is not null && to is not null)
                {
                    links.Add(new FinalLink(row.Id, row.Relation, MappingMethods.DatabaseMethod(row.Method), row.Confidence, from, to));
                }
            }
        }

        lock (_finals)
        {
            _finals[pair] = links;
        }

        return links;
    }

    /// <summary>What <see cref="CompositionPipeline"/> reads as the middle text's links to the target (its <c>Carried</c>).</summary>
    public ILookup<long, (long To, double Confidence)> Carried(int viaTextId, int toTextId, bool aligned) =>
        Links(viaTextId, toTextId)
            .Where(l => l.To.Length > 0 && (aligned || l.Method is not (LinkMethod.Aligner or LinkMethod.RuleBased)))
            .SelectMany(l => l.From.SelectMany(f => l.To.Select(t => (Bridge: f, To: t,
                Confidence: (l.Confidence ?? 1) * CompositionPipeline.SpecificityOf(l.From.Length)))))
            .ToLookup(r => r.Bridge, r => (r.To, r.Confidence));

    /// <summary>The target words each source word is joined to by a link that is not the aligner's (its <c>Statements</c>).</summary>
    public Dictionary<long, HashSet<long>> Statements(int fromTextId, int toTextId, bool rules)
    {
        var statements = new Dictionary<long, HashSet<long>>();
        foreach (var link in Links(fromTextId, toTextId).Where(l => l.Method != LinkMethod.Aligner && (rules || l.Method != LinkMethod.RuleBased)))
        {
            foreach (var word in link.From)
            {
                if (!statements.TryGetValue(word, out var targets))
                {
                    statements[word] = targets = [];
                }

                targets.UnionWith(link.To);
            }
        }

        return statements;
    }

    /// <summary>The one-to-one renderings a method other than the aligner states, by their two words (its <c>Stated</c>).</summary>
    public Dictionary<(long From, long To), long> Stated(int fromTextId, int toTextId) =>
        Links(fromTextId, toTextId)
            .Where(l => l.Relation == "renders" && l.Method != LinkMethod.Aligner && l.From.Length == 1 && l.To.Length == 1)
            .GroupBy(l => (l.From[0], l.To[0]))
            .ToDictionary(g => g.Key, g => g.First().Id);

    /// <summary>The target words a method other than the aligner says the source does not render (its <c>Unrendered</c>).</summary>
    public HashSet<long> Unrendered(int fromTextId, int toTextId) =>
        [.. Links(fromTextId, toTextId).Where(l => l.Relation == "omits" && l.Method != LinkMethod.Aligner).SelectMany(l => l.To)];

    /// <summary>The other texts of the source's language whose stated links to the target are enough to measure on (its <c>Proxies</c>).</summary>
    public List<(int Id, string Slug)> Proxies(int fromTextId, int toTextId)
    {
        var language = languages.GetValueOrDefault(fromTextId);
        var target = keys.Slug(toTextId);
        var proxies = new List<(int Id, string Slug, int Words)>();
        foreach (var pair in MappingProducts.Pairs(store).Where(p => p.EndsWith("-" + target, StringComparison.Ordinal)))
        {
            var (fromText, _) = MappingProducts.Split(pair);
            if (keys.TextId(fromText) is not { } id || id == fromTextId || languages.GetValueOrDefault(id) != language)
            {
                continue;
            }

            var words = Links(id, toTextId)
                .Where(l => l.To.Length > 0 && l.Method is not (LinkMethod.Aligner or LinkMethod.RuleBased))
                .SelectMany(l => l.From).Distinct().Count();
            if (words >= Admission.FewestToMeasure)
            {
                proxies.Add((id, fromText, words));
            }
        }

        return [.. proxies.OrderByDescending(p => p.Words).ThenBy(p => p.Slug, StringComparer.Ordinal).Select(p => (p.Id, p.Slug))];
    }

    /// <summary>
    /// A composition's answers as the pair's <c>composed</c> method file, replacing what it held: the fresh pairs and
    /// the agreements with a stated pair alike, since an agreement is a row of the stated link's shape and the merge
    /// folds it into that link as a claim. The pair's verse hashes gain the verses the rows cite where a text's
    /// manifest gives them.
    /// </summary>
    public long WriteComposed(string fromSlug, string toSlug, IEnumerable<(long From, long To, double Confidence, string Source, string? Evidence)> rows,
        IReadOnlyList<string> readFrom)
    {
        var pair = MappingProducts.Pair(fromSlug, toSlug);
        var written = rows
            .Select(r => new MappingRow(keys.Own(r.From), keys.Own(r.To), "renders", MappingMethods.Composed, r.Confidence, r.Source, r.Evidence, null, null))
            .Select(r => (Sort: MappingText.SortKey(r.From, r.To, fromSlug, toSlug), Row: r))
            .ToList();
        written.Sort((a, b) => MappingExport.Order(a.Sort.Side, a.Sort.Key, a.Row, b.Sort.Side, b.Sort.Key, b.Row));
        var inputs = readFrom.Select(store.Find).OfType<string>().Select(store.Input).ToList();
        using var writer = store.Create(MappingProducts.Of(pair, MappingMethods.Composed), ProductKind.Machine, MappingRow.Schema,
            ProductHeader.DescribeProducer($"forge compose-file {fromSlug} {toSlug}"), inputs);
        writer.WriteAll(written.Select(w => w.Row));
        MappingFiles.RecordHashes(store, pair, written.Select(w => w.Row));
        return written.Count;
    }

    private long[]? Ids(string own, int textId)
    {
        if (own.Length == 0)
        {
            return [];
        }

        var text = keys.Slug(textId);
        var parts = own.Split(' ');
        var ids = new long[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (keys.Id(textId, WordKey.Parse(parts[i], text)) is not { } id)
            {
                return null;
            }

            ids[i] = id;
        }

        return ids;
    }
}
