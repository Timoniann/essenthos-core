using System.Text.Json;

namespace Essenthos.Core.Database.Entities;

/// <summary>What <see cref="EntityAlternative.ReasonRenderings"/> holds: language code to the reason in that language.</summary>
public static class ReasonRenderings
{
    private static readonly JsonSerializerOptions Shape = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, string>? Read(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json, Shape);

    public static string? Write(IReadOnlyDictionary<string, string>? renderings) =>
        renderings is { Count: > 0 }
            ? JsonSerializer.Serialize(new SortedDictionary<string, string>(renderings.ToDictionary(), StringComparer.Ordinal), Shape)
            : null;

    /// <summary>The reason in <paramref name="language"/> (<c>ukr</c>, <c>deu</c>, <c>spa</c>), or null where none is held.</summary>
    public static string? In(string? json, string? language) =>
        language is null ? null : Read(json)?.GetValueOrDefault(language);

    /// <summary>Whether two stored values say the same thing, whatever order or spacing the database gave them.</summary>
    public static bool Same(string? one, string? other)
    {
        var a = Read(one);
        var b = Read(other);
        return a is null || b is null
            ? a is null && b is null
            : a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var text) && text == pair.Value);
    }
}
