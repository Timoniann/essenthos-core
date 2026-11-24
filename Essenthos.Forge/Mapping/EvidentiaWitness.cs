using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Essenthos.Core.Door43;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// EVIDENTIA as a second witness over a pair's links, read from files: the words the pair's links leave unplaced are given to
/// the per-language attached-word rules (<see cref="IEvidentiaSlavicWords"/>), with the pair's other links as their heads, and
/// every placement is scored against a key (<see cref="Key"/>). In a verse the key covers, the heads are the links of every
/// method but the stated ones, so a verse is measured as an unstated verse sees it.
///
/// <para>
/// Each key's verses are split once into a part rules are chosen on and a sealed part they are only measured on
/// (<see cref="Part"/>, <see cref="Sealed"/>), fixed before any figure of it was read.
/// </para>
/// </summary>
internal sealed class EvidentiaWitness(ProductStore store, UdpipeAnnotator annotator, Action<string> log)
{
    /// <summary>The share of verses a split leaves to choosing rules, in tenths; the rest are sealed.</summary>
    private const int DevTenths = 3;

    private const string Salt = "t1910b:";

    private const int Luke = 42;

    /// <summary>The method files that are a verse's hand alignment, never its heads.</summary>
    private static readonly HashSet<string> StatedMethods = new(StringComparer.Ordinal)
    {
        MappingMethods.Stated, PairSources.Laid, MappingMethods.Evidentia,
    };

    /// <summary>
    /// The verses a placement is scored in. <see cref="Dev"/> and <see cref="Held"/> are the pair's interlinear: every verse
    /// stretch 1 of TSK-1910 read is in dev, and the sealed part is Luke 1:1-54, which no rule had seen and PRB-1907 ruled
    /// whole. <see cref="BooksDev"/> and <see cref="BooksHeld"/> are the Door43 book projects' verses the interlinear lacks
    /// (<see cref="Door43BookAlignment"/>), a second key outside the gold; <see cref="SilverDev"/> and <see cref="SilverHeld"/>
    /// are the verses where the Strong layer's own links of the listed small words are hidden and become the key.
    /// </summary>
    public enum Part
    {
        Unstated,
        Dev,
        Held,
        BooksDev,
        BooksHeld,
        SilverDev,
        SilverHeld,
    }

    /// <summary>What a placement is scored against.</summary>
    public enum Key
    {
        /// <summary>The pair's hand alignment as the merge settled it (<see cref="HandAlignment"/>).</summary>
        Stated,

        /// <summary>The Strong layer's links of the words the language's lists hold, hidden from the heads.</summary>
        Strong,
    }

    /// <summary>Whether a verse of a split is sealed: the first byte of SHA-256 over the salt and its key, in tenths.</summary>
    public static bool Sealed(VerseKey verse) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Salt + verse))[0] % 10 >= DevTenths;

    private static bool IsHeld(Part part) => part is Part.Held or Part.BooksHeld or Part.SilverHeld;

    public sealed record Placement(int Word, int Original, string Rule, int Head);

    public sealed record Outcome(string Rule, Part Part, string Verdict);

    public async Task<string> Run(
        string pair,
        IEvidentiaSlavicWords language,
        IReadOnlySet<int> books,
        Key key,
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
        var parts = new Dictionary<VerseKey, Part>();
        var goldSources = new Dictionary<string, int>(StringComparer.Ordinal);
        var silver = new HashSet<int>();
        var methods = MappingProducts.Methods(store, pair);
        foreach (var method in methods.Where(method => !StatedMethods.Contains(method)))
        {
            using var reader = store.Open(MappingProducts.Of(pair, method), MappingRow.Schema);
            foreach (var row in reader.Rows())
            {
                var from = PairLinkFiles.Words(row.From, translationIds);
                var renders = row.Relation is "renders" or "equals";
                if (key == Key.Strong && method == MappingMethods.Strong && renders)
                {
                    var listed = from.Where(word => language.Words.Rows.Any(entry => entry.Form == translation.Words[word].Surface.ToLowerInvariant())).ToList();
                    Add(gold, listed, PairLinkFiles.Words(row.To, originalIds));
                    silver.UnionWith(listed);
                }

                reaching.UnionWith(from);
                if (renders)
                {
                    Add(heads, from, PairLinkFiles.Words(row.To, originalIds));
                }
            }
        }

        if (key == Key.Strong)
        {
            reaching.ExceptWith(silver);
            foreach (var word in silver)
            {
                heads.Remove(word);
                parts[translation.Words[word].Key.Verse] = Sealed(translation.Words[word].Key.Verse) ? Part.SilverHeld : Part.SilverDev;
            }

            goldSources[$"{MappingMethods.Strong} links of the listed words"] = silver.Count;
        }
        else
        {
            var bookGold = new Dictionary<int, SortedSet<int>>();
            var interlinear = InterlinearLinkLoader.Interlinear(fromSlug).Source;
            foreach (var (source, isBook) in Door43BookAlignment.Of(store, fromSlug).Select(book => (book.Source, true)).Prepend((interlinear, false)))
            {
                foreach (var link in HandAlignment.Read(store, pair, source))
                {
                    var name = isBook ? "Door43 book projects" : interlinear;
                    goldSources[name] = goldSources.GetValueOrDefault(name) + 1;
                    var from = link.From.Select(word => translationIds.TryGetValue(word.Own, out var id) ? id : -1).Where(id => id >= 0).ToList();
                    foreach (var word in from)
                    {
                        var verse = translation.Words[word].Key.Verse;
                        parts[verse] = !isBook ? verse.Book == Luke ? Part.Held : Part.Dev
                            : parts.TryGetValue(verse, out var already) && already is Part.Dev or Part.Held ? already
                            : Sealed(verse) ? Part.BooksHeld : Part.BooksDev;
                    }

                    if (!link.Absence)
                    {
                        Add(isBook ? bookGold : gold, from, [.. link.To.Select(word => originalIds.TryGetValue(word.Own, out var id) ? id : -1).Where(id => id >= 0)]);
                    }
                }
            }

            var shared = bookGold.Keys.Where(gold.ContainsKey).ToList();
            log($"{pair}: the book projects and the interlinear both align {shared.Count:N0} words; the same original words for "
                + $"{shared.Count(word => gold[word].SetEquals(bookGold[word])):N0}, overlapping for {shared.Count(word => gold[word].Overlaps(bookGold[word])):N0}");
            foreach (var (word, targets) in bookGold)
            {
                if (parts[translation.Words[word].Key.Verse] is Part.BooksDev or Part.BooksHeld)
                {
                    gold[word] = targets;
                }
            }
        }

        log($"{pair}: heads from {string.Join(", ", methods.Where(method => !StatedMethods.Contains(method)))}; "
            + $"key {key}: {gold.Count:N0} words in {parts.Count:N0} verses from {string.Join("; ", goldSources.Select(source => $"{source.Key} {source.Value:N0}"))}");

        Part PartOf(VerseKey verse) => parts.GetValueOrDefault(verse, Part.Unstated);

        var chapters = translation.Verses
            .Where(verse => books.Contains(verse.Book))
            .GroupBy(verse => (verse.Book, verse.Chapter))
            .Select(chapter => chapter.ToList())
            .ToList();
        var placements = new List<Placement>();
        var refusals = new List<Placement>();
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
                if (IsHeld(part) && !unsealHeld)
                {
                    continue;
                }

                var verseWords = translation.InVerse(verse).Select(id => byId[id]).ToList();
                var verseHeads = goldHeads && key == Key.Stated && part != Part.Unstated ? gold : heads;
                var originalVerses = verseWords.Where(word => verseHeads.ContainsKey((int)word.Id))
                    .SelectMany(word => verseHeads[(int)word.Id]).Select(id => original.Words[id].Key.Verse).Distinct().ToList();
                var originalWords = originalVerses.SelectMany(own => original.InVerse(own)).Distinct()
                    .Select(id => OriginalToken(original, id)).ToList();
                var placed = verseWords.Where(word => verseHeads.ContainsKey((int)word.Id))
                    .ToDictionary(word => word.Id, word => (IReadOnlyList<long>)[.. verseHeads[(int)word.Id].Select(id => (long)id)]);
                words[part] = words.GetValueOrDefault(part) + verseWords.Count;
                unplaced[part] = unplaced.GetValueOrDefault(part) + verseWords.Count(word => !reaching.Contains((int)word.Id));
                var verseUnplaced = verseWords.Where(word => !reaching.Contains((int)word.Id)).ToList();
                foreach (var proposal in EvidentiaSlavicAttachedWords.Place(language, verseWords, verseUnplaced, originalWords, placed,
                    refusal => refusals.Add(new Placement((int)refusal.Word, (int)refusal.Original, refusal.Rule, (int)refusal.Head))))
                {
                    placements.Add(new Placement((int)proposal.Word, (int)proposal.Original, proposal.Rule, (int)proposal.Head));
                }

                if (part is Part.Dev or Part.BooksDev or Part.SilverDev && outFolder is not null)
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
        report.AppendLine("  by rule (unstated: words it would add; each scored part: right / wrong / on a word the hand alignment leaves unaligned):");
        foreach (var rule in outcomes.GroupBy(outcome => outcome.Rule).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"    {rule.Key}: unstated {rule.Count(outcome => outcome.Part == Part.Unstated):N0}"));
            foreach (var part in Enum.GetValues<Part>().Where(part => part != Part.Unstated))
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

        var refused = refusals.Select(placement => new Outcome(placement.Rule, PartOf(translation.Words[placement.Word].Key.Verse),
            Verdict(placement, gold))).ToList();
        foreach (var rule in refused.GroupBy(outcome => outcome.Rule).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var scored = rule.Where(outcome => outcome.Part != Part.Unstated).ToList();
            report.AppendLine(CultureInfo.InvariantCulture,
                $"    refused as another phrase's or sentence's: {rule.Key}: unstated {rule.Count(outcome => outcome.Part == Part.Unstated):N0}; "
                + $"scored {scored.Count(outcome => outcome.Verdict == "right")}/{scored.Count(outcome => outcome.Verdict == "wrong")}/{scored.Count(outcome => outcome.Verdict == "unaligned")} right/wrong/unaligned");
        }

        if (outFolder is not null)
        {
            Directory.CreateDirectory(outFolder);
            await File.WriteAllLinesAsync(Path.Combine(outFolder, $"{pair}-unplaced-dev.tsv"),
                ["verse\tword\tupos\trel\tfeats\thead\thead_original\tgold\tgold_is_head", .. explore], cancellationToken);
            var random = new Random(1910);
            await File.WriteAllLinesAsync(Path.Combine(outFolder, $"{pair}-placements.tsv"),
                ["rule\tpart\tverdict\tverse\tcontext\tword\thead\toriginal\toriginal_parse\tgold",
                 .. placements.Zip(outcomes).Where(pair2 => !IsHeld(pair2.Second.Part))
                     .OrderBy(_ => random.Next())
                     .Select(pair2 => Line(translation, original, pair2.First, pair2.Second, gold))],
                cancellationToken);
            await File.WriteAllLinesAsync(Path.Combine(outFolder, $"{pair}-refused.tsv"),
                ["rule	part	verdict	verse	context	word	head	original	original_parse	gold",
                 .. refusals.Zip(refused).Where(pair2 => !IsHeld(pair2.Second.Part))
                     .OrderBy(_ => random.Next())
                     .Select(pair2 => Line(translation, original, pair2.First, pair2.Second, gold))],
                cancellationToken);
            if (unsealHeld)
            {
                await File.WriteAllLinesAsync(Path.Combine(outFolder, $"{pair}-placements-held.tsv"),
                    ["rule	part	verdict	verse	context	word	head	original	original_parse	gold",
                     .. placements.Zip(outcomes).Where(pair2 => IsHeld(pair2.Second.Part))
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
