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

    public async Task<OwnNameOutcome> Load(CancellationToken cancellationToken = default)
    {
        await Correct(cancellationToken);
        return await Load(Read().Names, cancellationToken);
    }

    /// <summary>
    /// The Strong numbers a dataset wrote on a name that are another word's, set to the name's own.
    ///
    /// <para>
    /// BibleData numbers the judge Deborah H1682, which is the word for a bee, where the name is
    /// H1683, and Antichrist G5000, which is Tabitha. A wrong number is worse than none: the name
    /// then resolves to whichever record has the right one, as every Deborah of Judges did to
    /// Rebekah's nurse, or stops resolving at all because a second record claims it, as Tabitha's
    /// did. A row still carrying the number the list says was wrong is given the right one; where
    /// the record already has the label under the right number the wrong row says nothing more and
    /// goes.
    /// </para>
    ///
    /// <para>
    /// Run before the words are annotated, so they are read against the corrected numbers, and again
    /// with the names, because a fold after that may bring a record a row the list corrects. A row
    /// already corrected is not found, so a second run changes nothing.
    /// </para>
    /// </summary>
    /// <returns>The rows corrected or removed on this run.</returns>
    public async Task<int> Correct(CancellationToken cancellationToken = default) =>
        await Correct(Read().Numbers ?? [], cancellationToken);

    internal async Task<int> Correct(IReadOnlyList<CorrectedNumber> corrections, CancellationToken cancellationToken)
    {
        var slugs = corrections.SelectMany(correction => correction.Entities).Distinct(StringComparer.Ordinal).ToList();
        var records = await db.Entities
            .Where(entity => slugs.Contains(entity.Slug))
            .Include(entity => entity.Names)
            .ToDictionaryAsync(entity => entity.Slug, StringComparer.Ordinal, cancellationToken);

        var corrected = 0;
        foreach (var correction in corrections)
        {
            foreach (var slug in correction.Entities)
            {
                if (!records.TryGetValue(slug, out var record))
                {
                    continue;
                }

                var wrong = record.Names
                    .Where(name => string.Equals(name.Label, correction.Label, StringComparison.Ordinal)
                                   && string.Equals(name.HebrewStrongNumber, correction.Was.Hebrew, StringComparison.Ordinal)
                                   && string.Equals(name.GreekStrongNumber, correction.Was.Greek, StringComparison.Ordinal))
                    .ToList();
                foreach (var name in wrong)
                {
                    var held = record.Names.Any(other =>
                        !ReferenceEquals(other, name)
                        && string.Equals(other.Label, correction.Label, StringComparison.Ordinal)
                        && string.Equals(other.HebrewStrongNumber, correction.HebrewStrongNumber, StringComparison.Ordinal)
                        && string.Equals(other.GreekStrongNumber, correction.GreekStrongNumber, StringComparison.Ordinal));
                    if (held)
                    {
                        record.Names.Remove(name);
                        db.EntityNames.Remove(name);
                    }
                    else
                    {
                        name.HebrewStrongNumber = correction.HebrewStrongNumber;
                        name.GreekStrongNumber = correction.GreekStrongNumber;
                    }

                    logger.LogInformation(
                        "Corrected the number of {Label} on {Slug}: {Why}", correction.Label, slug, correction.Why);
                    corrected++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return corrected;
    }

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

    private static OwnNames Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<OwnNames>(stream, Shape) ?? new OwnNames([], []);
    }
}

/// <summary>The embedded list, as a file.</summary>
internal sealed record OwnNames(IReadOnlyList<OwnName> Names, IReadOnlyList<CorrectedNumber>? Numbers);

/// <summary>
/// The Strong numbers a label should carry on these records, and the ones a dataset wrote that it
/// takes the place of; a side left out is no number on that side.
/// </summary>
internal sealed record CorrectedNumber(
    IReadOnlyList<string> Entities,
    string Label,
    WrongNumber Was,
    string? HebrewStrongNumber,
    string? GreekStrongNumber,
    string Why);

/// <summary>The numbers a dataset wrote on a name row, as the row still carries them.</summary>
internal sealed record WrongNumber(string? Hebrew, string? Greek);

/// <summary>One name and the records it is given to, with why.</summary>
internal sealed record OwnName(
    IReadOnlyList<string> Entities,
    string Label,
    string? Hebrew,
    string? HebrewTransliterated,
    string? HebrewStrongNumber,
    string? Meaning,
    string Why);
