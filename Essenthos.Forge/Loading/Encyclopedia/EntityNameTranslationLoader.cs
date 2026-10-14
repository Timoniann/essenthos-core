using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="NoFiles">No folder of renderings on this disk; the rows already loaded are kept.</param>
/// <param name="Refused">
/// Renderings that dropped or changed a verse the English cites, which the page turns into a link:
/// a gloss that lost its verse is a gloss the reader can no longer check.
/// </param>
internal sealed record EntityNameTranslationOutcome(
    bool NoFiles,
    int Files,
    int Written,
    int Removed,
    int Refused,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        NoFiles ? "no renderings of titles and glosses are on this disk, so the ones loaded are kept"
        : Written == 0 && Removed == 0 && Refused == 0
            ? "the titles and glosses of names in every language are already there"
            : $"{Written} renderings of titles and glosses written and {Removed} removed, from {Files} files, " +
              $"{Refused} refused for losing a verse the English cites, in {Elapsed}";
}

/// <summary>One English title or gloss and its renderings, as a generation pass wrote it.</summary>
internal sealed record EntityNameTranslationRecord(
    string English,
    string? Ukr,
    string? Deu,
    string? Spa,
    string? Rus,
    string Model,
    string AskedAt)
{
    public IEnumerable<(string Language, string Text)> Languages() =>
        new (string Language, string? Text)[] { ("ukr", Ukr), ("deu", Deu), ("spa", Spa), ("rus", Rus) }
            .Where(one => !string.IsNullOrWhiteSpace(one.Text))
            .Select(one => (one.Language, one.Text!.Trim()));
}

/// <summary>
/// The titles a record is given and the glosses of its names, in Ukrainian, German, Spanish and
/// Russian, read from what a generation pass wrote into <c>Essenthos/name-glosses</c> beside the
/// corpus sources: <em>King of Israel</em> as <em>цар Ізраїля</em>, <em>son [of] Jesse</em> as
/// <em>син [Єссея]</em>.
///
/// <para>
/// One file per pass, one JSON object per line, and a later line wins for the same English and
/// language. The rows are these files' alone: one no line asks for any more is removed, a row is
/// written only where it differs, and all of it is one save. Every row carries the model and the
/// date of the pass that wrote it.
/// </para>
/// </summary>
internal sealed partial class EntityNameTranslationLoader(AppDbContext db, ILogger<EntityNameTranslationLoader> logger)
{
    public static readonly string Folder = Path.Combine("Essenthos", "name-glosses");

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<EntityNameTranslationOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(resources, Folder);
        if (!Directory.Exists(directory))
        {
            logger.LogInformation(
                "No renderings of titles and glosses at {Directory}, so a names block shows them in English. " +
                "They are what a generation pass writes and they stay out of the repository", directory);
            return new EntityNameTranslationOutcome(true, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var (records, files) = Read(directory);
        return await Load(records, files, cancellationToken);
    }

    internal async Task<EntityNameTranslationOutcome> Load(
        IReadOnlyList<EntityNameTranslationRecord> records,
        int files,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var spelled = await Spell(cancellationToken);
        if (spelled > 0)
        {
            logger.LogInformation("{Spelled} titles and glosses of names spelled right", spelled);
        }

        var wanted = new Dictionary<(string Language, string English), (string Text, string Source)>();
        var refused = 0;
        foreach (var record in records)
        {
            foreach (var (language, text) in record.Languages())
            {
                if (!CitesTheSameVerses(record.English, text))
                {
                    logger.LogWarning(
                        "The {Language} rendering of \"{English}\" does not cite the verses the English cites, " +
                        "so it is not loaded: \"{Text}\"", language, record.English, text);
                    refused++;
                    continue;
                }

                wanted[(language, record.English)] =
                    (text, $"Essenthos, rendered from the English by {record.Model} on {record.AskedAt}");
            }
        }

        var held = await db.EntityNameTranslations.ToListAsync(cancellationToken);
        var removed = 0;
        foreach (var row in held.Where(r => !wanted.ContainsKey((r.Language, r.English))))
        {
            db.EntityNameTranslations.Remove(row);
            removed++;
        }

        var byKey = held.ToDictionary(r => (r.Language, r.English));
        var written = 0;
        foreach (var ((language, english), (text, source)) in wanted)
        {
            if (!byKey.TryGetValue((language, english), out var row))
            {
                db.EntityNameTranslations.Add(new EntityNameTranslation
                {
                    Language = language,
                    English = english,
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

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new EntityNameTranslationOutcome(false, files, written, removed, refused, started.Elapsed);
        logger.LogInformation("Titles and glosses of names in every language: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The titles and glosses a warm corpus loaded before their slips were put right
    /// (<see cref="LineSpellings.Name"/>), spelled as a cold load spells them, so the renderings
    /// are keyed by the English the reader is shown.
    /// </summary>
    internal async Task<int> Spell(CancellationToken cancellationToken)
    {
        var names = await db.EntityNames.ToListAsync(cancellationToken);
        var spelled = 0;
        foreach (var name in names)
        {
            var label = LineSpellings.Name(name.Label)!;
            var meaning = LineSpellings.Name(name.Meaning);
            if (label != name.Label || meaning != name.Meaning)
            {
                name.Label = label;
                name.Meaning = meaning;
                spelled++;
            }
        }

        return spelled;
    }

    internal static (IReadOnlyList<EntityNameTranslationRecord> Records, int Files) Read(string directory)
    {
        var records = new List<EntityNameTranslationRecord>();
        var files = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl").Order(StringComparer.Ordinal))
        {
            files++;
            foreach (var line in File.ReadLines(file).Where(line => line.Length > 0))
            {
                var record = JsonSerializer.Deserialize<EntityNameTranslationRecord>(line, Shape);
                if (record is null || string.IsNullOrWhiteSpace(record.English) ||
                    string.IsNullOrWhiteSpace(record.Model) || string.IsNullOrWhiteSpace(record.AskedAt))
                {
                    throw new InvalidDataException(
                        $"A line of {file} is not a rendering of a title or gloss. Each line must be one JSON " +
                        "object with english, model, askedAt and one or more of ukr, deu, spa and rus.");
                }

                records.Add(record);
            }
        }

        return (records, files);
    }

    internal static bool CitesTheSameVerses(string english, string text) =>
        Verses(english).SequenceEqual(Verses(text));

    private static IEnumerable<string> Verses(string text) =>
        Verse().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal);

    [GeneratedRegex(@"\b[1-3]?[A-Z]{2,3} \d+:\d+(?:-\d+)?")]
    private static partial Regex Verse();
}
