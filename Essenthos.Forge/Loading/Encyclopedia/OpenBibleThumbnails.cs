using System.Text.Json;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The one picture OpenBible.info's gazetteer recommends for each place, with its credit and its
/// licence.
///
/// <para>
/// Which picture is the gazetteer's own advice: a thumbnail on the place itself, else on the
/// identification it scores highest, else on that identification's resolution — the higher in the
/// object, the more it reflects the place rather than a spot inside it. The identification is the
/// same one <see cref="OpenBibleLocationLoader"/> takes the point from, so a place's picture and its
/// point never describe two different candidate sites.
/// </para>
///
/// <para>
/// The licence is read per image from <c>image.jsonl</c>, because the gazetteer's own CC BY 4.0 does
/// not reach the photographs: their terms "vary depending on the image". A picture whose terms are
/// not one this reads — <c>copyright</c> above all — or that carries nobody to credit is not taken.
/// </para>
/// </summary>
internal static partial class OpenBibleThumbnails
{
    public const string AncientFile = "ancient.jsonl";

    public const string ImageFile = "image.jsonl";

    public const string ArchiveFile = "thumbnails.zip";

    /// <summary>The folder under the images root the gazetteer's thumbnails are unpacked into.</summary>
    public const string Folder = "openbible";

    /// <param name="PlaceId">The gazetteer's identifier for the ancient place — what an entity holds.</param>
    /// <param name="File">The thumbnail's file name, as the archive and the metadata both spell it.</param>
    internal sealed record Thumbnail(
        string PlaceId,
        string File,
        string ImageId,
        string Credit,
        string? CreditUrl,
        string? Caption,
        string Licence,
        string? LicenceUrl);

    /// <param name="Unlicensed">Places whose recommended picture carries terms this does not take.</param>
    /// <param name="Uncredited">Places whose recommended picture names nobody to credit.</param>
    internal sealed record Reading(List<Thumbnail> Thumbnails, int Unlicensed, int Uncredited);

    public static Reading Read(string ancientFile, string imageFile)
    {
        var images = new Dictionary<string, (string? Licence, string? Credit, string? CreditUrl)>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(imageFile))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (OpenBibleLocationLoader.Text(root, "id") is { } id)
            {
                images[id] = (
                    OpenBibleLocationLoader.Text(root, "license"),
                    OpenBibleLocationLoader.Text(root, "credit") ?? OpenBibleLocationLoader.Text(root, "author"),
                    OpenBibleLocationLoader.Text(root, "credit_url"));
            }
        }

        var thumbnails = new List<Thumbnail>(1_400);
        int unlicensed = 0, uncredited = 0;
        foreach (var line in File.ReadLines(ancientFile))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (OpenBibleLocationLoader.Text(root, "id") is not { } placeId
                || Recommended(root) is not { } thumbnail
                || OpenBibleLocationLoader.Text(thumbnail, "file") is not { } file
                || OpenBibleLocationLoader.Text(thumbnail, "image_id") is not { } imageId)
            {
                continue;
            }

            var image = images.GetValueOrDefault(imageId);
            if (Licences.Of(image.Licence) is not { } licence)
            {
                unlicensed++;
                continue;
            }

            if ((OpenBibleLocationLoader.Text(thumbnail, "credit") ?? image.Credit) is not { } credit)
            {
                uncredited++;
                continue;
            }

            thumbnails.Add(new Thumbnail(
                placeId,
                file,
                imageId,
                credit,
                OpenBibleLocationLoader.Text(thumbnail, "credit_url") ?? image.CreditUrl,
                Caption(OpenBibleLocationLoader.Text(thumbnail, "description")),
                licence.Name,
                licence.Url));
        }

        return new Reading(thumbnails, unlicensed, uncredited);
    }

    /// <summary>
    /// The place's own thumbnail, else its best identification's, else that identification's
    /// resolution's.
    /// </summary>
    internal static JsonElement? Recommended(JsonElement root)
    {
        if (ThumbnailOf(root) is { } own)
        {
            return own;
        }

        return OpenBibleLocationLoader.Best(root) is { } best
            ? ThumbnailOf(best.Identification) ?? ThumbnailOf(best.Element)
            : null;
    }

    private static JsonElement? ThumbnailOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("media", out var media)
        && media.ValueKind == JsonValueKind.Object
        && media.TryGetProperty("thumbnail", out var thumbnail)
        && thumbnail.ValueKind == JsonValueKind.Object
            ? thumbnail
            : null;

    /// <summary>
    /// The gazetteer's description with its inline <c>&lt;modern&gt;</c> and <c>&lt;ancient&gt;</c>
    /// tags taken off, which is what it says to do before using one as alternative text.
    /// </summary>
    internal static string? Caption(string? description)
    {
        if (description is null)
        {
            return null;
        }

        var plain = InlineTag().Replace(description, string.Empty).Trim();
        return plain.Length == 0 ? null : char.ToUpperInvariant(plain[0]) + plain[1..];
    }

    [GeneratedRegex("</?(?:modern|ancient)\\b[^>]*>")]
    private static partial Regex InlineTag();
}

/// <summary>
/// The licences a picture may be shown under, by the short codes the gazetteer writes, each with the
/// name a credit line prints and the page that states it.
/// </summary>
internal static partial class Licences
{
    internal sealed record Licence(string Name, string? Url);

    private static readonly Dictionary<string, Licence> Named = new(StringComparer.Ordinal)
    {
        ["CC-Zero"] = new("CC0 1.0", "https://creativecommons.org/publicdomain/zero/1.0/"),
        ["PD"] = new("Public domain", null),
        ["GFDL"] = new("GNU FDL", "https://www.gnu.org/licenses/fdl-1.3.html"),
        ["GPL"] = new("GNU GPL", "https://www.gnu.org/licenses/gpl-3.0.html"),
        ["FAL"] = new("Free Art License", "https://artlibre.org/licence/lal/en/"),
        ["OGL-1.0"] = new(
            "Open Government Licence 1.0",
            "https://www.nationalarchives.gov.uk/doc/open-government-licence/version/1/"),
        ["attribution"] = new("Attribution", "https://commons.wikimedia.org/wiki/Template:Attribution"),
        ["sentinel"] = new(
            "Copernicus Sentinel data terms",
            "https://sentinel.esa.int/documents/247904/690755/Sentinel_Data_Legal_Notice"),
    };

    /// <summary>
    /// The licence a code names, or null for one that permits nothing — <c>copyright</c> — or that
    /// nobody here has read. A Creative Commons code is read by its parts, <c>CC-BY-SA-3.0-DE</c>
    /// being the German port.
    /// </summary>
    public static Licence? Of(string? code)
    {
        if (code is null)
        {
            return null;
        }

        if (Named.TryGetValue(code, out var named))
        {
            return named;
        }

        var match = CreativeCommons().Match(code);
        if (!match.Success)
        {
            return null;
        }

        var terms = match.Groups["terms"].Value;
        var version = match.Groups["version"].Value;
        var port = match.Groups["port"].Success ? match.Groups["port"].Value : null;
        return new Licence(
            $"CC {terms} {version}" + (port is null ? string.Empty : $" {port}"),
            $"https://creativecommons.org/licenses/{terms.ToLowerInvariant()}/{version}/"
            + (port is null ? string.Empty : $"{port.ToLowerInvariant()}/"));
    }

    [GeneratedRegex("^CC-(?<terms>BY(?:-SA)?)-(?<version>[1-4]\\.[05])(?:-(?<port>[A-Z]{2,3}))?$")]
    private static partial Regex CreativeCommons();
}
