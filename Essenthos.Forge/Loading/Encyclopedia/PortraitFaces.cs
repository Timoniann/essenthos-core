using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Where the face is in each picture of a person, and the head and shoulders a small rendering of
/// the picture shows around it.
///
/// <para>
/// The faces are measured offline by <c>scripts/find-faces.ps1</c> with Windows' own face detector,
/// and corrected by eye where it missed or chose a face in the crowd; the list is embedded here so
/// that a load on a server without that detector crops the same way. Each face carries the digest
/// of the file it was measured on, and a picture since replaced under the same name gets no crop
/// until the script is run again: the old face would frame the new picture's background.
/// </para>
/// </summary>
internal static class PortraitFaces
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.PortraitFaces.json";

    /// <summary>How many face widths across a bust is: the face, the hair and the shoulders to either side.</summary>
    internal const double BustPerFaceWidth = 2.6;

    /// <summary>How many face heights down a bust is, so a long face with a beard still has its shoulders.</summary>
    internal const double BustPerFaceHeight = 2.2;

    /// <summary>How far above the face the bust begins, in face heights: room for the hair or a headdress.</summary>
    internal const double HeadroomPerFaceHeight = 0.45;

    /// <param name="Digest">The first twelve hex digits of the SHA-256 of the file the face was measured on.</param>
    /// <param name="Face">Left, top, width and height, as fractions of the picture's; null where none was found.</param>
    internal sealed record Measured(string Digest, double[]? Face);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The faces by the path of the picture under the images folder.</summary>
    internal static IReadOnlyDictionary<string, Measured> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException(
                               $"{Resource} is not embedded in the assembly. It is listed in Essenthos.Forge.csproj " +
                               "as an EmbeddedResource; rebuild.");
        return JsonSerializer.Deserialize<Dictionary<string, Measured>>(stream, Json)
               ?? throw new InvalidDataException("PortraitFaces.json is empty. Run scripts/find-faces.ps1 to write it.");
    }

    /// <summary>
    /// The square of a picture that holds a face's head and shoulders, as fractions of the picture's
    /// width and height from the top left; null for a face that is not a box inside the picture.
    /// Where the square would run off an edge it is moved in, and where the picture is too small for
    /// it, it is made as large as the picture allows.
    /// </summary>
    internal static (double X, double Y, double Width, double Height)? Bust(double[] face, int width, int height)
    {
        if (face is not [var left and >= 0 and <= 1, var top and >= 0 and <= 1, var across and > 0, var down and > 0]
            || left + across > 1.0001 || top + down > 1.0001 || width <= 0 || height <= 0)
        {
            return null;
        }

        double faceWidth = across * width, faceHeight = down * height;
        var side = Math.Min(
            Math.Max(faceWidth * BustPerFaceWidth, faceHeight * BustPerFaceHeight),
            Math.Min(width, height));
        var x = Math.Clamp(left * width + faceWidth / 2 - side / 2, 0, width - side);
        var y = Math.Clamp(top * height - faceHeight * HeadroomPerFaceHeight, 0, height - side);
        return (x / width, y / height, side / width, side / height);
    }
}
