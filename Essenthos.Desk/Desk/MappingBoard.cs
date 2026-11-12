using System.Globalization;
using System.Text.Json;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Tsv;

namespace Essenthos.Core.Desk;

/// <summary>
/// One measurement of how right the links of a text to an original are, as <c>mapping/measures.tsv</c> records it.
/// </summary>
/// <param name="Text">The text, or <c>*</c> for a method measured on its own benchmark.</param>
/// <param name="Original">BHSA, NESTLE1904, TR1894, or <c>*</c> for every original of the text.</param>
/// <param name="Testament"><c>OT</c>, <c>NT</c> or <c>*</c>.</param>
/// <param name="Method">The method whose links were judged, or <c>all</c> for every link whatever its method.</param>
/// <param name="Band">The links judged by how sure they are: <c>all</c>, <c>&gt;=0.9</c>, <c>0.7-0.9</c> or <c>&lt;0.7</c>.</param>
/// <param name="Kind"><c>precision</c> (judged against a reference taken as right) or <c>agreement</c> (two independent sources compared, neither taken as right).</param>
/// <param name="Precision">The share judged right, in percent.</param>
/// <param name="N">How many words were judged.</param>
/// <param name="Against">What they were judged against.</param>
/// <param name="Source">The ticket that holds the measurement.</param>
/// <param name="Date">When it was measured.</param>
internal sealed record MappingMeasure(
    string Text,
    string Original,
    string Testament,
    string Method,
    string Band,
    string Kind,
    double Precision,
    int N,
    string Against,
    string Source,
    string Date,
    string? Note);

/// <param name="Counting">Whether a count is running now.</param>
/// <param name="Stale">Whether a file the count read has moved since: the figures shown are the last ones, and a new count is on its way.</param>
/// <param name="Problem">Why the last count failed, where it did.</param>
internal sealed record MappingState(string? At, double? Seconds, bool Counting, string? CountingSince, bool Stale, string? Problem);

/// <param name="MeasureProblems">Lines of the measures file that could not be read, each with why.</param>
internal sealed record MappingResponse(
    IReadOnlyList<TextMapping> Texts,
    MappingState State,
    IReadOnlyList<MappingMeasure> Measures,
    IReadOnlyList<string> MeasureProblems);

/// <summary>
/// How far each text's mapping to the originals reaches, for the console's texts page: the count of the build
/// store's files (<see cref="MappingCount"/>), kept on disk with what the files were when it was made and made
/// again in the background when one moves, and the measured precision people record by hand in
/// <c>mapping/measures.tsv</c>, read on every request because it is a few dozen lines.
/// </summary>
internal sealed class MappingBoard(DeskPaths paths, ILogger<MappingBoard> logger)
{
    public const string CountFile = "text-mapping.json";

    public const string MeasuresProduct = "mapping/measures";

    private static readonly string[] Testaments = ["OT", "NT", "*"];

    private static readonly string[] Bands = ["all", ">=0.9", "0.7-0.9", "<0.7"];

    private static readonly string[] Kinds = ["precision", "agreement"];

    private readonly Lock _lock = new();

    private MappingSnapshot? _snapshot;

    private bool _read;

    private Task? _counting;

    private string? _countingSince;

    private string? _problem;

    private string? _failedKey;

    public string CountPath => Path.Combine(paths.Cache, CountFile);

    private string Essenthos => Path.Combine(paths.BuildResources, ProductStore.EssenthosFolder);

    public MappingResponse Read()
    {
        var key = MappingCount.Key(Essenthos);
        MappingSnapshot? snapshot;
        lock (_lock)
        {
            if (!_read)
            {
                _snapshot = ReadKept();
                _read = true;
            }

            snapshot = _snapshot;
            var current = snapshot is not null && snapshot.Key == key;
            if (!current && _counting is not { IsCompleted: false } && _failedKey != key)
            {
                StartCount();
            }
        }

        var (measures, problems) = ReadMeasures(Path.Combine(paths.Owner, "mapping", "measures.tsv"));
        return new MappingResponse(snapshot?.Texts ?? [], State(snapshot, key), measures, problems);
    }

    /// <summary>Starts a count whatever the kept one says, unless one is running.</summary>
    public void Recount()
    {
        lock (_lock)
        {
            if (_counting is not { IsCompleted: false })
            {
                StartCount();
            }
        }
    }

    /// <summary>Waits for the count that is running, if one is; for a test, which cannot watch the page.</summary>
    public Task Counted()
    {
        lock (_lock)
        {
            return _counting ?? Task.CompletedTask;
        }
    }

    /// <summary>The measures file's rows, and a sentence for each line that could not be read.</summary>
    public static (IReadOnlyList<MappingMeasure> Measures, IReadOnlyList<string> Problems) ReadMeasures(string path)
    {
        if (!File.Exists(path))
        {
            return ([], []);
        }

        var measures = new List<MappingMeasure>();
        var problems = new List<string>();
        try
        {
            Parse(path, measures, problems);
        }
        catch (Exception exception) when (exception is ProductFormatException or IOException)
        {
            // A line a person broke while editing: what was read before it is shown, and the line is named.
            problems.Add(exception.Message);
        }

        return (measures, problems);
    }

    private static void Parse(string path, List<MappingMeasure> measures, List<string> problems)
    {
        using var reader = TsvFormat.HandEdited.Open(path);
        var columns = reader.Columns.Select(c => c.Name).ToList();
        string[] wanted = ["text", "original", "testament", "method", "band", "kind", "precision", "n", "against", "source", "date", "note"];
        var missing = wanted.Where(name => !columns.Contains(name)).ToList();
        if (missing.Count > 0)
        {
            problems.Add($"The measures file has no column {string.Join(", ", missing)}.");
            return;
        }

        var at = wanted.Select(name => columns.IndexOf(name)).ToArray();
        var line = 0;
        foreach (var row in reader.Rows())
        {
            line++;
            string? Field(int i) => row[at[i]] is { Length: > 0 } value ? value.Trim() : null;
            var text = Field(0);
            var original = Field(1);
            var testament = Field(2);
            var method = Field(3);
            var band = Field(4) ?? "all";
            var kind = Field(5);
            var why = new List<string>();
            if (text is null || original is null || method is null)
            {
                why.Add("text, original and method are needed");
            }

            if (testament is null || !Testaments.Contains(testament))
            {
                why.Add("the testament is OT, NT or *");
            }

            if (!Bands.Contains(band))
            {
                why.Add("the band is all, >=0.9, 0.7-0.9 or <0.7");
            }

            if (kind is null || !Kinds.Contains(kind))
            {
                why.Add("the kind is precision or agreement");
            }

            if (!double.TryParse(Field(6), NumberStyles.Float, CultureInfo.InvariantCulture, out var precision) || precision is < 0 or > 100)
            {
                why.Add("the precision is a percentage from 0 to 100");
            }

            if (!int.TryParse(Field(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0)
            {
                why.Add("n is how many words were judged, more than none");
            }

            if (Field(8) is null || Field(9) is null)
            {
                why.Add("what it was judged against and where it was measured are needed");
            }

            if (!DateOnly.TryParseExact(Field(10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                why.Add("the date is written yyyy-mm-dd");
            }

            if (why.Count > 0)
            {
                problems.Add($"Row {line} ({string.Join(" ", new[] { text, original, testament, method }.Where(f => f is not null))}): {string.Join("; ", why)}.");
                continue;
            }

            measures.Add(new MappingMeasure(text!, original!, testament!, method!, band, kind!, precision, n, Field(8)!, Field(9)!, Field(10)!, Field(11)));
        }
    }

    private void StartCount()
    {
        _problem = null;
        _countingSince = JsonFiles.Now();
        _counting = Task.Run(Count);
    }

    private MappingState State(MappingSnapshot? snapshot, string key)
    {
        lock (_lock)
        {
            var counting = _counting is { IsCompleted: false };
            return new MappingState(
                snapshot?.At,
                snapshot?.Seconds,
                counting,
                counting ? _countingSince : null,
                snapshot is not null && snapshot.Key != key,
                _problem);
        }
    }

    private void Count()
    {
        var key = string.Empty;
        try
        {
            key = MappingCount.Key(Essenthos);
            var snapshot = MappingCount.Count(Essenthos, CancellationToken.None);
            WriteKept(snapshot);
            lock (_lock)
            {
                _snapshot = snapshot;
                _failedKey = null;
            }

            logger.LogInformation("Counted the mapping of {Texts} texts in {Seconds} s", snapshot.Texts.Count, snapshot.Seconds);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Counting the mapping of the texts failed");
            lock (_lock)
            {
                // The same files are not counted again on every visit; a moved file, or the owner's button, tries again.
                _failedKey = key;
                _problem = exception is IOException or UnauthorizedAccessException
                    ? $"The build's files could not be read: {exception.Message}"
                    : $"The build's files could not be counted: {exception.Message}";
            }
        }
    }

    private MappingSnapshot? ReadKept()
    {
        if (!File.Exists(CountPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(CountPath);
            return JsonSerializer.Deserialize(stream, DeskJsonContext.Default.MappingSnapshot);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(exception, "The kept mapping count at {Path} could not be read; it will be made again", CountPath);
            return null;
        }
    }

    private void WriteKept(MappingSnapshot snapshot)
    {
        Directory.CreateDirectory(paths.Cache);
        var written = CountPath + ".writing";
        using (var stream = File.Create(written))
        {
            JsonSerializer.Serialize(stream, snapshot, DeskJsonContext.Default.MappingSnapshot);
        }

        File.Move(written, CountPath, overwrite: true);
    }
}
