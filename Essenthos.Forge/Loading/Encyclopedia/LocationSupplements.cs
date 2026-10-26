using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record LocationBinding(string Slug, string AncientId, string Name, string Reference);
internal sealed record IndependentPoint(string ModernId, string Site, double Lon, double Lat, string Credit);
internal sealed record IndependentRegion(string Slug, string Site, string ModernId, double Lon, double Lat, string Credit);
internal sealed record LocationSupplement(
    IReadOnlyList<LocationBinding> Bindings, IReadOnlyList<IndependentPoint> Points, IReadOnlyList<IndependentRegion> Regions)
{
    public static LocationSupplement Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Essenthos.Core.Loading.Encyclopedia.LocationSupplements.json")
            ?? throw new InvalidOperationException("The location supplement is not embedded.");
        return JsonSerializer.Deserialize<LocationSupplement>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public void ValidateBindings(string ancientFile, IReadOnlySet<string> activeSlugs)
    {
        var needed = Bindings.Where(b => activeSlugs.Contains(b.Slug)).ToList();
        var source = new Dictionary<string, (string? Name, HashSet<string> Verses)>();
        foreach (var line in File.ReadLines(ancientFile).Where(l => l.Length > 0))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetString()!;
            if (needed.Any(b => b.AncientId == id))
                source[id] = (root.GetProperty("friendly_id").GetString(),
                    root.GetProperty("verses").EnumerateArray().Select(v => v.GetProperty("usx").GetString()!).ToHashSet(StringComparer.Ordinal));
        }
        foreach (var binding in needed)
            if (!source.TryGetValue(binding.AncientId, out var row) || row.Name != binding.Name || !row.Verses.Contains(binding.Reference))
                throw new InvalidDataException($"The place binding {binding.Slug}/{binding.AncientId} no longer matches its named, cited source.");
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
                        CoordinatesSource = patch.Credit,
                    };
                }
        }
        var located = new List<OpenBibleLocationLoader.Located>();
        foreach (var (id, answers) in reading.Candidates)
        {
            var first = answers[0];
            if (first is { Special: null, Withheld: null, Longitude: { } lon, Latitude: { } lat, Kind: { } kind })
                located.Add(new OpenBibleLocationLoader.Located(id, lon, lat, kind, first.Score, first.ModernId!, first.CoordinatesSource!));
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
