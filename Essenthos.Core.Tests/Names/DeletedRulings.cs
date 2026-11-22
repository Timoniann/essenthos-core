namespace Essenthos.Core.Tests.Names;

/// <summary>
/// One word ruling of the 65 rulings files the old name loaders read, as <c>Resources/names/deleted-rulings.tsv</c> holds it:
/// the word by its text and address, the surface the ruling read, the records it names and one it must not name.
/// </summary>
internal sealed record DeletedRuling(
    string File,
    string Text,
    string Own,
    string Surface,
    IReadOnlyList<string> Names,
    string? Refuses,
    string Status,
    string? Why)
{
    public const string Holds = "holds";

    public const string Restored = "restored";

    public const string Superseded = "superseded";

    /// <summary>Every ruling, in the order the deleted files held them.</summary>
    public static IReadOnlyList<DeletedRuling> All { get; } = Read();

    private static List<DeletedRuling> Read()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "names", "deleted-rulings.tsv");
        var rows = new List<DeletedRuling>();
        string[]? header = null;
        foreach (var line in System.IO.File.ReadLines(path).Where(line => !line.StartsWith('#') && line.Length > 0))
        {
            var fields = line.Split('\t');
            if (header is null)
            {
                header = fields;
                continue;
            }

            string? Optional(string value) => value.Length == 0 ? null : value;
            rows.Add(new DeletedRuling(fields[0], fields[1], fields[2], fields[3], fields[4].Split(' '), Optional(fields[5]), fields[6], Optional(fields[7])));
        }

        return rows;
    }
}
