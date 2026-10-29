using System.Diagnostics;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Renderings added or rewritten: all of them on a cold corpus, none after.</param>
/// <param name="Removed">Renderings no file gives any more.</param>
/// <param name="Files">The files read.</param>
internal sealed record TextTranslationOutcome(int Written, int Removed, int Files, int Held, TimeSpan Elapsed)
{
    public override string ToString() =>
        Written == 0 && Removed == 0
            ? $"the {Held} machine translations of the records' English are already there"
            : $"{Written} machine translations of the records' English written and {Removed} removed, " +
              $"{Held} held, from {Files} file(s), in {Elapsed}";
}

/// <summary>
/// The machine translations of the English the records carry from their sources — events, reckonings'
/// arithmetic, places' modern sites, record notes — read from <c>Essenthos/translations/*.json</c>.
///
/// <para>
/// A file is <c>{ "source": "…", "entries": [ { "english": "…", "ukr": "…", "deu": "…", "spa": "…" } ] }</c>.
/// A rendering is keyed by the SHA-256 of its English and by nothing in the corpus, so it is stored
/// whatever the records say today; the serving side finds it only for a row that still says that
/// English. A later file renders an English an earlier one also renders.
/// </para>
///
/// <para>
/// Idempotent and atomic: the rows are these files' alone, a row is written only where it differs, a
/// row no file gives is removed, and all of it is one save.
/// </para>
/// </summary>
internal sealed class TextTranslationLoader(AppDbContext db, ILogger<TextTranslationLoader> logger)
{
    public const string Folder = "translations";

    public async Task<TextTranslationOutcome> Load(string resources, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(resources, "Essenthos", Folder);
        var files = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList()
            : [];
        if (files.Count == 0)
        {
            logger.LogWarning("No machine translations of the records' English at {Folder}; the pages show the English", folder);
        }

        var wanted = new Dictionary<(string Language, string Sha), (string Text, string Source)>();
        foreach (var file in files)
        {
            var read = JsonSerializer.Deserialize<TextTranslationFile>(
                await File.ReadAllTextAsync(file, cancellationToken), Shape)
                ?? throw new InvalidDataException($"{file} holds no translations file");
            if (string.IsNullOrWhiteSpace(read.Source))
            {
                throw new InvalidDataException($"{file} does not say what rendered it: give it a \"source\"");
            }

            foreach (var entry in read.Entries)
            {
                var sha = EnglishNotes.Digest(entry.English);
                foreach (var (language, text) in entry.Rendered())
                {
                    wanted[(language, sha)] = (text, read.Source);
                }
            }
        }

        return await Load(wanted, files.Count, cancellationToken);
    }

    internal async Task<TextTranslationOutcome> Load(
        IReadOnlyDictionary<(string Language, string Sha), (string Text, string Source)> wanted,
        int files,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var held = await db.TextTranslations.ToListAsync(cancellationToken);
        var byKey = held.ToDictionary(r => (r.Language, r.EnglishSha256));

        var removed = 0;
        foreach (var row in held.Where(r => !wanted.ContainsKey((r.Language, r.EnglishSha256))))
        {
            db.TextTranslations.Remove(row);
            removed++;
        }

        var written = 0;
        foreach (var ((language, sha), (text, source)) in wanted)
        {
            if (!byKey.TryGetValue((language, sha), out var row))
            {
                db.TextTranslations.Add(new TextTranslation
                {
                    Language = language,
                    EnglishSha256 = sha,
                    Text = text,
                    Source = source,
                });
                written++;
            }
            else if (row.Text != text || row.Source != source)
            {
                row.Text = text;
                row.Source = source;
                written++;
            }
        }

        if (written > 0 || removed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new TextTranslationOutcome(written, removed, files, wanted.Count, started.Elapsed);
    }

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed record TextTranslationFile(string Source, IReadOnlyList<TextTranslationEntry> Entries);

    private sealed record TextTranslationEntry(string English, string? Ukr, string? Deu, string? Spa)
    {
        public IEnumerable<(string Language, string Text)> Rendered()
        {
            if (!string.IsNullOrWhiteSpace(Ukr))
            {
                yield return ("ukr", Ukr);
            }

            if (!string.IsNullOrWhiteSpace(Deu))
            {
                yield return ("deu", Deu);
            }

            if (!string.IsNullOrWhiteSpace(Spa))
            {
                yield return ("spa", Spa);
            }
        }
    }
}
