using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// One answer a model gave about one word: whom it says the word names, how sure it was, and the
/// sentence it gave for it.
///
/// The row carries its own prompt version, model and run date rather than leaning on the manifest
/// beside it. That is not redundancy — a run directory is a folder somebody can copy a file into,
/// and an answer whose provenance lives in a sibling file is an answer that arrives unattributed
/// the first time the two are separated. Every field here ends up on the claim.
///
/// <para>
/// The word is named by its address (<see cref="RuledWord"/>), never by its row id, which a rebuilt
/// corpus gives to another word. <see cref="WordId"/> is where the address stands in the corpus being
/// loaded, found by <see cref="SenseReadingFiles.Place"/>, and is never read from the file.
/// </para>
/// </summary>
internal sealed record SenseReading(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("surface")] string Surface,
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

    [JsonIgnore]
    public RuledWord Word => new(Text, Reference, Position, Surface);

    [JsonIgnore]
    public long WordId { get; init; }
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
    string Text,
    string Reference,
    int Position,
    string Surface,
    string StrongNumber,
    string Reading,
    string Verdict,
    string? Instead,
    string Review,
    string Why)
{
    [JsonIgnore]
    public RuledWord Word => new(Text, Reference, Position, Surface);

    [JsonIgnore]
    public long WordId { get; init; }
}

internal sealed record RefusedReadings(string Reviewed, IReadOnlyList<RefusedReading> Readings);

/// <summary>The answers and refusals placed on the corpus being loaded.</summary>
/// <param name="Lost">Addresses that name no word in it, or a word that reads otherwise now.</param>
internal sealed record PlacedReadings(
    IReadOnlyList<SenseReading> Readings,
    IReadOnlySet<long> Contradicted,
    IReadOnlyList<RefusedReading> Refused,
    int Answers,
    int Runs,
    int Superseded,
    IReadOnlyList<RuledWord> Lost);

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
    string Text,
    string Reference,
    int Position,
    string Surface,
    string StrongNumber,
    string Was,
    string Now,
    string Confidence,
    string? Reason)
{
    [JsonIgnore]
    public RuledWord Word => new(Text, Reference, Position, Surface);
}

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
/// <param name="Text">The witness the word is a word of; with the next three, the <see cref="RuledWord"/>.</param>
/// <param name="Reference">The verse as that witness numbers it: <c>NEH 10:3</c> in BHSA.</param>
/// <param name="StrongNumber">
/// The number the word carries, or null for a word of a text that carries none: a name only the
/// Septuagint prints, in Brenton's Greek or English.
/// </param>
/// <param name="Corrects">
/// The record an earlier ruling gave this word, where this one corrects it. A ruling does not
/// overrule another ruling on its own, so the correction says which one it takes back.
/// </param>
internal sealed record OwnRecordRuling(
    string Text,
    string Reference,
    int Position,
    string Surface,
    string? StrongNumber,
    OwnRecord? Create,
    string? Existing,
    RecordSays? Says,
    IReadOnlyList<OwnAlternative>? Alternatives,
    string Why,
    string? Corrects = null,
    string? Alongside = null)
{
    public RuledWord Word => new(Text, Reference, Position, Surface);
}

/// <summary>
/// What a ruling makes a record say about itself, where the record is one the encyclopedia already
/// holds and what it says is what the ruling is about.
///
/// A ruling could already write a description onto a record it created and could not touch one it
/// merely named, so a decision that a compiled record claims too much had nowhere to land: Jerioth
/// arrived distinguished as the wife of Caleb, which is one of six readings of a verse that settles
/// none of them. Each field is set where it is given and left alone where it is not.
///
/// The name is the heading of a record the dataset headed by a word that is not the person's name.
/// </summary>
internal sealed record RecordSays(string? Distinguisher, string? Notes, string? Name = null);

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
/// <param name="Carry">Whether the reading may cross links into another edition. Edition-specific additions stay on their own words.</param>
internal sealed record OwnRecordRulings(
    string DecidedBy,
    string Policy,
    string Method,
    string Source,
    IReadOnlyList<OwnRecordRuling> Rulings,
    bool Carry = true);

/// <summary>
/// A record the owner ruled is a title rather than one person: what it is now said to be, the
/// records it takes the place of, and who else it might be.
/// </summary>
/// <param name="Why">What established it, as the claim a reader is shown.</param>
/// <param name="Source">
/// Who decided this one, where it is not the decision the file opens with. The owner ruled on
/// Abimelech first and on the rest of the list later, and a claim crediting the first ruling with
/// the second would misstate both.
/// </param>
/// <param name="Names">
/// The names of a title no dataset holds as anybody, which this loader writes as a record of its
/// own. Absent for a record that was a person and is held as a title now.
/// </param>
/// <param name="Bearers">Who the text gives the title to, each at the verse where it does.</param>
/// <param name="Words">Which words of the witnesses are the title, wherever the text uses it.</param>
/// <param name="LocalNames">The title in a reader's language, keyed by the language's code: <c>ukr</c>, <c>deu</c>, <c>spa</c>.</param>
internal sealed record TitleRecord(
    string Slug,
    string Name,
    string Distinguisher,
    string Notes,
    string Why,
    IReadOnlyList<ReplacedRecord>? Replaces,
    IReadOnlyList<OwnAlternative>? Alternatives,
    string? Source = null,
    IReadOnlyList<TitleName>? Names = null,
    IReadOnlyList<TitleBearerRecord>? Bearers = null,
    IReadOnlyList<TitleWord>? Words = null,
    IReadOnlyDictionary<string, string>? LocalNames = null);

/// <summary>
/// The occurrences of one Strong number that are the title: every one the witnesses number with
/// it, or those <see cref="Except"/> leaves, where <see cref="With"/> stands beside it when that is
/// given, and only the singular where <see cref="Singular"/> says so.
/// </summary>
/// <param name="Except">
/// Verses where the word is not this title, written as <c>MAT 2:6</c>: <em>hegemon</em> of the
/// princes of Judah, <em>pechah</em> of Solomon's or Nebuchadnezzar's officers.
/// </param>
/// <param name="With">
/// Another Strong number that has to stand within <see cref="ThingLoader.Reach"/> words in the same
/// verse: <em>the priest</em> beside <em>great</em>.
/// </param>
/// <param name="Singular">
/// Only the singular, where the plural of the word is something else: <em>archiereus</em> is the
/// high priest, and <em>archiereis</em> the chief priests as a body.
/// </param>
/// <param name="Beside">
/// The word is the title even where something else names it too: <em>Christos</em> in <em>Jesus
/// Christ</em> is the title and the man. Without it a title joins only the words nobody holds.
/// </param>
internal sealed record TitleWord(
    string Strong,
    IReadOnlyList<string>? Except = null,
    string? With = null,
    bool Singular = false,
    bool Beside = false)
{
    public bool Admits(int book, int chapter, int verse, int nth) =>
        Except is null || !Except.Any(span => ScriptureSpan.Parse(span).Holds(book, chapter, verse, nth));
}

/// <summary>
/// One name of a title written here. A Strong number is given only where the word already names
/// several records, so that the title joins a question the annotation pass cannot answer rather
/// than answering it: <em>high priest</em> is a common noun, and a title that were the only record
/// carrying its number would take every occurrence of the word.
/// </summary>
internal sealed record TitleName(
    string Label,
    string? Hebrew,
    string? HebrewTransliterated,
    string? Greek,
    string? GreekTransliterated,
    string? Meaning,
    string? HebrewStrongNumber,
    string? GreekStrongNumber);

/// <param name="Reference">The verse naming the person and the title together, as <c>2KI 23:29</c>.</param>
/// <param name="Why">What the verse says, so the claim can be checked against it.</param>
internal sealed record TitleBearerRecord(string Slug, string Reference, string Why);

/// <summary>A record written for one bearer of a title, which the title now answers for.</summary>
internal sealed record ReplacedRecord(string Slug, string Why);

/// <summary>
/// The owner's decision that some names are titles, in the file that also carries the words it
/// settles, so the claim on the records and the annotations on the words credit one decision.
/// </summary>
internal sealed record TitleDecision(
    string DecidedBy,
    string Policy,
    string Method,
    string Source,
    IReadOnlyList<TitleRecord> Titles);

/// <summary>
/// One occurrence of a title's word, read against its verse: whom the text fixes it to, or that the
/// text leaves open who is meant.
/// </summary>
/// <param name="Reference">
/// The verse, and which occurrence of the number in it where the ruling is of one of several:
/// <c>ISA 45:1</c>, <c>ACT 17:3#1</c>. Canonical numbering.
/// </param>
/// <param name="Bearer">
/// The record the text fixes the word to, which the word then names beside the title. Null where
/// the verse asks, denies, supposes or reports a claim, and the word names the title alone.
/// </param>
/// <param name="Doubt">What can be said for the other reading, where the ruling is the cautious one of two.</param>
/// <param name="Confidence">
/// How sure the reading is, where a model read the verse and no person ruled on it; null on a ruling.
/// </param>
/// <param name="Second">The second reader's sentence, where two models read the verse and agreed.</param>
internal sealed record TitleReading(
    string Reference,
    string Strong,
    string? Bearer,
    string Why,
    string? Doubt = null,
    double? Confidence = null,
    string? Second = null)
{
    public ScriptureSpan At { get; } = ScriptureSpan.Parse(Reference);
}

/// <summary>
/// The occurrences of one title's words ruled on one by one, in the file that says who decided and how.
/// </summary>
/// <param name="Title">The title the words are, by slug.</param>
internal sealed record TitleReadings(
    string DecidedBy,
    string Policy,
    string Method,
    string Source,
    string Title,
    IReadOnlyList<TitleReading> Readings)
{
    /// <summary>The verses where the number names the title alone.</summary>
    public IReadOnlyList<ScriptureSpan> Open(string strong) =>
        [.. Readings.Where(reading => reading.Bearer is null && reading.Strong == strong).Select(reading => reading.At)];
}

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

    private const string ReportResource = "Essenthos.Core.Loading.Encyclopedia.ReportRecords.json";

    private const string TitleResource = "Essenthos.Core.Loading.Encyclopedia.TitleRecords.json";

    private const string UnsettledResource = "Essenthos.Core.Loading.Encyclopedia.UnsettledRecords.json";

    private const string UnsettledSecondResource =
        "Essenthos.Core.Loading.Encyclopedia.UnsettledRecordsSecond.json";

    private const string GenealogyResource = "Essenthos.Core.Loading.Encyclopedia.GenealogyRecords.json";

    private const string SeveralPeopleResource = "Essenthos.Core.Loading.Encyclopedia.SeveralPeopleRecords.json";

    private const string SeveralPeopleSecondResource =
        "Essenthos.Core.Loading.Encyclopedia.SeveralPeopleRecordsSecond.json";

    private const string SeveralPeopleThirdResource =
        "Essenthos.Core.Loading.Encyclopedia.SeveralPeopleRecordsThird.json";

    private const string SeveralPeopleFourthResource =
        "Essenthos.Core.Loading.Encyclopedia.SeveralPeopleRecordsFourth.json";

    private const string NamesakeResource = "Essenthos.Core.Loading.Encyclopedia.NamesakeRecords.json";

    private const string AddressedResource = "Essenthos.Core.Loading.Encyclopedia.AddressedRecords.json";

    private const string TitleReadingsResource = "Essenthos.Core.Loading.Encyclopedia.TitleReadings.json";

    private const string PharaohReadingsResource = "Essenthos.Core.Loading.Encyclopedia.PharaohReadings.json";

    private const string CaesarReadingsResource = "Essenthos.Core.Loading.Encyclopedia.CaesarReadings.json";

    private const string NamesakeSecondResource =
        "Essenthos.Core.Loading.Encyclopedia.NamesakeRecordsSecond.json";

    private const string DatasetRecordResource = "Essenthos.Core.Loading.Encyclopedia.DatasetRecordRulings.json";

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
    public static (IReadOnlyList<SenseReading> Readings, IReadOnlySet<RuledWord> Contradicted, int Answers, int Runs, int Superseded)
        Read(string directory)
    {
        var byWord = new Dictionary<RuledWord, SenseReading>();
        var contradicted = new HashSet<RuledWord>();
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
                                  "with text, reference, position, surface, referent, confidence, prompt_version, model and run.");

                if (string.IsNullOrEmpty(reading.Text) || string.IsNullOrEmpty(reading.Reference))
                {
                    throw new InvalidDataException(
                        $"A line of {file} names its word by row id or not at all. An answer names its word by text, "
                        + "reference, position and surface; convert the files once with "
                        + "scripts/address-word-ids.py before loading them.");
                }

                answers++;
                if (byWord.TryGetValue(reading.Word, out var already))
                {
                    if (already.Referent != reading.Referent)
                    {
                        contradicted.Add(reading.Word);
                    }
                }
                else
                {
                    byWord[reading.Word] = reading;
                }
            }
        }

        var later = Superseded();
        var superseded = 0;
        foreach (var replacement in later.Readings)
        {
            if (!byWord.TryGetValue(replacement.Word, out var earlier))
            {
                continue;
            }

            // A word the second campaign answered has been answered, whatever the first campaign
            // did with it — including answering it two ways, which is what the second campaign was
            // asked to settle.
            contradicted.Remove(replacement.Word);
            if (earlier.Referent == replacement.Now)
            {
                continue;
            }

            superseded++;

            // The description a model gives for a referent nobody holds is not carried in the
            // record of what changed, so it is dropped rather than kept from the answer it
            // replaced: a sentence about the earlier referent is not a description of this one.
            byWord[replacement.Word] = earlier with
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

    /// <summary>
    /// Every answer and every refusal under a directory, each on the word its address names in the
    /// corpus the connection reads. An address that names no word there, or a word that no longer
    /// reads as it did, is not placed on whatever stands there now; it is counted in
    /// <see cref="PlacedReadings.Lost"/>.
    /// </summary>
    public static async Task<PlacedReadings> Place(
        NpgsqlConnection connection,
        string directory,
        CancellationToken cancellationToken = default)
    {
        var (readings, contradicted, answers, runs, superseded) = Read(directory);
        var refused = Refused().Readings;
        var (found, lost) = await RuledWords.Find(
            connection,
            readings.Select(r => r.Word).Concat(contradicted).Concat(refused.Select(r => r.Word)),
            cancellationToken);

        return new PlacedReadings(
            [.. readings.Where(r => found.ContainsKey(r.Word)).Select(r => r with { WordId = found[r.Word] })],
            contradicted.Where(found.ContainsKey).Select(word => found[word]).ToHashSet(),
            [.. refused.Where(r => found.ContainsKey(r.Word)).Select(r => r with { WordId = found[r.Word] })],
            answers,
            runs,
            superseded,
            lost);
    }

    public static RefusedReadings Refused() => Embedded<RefusedReadings>(RefusedResource);

    public static SupersededReadings Superseded() => Embedded<SupersededReadings>(SupersededResource);

    public static OwnRecordRulings Rulings() => Embedded<OwnRecordRulings>(RulingsResource);

    /// <summary>What the review of the readings decided, in the same vocabulary as the owner's own
    /// rulings, because it is the same kind of thing: a decision about one word, recorded where a
    /// person can read it and disagree.</summary>
    public static OwnRecordRulings ReviewRulings() => Embedded<OwnRecordRulings>(ReviewResource);

    /// <summary>
    /// What the owner's reading report was decided as, occurrence by occurrence, where the text
    /// itself had to be read: whether a Hebrew word is a name or a noun, and which of a man and a
    /// place the lexeme's marking settled wrongly.
    /// </summary>
    public static OwnRecordRulings ReportRulings() => Embedded<OwnRecordRulings>(ReportResource);

    /// <summary>
    /// The words the owner's decision on the titles settles, which are the occurrences no pass
    /// named: the title of Psalm 34, and the Septuagint's own Ochozath and Phicol.
    /// </summary>
    public static OwnRecordRulings TitleRulings() => Embedded<OwnRecordRulings>(TitleResource);

    /// <summary>The same decision read as what it says about the records themselves.</summary>
    public static TitleDecision Titles() => Embedded<TitleDecision>(TitleResource);

    /// <summary>
    /// The occurrences of the Anointed's words, each read against its verse: whom the text fixes the
    /// title to there, and where it leaves that open.
    /// </summary>
    public static TitleReadings TitleReadings() => Embedded<TitleReadings>(TitleReadingsResource);

    /// <summary>
    /// Which king Pharaoh means at each verse a dataset lists for one of the kings of Egypt, as two
    /// readings of the verse agreed: the king beside the title, or the title alone where the text does
    /// not say which.
    /// </summary>
    public static TitleReadings PharaohReadings() => Embedded<TitleReadings>(PharaohReadingsResource);

    /// <summary>The same for Caesar and the emperors the text speaks of.</summary>
    public static TitleReadings CaesarReadings() => Embedded<TitleReadings>(CaesarReadingsResource);

    /// <summary>Every title whose occurrences are read one by one, the Anointed first.</summary>
    public static IReadOnlyList<TitleReadings> AllTitleReadings() =>
        [TitleReadings(), PharaohReadings(), CaesarReadings()];

    /// <summary>
    /// The records the owner has ruled the text does not identify: what each of them says about
    /// itself now, and every reading of the verse that could be right instead.
    /// </summary>
    public static OwnRecordRulings UnsettledRulings() => Embedded<OwnRecordRulings>(UnsettledResource);

    /// <summary>
    /// The same decision taken for Iezer on 2026-09-29: whether he is the Abiezer whom Gilead's sister
    /// bore is open. A file of its own, because a rulings file a corpus has already recorded is skipped
    /// whole, and because its claim credits a different decision than Jerioth's.
    /// </summary>
    public static OwnRecordRulings UnsettledSecondRulings() => Embedded<OwnRecordRulings>(UnsettledSecondResource);

    /// <summary>
    /// Esau's wives, whom Genesis names twice in two lists that agree in one name and two fathers: each
    /// woman of the second list is a record of her own that names the woman of the first she may be, and
    /// Beeri is no longer said to be Anah.
    /// </summary>
    public static OwnRecordRulings UnsettledThirdRulings() =>
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.UnsettledRecordsThird.json");

    /// <summary>
    /// The occurrences in the genealogies a reading answered against the verse the genealogy repeats.
    /// </summary>
    public static OwnRecordRulings GenealogyRulings() => Embedded<OwnRecordRulings>(GenealogyResource);

    /// <summary>
    /// The occurrences of records that held several people, given to the one the verse names, and the
    /// descriptions the verse contradicts, corrected.
    /// </summary>
    public static OwnRecordRulings SeveralPeopleRulings() => Embedded<OwnRecordRulings>(SeveralPeopleResource);

    /// <summary>
    /// The same report read to its end: the records the first reading left, and the words a crossed
    /// link or reading gave to the other king of the name.
    /// </summary>
    public static OwnRecordRulings SeveralPeopleSecondRulings() =>
        Embedded<OwnRecordRulings>(SeveralPeopleSecondResource);

    /// <summary>
    /// The records the portrait briefs of 2026-09-27 found holding several people, the names a
    /// mapping crossed in the translations, and the earlier rulings a reading of the lists corrects.
    /// </summary>
    public static OwnRecordRulings SeveralPeopleThirdRulings() =>
        Embedded<OwnRecordRulings>(SeveralPeopleThirdResource);

    /// <summary>
    /// The names several men share that the genealogy review left unnamed, each read against its
    /// verse: the record the text makes him, or a person of his own where it gives him a family or
    /// an office no held record has.
    /// </summary>
    public static OwnRecordRulings NamesakeRulings() => Embedded<OwnRecordRulings>(NamesakeResource);

    /// <summary>
    /// The names a dataset lists a verse for and no word of ours named there, or named another bearer
    /// of: each read against its verse by two models that did not see each other's answer, and kept
    /// where both gave the same record.
    /// </summary>
    public static OwnRecordRulings NamesakeSecondRulings() => Embedded<OwnRecordRulings>(NamesakeSecondResource);

    /// <summary>
    /// The record of Deborah that held Rebekah's nurse and the judge, whose number the dataset had
    /// written as another word's: each occurrence given to the woman its verse names.
    /// </summary>
    public static OwnRecordRulings SeveralPeopleFourthRulings() =>
        Embedded<OwnRecordRulings>(SeveralPeopleFourthResource);

    /// <summary>
    /// The words in which somebody is addressed by what he or she is to the speaker, given to the person
    /// addressed: <em>Daughter</em>, said to the woman with the issue of blood.
    /// </summary>
    public static OwnRecordRulings AddressedRulings() => Embedded<OwnRecordRulings>(AddressedResource);

    /// <summary>
    /// The records a dataset supplied that no word of ours named: the word each one's verse prints
    /// given to it — in the Hebrew title of a psalm, or in the Septuagint where only the Septuagint
    /// names him — and the heading of one the text spells otherwise.
    /// </summary>
    public static OwnRecordRulings DatasetRecordRulings() => Embedded<OwnRecordRulings>(DatasetRecordResource);

    /// <summary>Every rulings file, in the order they were decided.</summary>
    public static IReadOnlyList<OwnRecordRulings> AllRulings() =>
    [
        Rulings(), ReviewRulings(), ReportRulings(), TitleRulings(), UnsettledRulings(), GenealogyRulings(),
        SeveralPeopleRulings(), SeveralPeopleSecondRulings(), SeveralPeopleThirdRulings(), NamesakeRulings(),
        UnsettledSecondRulings(), SeveralPeopleFourthRulings(), AddressedRulings(), NamesakeSecondRulings(),
        DatasetRecordRulings(), Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.PhilipRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.SynodalJehoiakimRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.RevelationTribeRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.SynodalChristTitleRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.OhienkoAdamRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.SynodalChristBearerRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.SeveralPeopleRecordsFifth.json"),
        UnsettledThirdRulings(),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.ReviewNoteRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.OhienkoGentilicRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.NoNameRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.SecondBearerRecords.json"),
        Embedded<OwnRecordRulings>("Essenthos.Core.Loading.Encyclopedia.KinshipRecords.json"),
    ];

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
