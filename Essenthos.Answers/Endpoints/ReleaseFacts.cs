using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The constants of a corpus release that take sweeps of the largest tables to count: what each text holds and is linked to
/// (<see cref="TextFacts"/>), the rows each dataset accounts for (<see cref="DatasetEndpoints"/>) and what the Chapter Context
/// weighs against (<see cref="ContextWeights"/>). The serving build counts them once (<c>release-facts</c>, table
/// <c>release_fact</c>) and the API reads them by key. A database without them, or with facts counted by other code than this
/// API's (<see cref="Stamp"/>), is counted here as before.
/// </summary>
internal static class ReleaseFacts
{
    /// <summary>Raise with any change to what the facts are counted from or how.</summary>
    public const int Version = 1;

    public const string TextsKey = "texts";

    public const string DatasetsKey = "datasets";

    public const string ContextWeightsKey = "context-weights";

    public static readonly string[] Keys = [TextsKey, DatasetsKey, ContextWeightsKey];

    /// <summary>
    /// The version of the counting code and the dataset declarations it credits rows by: a dataset added, or one whose prefixes
    /// or kinds of rows changed, makes the stored facts another API's.
    /// </summary>
    public static string Stamp { get; } = MakeStamp();

    private static string MakeStamp()
    {
        var declared = string.Join('\n', Datasets.All.Select(d => string.Join('|',
            d.Id, d.Prefix, string.Join(',', d.Methods ?? []), d.Lemmas, d.WordGlosses, d.Links, d.Lexicon, d.Glossary,
            d.Parsings, d.LinkClaims, d.Frame, d.Unlisted)));
        return $"{Version}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(declared)))[..16]}";
    }

    /// <summary>The stored fact under <paramref name="key"/> counted by this API's code; null where there is none.</summary>
    public static async Task<string?> Read(AppDbContext db, string key, CancellationToken cancellationToken)
    {
        try
        {
            return await db.ReleaseFacts.AsNoTracking()
                .Where(f => f.Key == key && f.Stamp == Stamp)
                .Select(f => f.Body)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }

    /// <summary>Every fact counted from the database as it stands, by key: what the build stores.</summary>
    public static async Task<IReadOnlyList<(string Key, string Body)>> Count(IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        var texts = await TextFacts.Count(scopes, cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var datasets = await StatementTimeout.Unhurried(db, () => DatasetEndpoints.Count(db, cancellationToken), cancellationToken);
        await using var other = scopes.CreateAsyncScope();
        var weights = await ContextWeights.Count(other.ServiceProvider.GetRequiredService<AppDbContext>(), cancellationToken);
        return
        [
            (TextsKey, WriteTexts(texts)),
            (DatasetsKey, WriteDatasets(datasets)),
            (ContextWeightsKey, WriteWeights(weights)),
        ];
    }

    public static string WriteTexts(IReadOnlyDictionary<string, TextTally> texts) =>
        JsonSerializer.Serialize(texts.OrderBy(t => t.Key, StringComparer.Ordinal).ToDictionary(), ReleaseFactJson.Default.DictionaryStringTextTally);

    public static IReadOnlyDictionary<string, TextTally> ReadTexts(string body) =>
        JsonSerializer.Deserialize(body, ReleaseFactJson.Default.DictionaryStringTextTally)
        ?? throw new InvalidDataException("The stored texts' facts are empty.");

    public static string WriteDatasets(DatasetTally tally) =>
        JsonSerializer.Serialize(
            new StoredDatasets([.. tally.Counted.Select(c => new StoredDatasetCounts(c.Dataset.Id, c.Counts))], [.. tally.Undeclared]),
            ReleaseFactJson.Default.StoredDatasets);

    /// <summary>The stored counts, each under the declaration of its dataset; null where one is no longer declared.</summary>
    public static DatasetTally? ReadDatasets(string body)
    {
        var stored = JsonSerializer.Deserialize(body, ReleaseFactJson.Default.StoredDatasets)
                     ?? throw new InvalidDataException("The stored datasets' counts are empty.");
        var counted = new List<(Datasets.Dataset Dataset, DatasetCounts Counts)>();
        foreach (var row in stored.Counted)
        {
            if (Datasets.All.FirstOrDefault(d => d.Id == row.Id) is not { } dataset)
            {
                return null;
            }

            counted.Add((dataset, row.Counts));
        }

        return new DatasetTally(counted, [.. stored.Undeclared]);
    }

    public static string WriteWeights(ContextWeights weights) =>
        JsonSerializer.Serialize(weights.Stored(), ReleaseFactJson.Default.StoredContextWeights);

    public static ContextWeights ReadWeights(string body) =>
        ContextWeights.From(JsonSerializer.Deserialize(body, ReleaseFactJson.Default.StoredContextWeights)
                            ?? throw new InvalidDataException("The stored Chapter Context weights are empty."));
}

internal sealed record StoredDatasetCounts(string Id, DatasetCounts Counts);

internal sealed record StoredDatasets(IList<StoredDatasetCounts> Counted, IList<UndeclaredResponse> Undeclared);

internal sealed record StoredTopicSubject(IList<string> Slugs, bool Personal);

internal sealed record StoredContextWeights(
    Dictionary<string, int> TopicVerses,
    Dictionary<string, StoredTopicSubject> About,
    IList<string> Places,
    Dictionary<string, int> EntityChapters);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, TextTally>))]
[JsonSerializable(typeof(StoredDatasets))]
[JsonSerializable(typeof(StoredContextWeights))]
internal sealed partial class ReleaseFactJson : JsonSerializerContext;
