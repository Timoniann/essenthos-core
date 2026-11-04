using System.Runtime.CompilerServices;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Word = Essenthos.Core.Loading.Links.OldTestamentLinkLoader.Word;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The Open Hebrew Bible's King James mapping read against both texts' words, as the database's
/// <see cref="OldTestamentLinkLoader"/> reads it on a load with no links yet: each verse whose Hebrew joins BHSA by
/// position and glosses, else through the running word numbers, is lined up with the King James and drawn
/// (<see cref="OldTestamentLinkLoader.Build"/>). The King James's words are those standing at the mapping's verse and
/// BHSA's those standing at the same frame address, a psalm's title before its first verse, as the loader grouped them.
/// Read once per run and shared by the stated and lexical sources and by the Chinese Union's, which borrows only the
/// running numbers (the mapping's Hebrew against BHSA's words) and so reads neither the King James nor TAHOT.
/// </summary>
internal sealed class OpenHebrewReading
{
    public const string Mapping = "mapping/KJV-OT-mapped-to-BHS-full-mapping.csv";

    public const string Tahot = "STEPBible";

    private const string TahotVolumes = "TAHOT *.txt";

    private static readonly ConditionalWeakTable<ProductTexts, OpenHebrewReading> Readings = new();

    private readonly ProductTexts _texts;
    private readonly Lazy<Hebrew> _hebrew;
    private readonly Lazy<(List<OldTestamentLinkLoader.LinkDraft> Drafts, string Summary)> _drafts;

    private OpenHebrewReading(ProductTexts texts)
    {
        _texts = texts;
        _hebrew = new Lazy<Hebrew>(ReadHebrew);
        _drafts = new Lazy<(List<OldTestamentLinkLoader.LinkDraft>, string)>(Draw);
    }

    /// <summary>The King James's links, English word ids being KJV's and Hebrew ones BHSA's (<see cref="ProductText"/> ids).</summary>
    public IReadOnlyList<OldTestamentLinkLoader.LinkDraft> Drafts => _drafts.Value.Drafts;

    public string Summary => _drafts.Value.Summary;

    /// <summary>
    /// The BHSA words each running number names, as the loader reads them now (a word BHSA splits as all of it): read
    /// from the mapping's Hebrew and BHSA's words alone.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<long>> Numbers => _hebrew.Value.Numbers;

    public static OpenHebrewReading Of(ProductTexts texts)
    {
        lock (Readings)
        {
            return Readings.GetValue(texts, t => new OpenHebrewReading(t));
        }
    }

    /// <param name="Unjoined">The records whose Hebrew does not join BHSA by position, in the file's order.</param>
    private sealed record Hebrew(
        IReadOnlyList<MappingRecord> Records,
        Dictionary<(int, int, int), List<Word>> Verses,
        Dictionary<MappingRecord, List<Word>> Joined,
        List<MappingRecord> Unjoined,
        Dictionary<int, Word> ByNumber,
        Dictionary<int, Word> Split,
        IReadOnlyDictionary<int, IReadOnlyList<long>> Numbers);

    private Hebrew ReadHebrew()
    {
        var records = KjvBhsMapping.Read(_texts.Store.FullPath(Mapping));
        var verses = ByAddress(_texts[BhsaTextSource.Slug]);
        var joined = new Dictionary<MappingRecord, List<Word>>(ReferenceEqualityComparer.Instance);
        var unjoined = new List<MappingRecord>();
        var placed = new Dictionary<long, int>(430_000);
        foreach (var record in records)
        {
            if (OldTestamentLinkLoader.JoinHebrew(record, verses, out _) is { } bhsa)
            {
                OldTestamentLinkLoader.Place(record, bhsa, placed);
                joined[record] = bhsa;
            }
            else
            {
                unjoined.Add(record);
            }
        }

        var byNumber = OldTestamentLinkLoader.WordsByNumber(records, verses, placed, out var split);
        var numbers = byNumber.Concat(split).ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<long>)[.. pair.Value.Also.Prepend(pair.Value.Id)]);
        return new Hebrew(records, verses, joined, unjoined, byNumber, split, numbers);
    }

    private (List<OldTestamentLinkLoader.LinkDraft>, string) Draw()
    {
        var hebrew = _hebrew.Value;
        var folder = _texts.Store.FullPath(Tahot);
        var volumes = Directory.Exists(folder) ? Directory.GetFiles(folder, TahotVolumes).Order(StringComparer.Ordinal).ToArray() : [];
        var segmentation = volumes.Length > 0 ? TahotSegmentation.Read(volumes) : null;
        var english = ByAddress(_texts[Bible4uTextSource.KingJames]);

        var drafts = new List<OldTestamentLinkLoader.LinkDraft>(400_000);
        int joined = 0, numbered = 0, refused = 0;
        foreach (var record in hebrew.Records)
        {
            if (hebrew.Joined.TryGetValue(record, out var bhsa))
            {
                _ = Take(record, bhsa) ? joined++ : refused++;
            }
        }

        // A verse the file and BHSA divide differently is read word by word through the running numbers.
        foreach (var record in hebrew.Unjoined)
        {
            _ = OldTestamentLinkLoader.ByRunningNumber(record, hebrew.ByNumber, hebrew.Split) is { } bhsa && Take(record, bhsa)
                ? numbered++
                : refused++;
        }

        return (drafts,
            $"{joined} verses joined by position, {numbered} through the running numbers, {refused} refused; {drafts.Count} links"
            + (segmentation is null ? "; TAHOT is not on disk, so the prefixes were matched from the project's own list" : string.Empty));

        bool Take(MappingRecord record, IReadOnlyList<Word?> bhsa)
        {
            if (!english.TryGetValue((record.Book, record.Chapter, record.Verse), out var kjv)
                || OldTestamentLinkLoader.Build(record, kjv, bhsa, segmentation?.Align(record.Book, record.Chapter, record.Verse, record.Hebrew))
                    is not { } made)
            {
                return false;
            }

            drafts.AddRange(made);
            return true;
        }
    }

    /// <summary>A text's words by frame address as the loader read them: id, surface and gloss.</summary>
    private static Dictionary<(int, int, int), List<Word>> ByAddress(ProductText text) =>
        text.ByAddress.ToDictionary(
            address => address.Key,
            address => address.Value.Select(id => new Word(id, text.Words[id].Surface, text.Words[id].Gloss)).ToList());
}

/// <summary>
/// The King James's links to BHSA the Open Hebrew Bible states (<see cref="OpenHebrewReading"/>): every link of the mapping
/// that carries no confidence, <c>renders</c>, <c>expands</c> for a word the King James prints in italics and
/// <c>omits</c> for a Hebrew word its English leaves unrendered.
/// </summary>
internal sealed class OpenHebrewStatedSource : IPairSource
{
    public const string Pair = "KJV-BHSA";

    public string Method => MappingMethods.Stated;

    public string Name => "open-hebrew";

    public bool States(string pair) => pair == Pair;

    public bool Makes(string source) => source == OldTestamentLinkLoader.Source;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [OpenHebrewReading.Mapping, OpenHebrewReading.Tahot];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var reading = OpenHebrewReading.Of(texts);
        log(reading.Summary);
        return OpenHebrewRows.Of(texts, reading.Drafts.Where(draft => draft.Confidence is null), Method);
    }
}

/// <summary>
/// The King James's function words matched to the Hebrew prefixes the mapping's phrases hold
/// (<see cref="HebrewPrefixes"/>), each with the confidence of the match; where TAHOT prints that very gloss for the
/// morpheme, a second row of the same words says so.
/// </summary>
internal sealed class OpenHebrewLexicalSource : IPairSource
{
    public string Method => MappingMethods.Lexical;

    public string Name => "open-hebrew-prefixes";

    public bool States(string pair) => pair == OpenHebrewStatedSource.Pair;

    public bool Makes(string source) => source is OldTestamentLinkLoader.Source or OldTestamentLinkLoader.StatedSource;

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [OpenHebrewReading.Mapping, OpenHebrewReading.Tahot];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var drafts = OpenHebrewReading.Of(texts).Drafts.Where(draft => draft.Confidence is not null).ToList();
        var rows = OpenHebrewRows.Of(texts, drafts, Method).ToList();
        rows.AddRange(OpenHebrewRows.Of(texts, drafts.Where(draft => draft.Stated), Method)
            .Select(row => row with
            {
                Confidence = HebrewPrefixes.Stated,
                Source = OldTestamentLinkLoader.StatedSource,
                Evidence = OldTestamentLinkLoader.StatedNote,
            }));
        log($"{drafts.Count} function words matched to their prefixes, {drafts.Count(draft => draft.Stated)} of them where TAHOT prints the gloss");
        return rows;
    }
}

internal static class OpenHebrewRows
{
    /// <summary>The rows of the drafts, one per shape and relation, the first draft of a shape standing as the database kept it.</summary>
    public static IEnumerable<MappingRow> Of(ProductTexts texts, IEnumerable<OldTestamentLinkLoader.LinkDraft> drafts, string method)
    {
        var english = texts[Bible4uTextSource.KingJames];
        var hebrew = texts[BhsaTextSource.Slug];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var draft in drafts)
        {
            var row = new MappingRow(null,
                MappingText.Keys(draft.English.Select(english.Key)),
                MappingText.Keys(draft.Hebrew.Select(hebrew.Key)),
                OldTestamentLinkLoader.RelationOf(draft).ToString().ToLowerInvariant(),
                method, draft.Confidence, OldTestamentLinkLoader.Source, null, null, null);
            if (seen.Add(row.Shape))
            {
                yield return row;
            }
        }
    }
}

/// <summary>
/// The Chinese Union Version's Old Testament linked to BHSA by the Open Hebrew Bible's mapping, as the database's
/// <see cref="OpenHebrewCuvLinkLoader"/> draws it: FHL's spans laid onto the traditional text's words by their letters, the
/// simplified text taking them by place wherever its verse holds as many words, and each span's running numbers read as
/// the King James mapping reads them now (<see cref="OpenHebrewReading.Numbers"/>). The Chinese verses are the text's own;
/// the running numbers name BHSA's words directly.
/// </summary>
internal sealed class OpenHebrewCuvSource : IPairSource
{
    public static readonly string File = string.Join('/', OpenHebrewCuvLinkLoader.File);

    public string Method => MappingMethods.Stated;

    public string Name => "open-hebrew-cuv";

    public bool States(string pair) =>
        pair == $"{SwordTextSource.ChineseUnion}-{BhsaTextSource.Slug}" || pair == $"{SwordTextSource.ChineseUnionSimplified}-{BhsaTextSource.Slug}";

    public bool Makes(string source) => source.StartsWith(OpenHebrewCuvLinkLoader.Credit, StringComparison.Ordinal);

    public IEnumerable<string> Inputs(ProductStore store, string pair) =>
    [
        File, OpenHebrewReading.Mapping,
        .. pair.StartsWith(SwordTextSource.ChineseUnionSimplified + "-", StringComparison.Ordinal)
            ? [store.Find(TextWord.ProductOf(SwordTextSource.ChineseUnion))!]
            : Array.Empty<string>(),
    ];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, _) = MappingProducts.Split(pair);
        var numbers = OpenHebrewReading.Of(texts).Numbers;
        var traditional = Verses(texts[SwordTextSource.ChineseUnion]);
        var chinese = texts[fromText];
        var words = fromText == SwordTextSource.ChineseUnion ? traditional : Verses(chinese);
        var hebrew = texts[BhsaTextSource.Slug];

        var rows = new List<MappingRow>(280_000);
        int spans = 0, withoutHebrew = 0, withoutChinese = 0, realigned = 0;
        foreach (var verse in OpenHebrewCuvMapping.Read(texts.Store.FullPath(File)))
        {
            var key = (verse.Book, verse.Chapter, verse.Verse);
            if (!traditional.TryGetValue(key, out var lettered))
            {
                continue;
            }

            var placements = OpenHebrewCuvLinkLoader.Place(verse.Runs, [.. lettered.Select(id => texts[SwordTextSource.ChineseUnion].Words[id].Surface)], out var lined);
            realigned += lined ? 1 : 0;
            words.TryGetValue(key, out var ours);
            var sameWords = ours is not null && ours.Count == lettered.Count;
            foreach (var placement in placements)
            {
                spans++;
                var hebrewWords = placement.Positions.Where(numbers.ContainsKey).SelectMany(p => numbers[p]).Distinct().Order().ToList();
                if (hebrewWords.Count == 0)
                {
                    withoutHebrew++;
                    continue;
                }

                if (!sameWords || placement.Words.Count == 0)
                {
                    withoutChinese++;
                    continue;
                }

                rows.Add(new MappingRow(null,
                    MappingText.Keys(placement.Words.Select(i => chinese.Key(ours![i]))),
                    MappingText.Keys(hebrewWords.Select(hebrew.Key)),
                    LinkRelation.Renders.ToString().ToLowerInvariant(), Method, null, OpenHebrewCuvLinkLoader.Credit, null, null, null));
            }
        }

        log($"{spans} spans, {rows.Count} links; {withoutHebrew} name no BHSA word, {withoutChinese} no word of ours; {realigned} verses lined up letter by letter");
        return rows;
    }

    /// <summary>A Chinese text's Old Testament words by its own verses, as the loader grouped them.</summary>
    private static Dictionary<(int Book, int Chapter, int Verse), List<int>> Verses(ProductText text) =>
        text.Verses
            .Where(verse => verse.Book <= BookReferences.OldTestamentBookCount)
            .GroupBy(verse => (verse.Book, verse.Chapter, verse.Verse))
            .ToDictionary(group => group.Key, group => group.SelectMany(text.InVerse).ToList());
}
