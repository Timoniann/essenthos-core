using System.Security.Cryptography;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Publishing;

/// <param name="Send">Files the manifest names that the target lacks or has with other bytes.</param>
/// <param name="Remove">Files on the target that the manifest does not name, which go once it is in place.</param>
internal sealed record DownloadsPlan(IReadOnlyList<string> Send, IReadOnlyList<string> Remove);

/// <summary>
/// The export as the target serves it: the files <c>forge export</c> wrote, in the folder the target's
/// proxy serves at <c>/downloads</c> and its API lists from.
///
/// <para>
/// A file's name carries the first digits of its SHA-256, so the bytes behind a name never change.
/// That is what makes the order of a publication safe: the files a manifest names arrive first, beside
/// the ones the manifest in place names, then the manifest replaces the old one in a single move, and
/// only then are the files nothing names any more taken away. A reader holding the old list and one
/// holding the new one both find every file they were promised.
/// </para>
///
/// <para>
/// Nothing is sent unless every file the manifest names is in the folder with the size and checksum it
/// states, so the target never lists a file that is not the one described.
/// </para>
/// </summary>
internal static class DownloadsPublication
{
    /// <summary>Every file a manifest names, in path order.</summary>
    public static IReadOnlyList<ExportedFile> Files(DownloadsManifest manifest) =>
    [
        .. manifest.Texts.SelectMany(text => new[] { text.File, text.Attribution }).OrderBy(file => file.Path, StringComparer.Ordinal),
    ];

    /// <summary>
    /// The manifest in <paramref name="folder"/> and what is wrong with it: no manifest, one this Forge
    /// does not read, a path that leaves the folder, or a file missing or not the bytes it states.
    /// </summary>
    public static (DownloadsManifest? Manifest, IReadOnlyList<string> Problems) Check(string folder)
    {
        var path = Path.Combine(folder, DownloadsManifest.FileName);
        if (!File.Exists(path))
        {
            return (null, [$"{folder} holds no {DownloadsManifest.FileName}; run `forge export` first"]);
        }

        DownloadsManifest? manifest;
        try
        {
            manifest = DownloadsManifest.Parse(File.ReadAllText(path));
        }
        catch (System.Text.Json.JsonException exception)
        {
            return (null, [$"{path} cannot be read: {exception.Message}"]);
        }

        if (manifest is not { Format: DownloadsManifest.CurrentFormat })
        {
            return (null, [$"{path} is not in a format this Forge publishes"]);
        }

        var files = Files(manifest);
        if (files.Count == 0)
        {
            return (null, [$"{path} names no file, and publishing it would take every download away"]);
        }

        var problems = new List<string>();
        foreach (var file in files)
        {
            if (!Safe(file.Path))
            {
                problems.Add($"{file.Path}: not a path inside the export");
                continue;
            }

            var full = Path.Combine(folder, file.Path);
            if (!File.Exists(full))
            {
                problems.Add($"{file.Path}: the manifest names it and it is not in {folder}");
                continue;
            }

            using var stream = File.OpenRead(full);
            if (stream.Length != file.Bytes)
            {
                problems.Add($"{file.Path}: {stream.Length:N0} bytes where the manifest says {file.Bytes:N0}");
            }
            else if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{file.Path}: its checksum is not the one the manifest states");
            }
        }

        return (problems.Count == 0 ? manifest : null, problems);
    }

    /// <summary>
    /// What to send and what to take away, given what the target has. The manifest itself is neither:
    /// it is put in place between the two.
    /// </summary>
    public static DownloadsPlan Plan(
        DownloadsManifest manifest, IReadOnlyDictionary<string, string> there)
    {
        var named = Files(manifest);
        var names = named.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var send = named
            .Where(file => !there.TryGetValue(file.Path, out var hash) || !string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            .Select(file => file.Path)
            .ToList();
        var remove = there.Keys
            .Where(path => path != DownloadsManifest.FileName && !names.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToList();
        return new DownloadsPlan(send, remove);
    }

    /// <summary>A relative path with forward slashes that stays inside the folder it is read against.</summary>
    internal static bool Safe(string path) =>
        path.Length > 0
        && !path.StartsWith('/')
        && !path.Contains('\\')
        && !path.Contains(':')
        && path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));
}
