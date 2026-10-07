using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Translations added or rewritten: all of them on a cold corpus, none after.</param>
/// <param name="Removed">Translations no entry asks for any more, or whose record's English has changed.</param>
/// <param name="Stale">
/// Entries whose English is no longer what the record says. They are not stored: a translation of
/// what a record used to say would be served as what it says.
/// </param>
/// <param name="Missing">Entries for a record the encyclopedia does not hold.</param>
/// <param name="Unsound">Entries whose digest is not the digest of their own English.</param>
internal sealed record NoteTranslationOutcome(
    int Written, int Removed, int Stale, int Missing, int Unsound, TimeSpan Elapsed)
{
    public override string ToString() =>
        Written == 0 && Removed == 0 && Stale == 0 && Missing == 0 && Unsound == 0
            ? "the notes of our own records in every language are already there"
            : $"{Written} translations of our own records' notes written, {Removed} removed, {Stale} entries " +
              $"whose English the record no longer says, {Missing} for records the encyclopedia does not hold, " +
              $"{Unsound} whose digest is not their English's, in {Elapsed}";
}

/// <summary>
/// The notes of the records this corpus wrote — objects, appointed times, the beings the narratives
/// turn on, titles, peoples and our own person records — in Ukrainian, German and Spanish, read from
/// <c>NoteTranslations.json</c>.
///
/// <para>
/// An entry names its record by slug and carries the SHA-256 of the English it renders. It is kept
/// only while the record still says that English; the reader is otherwise shown the English, which
/// is true, rather than a translation of something the record no longer says. The serving side asks
/// the same of every row, so a record rewritten by a load that did not reach this step is not
/// answered with the old translation either.
/// </para>
///
/// <para>
/// Idempotent and atomic: the rows are this file's alone, a row is written only where it differs, a
/// row no entry asks for is removed, and all of it is one save.
/// </para>
/// </summary>
internal sealed class NoteTranslationLoader(AppDbContext db, ILogger<NoteTranslationLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.NoteTranslations.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task<NoteTranslationOutcome> Load(CancellationToken cancellationToken = default) =>
        Load(Read(), cancellationToken);

    internal async Task<NoteTranslationOutcome> Load(
        IReadOnlyList<NoteTranslationRecord> records,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var slugs = records.Select(r => r.Slug).Distinct().ToList();
        var entities = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug, e.Notes })
            .ToDictionaryAsync(e => e.Slug, StringComparer.Ordinal, cancellationToken);
        var held = await db.EntityNoteTranslations.ToListAsync(cancellationToken);

        var wanted = new Dictionary<(int Entity, string Language), (string Text, string Sha, string Source)>();
        int stale = 0, missing = 0, unsound = 0;
        foreach (var record in records)
        {
            if (!string.Equals(EnglishNotes.Digest(record.English), record.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "The note translation of \"{Slug}\" from {File} carries a digest that is not the SHA-256 of its " +
                    "own English, so it is not loaded. The digest is of the English notes string exactly as the " +
                    "records file holds it, UTF-8, in hex", record.Slug, record.File);
                unsound++;
                continue;
            }

            if (!entities.TryGetValue(record.Slug, out var entity))
            {
                logger.LogWarning(
                    "NoteTranslations.json renders the notes of \"{Slug}\" from {File}, and the encyclopedia holds " +
                    "no record of that slug. Either it has not been loaded or the record was renamed", record.Slug,
                    record.File);
                missing++;
                continue;
            }

            if (!EnglishNotes.Renders(entity.Notes, record.Sha256))
            {
                stale++;
                continue;
            }

            foreach (var (language, text) in record.Languages())
            {
                wanted[(entity.Id, language)] = (text, record.Sha256.ToLowerInvariant(), record.Source);
            }
        }

        var removed = 0;
        foreach (var row in held.Where(r => !wanted.ContainsKey((r.EntityId, r.Language))))
        {
            db.EntityNoteTranslations.Remove(row);
            removed++;
        }

        var written = 0;
        var byKey = held.ToDictionary(r => (r.EntityId, r.Language));
        foreach (var ((entityId, language), (text, sha, source)) in wanted)
        {
            if (!byKey.TryGetValue((entityId, language), out var row))
            {
                db.EntityNoteTranslations.Add(new EntityNoteTranslation
                {
                    EntityId = entityId,
                    Language = language,
                    Text = text,
                    EnglishSha256 = sha,
                    Source = source,
                });
                written++;
            }
            else if (row.Text != text || row.EnglishSha256 != sha || row.Source != source)
            {
                row.Text = text;
                row.EnglishSha256 = sha;
                row.Source = source;
                written++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new NoteTranslationOutcome(written, removed, stale, missing, unsound, started.Elapsed);
        if (stale > 0)
        {
            logger.LogWarning(
                "{Stale} note translations render English their records no longer say, and are not shown: the " +
                "records' notes were rewritten since they were translated. Translate the current notes again and " +
                "put them in NoteTranslations.json", stale);
        }

        logger.LogInformation("The notes of our own records in every language: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>The file, which is an empty array until the notes have been rendered.</summary>
    internal static IReadOnlyList<NoteTranslationRecord> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<List<NoteTranslationRecord>>(stream, Shape)
               ?? throw new InvalidDataException($"{Resource} is empty. It should hold an array, [] where nothing is rendered.");
    }
}

/// <summary>
/// One record's notes rendered into the interface's other languages, as the rendering pass writes it.
/// </summary>
/// <param name="File">The records file the notes are from: <c>ObjectRecords.json</c>.</param>
/// <param name="Sha256">SHA-256 of <paramref name="English"/>, UTF-8, hex.</param>
/// <param name="English">The notes exactly as the records file holds them.</param>
internal sealed record NoteTranslationRecord(
    string File,
    string Slug,
    string Sha256,
    string English,
    string? Ukr,
    string? Deu,
    string? Spa,
    string? Model,
    string? Effort,
    string? At)
{
    /// <summary>Who rendered it, in the words a row carries.</summary>
    public string Source =>
        $"rendered from our own English notes by {Model ?? "a model"}" +
        (string.IsNullOrWhiteSpace(Effort) ? string.Empty : $", effort {Effort}") +
        (string.IsNullOrWhiteSpace(At) ? string.Empty : $", {At}");

    public IEnumerable<(string Language, string Text)> Languages() =>
        new (string Language, string? Text)[] { ("ukr", Ukr), ("deu", Deu), ("spa", Spa) }
            .Where(one => !string.IsNullOrWhiteSpace(one.Text))
            .Select(one => (one.Language, one.Text!.Trim()));
}
