using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Source">Who wrote and rendered the clauses and when, which every row carries.</param>
internal sealed record KnownForFile(string Source, IReadOnlyList<KnownForRecord> Records)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.KnownFor.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary><c>KnownFor.json</c>, embedded in the Forge: the records whose clauses the file holds and no console answer overrides.</summary>
    internal static KnownForFile Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<KnownForFile>(stream, Shape)
               ?? throw new InvalidDataException($"{Resource} is empty. It should hold a source and the records.");
    }
}

/// <summary>One person, by slug, and what they are known for, in the order it is told.</summary>
internal sealed record KnownForRecord(string Slug, IReadOnlyList<KnownForClause> Clauses);

/// <summary>
/// One clause, which reads after <em>known for</em>, in English and rendered, keyed by the language's
/// three-letter code, and the runs of verses that say it, as the record files write a span.
/// </summary>
internal sealed record KnownForClause(
    string English,
    string? Ukr,
    string? Deu,
    string? Spa,
    IReadOnlyList<string> References)
{
    public IEnumerable<(string Language, string Text)> Languages() =>
        new (string Language, string? Text)[] { ("ukr", Ukr), ("deu", Deu), ("spa", Spa) }
            .Where(one => !string.IsNullOrWhiteSpace(one.Text))
            .Select(one => (one.Language, one.Text!));
}
