using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Dataset">The declared dataset the coordinates are credited to, as <see cref="Datasets"/> names it.</param>
/// <param name="Credit">Where in that dataset they were read: the page, the table, the revision.</param>
internal sealed record IndependentPoint(string ModernId, string Site, double Lon, double Lat, string Dataset, string Credit);

/// <param name="Dataset">The declared dataset the point is credited to, as <see cref="Datasets"/> names it.</param>
internal sealed record IndependentRegion(
    string Slug, string Site, string ModernId, double Lon, double Lat, string Dataset, string Credit);

/// <summary>
/// Coordinates read from a named source other than the gazetteer, for sites whose gazetteer point
/// is OpenStreetMap's and so cannot be held, and one region the gazetteer does not place at all.
/// The identification, its rank and its score stay the gazetteer's; only the point is another's,
/// and it is credited to that other source.
/// </summary>
internal sealed record LocationSupplement(IReadOnlyList<IndependentPoint> Points, IReadOnlyList<IndependentRegion> Regions)
{
    public static LocationSupplement Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Essenthos.Core.Loading.Encyclopedia.LocationSupplements.json")
            ?? throw new InvalidOperationException("The location supplement is not embedded.");
        var supplement = JsonSerializer.Deserialize<LocationSupplement>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var dataset in supplement.Points.Select(p => p.Dataset).Concat(supplement.Regions.Select(r => r.Dataset)))
            if (!Datasets.All.Any(d => d.Id == dataset && !d.Unlisted))
                throw new InvalidDataException($"The location supplement credits \"{dataset}\", which is no declared dataset.");
        return supplement;
    }

    public OpenBibleLocationLoader.Reading Apply(OpenBibleLocationLoader.Reading reading)
    {
        foreach (var patch in Points)
        {
            if (!ValidPoint(patch.Lon, patch.Lat) || string.IsNullOrWhiteSpace(patch.Credit))
                throw new InvalidDataException($"Invalid independently sourced point: {patch.ModernId}.");
            foreach (var answers in reading.Candidates.Values)
                for (var i = 0; i < answers.Count; i++)
                {
                    var candidate = answers[i];
                    if (candidate.ModernId != patch.ModernId) continue;
                    if (candidate.Site != patch.Site || candidate.Withheld != "openstreetmap")
                        throw new InvalidDataException($"The independent point no longer matches its withheld site: {patch.ModernId}.");
                    answers[i] = candidate with
                    {
                        Longitude = patch.Lon, Latitude = patch.Lat, Kind = "point", Withheld = null,
                        CoordinatesSource = patch.Dataset, CoordinatesDataset = patch.Dataset,
                        CoordinatesCredit = patch.Credit,
                    };
                }
        }
        var located = new List<OpenBibleLocationLoader.Located>();
        foreach (var (id, answers) in reading.Candidates)
        {
            var first = answers[0];
            if (first is { Special: null, Withheld: null, Longitude: { } lon, Latitude: { } lat, Kind: { } kind })
                located.Add(new OpenBibleLocationLoader.Located(id, lon, lat, kind, first.Score, first.ModernId!, first.CoordinatesSource!)
                {
                    CoordinatesDataset = first.CoordinatesDataset,
                    CoordinatesCredit = first.CoordinatesCredit,
                });
        }
        return reading with
        {
            Located = located,
            OpenStreetMap = reading.Candidates.Values.Count(a => a[0].Withheld == "openstreetmap"),
        };
    }

    internal static bool ValidPoint(double lon, double lat) => double.IsFinite(lon) && double.IsFinite(lat)
        && lon is >= -180 and <= 180 && lat is >= -90 and <= 90;
}
