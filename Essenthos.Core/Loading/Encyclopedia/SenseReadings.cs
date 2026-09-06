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
/// A reading that has been read a second time and contradicted is not a weaker annotation: as an
/// answer it is not an annotation at all, and loading it with a lower confidence would be the
/// corpus publishing something it knows to be false and hedging. But the word still names somebody,
/// so the verdict is not the end of it — where the review named a referent, that referent is a
/// ruling in <c>ReviewRecords.json</c> and reaches the reader with this reading recorded beside it
/// as the thing that was ruled out. What is refused here is the answer, never the question.
///
/// <para>
/// A verdict is about a reading and not about a word, which is why <see cref="Reading"/> is stored
/// and compared rather than assumed. A word a later run answered differently has not been reviewed
/// at all, and refusing it on the strength of a verdict about the answer it no longer gives would
/// throw away the re-ask silently.
/// </para>
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
/// One answer a later run replaced, and what it replaced it with.
///
/// The readings were asked against a candidate list that was wrong in two directions at once: it
/// offered people who appear only in the Greek New Testament as referents for Masoretic words, and
/// it could offer none of the places the geocoding dataset supplies, because none of them carried a
/// Strong number. Both were repaired, the names whose lists had changed were asked again, and 480
/// answers moved.
///
/// <para>
/// A word answered twice must load the later answer, and which of two answers is later is not
/// something a file listing can be trusted to say — sorting run folders by name puts the second
/// campaign first only by the accident of its spelling. So the supersession is recorded rather than
/// inferred: it names the answer it replaces and the answer that stands, and applying it is correct
/// whichever of the two a directory walk happened to reach first. It travels inside the assembly
/// for the same reason the refusals do — a corpus holding the first campaign's answers and not this
/// would silently undo the re-ask.
/// </para>
/// </summary>
internal sealed record SupersededReading(
    long WordId,
    string StrongNumber,
    string Was,
    string Now,
    string Confidence,
    string? Reason);

internal sealed record SupersededReadings(
    string AskedAgain,
    string Model,
    string PromptVersion,
    string Run,
    int ReaskedNames,
    int ReaskedOccurrences,
    IReadOnlyList<SupersededReading> Readings);

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

/// <param name="Method">
/// What kind of thing decided these. The owner's own rulings and the review's are the same shape and
/// are not the same claim, so the file says which it is rather than the loader assuming.
/// </param>
/// <param name="Source">
/// Who decided, in the words a reader gets on the card: the person, or the review with its models,
/// its prompt version and its date.
/// </param>
internal sealed record OwnRecordRulings(
    string DecidedBy,
    string Policy,
    string Method,
    string Source,
    IReadOnlyList<OwnRecordRuling> Rulings);

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

    private const string SupersededResource =
        "Essenthos.Core.Loading.Encyclopedia.SupersededReadings.json";

    private const string RulingsResource = "Essenthos.Core.Loading.Encyclopedia.OwnRecords.json";

    private const string ReviewResource = "Essenthos.Core.Loading.Encyclopedia.ReviewRecords.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Every answer of every run under a directory, with the answers a later run replaced already
    /// replaced.
    ///
    /// A word answered twice in two shards of one campaign is ordinary — 154 of the run's 10,147
    /// rows are that, and nearly all of them agree. A word answered two *different* ways by one
    /// campaign is not, and it is returned separately rather than resolved: two answers that
    /// contradict each other are not evidence for either, and taking the first would make which
    /// shard finished first into a fact about the text.
    ///
    /// <para>
    /// A later campaign is the opposite case and must not be confused with it. Asking a name again
    /// on a repaired candidate list is deliberate, its answer is the one that stands, and the
    /// supersession says which answer it replaces — so it settles a word the first campaign
    /// contradicted itself about, and it lands the same way whether or not the second campaign's
    /// files are on this disk.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<SenseReading> Readings, IReadOnlySet<long> Contradicted, int Answers, int Runs, int Superseded)
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

        var later = Superseded();
        var superseded = 0;
        foreach (var replacement in later.Readings)
        {
            if (!byWord.TryGetValue(replacement.WordId, out var earlier))
            {
                continue;
            }

            // A word the second campaign answered has been answered, whatever the first campaign
            // did with it — including answering it two ways, which is what the second campaign was
            // asked to settle.
            contradicted.Remove(replacement.WordId);
            if (earlier.Referent == replacement.Now)
            {
                continue;
            }

            superseded++;

            // The description a model gives for a referent nobody holds is not carried in the
            // record of what changed, so it is dropped rather than kept from the answer it
            // replaced: a sentence about the earlier referent is not a description of this one.
            byWord[replacement.WordId] = earlier with
            {
                Referent = replacement.Now,
                Names = null,
                Confidence = replacement.Confidence,
                Reason = replacement.Reason,
                PromptVersion = later.PromptVersion,
                Model = later.Model,
                Run = later.Run,
            };
        }

        return ([.. byWord.Values], contradicted, answers, runs, superseded);
    }

    public static RefusedReadings Refused() => Embedded<RefusedReadings>(RefusedResource);

    public static SupersededReadings Superseded() => Embedded<SupersededReadings>(SupersededResource);

    public static OwnRecordRulings Rulings() => Embedded<OwnRecordRulings>(RulingsResource);

    /// <summary>What the review of the readings decided, in the same vocabulary as the owner's own
    /// rulings, because it is the same kind of thing: a decision about one word, recorded where a
    /// person can read it and disagree.</summary>
    public static OwnRecordRulings ReviewRulings() => Embedded<OwnRecordRulings>(ReviewResource);

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
