using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <summary>
/// Reads Tyndale's TVTMS — Translators Versification Traditions with Methodology for
/// Standardisation — into one <see cref="VersificationFrame"/> per tradition.
///
/// The frame is imported rather than invented, and that is the whole point of this task: a frame
/// worked out from the texts themselves passes every test and is wrong exactly where a reader is
/// comparing, because that is where the traditions disagree.
///
/// CC BY 4.0, Tyndale House Cambridge, credited on the text rows that use it.
/// </summary>
internal static class TvtmsReader
{
    private const string ExpandedSectionMarker = "#DataStart(Expanded)";
    private const string SectionEndMarker = "#DataEnd";
    private const char TraditionSeparator = '+';

    /// <summary>
    /// The names this file gives the traditions the corpus uses. The Russian schemes are absent from
    /// it, so a Synodal text cannot be placed from this source and has to say so rather than fall
    /// back to its own numbering and look right.
    /// </summary>
    private static readonly Dictionary<string, Versification> Traditions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hebrew"] = Versification.Original,
        ["Eng-KJV"] = Versification.English,
        ["Greek"] = Versification.Septuagint,
        ["Latin"] = Versification.Vulgate,
    };

    public static IReadOnlyDictionary<Versification, VersificationFrame> Read(string path)
    {
        var collected = new Dictionary<Versification, Dictionary<CanonicalReference, IReadOnlyList<CanonicalReference>>>();
        foreach (var tradition in Traditions.Values)
        {
            collected[tradition] = [];
        }

        var inSection = false;
        foreach (var line in File.ReadLines(path))
        {
            if (!inSection)
            {
                inSection = line.StartsWith(ExpandedSectionMarker, StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(SectionEndMarker, StringComparison.Ordinal))
            {
                break;
            }

            ReadRow(line, collected);
        }

        if (collected.Values.All(rules => rules.Count == 0))
        {
            throw new InvalidOperationException(
                $"No versification rules were read from {path}. The file should carry a " +
                $"\"{ExpandedSectionMarker}\" section of tab-separated rows; check that it is the TVTMS " +
                "release and not the spreadsheet export.");
        }

        return collected.ToDictionary(
            entry => entry.Key,
            entry => new VersificationFrame(entry.Key, entry.Value));
    }

    private static void ReadRow(
        string line,
        Dictionary<Versification, Dictionary<CanonicalReference, IReadOnlyList<CanonicalReference>>> collected)
    {
        var columns = line.Split('\t');
        if (columns.Length < 3)
        {
            return;
        }

        var sourceTypes = columns[0].Trim();
        var sourceRef = columns[1].Trim();
        var standardRef = columns[2].Trim();
        if (sourceTypes.Length == 0 || sourceTypes.StartsWith('\'') || sourceTypes == "SourceType")
        {
            return;
        }

        var sources = CanonicalReference.ParseAll(sourceRef);
        var standards = CanonicalReference.ParseAll(standardRef);
        if (sources.Count == 0 || standards.Count == 0)
        {
            return;
        }

        foreach (var name in sourceTypes.Split(TraditionSeparator, StringSplitOptions.TrimEntries))
        {
            if (!Traditions.TryGetValue(name, out var tradition))
            {
                continue;
            }

            var rules = collected[tradition];

            // A source verse split into parts appears once per part, each part placed separately.
            // The parts together are what the verse spans, so they accumulate rather than replace.
            foreach (var source in sources)
            {
                if (!rules.TryGetValue(source, out var existing))
                {
                    rules[source] = standards;
                    continue;
                }

                var merged = new List<CanonicalReference>(existing);
                merged.AddRange(standards.Where(s => !merged.Contains(s)));
                rules[source] = merged;
            }
        }
    }
}
