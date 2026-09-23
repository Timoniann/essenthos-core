using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Names added, which is all of them on a cold corpus and none after.</param>
/// <param name="Missing">Names for a record the encyclopedia does not hold, which the log names.</param>
internal sealed record OwnNameOutcome(int Written, int Missing, TimeSpan Elapsed)
{
    public override string ToString() =>
        Written == 0 && Missing == 0
            ? "the names this corpus gives records of other datasets are already there"
            : $"{Written} names this corpus gives records of other datasets written, {Missing} for records it " +
              $"does not hold, in {Elapsed}";
}

/// <summary>
/// A name a record should answer to that no dataset gives it — <em>heavens</em> for heaven, whose
/// Hebrew has no singular.
///
/// <para>
/// A name row and nothing else: the record, its verses and its other names stay the dataset's, and
/// a name already there under the same label is left as it is, so a boot after the first writes
/// nothing and a name added to the list later is written on its own.
/// </para>
/// </summary>
internal sealed class OwnNameLoader(AppDbContext db, ILogger<OwnNameLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.OwnNames.json";

    /// <summary>What a name row written here is, beside a title, as the datasets spell it.</summary>
    private const string NameKind = "name";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<OwnNameOutcome> Load(CancellationToken cancellationToken = default) =>
        await Load(Read(), cancellationToken);

    internal async Task<OwnNameOutcome> Load(IReadOnlyList<OwnName> names, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var slugs = names.SelectMany(name => name.Entities).Distinct(StringComparer.Ordinal).ToList();
        var records = await db.Entities
            .Where(entity => slugs.Contains(entity.Slug))
            .Include(entity => entity.Names)
            .ToDictionaryAsync(entity => entity.Slug, StringComparer.Ordinal, cancellationToken);

        var written = 0;
        var missing = 0;
        foreach (var name in names)
        {
            foreach (var slug in name.Entities)
            {
                if (!records.TryGetValue(slug, out var record))
                {
                    logger.LogWarning(
                        "The name {Label} is given to {Slug}, and the encyclopedia holds no record of that slug",
                        name.Label, slug);
                    missing++;
                    continue;
                }

                if (record.Names.Any(held => string.Equals(held.Label, name.Label, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                record.Names.Add(new EntityName
                {
                    Label = name.Label,
                    Hebrew = name.Hebrew,
                    HebrewTransliterated = name.HebrewTransliterated,
                    HebrewStrongNumber = name.HebrewStrongNumber,
                    Meaning = name.Meaning,
                    Kind = NameKind,
                });
                written++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new OwnNameOutcome(written, missing, started.Elapsed);
        logger.LogInformation("The names of our own: {Outcome}", outcome);
        return outcome;
    }

    private static IReadOnlyList<OwnName> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<OwnNames>(stream, Shape)?.Names ?? [];
    }
}

/// <summary>The embedded list, as a file.</summary>
internal sealed record OwnNames(IReadOnlyList<OwnName> Names);

/// <summary>One name and the records it is given to, with why.</summary>
internal sealed record OwnName(
    IReadOnlyList<string> Entities,
    string Label,
    string? Hebrew,
    string? HebrewTransliterated,
    string? HebrewStrongNumber,
    string? Meaning,
    string Why);
