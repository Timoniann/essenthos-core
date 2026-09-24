using System.Security.Cryptography;

namespace Essenthos.Core.Publishing;

/// <summary>
/// The pictures of a corpus as files and their hashes: which of this machine's a target lacks, and
/// whether every picture a release names is there for its API to serve.
///
/// <para>
/// The rows that name a picture travel in the release; the files do not, because a dump of 182 MB of
/// JPEGs is a dump nobody wants to restore twice. They go beside it, into one folder per corpus that
/// the target's API mounts read-only, and only what is new or changed is sent. Nothing is ever taken
/// away: the previous release, which a rollback puts back, names pictures too.
/// </para>
/// </summary>
internal static class ImageManifest
{
    /// <summary>How many hex digits of the SHA-256 a picture row keeps as its digest.</summary>
    private const int DigestLength = 12;

    /// <summary>
    /// What the API serves as a picture. Nothing else under the images folder leaves this machine: the
    /// portrait briefs beside the generated pictures carry their prompts and the owner's decisions.
    /// </summary>
    private static readonly HashSet<string> PictureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp",
    };

    /// <summary>Every picture under <paramref name="folder"/> by its path there, with forward slashes, and its SHA-256.</summary>
    public static Dictionary<string, string> Read(string folder)
    {
        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return manifest;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                     .Where(file => PictureExtensions.Contains(Path.GetExtension(file))))
        {
            using var stream = File.OpenRead(file);
            manifest[Relative(folder, file)] = Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        return manifest;
    }

    /// <summary>
    /// What <c>find . -type f -exec sha256sum {} +</c> printed: a hash, two spaces or a space and an
    /// asterisk, and a path starting <c>./</c>.
    /// </summary>
    public static Dictionary<string, string> Parse(string listing)
    {
        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.TrimEnd('\r');
            var space = text.IndexOf(' ');
            if (space <= 0)
            {
                continue;
            }

            var path = text[(space + 1)..].TrimStart(' ', '*');
            manifest[path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path] = text[..space].ToLowerInvariant();
        }

        return manifest;
    }

    /// <summary>The pictures a release names, from psql's unaligned <c>file|digest</c> lines.</summary>
    public static IEnumerable<(string File, string Digest)> Rows(string psql)
    {
        foreach (var line in psql.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.TrimEnd('\r');
            var bar = text.LastIndexOf('|');
            if (bar > 0)
            {
                yield return (text[..bar], text[(bar + 1)..]);
            }
        }
    }

    /// <summary>The files this machine has that the target does not, or has with other bytes.</summary>
    public static List<string> ToSend(IReadOnlyDictionary<string, string> here, IReadOnlyDictionary<string, string> there) =>
        here.Where(file => !there.TryGetValue(file.Key, out var hash) || hash != file.Value)
            .Select(file => file.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The pictures a release names that the target does not have at all, and those it has with bytes
    /// other than the ones the release was built from — a portrait replaced since the release was made.
    /// </summary>
    public static (List<string> Missing, List<string> Differing) Check(
        IEnumerable<(string File, string Digest)> named, IReadOnlyDictionary<string, string> there)
    {
        var missing = new List<string>();
        var differing = new List<string>();
        foreach (var (file, digest) in named)
        {
            if (!there.TryGetValue(file, out var hash))
            {
                missing.Add(file);
            }
            else if (!hash.StartsWith(digest, StringComparison.OrdinalIgnoreCase) || digest.Length != DigestLength)
            {
                differing.Add(file);
            }
        }

        return (missing, differing);
    }

    private static string Relative(string folder, string file) =>
        Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');
}
