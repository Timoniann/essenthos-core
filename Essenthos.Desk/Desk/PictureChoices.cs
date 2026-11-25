using System.Text.Json.Nodes;

namespace Essenthos.Core.Desk;

/// <param name="File">The picture's path under the images folder.</param>
/// <param name="Hidden">Kept off the site.</param>
/// <param name="Primary">The one the page leads with, among the pictures of its kind.</param>
/// <param name="Caption">The owner's own caption, shown instead of the source's; null for the source's.</param>
internal sealed record PictureChoiceRequest(string File, bool Hidden, bool Primary, string? Caption, string? Note);

internal sealed record PictureChoice(string File, bool Hidden, bool Primary, string? Caption);

/// <param name="File">The picture's path under the images folder.</param>
/// <param name="Review"><c>pending</c>, <c>approved</c> or <c>rejected</c>.</param>
internal sealed record PictureReviewRequest(string File, string Review, string? Note);

/// <summary>
/// The owner's choices about the pictures of one person or place — hidden, leading, captioned — in
/// the tracked file the image loader applies over every list of pictures on its next run. Each is
/// named by the entity's slug and the file's path under the images folder.
/// </summary>
internal sealed class PictureChoices(DeskPaths paths, ChangeLog log)
{
    public const string FileName = "image-choices.json";

    public const string Section = "pictures";

    /// <summary>The longest caption taken, which is a sentence or two of alternative text.</summary>
    public const int LongestCaption = 600;

    private const string About =
        "The project owner's choices about the pictures on person and place pages, made in his console: a picture " +
        "hidden from the site, the one a page leads with, a caption of his own. Each names the person or place by " +
        "its slug and the picture by its path under Resources/Images. The image loader applies them on its next " +
        "run (the Forge images verb).";

    public string File => Path.Combine(paths.Owner, FileName);

    /// <summary>Every choice, by the entity's slug.</summary>
    public static IReadOnlyDictionary<string, List<PictureChoice>> Read(DeskPaths paths)
    {
        var path = Path.Combine(paths.Owner, FileName);
        var chosen = new Dictionary<string, List<PictureChoice>>(StringComparer.Ordinal);
        if (!System.IO.File.Exists(path))
        {
            return chosen;
        }

        foreach (var entry in (JsonFiles.Read(path)["images"]?.AsArray() ?? []).OfType<JsonObject>())
        {
            if (entry["entity"]?.GetValue<string>() is not { } slug || entry["file"]?.GetValue<string>() is not { } file)
            {
                continue;
            }

            if (!chosen.TryGetValue(slug, out var list))
            {
                chosen[slug] = list = [];
            }

            list.Add(Of(entry, file));
        }

        return chosen;
    }

    /// <summary>
    /// Sets what the owner chose about one picture, replacing what he chose before. Leading with it
    /// takes the lead from any other picture of the entity he chose before; a picture with nothing
    /// chosen about it leaves the file.
    /// </summary>
    /// <param name="label">The person or place as the owner reads them, for the change log.</param>
    public async Task<PictureChoice?> Choose(string slug, PictureChoiceRequest request, string? label = null)
    {
        var file = request.File.Replace('\\', '/').Trim();
        if (file.Length == 0 || DeskPaths.Under(paths.Images, file) is null || (request.Caption?.Length ?? 0) > LongestCaption)
        {
            return null;
        }

        if (!System.IO.File.Exists(File))
        {
            Directory.CreateDirectory(paths.Owner);
            JsonFiles.Write(File, new JsonObject { ["about"] = About, ["images"] = new JsonArray() });
        }

        PictureChoice? before = null;
        var caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim();
        var after = new PictureChoice(file, request.Hidden, request.Primary && !request.Hidden, caption);
        await JsonFiles.Change(File, node =>
        {
            if (node["images"] is not JsonArray images)
            {
                node["images"] = images = [];
            }

            var mine = images.OfType<JsonObject>().Where(e => e["entity"]?.GetValue<string>() == slug).ToList();
            var existing = mine.FirstOrDefault(e => e["file"]?.GetValue<string>() == file);
            if (existing is not null)
            {
                before = Of(existing, file);
                images.Remove(existing);
            }

            if (after.Primary)
            {
                foreach (var other in mine.Where(e => e != existing && e["primary"] is not null))
                {
                    other.Remove("primary");
                    if (other.Count <= 2)
                    {
                        images.Remove(other);
                    }
                }
            }

            if (after.Hidden || after.Primary || after.Caption is not null)
            {
                var entry = new JsonObject { ["entity"] = slug, ["file"] = file };
                if (after.Hidden)
                {
                    entry["hidden"] = true;
                }

                if (after.Primary)
                {
                    entry["primary"] = true;
                }

                if (after.Caption is not null)
                {
                    entry["caption"] = after.Caption;
                }

                images.Add(entry);
            }

            return (true, after);
        });

        await log.Append(Section, "choice", $"{slug} {file}", Logged(before), Logged(after), request.Note, "images", label);
        return after;
    }

    /// <summary>
    /// Sets the owner's word on a public work found for his review, in the manifest that lists it beside the public
    /// works: approved, it is shown once the pictures are drawn again; pending or rejected, it stays off the site.
    /// False where the manifest lists no such picture of the record, or the word is none of the three.
    /// </summary>
    /// <param name="kind">The record's kind as the console names it, for the change log's target.</param>
    public async Task<bool> Review(string slug, string kind, PictureReviewRequest request, string? label = null)
    {
        var manifest = Path.Combine(paths.Commons, "manifest.json");
        var file = request.File.Replace('\\', '/').Trim();
        if (request.Review is not (PortraitEditor.Pending or PortraitEditor.Approved or PortraitEditor.Rejected)
            || !System.IO.File.Exists(manifest))
        {
            return false;
        }

        var (found, before) = await JsonFiles.Change(manifest, node =>
        {
            var entry = (node["images"]?.AsArray() ?? [])
                .OfType<JsonObject>()
                .FirstOrDefault(e => e["entity"]?.GetValue<string>() == slug && e["file"]?.GetValue<string>()?.Replace('\\', '/') == file);
            if (entry is null)
            {
                return (false, (false, (string?)null));
            }

            var was = entry["review"]?.GetValue<string>();
            entry["review"] = request.Review;
            return (true, (true, was));
        });
        if (!found)
        {
            return false;
        }

        await log.Append(Section, "review", $"{kind}/{slug} {file}", JsonValue.Create(before), JsonValue.Create(request.Review),
            request.Note, "images", label);
        return true;
    }

    private static PictureChoice Of(JsonObject entry, string file) => new(
        file,
        entry["hidden"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
        entry["primary"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
        entry["caption"]?.GetValue<string>());

    private static JsonObject? Logged(PictureChoice? choice) =>
        choice is null || (!choice.Hidden && !choice.Primary && choice.Caption is null)
            ? null
            : new JsonObject { ["hidden"] = choice.Hidden, ["primary"] = choice.Primary, ["caption"] = choice.Caption };
}
