using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The project owner's choices about the pictures, made in his console and kept in a tracked file
/// beside the review lists: a picture hidden from the site, the one a page leads with, a caption of
/// his own. Each names the picture by the person's or place's slug and the file's path under the
/// images folder, both of which survive a rebuild.
///
/// <para>
/// <c>{ "about": "...", "images": [{ "entity": slug, "file": path, "hidden": true, "primary": true,
/// "caption": "..." }] }</c>. A choice about a picture no list offers any more does nothing.
/// </para>
/// </summary>
internal static class ImageChoices
{
    /// <summary>The folder under the corpus folder that holds the owner's own files.</summary>
    public const string Folder = "Essenthos";

    public const string FileName = "image-choices.json";

    /// <summary>A generated picture the owner has not looked at yet.</summary>
    public const string Pending = "pending";

    public const string Approved = "approved";

    public const string Rejected = "rejected";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ImageChoiceFile Read(string path)
    {
        if (!File.Exists(path))
        {
            return new ImageChoiceFile([]);
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<ImageChoiceFile>(stream, Json) ?? new ImageChoiceFile([]);
    }

    /// <summary>
    /// The candidates with the owner's choices applied: a hidden one left out and counted, a caption
    /// replaced, with the list's translations of the caption it replaces dropped, and a picture he
    /// chose to lead with made the primary of its kind for its entity — the one it was primary before
    /// joining the gallery — and moved ahead so it is the first of them.
    /// </summary>
    public static List<EntityImageLoader.Candidate> Apply(
        IReadOnlyList<EntityImageLoader.Candidate> candidates, ImageChoiceFile choices, out int hidden)
    {
        var by = new Dictionary<(string, string), ImageChoice>();
        foreach (var choice in choices.Images)
        {
            by[(choice.Entity, Normal(choice.File))] = choice;
        }

        var chosen = by.Where(c => c.Value.Primary == true && c.Value.Hidden != true).Select(c => c.Key).ToHashSet();
        var leading = candidates.Where(c => chosen.Contains((c.Slug, c.File))).Select(c => (c.EntityId, c.Kind)).ToHashSet();

        hidden = 0;
        var kept = new List<EntityImageLoader.Candidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            by.TryGetValue((candidate.Slug, candidate.File), out var choice);
            if (choice?.Hidden == true)
            {
                hidden++;
                continue;
            }

            var role = candidate.Role;
            if (leading.Contains((candidate.EntityId, candidate.Kind)))
            {
                role = chosen.Contains((candidate.Slug, candidate.File)) ? EntityImageLoader.Primary : EntityImageLoader.Gallery;
            }

            var own = !string.IsNullOrWhiteSpace(choice?.Caption);
            kept.Add(candidate with
            {
                Role = role,
                Caption = own ? choice!.Caption!.Trim() : candidate.Caption,

                // The list's translations are of the caption it wrote, not of the one he put in its place.
                Captions = own ? null : candidate.Captions,
            });
        }

        return kept.OrderBy(c => chosen.Contains((c.Slug, c.File)) ? 0 : 1).ToList();
    }

    private static string Normal(string file) => file.Replace('\\', '/');
}

internal sealed record ImageChoiceFile(IReadOnlyList<ImageChoice> Images);

/// <param name="Entity">The slug of the person or place.</param>
/// <param name="File">The picture's path under the images folder.</param>
internal sealed record ImageChoice(string Entity, string File, bool? Hidden = null, bool? Primary = null, string? Caption = null);
