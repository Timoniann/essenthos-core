using System.Diagnostics;
using System.Text.Json;

namespace Essenthos.Core.Desk;

internal sealed record ReviewChunks(int Total, int Done, int Failed, int Running, int DoneHere);

internal sealed record ReviewVerses(int Total, int Reviewed, int Ok, int Fixed, int Unsure);

internal sealed record ReviewRows(int Refuse, int Accept);

/// <param name="Chunks">Results files already turned into rulings, and those waiting for the lead's conversion.</param>
internal sealed record ReviewConverted(int Chunks, int Waiting);

internal sealed record ReviewChunk(
    string Chunk, string At, int Seconds, int Verses, int Ok, int Fixed, int Unsure, int Refuse, int Accept, string? Pattern);

/// <param name="Kind"><c>limit</c> (Codex's usage limit), <c>rate</c>, <c>errors</c> (failures in a row) or <c>database</c>.</param>
internal sealed record ReviewWait(string Kind, string Since, string Until, string? Message);

internal sealed record ReviewError(string At, string Kind, string? Chunk, string Message);

internal sealed record ReviewSheets(string? Exported, string? Database);

/// <param name="Text">The translation under review, which is also its folder under <c>Resources/Essenthos/hand-review</c>.</param>
/// <param name="Service">The avioniq service that runs it, which is how the owner starts and stops it.</param>
/// <param name="State"><c>starting</c>, <c>exporting</c>, <c>running</c>, <c>waiting</c>, <c>stopped</c>, <c>done</c> or <c>failed</c>.</param>
/// <param name="Updated">When the run last wrote; it writes at least every half minute while it runs.</param>
internal sealed record HandReview(
    string Text,
    string Title,
    string Service,
    string State,
    int? Pid,
    string Started,
    string Updated,
    string Model,
    string Effort,
    int Lanes,
    ReviewChunks Chunks,
    ReviewVerses Verses,
    ReviewRows Rows,
    ReviewConverted Converted,
    ReviewChunk? Last,
    ReviewWait? Waiting,
    IReadOnlyList<ReviewError> Errors,
    ReviewSheets? Sheets,
    string? Note);

/// <summary>
/// How far each Codex hand review of a translation's links has got. Each run writes <c>run/status.json</c> in its
/// text's folder as it goes; this only reads them. A run stopped from avioniq ends at once without writing that it
/// stopped, so a status that still says it runs while its process is gone is shown as stopped.
/// </summary>
internal sealed class HandReviews(DeskPaths paths, ILogger<HandReviews> logger)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> Live = new(StringComparer.Ordinal) { "starting", "exporting", "running", "waiting" };

    public IReadOnlyList<HandReview> Read()
    {
        var folder = Path.Combine(paths.Owner, "hand-review");
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var reviews = new List<HandReview>();
        foreach (var text in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(text, "run", "status.json");
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                // Shared for writing and deleting too, so the run's replace of the file is never refused while it is read.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (JsonSerializer.Deserialize<HandReview>(stream, Web) is { } review)
                {
                    reviews.Add(Alive(review) ? review : review with
                    {
                        State = "stopped",
                        Waiting = null,
                        Chunks = review.Chunks with { Running = 0 },
                        Note = null,
                    });
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                logger.LogWarning(exception, "Could not read {File}", file);
            }
        }

        return reviews;
    }

    private static bool Alive(HandReview review)
    {
        if (!Live.Contains(review.State))
        {
            return true;
        }

        if (review.Pid is not { } pid)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited
                   && (!DateTimeOffset.TryParse(review.Started, out var started) || process.StartTime.ToUniversalTime() <= started.UtcDateTime.AddMinutes(1));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
