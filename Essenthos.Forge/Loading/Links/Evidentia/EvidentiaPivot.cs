namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// A text of the same language whose own links to the original are stated, read for the passage: its
/// words, and the original words its stated or Strong-numbered links put each of them on.
/// </summary>
internal sealed record EvidentiaPivotText(
    string Slug,
    IReadOnlyList<EvidentiaAnalysis> Words,
    IReadOnlyDictionary<long, HashSet<long>> LinkedTo);

/// <summary>
/// What other texts of the same language say each word of the verse renders. A content word of the
/// measured text and a content word of a pivot text are read as one rendering when they share a lemma and
/// the verse writes that lemma as often in both, the first with the first: the pivot's word then names the
/// original word it stands on, which is also which occurrence of a repeated lexeme the measured word is.
/// Matching is by lemma inside one verse, so it carries a pivot's stated link and never a guess of its own.
/// </summary>
internal sealed class EvidentiaPivot
{
    public static readonly EvidentiaPivot None = new([]);

    private static readonly IReadOnlyDictionary<string, HashSet<long>> Nothing = new Dictionary<string, HashSet<long>>();

    private readonly Dictionary<long, Dictionary<string, HashSet<long>>> bySource;

    private EvidentiaPivot(Dictionary<long, Dictionary<string, HashSet<long>>> bySource) => this.bySource = bySource;

    public bool IsEmpty => bySource.Count == 0;

    /// <summary>The original words each pivot text puts the word on, by text; empty where no pivot word matches it.</summary>
    public IReadOnlyDictionary<string, HashSet<long>> Of(long source) =>
        bySource.TryGetValue(source, out var texts) ? texts : Nothing;

    /// <summary>Every original word some pivot text puts the word on.</summary>
    public IReadOnlySet<long> Named(long source) => Of(source).Values.SelectMany(targets => targets).ToHashSet();

    /// <summary>How many pivot texts put the word on this original word.</summary>
    public int Naming(long source, long target) => Of(source).Values.Count(targets => targets.Contains(target));

    public bool Agrees(long source, long target) => Naming(source, target) > 0;

    /// <summary>Some pivot text names the word, and none of them names this original word.</summary>
    public bool Contradicts(long source, long target) => Of(source).Count > 0 && !Agrees(source, target);

    public static EvidentiaPivot Match(IReadOnlyList<EvidentiaAnalysis> source, IEnumerable<EvidentiaPivotText> texts)
    {
        var bySource = new Dictionary<long, Dictionary<string, HashSet<long>>>();
        var sourceByVerse = Keyed(source);
        foreach (var text in texts)
        {
            var pivotByVerse = Keyed(text.Words);
            foreach (var ((address, key), words) in sourceByVerse)
            {
                if (!pivotByVerse.TryGetValue((address, key), out var pivotWords) || pivotWords.Count != words.Count)
                {
                    continue;
                }

                foreach (var (word, pivotWord) in words.Zip(pivotWords))
                {
                    if (!text.LinkedTo.TryGetValue(pivotWord.Token.Id, out var targets) || targets.Count == 0)
                    {
                        continue;
                    }

                    if (!bySource.TryGetValue(word.Token.Id, out var byText))
                    {
                        bySource[word.Token.Id] = byText = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
                    }

                    byText[text.Slug] = [.. targets];
                }
            }
        }

        return new EvidentiaPivot(bySource);
    }

    /// <summary>The content words of each verse by the key they are matched on, in the order the verse writes them.</summary>
    private static Dictionary<(EvidentiaAddress Address, string Key), List<EvidentiaAnalysis>> Keyed(
        IEnumerable<EvidentiaAnalysis> words)
    {
        var keyed = new Dictionary<(EvidentiaAddress, string), List<EvidentiaAnalysis>>();
        foreach (var word in words.DistinctBy(word => word.Token.Id).OrderBy(word => word.Token.Address.Verse).ThenBy(word => word.Token.Position))
        {
            if (Key(word) is not { } key)
            {
                continue;
            }

            if (!keyed.TryGetValue((word.Token.Address, key), out var list))
            {
                keyed[(word.Token.Address, key)] = list = [];
            }

            list.Add(word);
        }

        return keyed;
    }

    /// <summary>The lemma where the language pack gives one, else the normalised form; nothing for a function word.</summary>
    private static string? Key(EvidentiaAnalysis word) =>
        !word.IsContentWord ? null
        : word.Capabilities.HasFlag(LanguagePackCapability.Lemma) && word.Lemma is { Length: > 0 } lemma ? lemma.ToLowerInvariant()
        : word.Normalised;
}
