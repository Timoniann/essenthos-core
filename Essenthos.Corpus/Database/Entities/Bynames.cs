using System.Text.Json;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A person's byname as <see cref="Entity.Byname"/> holds it: the verse that gives it, and the byname
/// as each language's text prints it, more than one where the verse gives more (<em>Joseph called
/// Barsabas, who was surnamed Justus</em>, ACT 1:23).
/// </summary>
/// <param name="Names">Language code to the bynames in that language's text, in the verse's order.</param>
public sealed record Bynames(int Book, int Chapter, int Verse, IReadOnlyDictionary<string, IReadOnlyList<string>> Names)
{
    private static readonly JsonSerializerOptions Shape = new(JsonSerializerDefaults.Web);

    public static Bynames? Read(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Bynames>(json, Shape);

    public string Write() => JsonSerializer.Serialize(this, Shape);
}

/// <summary>What <see cref="TitleBearer.Called"/> holds: language code to the name the bearer is called by.</summary>
public static class TitleCalled
{
    private static readonly JsonSerializerOptions Shape = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, string>? Read(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json, Shape);

    public static string? Write(IReadOnlyDictionary<string, string>? called) =>
        called is { Count: > 0 }
            ? JsonSerializer.Serialize(new SortedDictionary<string, string>(called.ToDictionary(), StringComparer.Ordinal), Shape)
            : null;
}
