using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Descriptions;

/// <summary>
/// What the corpus says each record is, read as the serving build stored it (<c>record_descriptor</c>): the answer
/// <see cref="Descriptors.Of(AppDbContext, IEnumerable{string}, string?, CancellationToken, string?)"/> gives, keyed by the
/// language of words it is phrased in, the language of names and the slug. A pair of languages the build did not make, and
/// a record it holds no row for, is composed here as the build composed the rest.
/// </summary>
internal static class RecordDescriptors
{
    /// <summary>Raise with any change to what a stored descriptor holds or how it is written.</summary>
    public const int Version = 1;

    private static readonly DescriptorJson Json = new(new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static string Write(EntityDescriptorResponse descriptor) => JsonSerializer.Serialize(descriptor, Json.EntityDescriptorResponse);

    public static EntityDescriptorResponse Read(string body) =>
        JsonSerializer.Deserialize(body, Json.EntityDescriptorResponse)
        ?? throw new InvalidDataException("A stored descriptor is empty.");

    /// <summary>
    /// The pair of languages a descriptor asked for in these languages is stored under: the language its words are phrased
    /// in, and the language of names as <see cref="Descriptors"/> reads it. Two requests that differ only where the
    /// descriptors do not look are one answer.
    /// </summary>
    public static (string Prose, string Names) Key(string? language, string? names)
    {
        var spoken = DescriptorPhrasings.Spoken(language);
        return (spoken, string.IsNullOrWhiteSpace(names) ? spoken : names.Trim());
    }

    /// <summary>As <see cref="Descriptors.Of(AppDbContext, IEnumerable{string}, string?, CancellationToken, string?)"/> answers, from the stored rows.</summary>
    public static async Task<Dictionary<string, EntityDescriptorResponse>> Of(
        AppDbContext db,
        IEnumerable<string> slugs,
        string? language,
        CancellationToken cancellationToken,
        string? names = null)
    {
        var wanted = slugs.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var (prose, named) = Key(language, names);
        var stored = await db.RecordDescriptors
            .Where(d => d.Prose == prose && d.Names == named && wanted.Contains(d.Slug))
            .Select(d => new { d.Slug, d.Body })
            .ToListAsync(cancellationToken);

        var found = new Dictionary<string, EntityDescriptorResponse>(StringComparer.Ordinal);
        foreach (var row in stored)
        {
            if (row.Body is not null)
            {
                found[row.Slug] = Read(row.Body);
            }
        }

        if (stored.Count < wanted.Count)
        {
            var held = stored.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal);
            var missing = wanted.Where(slug => !held.Contains(slug)).ToList();
            foreach (var (slug, descriptor) in await Descriptors.Of(db, missing, language, cancellationToken, names))
            {
                found[slug] = descriptor;
            }
        }

        return found;
    }

    /// <summary>The same for one record.</summary>
    public static async Task<EntityDescriptorResponse?> Of(
        AppDbContext db,
        string slug,
        string? language,
        CancellationToken cancellationToken,
        string? names = null) =>
        (await Of(db, [slug], language, cancellationToken, names)).GetValueOrDefault(slug);
}

[JsonSerializable(typeof(EntityDescriptorResponse))]
internal partial class DescriptorJson : JsonSerializerContext;
