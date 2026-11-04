using System.Globalization;
using Essenthos.Core.Products.Essx;

namespace Essenthos.Core.Products.Tools;

/// <summary>What a view asks for: the first rows, a verse, rows where a column has a value, some columns.</summary>
public sealed record ViewQuery
{
    public int? Head { get; init; }

    /// <summary>Rows whose key column is in this verse; the key column is the first key-typed one unless named.</summary>
    public VerseKey? Verse { get; init; }

    public string? KeyColumn { get; init; }

    public IReadOnlyList<(string Column, string? Value)> Where { get; init; } = [];

    public IReadOnlyList<string>? Columns { get; init; }

    public string? Table { get; init; }
}

/// <summary>
/// Reads a product for a person: rows as TSV lines, or the statistics of the file. A binary file is
/// read by row group and only in the columns the view shows or filters on; a verse is found through
/// the group statistics of its key column and then by the key's decoded parts, so a verse costs its
/// group's key and shown columns, and only its own rows are turned into text.
/// </summary>
public static class ProductView
{
    public static void Rows(string path, ViewQuery query, TextWriter output)
    {
        if (TextTables.Kind(path) == EssxLayout.Extension)
        {
            EssxRows(path, query, output);
            return;
        }

        using var table = TextTables.Open(path, query.Table);
        var names = table.Names;
        var selected = Selected(names, query);
        var where = Where(names, query);
        var keyIndex = -1;
        if (query.Verse is not null)
        {
            keyIndex = query.KeyColumn is { } named ? names.IndexOf(named) : 0;
            if (keyIndex < 0)
            {
                throw new ArgumentException("No key column to find the verse by.");
            }
        }

        WriteNames(output, names, selected);
        var shown = 0;
        foreach (var row in table.Rows())
        {
            if (query.Verse is { } wanted && !InVerse(row[keyIndex], wanted)) continue;
            if (where.Any(condition => row[condition.Index] != condition.Value)) continue;
            output.Write(string.Join('\t', selected.Select(index => Tsv.TsvEscaping.Escape(row[index]))));
            output.Write('\n');
            if (++shown == query.Head) break;
        }
    }

    private static void EssxRows(string path, ViewQuery query, TextWriter output)
    {
        using var reader = EssxReader.Open(path);
        var table = query.Table is null ? reader.Tables[0] : reader.Table(query.Table);
        var names = table.Columns.Select(column => column.Name).ToList();
        var selected = Selected(names, query);
        var where = Where(names, query);
        var keyIndex = -1;
        if (query.Verse is not null)
        {
            keyIndex = query.KeyColumn is { } named
                ? names.IndexOf(named)
                : table.Columns.ToList().FindIndex(column => column.Type is ColumnType.WordKey or ColumnType.VerseKey);
            if (keyIndex < 0 || table.Columns[keyIndex].Type is not (ColumnType.WordKey or ColumnType.VerseKey))
            {
                throw new ArgumentException("No key column to find the verse by.");
            }
        }

        WriteNames(output, names, selected);
        if (query.Head == 0)
        {
            return;
        }

        string[] read = [.. selected.Concat(where.Select(condition => condition.Index)).Distinct().Select(index => names[index])];
        var shown = 0;
        foreach (var g in Groups(table, query.Verse, keyIndex))
        {
            var rows = query.Verse is { } verse ? RowsInVerse(table, g, keyIndex, verse) : null;
            if (rows is { Count: 0 })
            {
                continue;
            }

            var group = table.ReadGroup(g, read);
            var count = rows?.Count ?? group.Rows;
            for (var n = 0; n < count; n++)
            {
                var r = rows?[n] ?? n;
                if (where.Any(condition => group.Column(condition.Index).Text(r) != condition.Value)) continue;
                for (var c = 0; c < selected.Length; c++)
                {
                    if (c > 0) output.Write('\t');
                    Tsv.TsvEscaping.WriteEscaped(output, group.Column(selected[c]).Text(r));
                }

                output.Write('\n');
                if (++shown == query.Head) return;
            }
        }
    }

    /// <summary>Every group, or only those whose book and chapter statistics admit the verse.</summary>
    private static IEnumerable<int> Groups(EssxTable table, VerseKey? verse, int keyIndex)
    {
        if (verse is not { } wanted)
        {
            return Enumerable.Range(0, table.RowGroups);
        }

        var key = table.Info.Columns[keyIndex];
        var books = table.GroupsWhere(key.Physical[0], (min, max) => min is null || min <= wanted.Book && max >= wanted.Book).ToHashSet();
        return table.GroupsWhere(key.Physical[1], (min, max) => min is null || min <= wanted.Chapter && max >= wanted.Chapter).Where(books.Contains);
    }

    /// <summary>The rows of a group in the verse, compared on the key's decoded parts: no row is turned into text to find them.</summary>
    public static List<int> RowsInVerse(EssxTable table, int group, int keyIndex, VerseKey verse)
    {
        var key = (KeyData)table.ReadGroup(group, [table.Columns[keyIndex].Name]).Column(keyIndex);
        var rows = new List<int>();
        var books = key.Book.Values;
        var chapters = key.Chapter.Values;
        var verses = key.Verse.Values;
        for (var r = 0; r < books.Length; r++)
        {
            if (books[r] == verse.Book && chapters[r] == verse.Chapter && verses[r] == verse.Verse && !key.IsNull(r)
                && (key.ChapterLabel.Text(r) ?? string.Empty) == verse.ChapterLabel && (key.VerseLabel.Text(r) ?? string.Empty) == verse.VerseLabel)
            {
                rows.Add(r);
            }
        }

        return rows;
    }

    private static int[] Selected(IReadOnlyList<string> names, ViewQuery query) =>
        query.Columns is null
            ? [.. Enumerable.Range(0, names.Count)]
            : [.. query.Columns.Select(name => names.IndexOf(name) is var index and >= 0 ? index : throw new ArgumentException($"No column '{name}'; the columns are {string.Join(", ", names)}."))];

    private static List<(int Index, string? Value)> Where(IReadOnlyList<string> names, ViewQuery query) =>
        [.. query.Where.Select(condition => (names.IndexOf(condition.Column) is var index and >= 0 ? index : throw new ArgumentException($"No column '{condition.Column}'."), condition.Value))];

    private static void WriteNames(TextWriter output, IReadOnlyList<string> names, int[] selected)
    {
        output.Write(string.Join('\t', selected.Select(index => names[index])));
        output.Write('\n');
    }

    private static bool InVerse(string? key, VerseKey verse)
    {
        if (key is null) return false;
        if (VerseKey.TryParse(key, out var asVerse)) return asVerse == verse;
        return WordKey.TryParseOwn(key, "X", out var asWord) && asWord.Verse == verse;
    }

    /// <summary>The file in numbers: header, tables, columns with their encodings and sizes, row groups.</summary>
    public static void Stats(string path, TextWriter output)
    {
        if (TextTables.Kind(path) != EssxLayout.Extension)
        {
            using var table = TextTables.Open(path);
            output.WriteLine($"file\t{path}\t{new FileInfo(path).Length} bytes");
            if (table.Header is { } header) output.Write(header.Format());
            var nulls = new long[table.Names.Count];
            var rows = 0L;
            foreach (var row in table.Rows())
            {
                rows++;
                for (var i = 0; i < row.Length; i++) if (row[i] is null) nulls[i]++;
            }

            output.WriteLine($"rows\t{rows}");
            for (var i = 0; i < table.Names.Count; i++) output.WriteLine($"column\t{table.Names[i]}\tnulls={nulls[i]}");
            return;
        }

        using var reader = EssxReader.Open(path);
        output.WriteLine($"file\t{path}\t{reader.Length} bytes\tversion {reader.Footer.Version}");
        output.Write(reader.Header.Format());
        var footerLength = reader.Length - EssxLayout.TrailerLength - reader.Footer.Tables.SelectMany(t => t.RowGroups.SelectMany(g => g.Chunks.Select(c => c.Length))).Sum()
                           - reader.Footer.Tables.SelectMany(t => t.Physical.Select(p => p.Dictionary?.Length ?? 0)).Sum() - EssxLayout.PreambleLength;
        output.WriteLine($"footer\t{footerLength} bytes");
        foreach (var table in reader.Tables)
        {
            var info = table.Info;
            output.WriteLine($"table\t{info.Name}\trows={info.Rows}\tgroups={info.RowGroups.Count}\tgroup={info.RowGroupRows}\tschema={info.SchemaVersion}");
            foreach (var column in info.Columns)
            {
                foreach (var physical in column.Physical)
                {
                    var index = info.Physical.FindIndex(p => p.Name == physical);
                    var stored = info.Physical[index];
                    var chunks = info.RowGroups.Select(group => group.Chunks[index]).ToList();
                    var bytes = chunks.Sum(chunk => chunk.Length) + (stored.Dictionary?.Length ?? 0);
                    var raw = chunks.Sum(chunk => chunk.RawLength) + (stored.Dictionary?.RawLength ?? 0);
                    var nulls = chunks.Sum(chunk => (long)chunk.Nulls);
                    var line = $"column\t{physical}\t{column.Type}{(column.Nullable ? "?" : "")}\t{stored.Encoding}\tbytes={bytes}";
                    if (raw != bytes) line += $"\traw={raw}";
                    if (nulls > 0) line += $"\tnulls={nulls}";
                    if (stored.Dictionary is { } dictionary) line += $"\tdistinct={dictionary.Count}";
                    if (stored.Encoding == "int")
                    {
                        var mins = chunks.Where(chunk => chunk.Min is not null).Select(chunk => chunk.Min!.Value).ToList();
                        if (mins.Count > 0) line += $"\tmin={mins.Min()}\tmax={chunks.Where(chunk => chunk.Max is not null).Max(chunk => chunk.Max!.Value)}";
                    }

                    if (stored.Encoding == "bool") line += $"\ttrue={chunks.Sum(chunk => chunk.Min ?? 0)}";
                    output.WriteLine(line);
                }
            }
        }
    }

    public static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
