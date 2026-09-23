using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Desk;

/// <param name="Value">The field's new value as JSON, or null to take the field out of the brief.</param>
internal sealed record BriefFieldRequest(JsonNode? Value, string? Note);

internal sealed record PortraitStatusRequest(string Status, string? Note);

/// <param name="File">The generated picture, by its path under the images folder.</param>
/// <param name="Review"><c>pending</c>, <c>approved</c> or <c>rejected</c>.</param>
internal sealed record PortraitReviewRequest(string File, string Review, string? Note);

/// <param name="Detail">The person as they stand after the change, or null where it was refused.</param>
/// <param name="Problem">Why it was refused, in a sentence.</param>
internal sealed record PortraitChange(PortraitDetail? Detail, string? Problem);

/// <summary>
/// What the owner changes about a portrait: the brief the artist works from, field by field; where
/// the portrait stands; a generated picture he adds; and his word on it. The brief and the manifest
/// are the files beside the pictures, written in the layout they already have, and every change goes
/// into the change log.
///
/// <para>
/// God is never given a face or a figure: the one picture a record of God may be given here is ours
/// of the glory, which the upload must say it is, and the loader refuses any other whoever lists it.
/// </para>
/// </summary>
internal sealed partial class PortraitEditor(PortraitBoard board, DeskPaths paths, ChangeLog log)
{
    public const string Section = "portraits";

    public const string Pending = "pending";

    public const string Approved = "approved";

    public const string Rejected = "rejected";

    private static readonly IReadOnlySet<string> Reviews = new HashSet<string>(StringComparer.Ordinal) { Pending, Approved, Rejected };

    /// <summary>The largest picture taken: a generated portrait is a few megabytes at most, even as PNG.</summary>
    public const long LargestUpload = 30 * 1024 * 1024;

    /// <summary>The longest value a brief field takes, which is several pages of prose.</summary>
    private const int LongestField = 40_000;

    /// <summary>The fields a brief is found by, which are not the owner's to rename.</summary>
    private static readonly IReadOnlySet<string> Fixed = new HashSet<string>(StringComparer.Ordinal) { "slug" };

    private const string DefaultSource = "Essenthos, generated";

    private const string DefaultCredit = "Essenthos, generated with OpenAI's image model through Codex";

    private const string DefaultLicence = "Essenthos's own work";

    private static readonly JsonWriterOptions Compact = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private string Briefs => Path.Combine(paths.Generated, PortraitBoard.BriefsFile);

    private string Manifest => Path.Combine(paths.Generated, PortraitBoard.ManifestFile);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex FieldName();

    public async Task<PortraitChange> SetField(string slug, string field, BriefFieldRequest request, CancellationToken cancellationToken)
    {
        if (!FieldName().IsMatch(field) || Fixed.Contains(field))
        {
            return Refused("That is not a field a brief can have. Fields are lower-case words joined by underscores, and the slug cannot change.");
        }

        if (request.Value is not null && Length(request.Value) > LongestField)
        {
            return Refused("The value is too long for one field of a brief.");
        }

        if (await board.Detail(slug, cancellationToken) is not { } person)
        {
            return Refused("There is no such person in the corpus.");
        }

        JsonNode? before = null;
        await ChangeBrief(person, brief =>
        {
            before = brief[field]?.DeepClone();
            if (request.Value is null)
            {
                brief.Remove(field);
            }
            else
            {
                brief[field] = request.Value.DeepClone();
            }
        });

        await log.Append(Section, "brief", $"person/{slug} {field}", before, request.Value, request.Note, null, NameOf(person));
        return await Now(slug, cancellationToken);
    }

    public async Task<PortraitChange> SetStatus(string slug, PortraitStatusRequest request, CancellationToken cancellationToken)
    {
        if (!PortraitBoard.Statuses.Contains(request.Status))
        {
            return Refused($"A portrait's status is one of {string.Join(", ", PortraitBoard.Statuses)}.");
        }

        if (await board.Detail(slug, cancellationToken) is not { } person)
        {
            return Refused("There is no such person in the corpus.");
        }

        var before = person.Person.Status;
        await ChangeBrief(person, brief => brief["status"] = request.Status);

        // Approving or rejecting the portrait is the owner's word on each picture still waiting for it.
        var reviewed = request.Status is PortraitBoard.Approved or PortraitBoard.Rejected
                       && await Review(slug, file => file["review"]?.GetValue<string>() == Pending,
                           request.Status == PortraitBoard.Approved ? Approved : Rejected);

        await log.Append(Section, "status", $"person/{slug}", JsonValue.Create(before), JsonValue.Create(request.Status),
            request.Note, reviewed ? "images" : null, NameOf(person));
        return await Now(slug, cancellationToken);
    }

    public async Task<PortraitChange> SetReview(string slug, PortraitReviewRequest request, CancellationToken cancellationToken)
    {
        if (!Reviews.Contains(request.Review))
        {
            return Refused("A generated picture is pending, approved or rejected.");
        }

        if (await board.Detail(slug, cancellationToken) is not { } person)
        {
            return Refused("There is no such person in the corpus.");
        }

        var file = request.File.Replace('\\', '/');
        var was = person.Images.FirstOrDefault(i => i.File == file && i.Kind == PortraitBoard.Generated);
        if (was is null || !await Review(slug, entry => PortraitBoard.File(entry) == file, request.Review))
        {
            return Refused("Our manifest lists no such picture of this person.");
        }

        // The portrait stands where its pictures do together: one still waiting keeps it waiting, and
        // one refused among approved ones leaves it approved.
        var status = PortraitBoard.StatusOf(null, PortraitBoard.Listed(JsonFiles.Read(Manifest), slug).ToList());
        await ChangeBrief(person, brief => brief["status"] = status);
        await log.Append(Section, "review", $"person/{slug} {file}", JsonValue.Create(was.Review), JsonValue.Create(request.Review),
            request.Note, "images", NameOf(person));
        return await Now(slug, cancellationToken);
    }

    /// <summary>
    /// Saves a generated picture of the person into the generated folder, under a name of its own, and
    /// lists it in the manifest as waiting for the owner's word, so nothing reaches the site before he
    /// approves it and the pictures are loaded again.
    /// </summary>
    public async Task<PortraitChange> Upload(
        string slug, string? name, bool glory, string? note, Stream body, CancellationToken cancellationToken)
    {
        if (await board.Detail(slug, cancellationToken) is not { } person)
        {
            return Refused("There is no such person in the corpus.");
        }

        if (person.Person.NeverPictured && !glory)
        {
            return Refused("God is never given a face or a figure. The one picture a record of God may have is ours of the glory as light; mark the upload as that picture, or do not add it.");
        }

        if (!person.Person.NeverPictured && glory)
        {
            return Refused("Only a record of God has the picture of the glory.");
        }

        if (person.Person.Status == PortraitBoard.NoPortrait)
        {
            return Refused("This person is marked as not portrayed. Change the status first if that has changed.");
        }

        var bytes = await Read(body, cancellationToken);
        if (bytes is null)
        {
            return Refused("The picture is larger than 30 MB.");
        }

        if (ExtensionOf(bytes) is not { } extension)
        {
            return Refused("That is not a WebP, PNG or JPEG picture.");
        }

        Directory.CreateDirectory(paths.Generated);
        var file = Free(slug, extension);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(paths.Images, file), bytes, cancellationToken);

        if (!System.IO.File.Exists(Manifest))
        {
            JsonFiles.Write(Manifest, new JsonObject
            {
                ["source"] = DefaultSource,
                ["credit"] = DefaultCredit,
                ["licence"] = DefaultLicence,
                ["images"] = new JsonArray(),
            });
        }

        var entry = new JsonObject { ["entity"] = slug, ["file"] = file };
        if (glory)
        {
            entry["glory"] = true;
        }

        entry["review"] = Pending;
        await JsonFiles.Change(Manifest, node =>
        {
            if (node["images"] is not JsonArray images)
            {
                node["images"] = images = [];
            }

            images.Add(entry);
            return (true, true);
        });

        await ChangeBrief(person, brief => brief["status"] = PortraitBoard.Generated);
        await log.Append(Section, "upload", $"person/{slug} {file}", null,
            new JsonObject { ["file"] = file, ["from"] = Path.GetFileName(name ?? string.Empty), ["bytes"] = bytes.Length },
            note, null, NameOf(person));
        return await Now(slug, cancellationToken);
    }

    /// <summary>The person as the owner reads them, in his language where the corpus has it.</summary>
    private static string NameOf(PortraitDetail person) => person.Person.LocalName ?? person.Person.Name;

    /// <summary>
    /// Changes the person's brief, writing a brief of the person's name, sex and verses first where
    /// there is none, so a status or a field can be set before anyone has written the rest.
    /// </summary>
    private async Task ChangeBrief(PortraitDetail person, Action<JsonObject> change)
    {
        if (!System.IO.File.Exists(Briefs))
        {
            Directory.CreateDirectory(paths.Generated);
            JsonFiles.Write(Briefs, new JsonArray());
        }

        await JsonFiles.Change(Briefs, node =>
        {
            var briefs = node.AsArray();
            if (PortraitBoard.BriefOf(briefs, person.Person.Slug) is not JsonObject brief)
            {
                brief = new JsonObject
                {
                    ["tier"] = person.Person.Tier,
                    ["slug"] = person.Person.Slug,
                    ["name"] = person.Person.Name,
                    ["name_uk"] = person.Person.LocalName,
                    ["sex"] = person.Person.Sex,
                    ["verses"] = person.Person.Verses,
                };

                // Where the portrait already stands beyond a brief, the brief says so from the start.
                if (person.Person.Status is not (PortraitBoard.NotStarted or PortraitBoard.Ready))
                {
                    brief["status"] = person.Person.Status;
                }

                briefs.Add(brief);
            }

            change(brief);
            return (true, true);
        });
    }

    /// <summary>Sets the review of the person's manifest entries that <paramref name="which"/> picks; whether any was.</summary>
    private async Task<bool> Review(string slug, Func<JsonNode, bool> which, string review)
    {
        if (!System.IO.File.Exists(Manifest))
        {
            return false;
        }

        return await JsonFiles.Change(Manifest, node =>
        {
            var entries = PortraitBoard.Listed(node, slug).Where(which).OfType<JsonObject>().ToList();
            foreach (var entry in entries)
            {
                entry["review"] = review;
            }

            return (entries.Count > 0, entries.Count > 0);
        });
    }

    private string Free(string slug, string extension)
    {
        for (var n = 1; ; n++)
        {
            var file = $"{PortraitBoard.Generated}/{slug}{(n == 1 ? string.Empty : $"-{n}")}{extension}";
            if (!System.IO.File.Exists(Path.Combine(paths.Images, file)))
            {
                return file;
            }
        }
    }

    private static async Task<byte[]?> Read(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > LargestUpload)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>The extension the picture's own first bytes say it has, or null where they say it is none of the three.</summary>
    internal static string? ExtensionOf(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ? ".png"
        : bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ? ".jpg"
        : bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8) ? ".webp"
        : null;

    private static int Length(JsonNode value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Compact))
        {
            value.WriteTo(writer);
        }

        return (int)buffer.Length;
    }

    private async Task<PortraitChange> Now(string slug, CancellationToken cancellationToken) =>
        new(await board.Detail(slug, cancellationToken), null);

    private static PortraitChange Refused(string problem) => new(null, problem);
}
