using System.Globalization;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// How often the passes have put one form of the translation on one lexeme of the original.
/// </summary>
/// <param name="Safe">The placements of the pair that stood in the safe tier.</param>
/// <param name="Placed">Every lexical placement of the pair, safe or for review.</param>
/// <param name="FormPlaced">Every lexical placement of the form, whichever lexeme it landed on.</param>
internal readonly record struct ConfirmedRendering(int Safe, int Placed, int FormPlaced)
{
    public double Share => FormPlaced == 0 ? 0 : (double)Placed / FormPlaced;
}

/// <summary>
/// The pairs a pass placed over a whole text, counted: <em>livestock</em> on בְּהֵמָה forty times, by
/// whichever tier found each one. A text renders its own vocabulary consistently, so a pair the first
/// pass found many times by evidence of its own is evidence for the same pair in a verse where that
/// evidence was missing. The rows are kept by the form as written, and normalised when they are read
/// for a language, so a text of the same language can be read against them as well.
/// </summary>
internal sealed class EvidentiaConfirmedRenderings
{
    private const char Separator = '\t';

    private readonly Dictionary<(string Form, string StrongNumber), (int Safe, int Placed)> rows = [];

    public int Pairs => rows.Count;

    public void Add(string form, string strongNumber, int safe, int placed)
    {
        var key = (form.ToLowerInvariant(), strongNumber);
        var (knownSafe, knownPlaced) = rows.GetValueOrDefault(key);
        rows[key] = (knownSafe + safe, knownPlaced + placed);
    }

    /// <summary>
    /// Counts what another pass counted. Its pairs new here follow these in the order it first met them,
    /// so passes over the books one by one, added in book order, hold the rows one pass over all of them would.
    /// </summary>
    public void Add(EvidentiaConfirmedRenderings other)
    {
        foreach (var ((form, number), (safe, placed)) in other.rows)
        {
            Add(form, number, safe, placed);
        }
    }

    /// <summary>
    /// Counts what a chapter placed by lexical evidence in its own verse. A word attached to its head
    /// says nothing of its own, and a pair the confirmed renderings themselves placed is not counted
    /// again: it would raise the share of the pair that placed it.
    /// </summary>
    public void Learn(IEnumerable<EvidentiaProposal> lexical, IReadOnlySet<(long From, long To)> safe)
    {
        foreach (var proposal in lexical)
        {
            if (proposal.Kind is EvidentiaProposalKind.AttachedWord or EvidentiaProposalKind.ConfirmedRendering
                || proposal.Target.Token.StrongNumber is not { } number
                || proposal.Source.Token.Address != proposal.Target.Token.Address)
            {
                continue;
            }

            Add(proposal.Source.Token.Surface, number,
                safe.Contains((proposal.Source.Token.Id, proposal.Target.Token.Id)) ? 1 : 0, 1);
        }
    }

    /// <summary>The rows as a lookup for one language: by the form as written, and by the key its pack gives each form.</summary>
    public EvidentiaConfirmedIndex For(string language, LanguagePackRegistry languagePacks)
    {
        var byForm = new Dictionary<string, Dictionary<string, (int Safe, int Placed)>>(StringComparer.Ordinal);
        var byKey = new Dictionary<string, Dictionary<string, (int Safe, int Placed)>>(StringComparer.Ordinal);
        foreach (var ((form, number), counts) in rows)
        {
            var analysed = languagePacks.TryAnalyse(new EvidentiaToken(0, default, 0, form, language), out var analysis);
            Count(byForm, analysed ? EvidentiaConfirmedIndex.Form(analysis!) : form, number, counts);
            Count(byKey, analysed ? analysis!.Normalised : form, number, counts);
        }

        return new EvidentiaConfirmedIndex(byForm, byKey);
    }

    private static void Count(
        Dictionary<string, Dictionary<string, (int Safe, int Placed)>> index,
        string key,
        string number,
        (int Safe, int Placed) counts)
    {
        if (!index.TryGetValue(key, out var byNumber))
        {
            index[key] = byNumber = new Dictionary<string, (int Safe, int Placed)>(StringComparer.Ordinal);
        }

        var known = byNumber.GetValueOrDefault(number);
        byNumber[number] = (known.Safe + counts.Safe, known.Placed + counts.Placed);
    }

    public static EvidentiaConfirmedRenderings Read(IEnumerable<string> paths)
    {
        var renderings = new EvidentiaConfirmedRenderings();
        // A folder stands for the files in it: a whole text measured book by book writes one each.
        foreach (var path in paths.SelectMany<string, string>(path =>
                     Directory.Exists(path) ? Directory.EnumerateFiles(path, "*.tsv").Order(StringComparer.Ordinal) : [path]))
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"There is no confirmed-renderings file at {path}. Write one with evidentia-confirmed, or with " +
                    "--confirmed-out on a measurement.", path);
            }

            foreach (var line in File.ReadLines(path))
            {
                var fields = line.Split(Separator);
                if (fields.Length != 4
                    || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var safe)
                    || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var placed))
                {
                    throw new FormatException(
                        $"'{line}' in {path} is not a confirmed rendering. Each line is a form, a Strong number, the " +
                        "safe placements and all placements, separated by tabs.");
                }

                renderings.Add(fields[0], fields[1], safe, placed);
            }
        }

        return renderings;
    }

    public void Write(string path) => File.WriteAllLines(path, rows
        .OrderBy(row => row.Key.Form, StringComparer.Ordinal)
        .ThenBy(row => row.Key.StrongNumber, StringComparer.Ordinal)
        .Select(row => string.Join(Separator, row.Key.Form, row.Key.StrongNumber,
            row.Value.Safe.ToString(CultureInfo.InvariantCulture), row.Value.Placed.ToString(CultureInfo.InvariantCulture))));
}

/// <summary>
/// The confirmed renderings of one language. A word is read by its form as written where the passes
/// placed that form at all, and only otherwise by its normalisation: <em>here</em> and <em>her</em>
/// normalise alike, and the placements of one say nothing of the other.
/// </summary>
internal sealed class EvidentiaConfirmedIndex(
    IReadOnlyDictionary<string, Dictionary<string, (int Safe, int Placed)>> byForm,
    IReadOnlyDictionary<string, Dictionary<string, (int Safe, int Placed)>> byKey)
{
    /// <summary>The form as the pack reads it, without the marks an edition prints against it.</summary>
    public static string Form(EvidentiaAnalysis analysis) => analysis.Token.Surface.ToLowerInvariant();

    public IEnumerable<(string StrongNumber, ConfirmedRendering Rendering)> Of(EvidentiaAnalysis analysis)
    {
        if (!byForm.TryGetValue(Form(analysis), out var byNumber) && !byKey.TryGetValue(analysis.Normalised, out byNumber))
        {
            return [];
        }

        var all = byNumber.Values.Sum(counts => counts.Placed);
        return byNumber.Select(pair => (pair.Key, new ConfirmedRendering(pair.Value.Safe, pair.Value.Placed, all)));
    }

    public ConfirmedRendering? Of(EvidentiaAnalysis analysis, string strongNumber) =>
        Of(analysis).Where(rendering => rendering.StrongNumber == strongNumber)
            .Select(rendering => (ConfirmedRendering?)rendering.Rendering)
            .FirstOrDefault();
}
