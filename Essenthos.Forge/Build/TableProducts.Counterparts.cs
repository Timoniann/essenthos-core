using Essenthos.Core.Conversion;
using Essenthos.Core.Glaux;
using Essenthos.Core.Indexes;
using Essenthos.Core.Lexicons;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.Strong;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Build;

/// <summary>
/// The Greek words that stand for each Hebrew word and back (<c>strong_counterpart</c>): counted from the settled links of
/// Brenton's Greek (the Septuagint) to BHSA, a Greek word numbered by the Strong entry GLAUx's lemma reaches as
/// <see cref="SeptuagintStrongs"/> reaches it, and from the derivations Strong's Greek dictionary states
/// (<see cref="StrongCounterparts"/>).
/// </summary>
internal static partial class TableProducts
{
    /// <summary>The pair of the Septuagint and the Hebrew whose links are counted.</summary>
    private static readonly string SeptuagintPair = MappingProducts.Pair(Brenton, Bhsa);

    private static readonly Table[] Counterparts =
    [
        new("strong_counterpart",
            ["hebrew", "greek", "septuagint", "greek_words", "hebrew_linked", "greek_linked", "stated", "dictionary", "statement"],
            [StrongHebrew, StrongGreek],
            [
                TextWord.ProductOf(Brenton), TextWord.ProductOf(Bhsa), TextWord.ProductOf(Nestle),
                MappingProducts.Of(SeptuagintPair, MappingProducts.Final),
            ],
            CounterpartRows),
    ];

    private static Rows CounterpartRows(ProductStore store)
    {
        var entries = StrongByNumber(store);
        var lemmas = GreekLemmas(entries);
        var greek = new Dictionary<string, string>(StringComparer.Ordinal);
        int numbered = 0, divided = 0;
        foreach (var word in WordsOf(store, Brenton).Where(w => w.Lemma is not null))
        {
            var (numbers, _) = SeptuagintStrongs.Numbers(lemmas, GreekLetters.Bare(word.Lemma!));
            if (numbers.Count == 1)
            {
                greek[word.Key.Own] = numbers[0];
                numbered++;
            }
            else if (numbers.Count > 1)
            {
                divided++;
            }
        }

        var hebrew = new Dictionary<string, string>(StringComparer.Ordinal);
        var parts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var slug in new[] { Bhsa, Nestle })
        {
            foreach (var word in WordsOf(store, slug).Where(w => w.Strong is not null))
            {
                if (slug == Bhsa)
                {
                    hebrew[word.Key.Own] = word.Strong!;
                }

                var part = OriginalWords.Part(word.Morphology) ?? string.Empty;
                if (!parts.TryGetValue(word.Strong!, out var tally))
                {
                    parts[word.Strong!] = tally = new Dictionary<string, int>(StringComparer.Ordinal);
                }

                tally[part] = tally.GetValueOrDefault(part) + 1;
            }
        }

        var content = parts.ToDictionary(
            p => p.Key,
            p => OriginalWords.Of(p.Key, p.Value.MaxBy(c => c.Value).Key, new HashSet<string>()) == WordClass.Content,
            StringComparer.Ordinal);

        var links = new List<StrongCounterparts.Link>();
        using (var reader = store.Open(MappingProducts.Of(SeptuagintPair, MappingProducts.Final), FinalRow.Schema))
        {
            foreach (var row in reader.Rows().Where(row => row.Settled && row.Relation is "renders" or "equals"))
            {
                links.Add(new StrongCounterparts.Link([.. MappingText.Split(row.From)], [.. MappingText.Split(row.To)], row.Method == "stated"));
            }
        }

        var relations = StrongRelationReading.Of(entries)
            .Where(r => r.ToNumber is not null)
            .Select(r => new StrongCounterparts.Relation(r.FromNumber, r.ToNumber!, r.Kind, r.Statement));
        var counted = StrongCounterparts.Count(links, greek, hebrew, number => content.GetValueOrDefault(number, true), relations);
        return new(
            counted.Select(c => new[]
            {
                c.Hebrew, c.Greek, Int(c.Septuagint), Int(c.GreekWords), Int(c.HebrewLinked), Int(c.GreekLinked), Int(c.Stated), c.Dictionary,
                c.Statement,
            }),
            $"{links.Count} settled links of {SeptuagintPair}; {numbered} Greek words numbered through their lemma, {divided} left out " +
            $"because their lemma reaches several entries; kept from {StrongCounterparts.Least} words and {StrongCounterparts.Share:P0} " +
            $"of either word's pairings, or {StrongCounterparts.Always} words");
    }

    /// <summary>Strong's Greek entries by their folded lemma: the numbers each lemma names, in the dictionary's order.</summary>
    private static Dictionary<string, List<string>> GreekLemmas(IEnumerable<StrongParsedEntry> entries)
    {
        var lemmas = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => Optional(e.Lemma) is not null && e.StrongNumber.StartsWith(StrongNumbers.Greek)))
        {
            var folded = GreekLetters.Bare(entry.Lemma!);
            if (folded.Length == 0)
            {
                continue;
            }

            if (!lemmas.TryGetValue(folded, out var numbers))
            {
                lemmas[folded] = numbers = [];
            }

            if (!numbers.Contains(entry.StrongNumber, StringComparer.Ordinal))
            {
                numbers.Add(entry.StrongNumber);
            }
        }

        return lemmas;
    }
}
