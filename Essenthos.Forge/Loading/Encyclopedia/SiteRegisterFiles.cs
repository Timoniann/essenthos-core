using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>One reading of one occurrence, with the model that made it and the day it was asked.</summary>
internal sealed record SiteReadingRecord(
    string? Referent,
    string? Confidence,
    string? Reason,
    string? Model,
    string? Effort,
    string? PromptVersion,
    string? AskedAt,
    string? Note);

/// <summary>
/// The second reading of an occurrence the first reading and the gazetteer do not settle the same
/// way. It is shown both answers and may uphold neither.
/// </summary>
internal sealed record SiteCheckRecord(
    string? Referent,
    string? Why,
    string? Model,
    string? PromptVersion,
    string? AskedAt);

/// <summary>
/// One occurrence of a name several sites bear, as the pass in <c>scripts/sites.py</c> decided it.
/// </summary>
/// <param name="Referent">
/// The place the occurrence names, or null where nothing settled it. Null is an answer and it is
/// carried on purpose: what a register is asked next is why a verse is not on the page.
/// </param>
/// <param name="Standing">
/// What settled it — <see cref="SiteRegisterFiles.ByBoth"/>, <see cref="SiteRegisterFiles.ByTheGazetteer"/>
/// or <see cref="SiteRegisterFiles.ByTheReading"/>. It decides the method, the confidence and the
/// source the annotation is written under, because those three are the difference between two
/// independent accounts agreeing and one account speaking alone.
/// </param>
/// <param name="Gazetteer">
/// Which of this number's records the geocoding dataset places in the verse. One is its answer;
/// none or several is its silence, and the reading is then on its own.
/// </param>
/// <param name="OtherDataset">The same from the second dataset, which speaks about a twentieth of these.</param>
/// <param name="Annotated">
/// What the corpus already annotates the word with. A word that already names somebody is not
/// re-answered here: an occurrence naming two places is worse than one naming none.
/// </param>
/// <param name="Address">
/// The word, by its address (<see cref="RuledWord"/>): a row id names another word after a rebuild.
/// </param>
internal sealed record SiteRegisterRecord(
    string Number,
    string Name,
    RuledWord Address,
    string? Witness,
    string? Reference,
    string? Spelling,
    string? Referent,
    string? Standing,
    string Why,
    IReadOnlyList<string>? Candidates,
    IReadOnlyList<string>? Gazetteer,
    IReadOnlyList<string>? OtherDataset,
    IReadOnlyList<string>? Annotated,
    SiteReadingRecord? Reading,
    SiteCheckRecord? Check)
{
    /// <summary>Where <see cref="Address"/> stands in the corpus being loaded; never read from the file.</summary>
    [JsonIgnore]
    public long WordId { get; init; }
}

/// <summary>
/// Where the site register is read from, and what one line of one has to be.
///
/// It is the output of the pass in <c>scripts/sites.py</c> — one file per two hundred occurrences,
/// one JSON object per line — and it lives with the corpus sources under this project's own folder,
/// beside the descriptors, the name forms, the places and the persons, because it is a model run and
/// not a fetch. A checkout without it loads nothing and says so.
/// </summary>
internal static class SiteRegisterFiles
{
    public const string ConfigurationKey = "Dataset:SiteRegisterPath";

    public static readonly string DefaultFolder = Path.Combine("Essenthos", "sites");

    public const string FilePattern = "*.jsonl";

    /// <summary>
    /// The gazetteer's verse list and a reading of the verse, arrived at independently, naming the
    /// same record. The reading never saw the list: every verse being asked about is struck out of
    /// each candidate's attestation before the payload is written.
    /// </summary>
    public const string ByBoth = "agreed";

    /// <summary>The gazetteer alone, the reading having declined to choose.</summary>
    public const string ByTheGazetteer = "gazetteer";

    /// <summary>
    /// A reading of the verse, upheld on a second reading, where the gazetteer places nobody or
    /// places two of them at once.
    /// </summary>
    public const string ByTheReading = "reading";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Each record on the word its address names in the corpus the connection reads, and the
    /// addresses that name no word there or a word that reads otherwise now.
    /// </summary>
    public static async Task<(IReadOnlyList<SiteRegisterRecord> Placed, IReadOnlyList<RuledWord> Lost)> Place(
        NpgsqlConnection connection,
        IReadOnlyList<SiteRegisterRecord> records,
        CancellationToken cancellationToken = default)
    {
        var (found, lost) = await RuledWords.Find(connection, records.Select(r => r.Address), cancellationToken);
        return ([.. records.Where(r => found.ContainsKey(r.Address)).Select(r => r with { WordId = found[r.Address] })], lost);
    }

    public static IReadOnlyList<SiteRegisterRecord> Read(string directory)
    {
        var records = new List<SiteRegisterRecord>();
        foreach (var file in Directory
                     .EnumerateFiles(directory, FilePattern, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var record =
                    JsonSerializer.Deserialize<SiteRegisterRecord>(line, Shape)
                    ?? throw new InvalidDataException(
                        $"A line of {file} is not a register entry. Each line must be one JSON "
                        + "object with at least a number, a name, an address and a why; regenerate "
                        + "the folder with \"python scripts/sites.py publish\".");
                if (record.Address is null || string.IsNullOrEmpty(record.Address.Reference))
                {
                    throw new InvalidDataException(
                        $"A line of {file} names its word by row id or not at all. A register line names its word "
                        + "by its address; convert the register once with scripts/address-word-ids.py before loading it.");
                }

                records.Add(record);
            }
        }

        return records;
    }
}
