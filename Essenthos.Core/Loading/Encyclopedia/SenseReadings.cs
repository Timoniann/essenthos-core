using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// One answer a model gave about one word: whom it says the word names, how sure it was, and the
/// sentence it gave for it.
///
/// The row carries its own prompt version, model and run date rather than leaning on the manifest
/// beside it. That is not redundancy — a run directory is a folder somebody can copy a file into,
/// and an answer whose provenance lives in a sibling file is an answer that arrives unattributed
/// the first time the two are separated. Every field here ends up on the claim.
/// </summary>
internal sealed record SenseReading(
    [property: JsonPropertyName("word_id")] long WordId,
    [property: JsonPropertyName("strong_number")] string StrongNumber,
    [property: JsonPropertyName("referent")] string Referent,
    [property: JsonPropertyName("names")] string? Names,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("prompt_version")] string PromptVersion,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("run")] string Run)
{
    /// <summary>The model naming a referent the encyclopedia does not hold, rather than picking the
    /// least-bad record it was offered. It is the most valuable answer in the file and the one thing
    /// no tie-break could ever produce, and it is not an annotation: there is nobody to point at.
    /// </summary>
    public const string Unlisted = "unlisted";

    /// <summary>The model declining. Not an answer, and not loaded.</summary>
    public const string Unclear = "unclear";

    public bool NamesAnEntity => Referent is not (Unlisted or Unclear);
}

/// <summary>
/// One reading a later pass found wrong, and what it found instead.
///
/// These are evidence about the method rather than data about the text, and the difference is the
/// whole reason this file exists: a reading that has been read a second time and contradicted is
/// not a weaker annotation, it is not an annotation at all. Loading it with a lower confidence
/// would be the corpus publishing something it knows to be false and hedging.
/// </summary>
internal sealed record RefusedReading(
    long WordId,
    string StrongNumber,
    string? Reference,
    string Reading,
    string Verdict,
    string? Instead,
    string Review,
    string Why);

internal sealed record RefusedReadings(string Reviewed, IReadOnlyList<RefusedReading> Readings);

/// <summary>
/// The owner's ruling on one occurrence: whom the word names, and whether that is somebody the
/// encyclopedia already holds or a record this corpus has to write.
/// </summary>
internal sealed record OwnRecordRuling(
    long WordId,
    string Reference,
    string StrongNumber,
    OwnRecord? Create,
    string? Existing,
    IReadOnlyList<OwnAlternative>? Alternatives,
    string Why);

internal sealed record OwnRecord(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    string? Notes);

internal sealed record OwnAlternative(string? Slug, string? Describes, string Reason);

internal sealed record OwnRecordRulings(string DecidedBy, string Policy, IReadOnlyList<OwnRecordRuling> Rulings);

/// <summary>
/// Where the readings and the two review files are read from.
///
/// The answers are a gigabyte-scale by-product of running a model over the corpus and they stay out
/// of the repository like every other corpus source, so they arrive through the configured
/// resources path and a checkout without them loads nothing and says so. The two review files are
/// the opposite: they are small, they are the record of what a second pass found wrong, and a copy
/// of the corpus that had the readings and not the refusals would load the seventy-four answers
/// somebody already established are false. So those travel inside the assembly, where they cannot
/// be missing.
/// </summary>
internal static class SenseReadingFiles
{
    public const string ConfigurationKey = "Dataset:SenseReadingsPath";

    /// <summary>Under the corpus sources, one directory per run, each holding its answers.</summary>
    public const string DefaultFolder = "SenseReadings";

    public const string AnswersFileName = "answers.jsonl";

    private const string RefusedResource = "Essenthos.Core.Loading.Encyclopedia.RefusedReadings.json";

    private const string RulingsResource = "Essenthos.Core.Loading.Encyclopedia.OwnRecords.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Every answer of every run under a directory, the first answer for a word winning.
    ///
    /// A word answered twice in two shards is ordinary — 154 of the run's 10,147 rows are that, and
    /// nearly all of them agree. A word answered two *different* ways is not, and it is returned
    /// separately rather than resolved: two answers that contradict each other are not evidence for
    /// either, and taking the first would make which shard finished first into a fact about the
    /// text.
    /// </summary>
    public static (IReadOnlyList<SenseReading> Readings, IReadOnlySet<long> Contradicted, int Answers, int Runs)
        Read(string directory)
    {
        var byWord = new Dictionary<long, SenseReading>();
        var contradicted = new HashSet<long>();
        var answers = 0;
        var runs = 0;

        foreach (var file in Directory
                     .EnumerateFiles(directory, AnswersFileName, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            runs++;
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var reading = JsonSerializer.Deserialize<SenseReading>(line, Shape)
                              ?? throw new InvalidDataException(
                                  $"A line of {file} is not an answer. Each line must be one JSON object " +
                                  "with word_id, referent, confidence, prompt_version, model and run.");

                answers++;
                if (byWord.TryGetValue(reading.WordId, out var already))
                {
                    if (already.Referent != reading.Referent)
                    {
                        contradicted.Add(reading.WordId);
                    }
                }
                else
                {
                    byWord[reading.WordId] = reading;
                }
            }
        }

        return ([.. byWord.Values], contradicted, answers, runs);
    }

    public static RefusedReadings Refused() => Embedded<RefusedReadings>(RefusedResource);

    public static OwnRecordRulings Rulings() => Embedded<OwnRecordRulings>(RulingsResource);

    private static T Embedded<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{name}\" is not in this assembly. It is added " +
                               "by the EmbeddedResource item in Essenthos.Core.csproj; if the file was " +
                               "moved or renamed, that item and this name have to move with it.",
                               name);

        return JsonSerializer.Deserialize<T>(stream, Shape)
               ?? throw new InvalidDataException($"The embedded resource \"{name}\" is empty.");
    }
}
