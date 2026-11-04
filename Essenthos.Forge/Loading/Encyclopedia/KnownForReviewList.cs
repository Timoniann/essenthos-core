using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// What people, places and things are known for, as a language model wrote it, for the owner to read
/// before any of it is shown: <c>Resources/Essenthos/review/known-for.json</c>. His console writes his
/// answer into each entry; a load takes the entries he approved and nothing else.
/// </summary>
internal static class KnownForReviewList
{
    public const string FileName = "known-for.json";

    public const string ApprovedAnswer = "approved";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The list's path under the resources folder.</summary>
    public static string PathUnder(string resources) => Path.Combine(resources, "Essenthos", "review", FileName);

    /// <summary>The approved entries as a file of their own, carrying the list's source; empty where there is no list.</summary>
    public static KnownForFile Approved(string path)
    {
        if (!File.Exists(path))
        {
            return new KnownForFile(string.Empty, []);
        }

        var list = Read(path);
        var approved = list.Records
            .Where(entry => entry.Decision?.Answer == ApprovedAnswer)
            .Select(entry => new KnownForRecord(entry.Slug, entry.Clauses))
            .ToList();
        if (approved.Count > 0 && string.IsNullOrWhiteSpace(list.Source))
        {
            throw new InvalidDataException(
                $"{path} approves {approved.Count} records and says nothing of who wrote them. Give the list its source.");
        }

        return new KnownForFile(list.Source ?? string.Empty, approved);
    }

    /// <summary>Every entry of the list, answered or not.</summary>
    public static KnownForReviewFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<KnownForReviewFile>(stream, Shape)
               ?? throw new InvalidDataException($"{path} is empty. It should hold a source and the records.");
    }
}

/// <summary>The owner's word on one entry: <c>approved</c> or <c>rejected</c>.</summary>
internal sealed record KnownForDecision(string? Answer, string? Note, string? At);

internal sealed record KnownForReviewEntry(string Slug, IReadOnlyList<KnownForClause> Clauses, KnownForDecision? Decision);

internal sealed record KnownForReviewFile(string? Source, IReadOnlyList<KnownForReviewEntry> Records);
