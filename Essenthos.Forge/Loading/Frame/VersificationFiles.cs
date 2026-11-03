using System.Reflection;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Frame;

/// <summary>One row of a versification file, by its column names, with the line it stands on.</summary>
internal sealed class VersificationLine(string file, int number, IReadOnlyDictionary<string, string> fields)
{
    public int Number => number;

    public string this[string column] => fields[column];

    /// <summary>The cell, or null where it is empty or <c>-</c>.</summary>
    public string? Optional(string column) => fields[column] is "" or "-" ? null : fields[column];

    public string Required(string column) =>
        Optional(column) ?? throw Fail($"the column \"{column}\" is empty");

    public Versification Tradition(string column) =>
        Enum.TryParse<Versification>(this[column], ignoreCase: true, out var tradition) && Enum.IsDefined(tradition)
            ? tradition
            : throw Fail($"\"{this[column]}\" is not a numbering tradition ({string.Join(", ", Enum.GetNames<Versification>())})");

    public FormatException Fail(string problem) => new($"{file} line {number}: {problem}.");
}

/// <summary>
/// The versification rules that are this project's rather than the versification data's, kept as
/// tab-separated files under <c>Resources/Essenthos/versification/</c> and compiled into the Forge, as
/// <c>geez.tsv</c> is, so a worktree reads its own copy and a changed rule moves the frame step's digest.
///
/// <para>
/// Every file has a comment head (<c>#</c> lines) saying what its rows are, then a line naming the
/// columns, then one row per line. A rule's <c>why</c> is what was read to write it.
/// </para>
/// </summary>
internal static class VersificationFiles
{
    public const string Schemes = "Essenthos.Core.Loading.Frame.Versification.schemes.tsv";

    public const string Corrections = "Essenthos.Core.Loading.Frame.Versification.corrections.tsv";

    public const string Supplements = "Essenthos.Core.Loading.Frame.Versification.supplements.tsv";

    public const string BookTraditions = "Essenthos.Core.Loading.Frame.Versification.book-traditions.tsv";

    public const string EditionReadings = "Essenthos.Core.Loading.Frame.Versification.edition-readings.tsv";

    private const string Prefix = "Essenthos.Core.Loading.Frame.Versification.";

    /// <summary>
    /// The rows of one file, refusing a file whose columns are not the ones named. Splitting a line is
    /// the shared product reader's work once it exists (TSK-1442); until then these files hold no tab,
    /// line break or backslash inside a cell, and a backslash is refused rather than read wrongly.
    /// </summary>
    public static IReadOnlyList<VersificationLine> Read(string resource, params string[] columns)
    {
        var file = resource[Prefix.Length..];
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException(
                               $"{resource} is not embedded in the Forge; Resources/Essenthos/versification/{file} is " +
                               "listed as an EmbeddedResource in its project file.");
        using var reader = new StreamReader(stream);

        var rows = new List<VersificationLine>();
        string[]? header = null;
        var number = 0;
        while (reader.ReadLine() is { } line)
        {
            number++;
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.Contains('\\'))
            {
                throw new FormatException($"{file} line {number}: a backslash is not read in a versification file yet.");
            }

            var fields = line.Split('\t');
            if (header is null)
            {
                header = fields;
                if (!header.SequenceEqual(columns))
                {
                    throw new FormatException(
                        $"{file} line {number}: the columns are \"{string.Join(' ', header)}\" where " +
                        $"\"{string.Join(' ', columns)}\" are expected.");
                }

                continue;
            }

            if (fields.Length != header.Length)
            {
                throw new FormatException(
                    $"{file} line {number} has {fields.Length} cells where {header.Length} columns are named.");
            }

            rows.Add(new VersificationLine(file, number,
                header.Select((column, index) => (column, index)).ToDictionary(pair => pair.column, pair => fields[pair.index].Trim())));
        }

        return header is null ? throw new FormatException($"{file} names no columns.") : rows;
    }
}
