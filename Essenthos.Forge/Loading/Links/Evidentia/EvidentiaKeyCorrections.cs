using System.Globalization;
using System.Text.Json;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What two readers, and where they differed a third, made of one placement the key and another reading disagree on.</summary>
internal enum EvidentiaKeyVerdict
{
    /// <summary>The other reading is where the word belongs and the key's is not.</summary>
    Corrected,

    /// <summary>Both are defensible: the other reading is right as well as the key's.</summary>
    Defensible,

    /// <summary>The key is right and the other reading is not.</summary>
    Confirmed,

    /// <summary>The word belongs on neither.</summary>
    Neither,

    /// <summary>Nobody could settle it: it stays as the key has it.</summary>
    Unsettled,
}

/// <summary>
/// One reading of the key against the original, by canonical address and never by a row id: the word of
/// the translation, what the key names for it and the other reading it was compared with, each as
/// <c>chapter:verse:position surface</c>.
/// </summary>
/// <param name="Supplied">The other reading is that the original writes nothing for the word.</param>
/// <param name="Readers">Each reader's answer: <c>key</c>, <c>read</c>, <c>both</c>, <c>neither</c> or <c>unsure</c>.</param>
/// <param name="Lead">The third reading, where the two readers differed, and <paramref name="Why"/> it went so.</param>
internal sealed record EvidentiaKeyCorrection(
    string From,
    string To,
    int Book,
    string Word,
    IReadOnlyList<string> Key,
    IReadOnlyList<string> Read,
    string Verdict,
    IReadOnlyDictionary<string, string>? Readers = null,
    string? Lead = null,
    string? Why = null,
    string? Date = null,
    bool Supplied = false);

/// <summary>
/// The answer key's own errors, as far as they have been read. The Berean tables link an English chunk
/// with a written Hebrew word and the Clear Bible alignment has its own habits, so where a placement and
/// the key disagree the key is not always the one that is right. Every such disagreement on the measured
/// passages was read against the original by two readers who saw neither each other's answer nor how the
/// word had been placed, and <c>EvidentiaKeyCorrections.json</c> holds what they found.
///
/// <para>It is evaluation data only. Nothing here is a link, a training label or evidence for a
/// placement: it changes how a run is scored and nothing else, and nothing outside the scorer reads it.</para>
/// </summary>
internal static class EvidentiaKeyCorrections
{
    private const string Resource = "Essenthos.Core.Loading.Links.Evidentia.EvidentiaKeyCorrections.json";

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<IReadOnlyList<EvidentiaKeyCorrection>> Shipped = new(() =>
    {
        using var stream = typeof(EvidentiaKeyCorrections).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"{Resource} is not in the assembly. It is an EmbeddedResource of Essenthos.Forge.csproj; restore that item and rebuild.");
        return Read(stream);
    });

    public static IReadOnlyList<EvidentiaKeyCorrection> All => Shipped.Value;

    public static IReadOnlyList<EvidentiaKeyCorrection> Read(Stream stream)
    {
        var file = JsonSerializer.Deserialize<File>(stream, Shape)
            ?? throw new InvalidOperationException("EvidentiaKeyCorrections.json is empty; it must hold an object with an \"entries\" list.");
        foreach (var entry in file.Entries)
        {
            if (!Enum.TryParse<EvidentiaKeyVerdict>(entry.Verdict, ignoreCase: true, out _))
            {
                throw new InvalidOperationException(
                    $"EvidentiaKeyCorrections.json: \"{entry.Verdict}\" is not a verdict ({entry.Book} {entry.Word}). " +
                    $"Use one of {string.Join(", ", Enum.GetNames<EvidentiaKeyVerdict>().Select(name => name.ToLowerInvariant()))}.");
            }
        }

        return file.Entries;
    }

    private sealed record File(IReadOnlyList<EvidentiaKeyCorrection> Entries);
}

/// <summary>How many readings of the key stand on a passage, by verdict, and how much of the passage is still unread.</summary>
/// <param name="Stale">Readings whose word or reading the text no longer has at that address: not applied.</param>
/// <param name="WrongByTheKey">Placements the split key counts wrong.</param>
/// <param name="Unread">Those of them no reading speaks of: the judged figure counts them wrong, as the key does.</param>
internal readonly record struct EvidentiaJudgedCount(
    int Readings,
    int Corrected,
    int Defensible,
    int Confirmed,
    int Neither,
    int Unsettled,
    int Stale,
    int WrongByTheKey,
    int Unread)
{
    public static EvidentiaJudgedCount operator +(EvidentiaJudgedCount one, EvidentiaJudgedCount two) => new(
        one.Readings + two.Readings,
        one.Corrected + two.Corrected,
        one.Defensible + two.Defensible,
        one.Confirmed + two.Confirmed,
        one.Neither + two.Neither,
        one.Unsettled + two.Unsettled,
        one.Stale + two.Stale,
        one.WrongByTheKey + two.WrongByTheKey,
        one.Unread + two.Unread);
}

/// <summary>
/// The key of one passage as the readers left it: the pairs they found right, the pairs they found wrong,
/// and for everything else the key's own verdict. A reading names its words by address, so it is matched
/// to the words of this load and dropped, and counted, where the text no longer reads so.
/// </summary>
internal sealed class EvidentiaJudgedKey
{
    public const string SuppliedReading = "supplied";

    private readonly HashSet<(long From, long To)> right = [];
    private readonly HashSet<(long From, long To)> wrong = [];
    private readonly HashSet<(long From, long To)> read = [];
    private readonly HashSet<long> suppliedRight = [];
    private readonly HashSet<long> suppliedRead = [];
    private readonly Dictionary<(long From, long To), EvidentiaKeyVerdict> verdicts = [];
    private readonly Dictionary<long, EvidentiaKeyVerdict> suppliedVerdicts = [];
    private readonly Dictionary<EvidentiaKeyVerdict, int> counts = [];

    public static readonly EvidentiaJudgedKey None = new();

    public int Readings => counts.Values.Sum();

    public int Stale { get; private set; }

    public int Count(EvidentiaKeyVerdict verdict) => counts.GetValueOrDefault(verdict);

    public static EvidentiaJudgedKey Of(
        IReadOnlyList<EvidentiaKeyCorrection> corrections,
        string from,
        string to,
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target)
    {
        var key = new EvidentiaJudgedKey();
        var sourceWords = Places(source);
        var targetWords = Places(target);
        var books = source.Select(word => word.Token.Address.Book).ToHashSet();
        foreach (var correction in corrections)
        {
            if (!correction.From.Equals(from, StringComparison.OrdinalIgnoreCase)
                || !correction.To.Equals(to, StringComparison.OrdinalIgnoreCase) || !books.Contains(correction.Book))
            {
                continue;
            }

            var (address, position, surface) = Parse(correction.Book, correction.Word);
            if (!sourceWords.TryGetValue((address, position), out var word))
            {
                // Another chapter of the book, unless the verse is here and the word is not.
                key.Stale += sourceWords.Keys.Any(place => place.Address.Equals(address)) ? 1 : 0;
                continue;
            }

            var keyWords = Words(correction.Book, correction.Key, targetWords);
            var readWords = Words(correction.Book, correction.Read, targetWords);
            if (!Reads(surface, word.Token.Surface) || keyWords is null || readWords is null
                || correction.Supplied != (readWords.Count == 0))
            {
                key.Stale++;
                continue;
            }

            key.Apply(Enum.Parse<EvidentiaKeyVerdict>(correction.Verdict, ignoreCase: true), word.Token.Id, keyWords, readWords, correction.Supplied);
        }

        return key;
    }

    /// <summary>A placement's verdict: the readers' where they gave one, and otherwise the key's.</summary>
    public bool? Verdict(long from, long to, bool? byTheKey) =>
        byTheKey is null ? null
        : right.Contains((from, to)) ? true
        : wrong.Contains((from, to)) ? false
        : byTheKey;

    /// <summary>The verdict on a word said to be supplied.</summary>
    public bool? Supplied(long word, bool? byTheKey) =>
        byTheKey is null ? null : suppliedRight.Contains(word) || byTheKey.Value;

    /// <summary>Whether any reading, settled or not, speaks of this placement.</summary>
    public bool HasRead(long from, long to) => read.Contains((from, to));

    public bool HasReadSupplied(long word) => suppliedRead.Contains(word);

    /// <summary>What the readers made of this placement, where they read it.</summary>
    public EvidentiaKeyVerdict? Reading(long from, long to) =>
        verdicts.TryGetValue((from, to), out var verdict) ? verdict : null;

    public EvidentiaKeyVerdict? ReadingOfSupplied(long word) =>
        suppliedVerdicts.TryGetValue(word, out var verdict) ? verdict : null;

    private void Apply(EvidentiaKeyVerdict verdict, long word, IReadOnlyList<long> keyWords, IReadOnlyList<long> readWords, bool supplied)
    {
        counts[verdict] = counts.GetValueOrDefault(verdict) + 1;
        var theKeys = keyWords.Select(to => (word, to)).ToList();
        var theOthers = readWords.Select(to => (word, to)).ToList();
        read.UnionWith(theKeys);
        read.UnionWith(theOthers);
        foreach (var pair in theOthers)
        {
            verdicts[pair] = verdict;
        }

        if (supplied)
        {
            suppliedRead.Add(word);
            suppliedVerdicts[word] = verdict;
        }

        switch (verdict)
        {
            case EvidentiaKeyVerdict.Corrected:
                right.UnionWith(theOthers);
                wrong.UnionWith(theKeys.Except(theOthers));
                if (supplied)
                {
                    suppliedRight.Add(word);
                }

                break;
            case EvidentiaKeyVerdict.Defensible:
                right.UnionWith(theOthers);
                if (supplied)
                {
                    suppliedRight.Add(word);
                }

                break;
            case EvidentiaKeyVerdict.Confirmed:
                wrong.UnionWith(theOthers.Except(theKeys));
                break;
            case EvidentiaKeyVerdict.Neither:
                wrong.UnionWith(theOthers);
                wrong.UnionWith(theKeys);
                break;
        }

        // A pair one reading found right and another found wrong is settled by neither.
        foreach (var pair in right.Intersect(wrong).ToList())
        {
            right.Remove(pair);
            wrong.Remove(pair);
        }
    }

    private static Dictionary<(EvidentiaAddress Address, int Position), EvidentiaAnalysis> Places(IReadOnlyList<EvidentiaAnalysis> words) =>
        words.DistinctBy(word => word.Token.Id)
            .GroupBy(word => (word.Token.Address, word.Token.Position))
            .Where(place => place.Count() == 1)
            .ToDictionary(place => place.Key, place => place.First());

    private static List<long>? Words(
        int book,
        IReadOnlyList<string> readings,
        Dictionary<(EvidentiaAddress Address, int Position), EvidentiaAnalysis> words)
    {
        var found = new List<long>();
        foreach (var reading in readings)
        {
            var (address, position, surface) = Parse(book, reading);
            if (!words.TryGetValue((address, position), out var word) || !Reads(surface, word.Token.Surface))
            {
                return null;
            }

            found.Add(word.Token.Id);
        }

        return found;
    }

    /// <summary>
    /// Whether a word the text prints as <paramref name="printed"/> is the word read as <paramref name="surface"/>:
    /// the language pack reads an English word without the quotation marks printed against it (<em>“At</em> is
    /// <em>At</em>) and a word that completes a compound as the compound.
    /// </summary>
    private static bool Reads(string printed, string surface)
    {
        if (printed == surface)
        {
            return true;
        }

        var bare = EnglishSpelling.Bare(new EvidentiaToken(0, default, 0, printed, "eng")).Surface;
        return bare.Length > 0 && surface.Contains(bare, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>34:4:12 and</c>: chapter, verse, the word's place in the verse, and the word as the text prints it (nothing, for an elided article).</summary>
    private static (EvidentiaAddress Address, int Position, string Surface) Parse(int book, string place)
    {
        var space = place.IndexOf(' ');
        var numbers = (space < 0 ? place : place[..space]).Split(':');
        if (numbers.Length != 3 || !numbers.All(number => int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            throw new InvalidOperationException(
                $"EvidentiaKeyCorrections.json: \"{place}\" is not a place. Write it as chapter:verse:position and the word, e.g. \"34:4:12 and\".");
        }

        int Number(int index) => int.Parse(numbers[index], CultureInfo.InvariantCulture);
        return (new EvidentiaAddress(book, Number(0), Number(1)), Number(2), space < 0 ? string.Empty : place[(space + 1)..]);
    }
}
