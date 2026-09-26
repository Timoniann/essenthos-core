using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Ussher's years for the events BibleData leaves undated in his reckoning although his Annals date
/// them, read from the paragraphs <c>UssherDatings.json</c> names.
///
/// A period is drawn in a reckoning only where that reckoning dates both the events bounding it, so
/// one blank cell in BibleData's Ussher column takes a whole reign off his timeline. The list names
/// the paragraph for each such event, and the years come from the paragraph itself: his year from
/// the creation and the year before Christ he printed beside it, which have to agree with each
/// other before either is written.
/// </summary>
internal static class UssherDatings
{
    public const string Resource = "Essenthos.Core.Loading.Encyclopedia.UssherDatings.json";

    /// <summary>His year 1 is 4004 BC, and his year opens in the autumn, so a year BC is one of two of his.</summary>
    private const int FirstYearBeforeChrist = 4004;

    private const string BeforeChrist = "BC";

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    /// <summary>One event's date in Ussher's reckoning, as an <see cref="Database.Entities.EventDate"/> holds it.</summary>
    internal sealed record Dating(int Year, int StatedYear, string Citation);

    private sealed record Listed(string Event, int Paragraph, string? Says);

    private sealed record ListFile(IReadOnlyList<Listed> Events);

    /// <summary>Each listed event's date by its slug, or none where the folder has no Annals.</summary>
    internal static IReadOnlyDictionary<string, Dating> Read(string folder)
    {
        var path = Path.Combine(folder, UssherAnnalsLoader.File);
        if (!File.Exists(path))
        {
            return new Dictionary<string, Dating>();
        }

        var listed = List();
        var wanted = listed.Select(one => Number(one.Paragraph)).ToHashSet(StringComparer.Ordinal);
        var paragraphs = Csv.Read(path)
            .Where(row => wanted.Contains(row["paragraph_nr"]))
            .ToDictionary(row => row["paragraph_nr"], StringComparer.Ordinal);

        return listed.ToDictionary(
            one => one.Event,
            one => paragraphs.TryGetValue(Number(one.Paragraph), out var row)
                ? Of(row, one)
                : throw new InvalidDataException(
                    $"UssherDatings.json dates {one.Event} from paragraph {one.Paragraph}, and " +
                    $"{UssherAnnalsLoader.File} has no such paragraph. Correct the number in the list."),
            StringComparer.Ordinal);
    }

    private static Dating Of(Dictionary<string, string> row, Listed one)
    {
        if (!string.Equals(row["gc_bc_ad"], BeforeChrist, StringComparison.Ordinal)
            || !int.TryParse(row["gc_year"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(row["am_year_only"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var anno))
        {
            throw new InvalidDataException(
                $"Paragraph {one.Paragraph}, which UssherDatings.json dates {one.Event} from, does not carry " +
                "both a year from the creation and a year before Christ. Name a paragraph that does, or " +
                "take the event off the list.");
        }

        if (anno != FirstYearBeforeChrist - year && anno != FirstYearBeforeChrist - year + 1)
        {
            throw new InvalidDataException(
                $"Paragraph {one.Paragraph}, which UssherDatings.json dates {one.Event} from, reads year " +
                $"{anno} from the creation and {year} BC, which his reckoning cannot both be. Name another " +
                "paragraph, or take the event off the list.");
        }

        return new Dating(anno, -year, $"¶{one.Paragraph}");
    }

    private static IReadOnlyList<Listed> List()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{Resource}\" is not in this assembly. It is added by the " +
                               "EmbeddedResource item in Essenthos.Forge.csproj; if the file was moved or renamed, " +
                               "that item and this name have to move with it.",
                               Resource);

        return (JsonSerializer.Deserialize<ListFile>(stream, Shape)
                ?? throw new InvalidDataException($"The embedded resource \"{Resource}\" is empty.")).Events;
    }

    private static string Number(int paragraph) => paragraph.ToString(CultureInfo.InvariantCulture);
}
