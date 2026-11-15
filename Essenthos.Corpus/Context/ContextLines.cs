using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Descriptions;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Context;

/// <summary>
/// One record as a chapter's context shows it in a language of words and a language of names, whatever the chapter: its
/// name in the language of names, our own line in the language of words, and the line's claims with every record they
/// point at (<see cref="ContextDescriptors"/>). Stored per record and pair of languages (<c>context_line</c>).
/// </summary>
/// <param name="Records">The records the claims point at, by slug, as the answer's one map holds them.</param>
internal sealed record ContextLineBody(
    string? LocalName,
    string? LocalDistinguisher,
    ContextDescriptorResponse? Descriptor,
    Dictionary<string, ContextRecordResponse>? Records)
{
    public static readonly ContextLineBody Empty = new(null, null, null, null);
}

internal static class ContextLines
{
    /// <summary>Raise with any change to what a line holds or how it is composed: a stored one is made again only when this moves.</summary>
    public const int Version = 1;

    public static string Write(ContextLineBody line) => JsonSerializer.Serialize(line, ContextDocumentJson.Default.ContextLineBody);

    public static ContextLineBody Read(string body) =>
        JsonSerializer.Deserialize(body, ContextDocumentJson.Default.ContextLineBody)
        ?? throw new InvalidDataException("A stored context line is empty.");

    /// <summary>
    /// The lines of the records named, as the context composes them for the languages given: the language of names as the
    /// request names it, the language of words as <see cref="ReaderLanguages.Prose"/> reads the two. A slug no record has is
    /// left out; every record named has a line, empty where nothing is said.
    /// </summary>
    public static async Task<Dictionary<string, ContextLineBody>> Gather(
        AppDbContext db,
        IReadOnlyCollection<string> slugs,
        string? language,
        string? prose,
        CancellationToken cancellationToken)
    {
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug })
            .ToListAsync(cancellationToken);
        var local = await EntityNames.Of(db, [.. records.Select(r => r.Id)], language, cancellationToken);
        var words = ReaderLanguages.Prose(prose, language);
        var lines = await EntityDistinguishers.OfSlugs(db, [.. records.Select(r => r.Slug)], words, cancellationToken);
        var described = await Descriptors.Of(db, records.Select(r => r.Slug), words, cancellationToken, language);

        var composed = new Dictionary<string, ContextLineBody>(StringComparer.Ordinal);
        foreach (var record in records.OrderBy(r => r.Id))
        {
            var slim = described.TryGetValue(record.Slug, out var descriptor)
                ? ContextDescriptors.Slim(new Dictionary<string, EntityDescriptorResponse>(StringComparer.Ordinal) { [record.Slug] = descriptor })
                : null;
            composed[record.Slug] = new ContextLineBody(
                local.GetValueOrDefault(record.Id),
                lines.GetValueOrDefault(record.Slug),
                slim?.Lines.GetValueOrDefault(record.Slug),
                slim is null || slim.Records.Count == 0 ? null : new Dictionary<string, ContextRecordResponse>(slim.Records, StringComparer.Ordinal));
        }

        return composed;
    }
}
