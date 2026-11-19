using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// EVIDENTIA as a second witness over a pair's links, read from files: the words the pair's links leave unplaced are given to
/// the per-language attached-word rules (<see cref="IEvidentiaSlavicWords"/>), with the pair's other links as their heads, and
/// every placement is scored against the hand alignment the pair states. In a verse the hand alignment covers, the heads are
/// the links of every method but the stated ones, so a verse is measured as an unstated verse sees it.
///
/// <para>
/// The stated verses are split once into a part rules are chosen on and a sealed part they are only measured on, by a hash of
/// the verse's key (<see cref="Held"/>); Luke's verses, whose alignment is unchecked personal work (PRB-1907), are in neither
/// and reported apart.
/// </para>
/// </summary>
internal sealed class EvidentiaWitness(ProductStore store, UdpipeAnnotator annotator, Action<string> log)
{
    /// <summary>The share of stated verses in the sealed part, in tenths.</summary>
    private const int HeldTenths = 3;

    private const string Salt = "t1910:";

    private const int Luke = 42;

    /// <summary>The method files that are a verse's hand alignment, never its heads.</summary>
    private static readonly HashSet<string> StatedMethods = new(StringComparer.Ordinal)
    {
        MappingMethods.Stated, PairSources.Laid, MappingMethods.Evidentia,
    };

    public enum Part
    {
        Unstated,
        Dev,
        Held,
        Luke,
    }

    /// <summary>Whether a stated verse is in the sealed part: the first byte of SHA-256 over the salt and its key, in tenths.</summary>
    public static bool Held(VerseKey verse) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Salt + verse))[0] % 10 < HeldTenths;

    public sealed record Placement(int Word, int Original, string Rule, int Head);

    public sealed record Outcome(string Rule, Part Part, string Verdict);

    public async Task<string> Run(
        string pair,
        IEvidentiaSlavicWords language,
        IReadOnlySet<int> books,
        bool unsealHeld,
        bool goldHeads,
        string? outFolder,
        int parallel,
        CancellationToken cancellationToken)
    {
        var (fromSlug, toSlug) = MappingProducts.Split(pair);
        var translation = ProductText.Read(store, fromSlug);
        var original = ProductText.Read(store, toSlug);
        var translationIds = PairLinkFiles.Ids(translation);
        var originalIds = PairLinkFiles.Ids(original);

        var heads = new Dictionary<int, SortedSet<int>>();
        var reaching = new HashSet<int>();
        var gold = new Dictionary<int, SortedSet<int>>();
        var goldVerses = new HashSet<VerseKey>();
        var goldSources = new Dictionary<string, int>(StringComparer.Ordinal);
        var methods = MappingProducts.Methods(store, pair);
        foreach (var method in methods)
        {
            using var reader = store.Open(MappingProducts.Of(pair, method), MappingRow.Schema);
            var stated = StatedMethods.Contains(method);
            foreach (var row in reader.Rows())
            {
                var from = PairLinkFiles.Words(row.From, translationIds);
                if (stated)
                {
                    if (method != MappingMethods.Stated)
                    {
                        continue;
                    }

                    goldSources[row.Source] = goldSources.GetValueOrDefault(row.Source) + 1;
                    goldVerses.UnionWith(from.Select(word => translation.Words[word].Key.Verse));
                    if (row.Relation is "renders" or "equals")
                    {
                        Add(gold, from, PairLinkFiles.Words(row.To, originalIds));
                    }

                    continue;
                }

                reaching.UnionWith(from);
                if (row.Relation is "renders" or "equals")
                {
                    Add(heads, from, PairLinkFiles.Words(row.To, originalIds));
                }
            }
        }

        log($"{pair}: heads from {string.Join(", ", methods.Where(method => !StatedMethods.Contains(method)))}; "
            + $"gold {gold.Count:N0} words in {goldVerses.Count:N0} verses from {string.Join("; ", goldSources.Select(source => $"{source.Key} {source.Value:N0}"))}");

        Part PartOf(VerseKey verse) => !goldVerses.Contains(verse) ? Part.Unstated
            : verse.Book == Luke ? Part.Luke
            : Held(verse) ? Part.Held : Part.Dev;

        var chapters = translation.Verses
            .Where(verse => books.Contains(verse.Book))
            .GroupBy(verse => (verse.Book, verse.Chapter))
            .Select(chapter => chapter.ToList())
            .ToList();
        var placements = new List<Placement>();
        var explore = new List<string>();
        var parsedWords = 0;
        var unplaced = new Dictionary<Part, int>();
        var words = new Dictionary<Part, int>();
        using var slots = new SemaphoreSlim(Math.Clamp(parallel, 1, UdpipeAnnotator.MaxProcesses));
        var tasks = chapters.Select(async chapter =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                var tokens = chapter.SelectMany(verse => translation.InVerse(verse)).Select(id => Token(translation, id, language.Language)).ToList();
                var annotation = await annotator.Annotate(tokens, cancellationToken);
                if (annotation.Status != UdpipeAnnotationStatus.Annotated)
                {
                    throw new InvalidOperationException($"{fromSlug} {chapter[0].Book}.{chapter[0].Chapter}: UDPipe {annotation.Status} {annotation.Detail}");
                }

                return (chapter, annotation.Tokens);
            }
            finally
            {
                slots.Release();
            }
        }).ToList();

        foreach (var task in tasks)
        {
            var (chapter, parsed) = await task;
            parsedWords += parsed.Count;
            var byId = parsed.ToDictionary(token => (int)token.Id);
            foreach (var verse in chapter)
            {
                var part = PartOf(verse);
                if (part != Part.Unstated && part == Part.Held && !unsealHeld)
                {
                    continue;
                }

                var verseWords = translation.InVerse(verse).Select(id => byId[id]).ToList();
                var verseHeads = goldHeads && part != Part.Unstated ? gold : heads;
                var originalVerses = verseWords.Where(word => verseHeads.ContainsKey((int)word.Id))
                    .SelectMany(word => verseHeads[(int)word.Id]).Select(id => original.Words[id].Key.Verse).Distinct().ToList();
                var originalWords = originalVerses.SelectMany(own => original.InVerse(own)).Distinct()
                    .Select(id => OriginalToken(original, id)).ToList();
                var placed = verseWords.Where(word => verseHeads.ContainsKey((int)word.Id))
                    .ToDictionary(word => word.Id, word => (IReadOnlyList<long>)[.. verseHeads[(int)word.Id].Select(id => (long)id)]);
                words[part] = words.GetValueOrDefault(part) + verseWords.Count;
                unplaced[part] = unplaced.GetValueOrDefault(part) + verseWords.Count(word => !reaching.Contains((int)word.Id));
                var verseUnplaced = verseWords.Where(word => !reaching.Contains((int)word.Id)).ToList();
                foreach (var proposal in EvidentiaSlavicAttachedWords.Place(language, verseWords, verseUnplaced, originalWords, placed))
                {
                    placements.Add(new Placement((int)proposal.Word, (int)proposal.Original, proposal.Rule, (int)proposal.Head));
                }

                if (part == Part.Dev && outFolder is not null)
                {
                    foreach (var word in verseUnplaced)
                    {
                        explore.Add(Explore(translation, original, word, verseWords, heads, gold));
                    }
                }
            }
        }

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"{pair} books {string.Join(",", books.Order())}: {parsedWords:N0} words parsed (UDPipe {language.Language}); heads in stated verses: {(goldHeads ? "the hand alignment's other words (precision given a right head)" : "the pair's other method files")}");
        foreach (var part in words.Keys.Order())
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {part}: {words[part]:N0} words, {unplaced[part]:N0} reached by no link but the stated ({Share(unplaced[part], words[part])})");
        }

        var outcomes = placements.Select(placement => new Outcome(placement.Rule, PartOf(translation.Words[placement.Word].Key.Verse),
            Verdict(placement, gold))).ToList();
        report.AppendLine("  by rule (unstated: words it would add; dev/held/luke: right / wrong / on a word the hand alignment leaves unaligned):");
        foreach (var rule in outcomes.GroupBy(outcome => outcome.Rule).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"    {rule.Key}: unstated {rule.Count(outcome => outcome.Part == Part.Unstated):N0}"));
            foreach (var part in new[] { Part.Dev, Part.Held, Part.Luke })
            {
                var scored = rule.Where(outcome => outcome.Part == part).ToList();
                if (scored.Count == 0)
                {
                    continue;
                }

                var right = scored.Count(outcome => outcome.Verdict == "right");
                var wrong = scored.Count(outcome => outcome.Verdict == "wrong");
                var unaligned = scored.Count(outcome => outcome.Verdict == "unaligned");
                line.Append(CultureInfo.InvariantCulture,
                    $"; {part.ToString().ToLowerInvariant()} {right}/{wrong}/{unaligned} = {Share(right, right + wrong)} of aligned, {Share(right, scored.Count)} strict");
            }

            report.AppendLine(line.ToString());
        }

        if (outFolder is not null)
        {
            Directory.CreateDirectory(outFolder);
            await File.WriteAllLinesAsync(Path.Combine(outFolder, $"{pair}-unplaced-dev.tsv"),
                ["verse\tword\tupos\trel\tfeats\thead\thead_original\tgold\tgold_is_head", .. explore], cancellationToken);
            var random = new Random(1910);
            await File.WriteAllLinesAsync(Path.Combine(outFolder, $"{pair}-placements.tsv"),
                ["rule\tpart\tverdict\tverse\tcontext\tword\thead\toriginal\toriginal_parse\tgold",
                 .. placements.Zip(outcomes).Where(pair2 => pair2.Second.Part != Part.Held)
                     .OrderBy(_ => random.Next())
                     .Select(pair2 => Line(translation, original, pair2.First, pair2.Second, gold))],
                cancellationToken);
            if (unsealHeld)
            {
                await File.WriteAllLinesAsync(Path.Combine(outFolder, $"{pair}-placements-held.tsv"),
                    ["rule	part	verdict	verse	context	word	head	original	original_parse	gold",
                     .. placements.Zip(outcomes).Where(pair2 => pair2.Second.Part == Part.Held)
                         .Select(pair2 => Line(translation, original, pair2.First, pair2.Second, gold))],
                    cancellationToken);
            }
        }

        return report.ToString();
    }

    private static string Verdict(Placement placement, Dictionary<int, SortedSet<int>> gold) =>
        !gold.TryGetValue(placement.Word, out var targets) ? "unaligned"
        : targets.Contains(placement.Original) ? "right" : "wrong";

    private static string Share(int part, int whole) =>
        whole == 0 ? "-" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static void Add(Dictionary<int, SortedSet<int>> links, List<int> from, List<int> to)
    {
        foreach (var word in from)
        {
            if (!links.TryGetValue(word, out var targets))
            {
                links[word] = targets = [];
            }

            targets.UnionWith(to);
        }
    }

    private static EvidentiaToken Token(ProductText text, int id, string language)
    {
        var word = text.Words[id];
        return new EvidentiaToken(id, Address(word.Key.Verse), word.Key.Position, word.Surface, language, word.Trailer);
    }

    private static EvidentiaAddress Address(VerseKey verse) => new(verse.Book, verse.Chapter, verse.Verse);

    /// <summary>An original word with its morphology as the products hold it, every value as text.</summary>
    internal static EvidentiaToken OriginalToken(ProductText text, int id)
    {
        var word = text.Words[id];
        var morphology = new Dictionary<string, string>(StringComparer.Ordinal);
        if (word.Morphology is { } json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    {
                        morphology[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                    }
                }
            }
        }

        var language = morphology.GetValueOrDefault("language") ?? (word.Strong?.StartsWith('H') == true ? "hbo" : "grc");
        return new EvidentiaToken(id, Address(word.Key.Verse), word.Key.Position, word.Surface, language, word.Trailer,
            word.Lemma, word.Strong, word.Gloss, morphology.GetValueOrDefault("pos"), morphology);
    }

    private static string Describe(ProductText original, int id)
    {
        var word = original.Words[id];
        var form = word.Morphology is { } json && JsonDocument.Parse(json).RootElement is { ValueKind: JsonValueKind.Object } root
            ? root.TryGetProperty("form", out var f) ? f.GetString() : root.TryGetProperty("pos", out var p) ? p.GetString() : null
            : null;
        return $"{word.Surface}({word.Strong} {form} {word.Key.Own})";
    }

    private static string Explore(
        ProductText translation,
        ProductText original,
        EvidentiaToken word,
        List<EvidentiaToken> verse,
        Dictionary<int, SortedSet<int>> heads,
        Dictionary<int, SortedSet<int>> gold)
    {
        var head = verse.FirstOrDefault(other => other.Id == word.SyntacticHead);
        var headOriginal = head is not null && heads.TryGetValue((int)head.Id, out var targets)
            ? string.Join(" ", targets.Select(id => Describe(original, id)))
            : "";
        var goldTargets = gold.TryGetValue((int)word.Id, out var answer) ? answer : null;
        var goldIsHead = goldTargets is null ? "unaligned"
            : head is not null && heads.TryGetValue((int)head.Id, out var headTargets) && headTargets.Overlaps(goldTargets) ? "head"
            : "other";
        var feats = word.Morphology is null ? "" : string.Join("|", word.Morphology.Select(feature => $"{feature.Key}={feature.Value}"));
        return string.Join('\t', translation.Words[(int)word.Id].Key.Own, word.Surface.ToLowerInvariant(), word.PartOfSpeech, word.Relation, feats,
            head?.Surface ?? "", headOriginal, goldTargets is null ? "" : string.Join(" ", goldTargets.Select(id => Describe(original, id))), goldIsHead);
    }

    private static string Line(ProductText translation, ProductText original, Placement placement, Outcome outcome, Dictionary<int, SortedSet<int>> gold)
    {
        var key = translation.Words[placement.Word].Key;
        var context = string.Join(" ", translation.InVerse(key.Verse).Select(id => id == placement.Word ? $"[{translation.Words[id].Surface}]" : translation.Words[id].Surface));
        var originalVerse = original.Words[placement.Original].Key.Verse;
        var originalContext = string.Join(" ", original.InVerse(originalVerse).Select(id => id == placement.Original ? $"[{original.Words[id].Surface}]" : original.Words[id].Surface));
        return string.Join('\t', outcome.Rule, outcome.Part, outcome.Verdict, key.Own, context, translation.Words[placement.Word].Surface,
            translation.Words[placement.Head].Surface, Describe(original, placement.Original), originalContext,
            gold.TryGetValue(placement.Word, out var targets) ? string.Join(" ", targets.Select(id => Describe(original, id))) : "");
    }
}
