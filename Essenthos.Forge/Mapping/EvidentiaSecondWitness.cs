using System.Globalization;
using System.Text;
using Essenthos.Core.Door43;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The aligner's links read beside two witnesses that do not use them, from files: a stored EVIDENTIA run's decisions (a file of
/// them, <see cref="Decision"/>) and every name the names-first pass places (<see cref="NamesFirstSource.All"/>). For each word
/// the aligner links it says whether the witness agrees, disagrees or is silent, and where the pair states a hand alignment
/// (<see cref="HandAlignment"/>), who is right. Without decisions it measures the aligner alone: its precision on the hand
/// alignment, by the original word's part of speech, and on the Strong layer's own links where the pair has them (a silver key).
///
/// <para>
/// The aligner's links are its method files as written (<see cref="MappingMethods.Composed"/>, <see cref="MappingMethods.Aligner"/>):
/// in a verse the hand alignment covers the merge withdraws the ones it contradicts, so the final would read the aligner as
/// better than it is. Outside those verses the final's links whose own claim is the aligner's are what a reader sees, and the
/// witness is read against them.
/// </para>
/// </summary>
internal sealed class EvidentiaSecondWitness(ProductStore store, Action<string> log)
{
    private static readonly HashSet<string> AlignerMethods = new(StringComparer.Ordinal) { MappingMethods.Composed, MappingMethods.Aligner };

    private static readonly string[] Classes = ["name", "noun", "verb", "adjective", "adverb", "pronoun", "preposition", "conjunction", "article", "other"];

    private static readonly HashSet<string> Content = new(StringComparer.Ordinal) { "name", "noun", "verb", "adjective", "adverb" };

    /// <summary>A line of a decisions file: a stored run's placement of one translation word, by the words' keys.</summary>
    public sealed record Decision(string Source, string Target, string Tier, string Kind, bool Content);

    public static IReadOnlyList<Decision> ReadDecisions(string path) =>
        [.. File.ReadLines(path).Where(line => line.Length > 0 && !line.StartsWith("source\t", StringComparison.Ordinal))
            .Select(line => line.Split('\t'))
            .Select(fields => new Decision(fields[0], fields[1], fields[2], fields[3], fields[4] is "t" or "true"))];

    public string Run(string pair, IReadOnlySet<int> books, IReadOnlyList<Decision>? decisions, string? outFolder)
    {
        var (fromSlug, toSlug) = MappingProducts.Split(pair);
        var texts = new ProductTexts(store);
        var translation = texts[fromSlug];
        var original = texts[toSlug];
        var translationIds = PairLinkFiles.Ids(translation);
        var originalIds = PairLinkFiles.Ids(original);
        bool InBooks(int word) => books.Contains(translation.Words[word].Key.Verse.Book);

        var raw = new Dictionary<int, SortedSet<int>>();
        var strong = new Dictionary<int, SortedSet<int>>();
        var others = new HashSet<int>();
        foreach (var method in MappingProducts.Methods(store, pair).Where(method => method is not (MappingMethods.Stated or PairSources.Laid or MappingMethods.Evidentia)))
        {
            using var reader = store.Open(MappingProducts.Of(pair, method), MappingRow.Schema);
            foreach (var row in reader.Rows().Where(row => row.Relation is "renders" or "equals"))
            {
                var from = PairLinkFiles.Words(row.From, translationIds).Where(InBooks).ToList();
                var to = PairLinkFiles.Words(row.To, originalIds);
                Add(AlignerMethods.Contains(method) ? raw : method == MappingMethods.Strong ? strong : null, from, to);
                if (!AlignerMethods.Contains(method))
                {
                    others.UnionWith(from);
                }
            }
        }

        var live = new Dictionary<int, SortedSet<int>>();
        var linked = new HashSet<int>();
        using (var reader = store.Open(MappingProducts.Of(pair, MappingProducts.Final), FinalRow.Schema))
        {
            foreach (var row in reader.Rows().Where(row => row.Relation is "renders" or "equals"))
            {
                var from = PairLinkFiles.Words(row.From, translationIds).Where(InBooks).ToList();
                linked.UnionWith(from);
                if (AlignerMethods.Contains(row.Method))
                {
                    Add(live, from, PairLinkFiles.Words(row.To, originalIds));
                }
            }
        }

        var gold = new Dictionary<int, SortedSet<int>>();
        var stated = new HashSet<VerseKey>();
        var interlinear = InterlinearLinkLoader.Interlinear(fromSlug).Source;
        foreach (var source in Door43BookAlignment.Of(store, fromSlug).Select(book => book.Source).Prepend(interlinear))
        {
            foreach (var link in HandAlignment.Read(store, pair, source))
            {
                var from = link.From.Select(word => translationIds.TryGetValue(word.Own, out var id) ? id : -1).Where(id => id >= 0 && InBooks(id)).ToList();
                stated.UnionWith(from.Select(word => translation.Words[word].Key.Verse));
                if (!link.Absence)
                {
                    Add(gold, from, [.. link.To.Select(word => originalIds.TryGetValue(word.Own, out var id) ? id : -1).Where(id => id >= 0)]);
                }
            }
        }

        bool Stated(int word) => stated.Contains(translation.Words[word].Key.Verse);
        string ClassOf(SortedSet<int> targets) => Class(EvidentiaWitness.OriginalToken(original, targets.Min));
        string Describe(SortedSet<int>? targets) => targets is null ? "" : string.Join(" ", targets.Select(id =>
            $"{original.Words[id].Surface}({original.Words[id].Strong} {original.Words[id].Gloss} {original.Words[id].Key.Own})"));
        string Line(int word, SortedSet<int>? aligner, SortedSet<int> witness, string tier)
        {
            var key = translation.Words[word].Key;
            var context = string.Join(" ", translation.InVerse(key.Verse).Select(id => id == word ? $"[{translation.Words[id].Surface}]" : translation.Words[id].Surface));
            return string.Join('\t', key.Own, context, translation.Words[word].Surface, Describe(aligner), Describe(witness), tier, "");
        }


        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"{pair} books {Books(books)}: aligner rows on {raw.Count:N0} words ({live.Count:N0} live in the final); hand alignment on {gold.Count:N0} words in {stated.Count:N0} verses; Strong rows on {strong.Count:N0} words");

        // The aligner alone, scored per (word, original word) pair: right where the key has the pair, wrong where the key links
        // the word elsewhere, unaligned where a word the key covers has no link there.
        void Precision(string title, Dictionary<int, SortedSet<int>> key, Func<int, bool> scored)
        {
            var tally = Classes.ToDictionary(name => name, _ => new int[3]);
            foreach (var (word, targets) in raw.Where(entry => scored(entry.Key)))
            {
                var cell = tally[ClassOf(targets)];
                foreach (var target in targets)
                {
                    cell[!key.TryGetValue(word, out var answer) ? 2 : answer.Contains(target) ? 0 : 1]++;
                }
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"  aligner against {title} (pairs right / wrong / on a word the key leaves unaligned; precision of the aligned):");
            foreach (var name in Classes.Where(name => tally[name].Sum() > 0))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    {name}: {Cells(tally[name])}");
            }

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    content (name, noun, verb, adjective, adverb): {Cells(Sum(Content.Select(name => tally[name])))}; all: {Cells(Sum(tally.Values))}");
        }

        if (gold.Count > 0)
        {
            Precision("the hand alignment, in the verses it covers", gold, word => Stated(word));
        }

        if (strong.Count > 0)
        {
            Precision("the Strong layer's own links, on the words it links (silver)", strong, word => strong.ContainsKey(word));
        }

        var livePairs = Classes.ToDictionary(name => name, _ => 0);
        foreach (var (_, targets) in live)
        {
            livePairs[ClassOf(targets)] += targets.Count;
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"  aligner pairs live in the final, by class: {string.Join(", ", Classes.Where(name => livePairs[name] > 0).Select(name => $"{name} {livePairs[name]:N0}"))}");

        var names = NamesFirstSource.All(texts, pair).Where(placing => InBooks((int)placing.Source))
            .GroupBy(placing => (int)placing.Source).ToDictionary(group => group.Key, group => new SortedSet<int>(group.Select(placing => (int)placing.Target)));
        Witness("names-first", names.ToDictionary(entry => entry.Key, entry => (entry.Value, "names")));

        if (decisions is not null)
        {
            var placed = new Dictionary<int, (SortedSet<int> Targets, string Tier)>();
            var content = new HashSet<int>();
            foreach (var decision in decisions)
            {
                if (!translationIds.TryGetValue(decision.Source, out var word) || !InBooks(word))
                {
                    continue;
                }

                if (decision.Content)
                {
                    content.Add(word);
                }

                // A decision with no target is the run's abstention: the word is asked, and the witness is silent on it.
                if (!originalIds.TryGetValue(decision.Target, out var target))
                {
                    continue;
                }

                if (!placed.TryGetValue(word, out var entry))
                {
                    placed[word] = entry = ([], decision.Tier);
                }

                entry.Targets.Add(target);
            }

            Witness("EVIDENTIA (content words)", placed.Where(entry => content.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value),
                content);
            Witness("EVIDENTIA (other words)", placed.Where(entry => !content.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value),
                excludeContent: content);
        }

        return report.ToString();

        // agree / disagree / silent on the words the aligner links, by the original's class and the witness's tier; in stated
        // verses who the key says is right; and the witness's own placements where the final links nothing.
        void Witness(string name, Dictionary<int, (SortedSet<int> Targets, string Tier)> witness, IReadOnlySet<int>? only = null, IReadOnlySet<int>? excludeContent = null)
        {
            bool Asked(int word) => (only is null || only.Contains(word)) && (excludeContent is null || !excludeContent.Contains(word));
            var byClass = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            var byTier = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            var judged = new Dictionary<string, int>(StringComparer.Ordinal);
            var disagreements = new List<int>();
            foreach (var word in raw.Keys.Union(live.Keys).Where(Asked))
            {
                var stated = Stated(word);
                var aligner = stated ? raw.GetValueOrDefault(word) : live.GetValueOrDefault(word);
                if (aligner is null)
                {
                    continue;
                }

                var said = witness.TryGetValue(word, out var entry) ? entry.Targets : null;
                var verdict = said is null ? 2 : said.Overlaps(aligner) ? 0 : 1;
                var cls = (stated ? "stated " : "") + ClassOf(aligner);
                (byClass.TryGetValue(cls, out var cell) ? cell : byClass[cls] = new int[3])[verdict]++;
                if (said is not null)
                {
                    (byTier.TryGetValue(entry.Tier, out var tierCell) ? tierCell : byTier[entry.Tier] = new int[3])[verdict]++;
                }

                if (stated && gold.TryGetValue(word, out var answer))
                {
                    var outcome = (verdict switch { 0 => "agree", 1 => "disagree", _ => "silent" })
                        + ": aligner " + (aligner.Overlaps(answer) ? "right" : "wrong")
                        + (said is null ? "" : ", witness " + (said.Overlaps(answer) ? "right" : "wrong"));
                    judged[outcome] = judged.GetValueOrDefault(outcome) + 1;
                }
                else if (!stated && verdict == 1)
                {
                    disagreements.Add(word);
                }
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"  {name} on the words the aligner links (agree / disagree / silent), by the aligner's original word:");
            foreach (var (cls, cell) in byClass)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    {cls}: {cell[0]:N0} / {cell[1]:N0} / {cell[2]:N0}");
            }

            report.AppendLine(CultureInfo.InvariantCulture, $"    by the witness's tier: {string.Join("; ", byTier.Select(entry => $"{entry.Key} {entry.Value[0]:N0}/{entry.Value[1]:N0}"))}");
            report.AppendLine(CultureInfo.InvariantCulture, $"    in stated verses, by the key: {string.Join("; ", judged.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => $"{entry.Key} {entry.Value:N0}"))}");

            // New: the witness places a word no link of the final reaches (in a stated verse: no method but the stated).
            var fresh = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
            var freshLines = new List<string>();
            foreach (var (word, entry) in witness.Where(entry => Asked(entry.Key)))
            {
                var stated = Stated(word);
                if (stated ? raw.ContainsKey(word) || others.Contains(word) : linked.Contains(word))
                {
                    continue;
                }

                var cell = fresh.TryGetValue(entry.Tier, out var found) ? found : fresh[entry.Tier] = new int[4];
                if (!stated)
                {
                    cell[3]++;
                    freshLines.Add(Line(word, null, entry.Targets, entry.Tier));
                }
                else
                {
                    cell[!gold.TryGetValue(word, out var answer) ? 2 : entry.Targets.Overlaps(answer) ? 0 : 1]++;
                }
            }

            report.AppendLine(CultureInfo.InvariantCulture,
                $"    new placements, on words no link reaches (unstated verses; stated verses right / wrong / unaligned by the key): {string.Join("; ", fresh.Select(entry => $"{entry.Key} {entry.Value[3]:N0} ({entry.Value[0]}/{entry.Value[1]}/{entry.Value[2]})"))}");
            if (outFolder is not null)
            {
                Directory.CreateDirectory(outFolder);
                var file = name.Split(' ')[0].ToLowerInvariant();
                var random = new Random(1910);
                File.WriteAllLines(Path.Combine(outFolder, $"{pair}-{file}{(only is null && excludeContent is not null ? "-other" : "")}-disagreements.tsv"),
                    ["verse\tcontext\tword\taligner\twitness\ttier\tverdict", .. disagreements.OrderBy(_ => random.Next()).Select(word => Line(word, live[word], witness[word].Targets, witness[word].Tier))]);
                File.WriteAllLines(Path.Combine(outFolder, $"{pair}-{file}{(only is null && excludeContent is not null ? "-other" : "")}-new.tsv"),
                    ["verse\tcontext\tword\taligner\twitness\ttier\tverdict", .. freshLines.OrderBy(_ => random.Next())]);
            }
        }
    }

    /// <summary>The original word's class, from its part of speech; a Greek noun with a capital lemma is a name.</summary>
    internal static string Class(EvidentiaToken token) => token.PartOfSpeech switch
    {
        "nmpr" => "name",
        "noun" when token.Lemma is { Length: > 0 } lemma && char.IsUpper(lemma[0]) => "name",
        "subs" or "noun" => "noun",
        "verb" => "verb",
        "adjv" or "adj" or "adjective" => "adjective",
        "advb" or "adv" or "adverb" => "adverb",
        "prps" or "prde" or "prin" or "pron" or "pronoun" => "pronoun",
        "prep" or "preposition" => "preposition",
        "conj" or "conjunction" => "conjunction",
        "art" or "article" or "det" => "article",
        _ => "other",
    };

    private static string Books(IReadOnlySet<int> books) => books.Count == 0 ? "-" : $"{books.Min()}-{books.Max()}";

    private static int[] Sum(IEnumerable<int[]> cells)
    {
        var sum = new int[3];
        foreach (var cell in cells)
        {
            for (var index = 0; index < 3; index++)
            {
                sum[index] += cell[index];
            }
        }

        return sum;
    }

    private static string Cells(int[] cell) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{cell[0]:N0} / {cell[1]:N0} / {cell[2]:N0} = {(cell[0] + cell[1] == 0 ? "-" : (100.0 * cell[0] / (cell[0] + cell[1])).ToString("0.0", CultureInfo.InvariantCulture) + "%")}");

    private static void Add(Dictionary<int, SortedSet<int>>? links, List<int> from, List<int> to)
    {
        if (links is null || to.Count == 0)
        {
            return;
        }

        foreach (var word in from)
        {
            if (!links.TryGetValue(word, out var targets))
            {
                links[word] = targets = [];
            }

            targets.UnionWith(to);
        }
    }
}
