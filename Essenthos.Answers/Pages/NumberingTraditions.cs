using System.Reflection;

namespace Essenthos.Core.Pages;

/// <summary>
/// Which numbering tradition the frame follows in a book, which other tradition a chapter can be known by, and the
/// texts that other tradition is read from, from <c>NumberingTraditions.tsv</c>.
/// </summary>
internal static class NumberingTraditions
{
    private const string Resource = "NumberingTraditions.tsv";

    private sealed record Row(string Book, string Frame, string Other, IReadOnlyList<string> Texts);

    private static readonly Lazy<IReadOnlyList<Row>> Rows = new(Read);

    /// <summary>The frame's tradition in the book, and the other one.</summary>
    public static (string Frame, string Other) Of(string book)
    {
        var row = RowOf(book);
        return (row.Frame, row.Other);
    }

    /// <summary>The texts the book's other tradition is read from, first preferred.</summary>
    public static IReadOnlyList<string> Texts(string book) => RowOf(book).Texts;

    private static Row RowOf(string book) =>
        Rows.Value.FirstOrDefault(row => row.Book == book) ?? Rows.Value.First(row => row.Book == "*");

    private static IReadOnlyList<Row> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"The embedded resource {Resource} is missing from the build.");
        using var reader = new StreamReader(stream);
        var rows = new List<Row>();
        var header = true;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (header)
            {
                header = false;
                continue;
            }

            var cells = line.Split('\t');
            rows.Add(new Row(cells[0], cells[1], cells[2], cells[3].Split(',', StringSplitOptions.RemoveEmptyEntries)));
        }

        return rows;
    }
}
