using Essenthos.Core.Products;
using Essenthos.Core.Products.Esw;
using Essenthos.Core.Products.Tools;

// esw: the command line over product files.
//
//   esw view <file> [--head N] [--verse 1.1:1] [--key column] [--where column=value]... [--columns a,b] [--table name]
//   esw stats <file>
//   esw header <file>
//   esw convert <from> <to> [--types own:wordkey,count:integer] [--compress] [--group N] [--table name]
//   esw validate <file>...
//
// Output is TSV on standard output; problems go to standard error and the exit code is 1.

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.Error.WriteLine(Usage);
    return args.Length == 0 ? 1 : 0;
}

try
{
    return args[0] switch
    {
        "view" => View(args[1..]),
        "stats" => Stats(args[1..]),
        "header" => Header(args[1..]),
        "convert" => Convert(args[1..]),
        "validate" => Validate(args[1..]),
        _ => Fail($"Unknown command '{args[0]}'.\n{Usage}"),
    };
}
catch (Exception exception) when (exception is ProductFormatException or FormatException or ArgumentException or IOException)
{
    return Fail(exception.Message);
}

static int View(string[] args)
{
    var (file, options) = Parse(args, 1);
    var where = options.Where(option => option.Key == "where")
        .Select(option => option.Value.Split('=', 2) is [var column, var value] ? (column, value == @"\N" ? null : value) : throw new ArgumentException("--where takes column=value"))
        .ToList();
    var query = new ViewQuery
    {
        Head = Option(options, "head") is { } head ? int.Parse(head) : null,
        Verse = Option(options, "verse") is { } verse ? VerseKey.Parse(verse) : null,
        KeyColumn = Option(options, "key"),
        Where = where,
        Columns = Option(options, "columns")?.Split(','),
        Table = Option(options, "table"),
    };
    using var output = new StreamWriter(Console.OpenStandardOutput(), Utf8.NoBom, 1 << 16) { NewLine = "\n" };
    ProductView.Rows(file[0], query, output);
    return 0;
}

static int Stats(string[] args)
{
    var (file, _) = Parse(args, 1);
    using var output = new StreamWriter(Console.OpenStandardOutput(), Utf8.NoBom) { NewLine = "\n" };
    ProductView.Stats(file[0], output);
    return 0;
}

static int Header(string[] args)
{
    var (file, options) = Parse(args, 1);
    using var table = TextTables.Open(file[0], Option(options, "table"));
    Console.Out.Write(table.Header?.Format() ?? "(no header)\n");
    Console.Out.WriteLine(string.Join('\t', (table.Columns ?? table.Names.Select(name => new Column(name, ColumnType.Text, true))).Select(column => column.ToString())));
    return 0;
}

static int Convert(string[] args)
{
    var (files, options) = Parse(args, 2);
    var result = ProductConversion.Convert(files[0], files[1], new ConversionOptions
    {
        Types = Option(options, "types") is { } types ? TextTables.ParseTypes(types) : null,
        Table = Option(options, "table"),
        Esw = new EswOptions
        {
            Codec = options.Any(option => option.Key == "compress") ? EswLayout.CodecDeflate : EswLayout.CodecNone,
            RowGroupRows = Option(options, "group") is { } group ? int.Parse(group) : EswOptions.Default.RowGroupRows,
        },
    });
    Console.Error.WriteLine($"{result.Rows} rows into {result.Path}: {string.Join(", ", result.Columns)}");
    return 0;
}

static int Validate(string[] args)
{
    var (files, _) = Parse(args, 1);
    var failed = 0;
    foreach (var file in files)
    {
        var report = ProductValidation.Validate(file);
        Console.Out.WriteLine(report.Ok ? $"{file}\tok\t{report.Rows} rows" : $"{file}\tPROBLEMS\t{report.Rows} rows");
        foreach (var problem in report.Problems)
        {
            Console.Out.WriteLine($"\t{problem}");
            failed = 1;
        }
    }

    return failed;
}

static (List<string> Files, List<KeyValuePair<string, string>> Options) Parse(string[] args, int fewestFiles)
{
    var files = new List<string>();
    var options = new List<KeyValuePair<string, string>>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--"))
        {
            var name = args[i][2..];
            var takesValue = name is not "compress";
            options.Add(new(name, takesValue && i + 1 < args.Length ? args[++i] : string.Empty));
        }
        else
        {
            files.Add(args[i]);
        }
    }

    if (files.Count < fewestFiles)
    {
        throw new ArgumentException($"Expected {fewestFiles} file(s).\n{Usage}");
    }

    return (files, options);
}

static string? Option(List<KeyValuePair<string, string>> options, string name) =>
    options.LastOrDefault(option => option.Key == name).Value;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

partial class Program
{
    private const string Usage = """
        esw view <file> [--head N] [--verse 1.1:1] [--key column] [--where column=value]... [--columns a,b] [--table name]
        esw stats <file>
        esw header <file>
        esw convert <from> <to> [--types own:wordkey,count:integer] [--compress] [--group N] [--table name]
        esw validate <file>...
        Files are .esw, .tsv, .csv, .jsonl or .xml, told by extension.
        """;
}
