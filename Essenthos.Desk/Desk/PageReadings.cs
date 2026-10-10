using System.Text.Json;

namespace Essenthos.Core.Desk;

internal sealed record PageCount(int Total, int Done, int Failed);

/// <param name="Read">How many items have an answer; the other three are how they were answered.</param>
internal sealed record ItemCount(int Total, int Read, int Right, int Wrong, int Neither);

/// <param name="Folder">The pipeline folder under <c>Resources/Essenthos/generation</c>.</param>
/// <param name="Action">The avioniq action that runs it, which is how the owner starts it.</param>
/// <param name="State"><c>running</c>, <c>done</c>, <c>limit</c> (Codex's usage limit stopped it) or <c>failed</c>.</param>
/// <param name="Updated">When the run last wrote, so a run that died while <c>running</c> shows its age.</param>
/// <param name="Note">Why it stopped, in a sentence, or null.</param>
/// <param name="After">What has to happen once it is done.</param>
internal sealed record PageReading(
    string Folder,
    string Title,
    string Action,
    string State,
    string Started,
    string Updated,
    PageCount Pages,
    ItemCount Items,
    string? Note,
    string? After);

/// <summary>
/// How far each reading of the printed pages has got: the Codex runs that check Ottley's and Swete's
/// transcriptions against the scans. Each run writes <c>progress.json</c> in its own pipeline folder
/// as it goes, whether it was started from avioniq or anywhere else, and this only reads them.
/// </summary>
internal sealed class PageReadings(DeskPaths paths, ILogger<PageReadings> logger)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<PageReading> Read()
    {
        var generation = Path.Combine(paths.Owner, "generation");
        if (!Directory.Exists(generation))
        {
            return [];
        }

        var readings = new List<PageReading>();
        foreach (var folder in Directory.EnumerateDirectories(generation, "pages-*").Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(folder, "progress.json");
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<PageReading>(File.ReadAllText(file), Web) is { } reading)
                {
                    readings.Add(reading with { Folder = Path.GetFileName(folder) });
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                // A run rewrites the file as it goes; one caught mid-write is read again on the next poll.
                logger.LogWarning(exception, "Could not read {File}", file);
            }
        }

        return readings;
    }
}
