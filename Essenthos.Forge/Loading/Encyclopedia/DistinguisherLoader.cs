using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Lines added or rewritten, which is all of them on a cold corpus and none after.</param>
/// <param name="Stale">
/// Records whose English line is no longer the one the file renders, which keep none: a translation
/// of what a record used to say would be served as what it says.
/// </param>
/// <param name="Missing">Records the file renders and the encyclopedia does not hold.</param>
/// <param name="Lined">
/// Records a dataset supplied whose English line was set to the one this project wrote for it, which
/// is all of them on a corpus loaded before the lines were written and none after.
/// </param>
internal sealed record DistinguisherOutcome(int Written, int Stale, int Missing, TimeSpan Elapsed, int Lined = 0)
{
    public override string ToString() =>
        Written == 0 && Stale == 0 && Missing == 0 && Lined == 0
            ? "our own lines in every language are already there"
            : $"{Lined} records given the English line this project wrote for them, " +
              $"{Written} of our own lines written in another language, {Stale} records whose English has " +
              $"changed since it was rendered, {Missing} for records the encyclopedia does not hold, in {Elapsed}";
}

/// <summary>
/// The line under the name of every record whose English line this corpus wrote, in Ukrainian,
/// German and Spanish: the objects, the appointed times, the beings the narratives turn on, the
/// titles, the tribes and the words for God; and the line itself, in English as well, of the records a
/// dataset supplied that this project has read for itself and describes by no clause, which replaces
/// the dataset's.
///
/// <para>
/// A rendering is kept only while the record still says what it renders. The file carries the
/// English beside each translation, and a record whose line has since been rewritten keeps no
/// translation until the file is rendered again — the page then shows the English, which is true,
/// rather than a translation of something it no longer says.
/// </para>
///
/// <para>
/// Idempotent: a row is written only where it differs, and the rows are this file's alone, so a
/// record the file no longer names loses its rows.
/// </para>
/// </summary>
internal sealed class DistinguisherLoader(AppDbContext db, ILogger<DistinguisherLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.Distinguishers.json";

    private const string OwnLinesResource = "Essenthos.Core.Loading.Encyclopedia.OwnLines.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<DistinguisherOutcome> Load(CancellationToken cancellationToken = default) =>
        await Load([Read(), ReadOwnLines()], cancellationToken);

    internal Task<DistinguisherOutcome> Load(DistinguisherFile file, CancellationToken cancellationToken) =>
        Load([file], cancellationToken);

    /// <summary>
    /// Every file's renderings, in one pass because the rows are theirs together: a row no file asks
    /// for any more is removed. A file that <see cref="DistinguisherFile.SetsTheLine"/> first makes its
    /// English the record's line.
    /// </summary>
    internal async Task<DistinguisherOutcome> Load(
        IReadOnlyList<DistinguisherFile> files,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var slugs = files.SelectMany(file => file.Records).Select(r => r.Slug).Distinct().ToList();
        var entities = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, StringComparer.Ordinal, cancellationToken);
        var held = await db.EntityDistinguishers.ToListAsync(cancellationToken);

        var lined = 0;
        foreach (var record in files.Where(file => file.SetsTheLine).SelectMany(file => file.Records))
        {
            if (entities.TryGetValue(record.Slug, out var entity)
                && !string.Equals(entity.Distinguisher, record.English, StringComparison.Ordinal))
            {
                entity.Distinguisher = record.English;
                lined++;
            }
        }

        var wanted = new Dictionary<(int Entity, string Language), (string Text, string English, string Source)>();
        var stale = 0;
        var missing = 0;
        foreach (var (file, record) in files.SelectMany(file => file.Records.Select(record => (file, record))))
        {
            if (!entities.TryGetValue(record.Slug, out var entity))
            {
                logger.LogWarning(
                    "Distinguishers.json renders the line of \"{Slug}\", and the encyclopedia holds no record of " +
                    "that slug. Either it has not been loaded yet or the record was renamed, and the file has to " +
                    "follow it", record.Slug);
                missing++;
                continue;
            }

            if (!string.Equals(entity.Distinguisher, record.English, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "The English line of \"{Slug}\" is no longer the one Distinguishers.json renders, so it keeps " +
                    "no translation. Render \"{English}\" again and put it in the file", record.Slug, entity.Distinguisher);
                stale++;
                continue;
            }

            foreach (var (language, text) in record.Languages())
            {
                wanted[(entity.Id, language)] = (text, record.English, file.Source);
            }
        }

        foreach (var row in held.Where(r => !wanted.ContainsKey((r.EntityId, r.Language))))
        {
            db.EntityDistinguishers.Remove(row);
        }

        var written = 0;
        foreach (var ((entityId, language), (text, english, source)) in wanted)
        {
            var row = held.FirstOrDefault(r => r.EntityId == entityId && r.Language == language);
            if (row is null)
            {
                db.EntityDistinguishers.Add(new EntityDistinguisher
                {
                    EntityId = entityId,
                    Language = language,
                    Text = text,
                    English = english,
                    Source = source,
                });
                written++;
            }
            else if (row.Text != text || row.English != english || row.Source != source)
            {
                row.Text = text;
                row.English = english;
                row.Source = source;
                written++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new DistinguisherOutcome(written, stale, missing, started.Elapsed, lined);
        logger.LogInformation("Our own lines in every language: {Outcome}", outcome);
        return outcome;
    }

    internal static DistinguisherFile Read() => Embedded(Resource);

    /// <summary>
    /// The lines this project wrote from their verses for the records a dataset supplied that it has
    /// read for itself and describes by no clause, in English and rendered.
    /// </summary>
    internal static DistinguisherFile ReadOwnLines() => Embedded(OwnLinesResource) with { SetsTheLine = true };

    private static DistinguisherFile Embedded(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"{resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<DistinguisherFile>(stream, Shape)
               ?? throw new InvalidDataException($"{resource} is empty. It should hold a source and the records.");
    }
}

/// <param name="Source">Who wrote or rendered the lines and when, which every row carries.</param>
internal sealed record DistinguisherFile(string Source, IReadOnlyList<DistinguisherRecord> Records)
{
    /// <summary>
    /// Whether the file's English is the record's line itself rather than a line the record already
    /// holds: the lines this project wrote for records a dataset supplied, which replace the dataset's.
    /// </summary>
    [JsonIgnore]
    public bool SetsTheLine { get; init; }
}

/// <summary>One record's English line and its renderings, keyed by the language's three-letter code.</summary>
internal sealed record DistinguisherRecord(string Slug, string English, string? Ukr, string? Deu, string? Spa)
{
    public IEnumerable<(string Language, string Text)> Languages() =>
        new (string Language, string? Text)[] { ("ukr", Ukr), ("deu", Deu), ("spa", Spa) }
            .Where(one => !string.IsNullOrWhiteSpace(one.Text))
            .Select(one => (one.Language, one.Text!));
}
