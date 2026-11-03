using System.Reflection;
using System.Text;

namespace Essenthos.Core.Products;

/// <summary>One file a product was made from, and the SHA-256 of that file as it stood.</summary>
/// <param name="Path">Relative to <c>Resources/</c>, with forward slashes: <c>bible4u/kjv.zip</c>, <c>Essenthos/texts/KJV/corrections.tsv</c>.</param>
/// <param name="Sha256">Sixty-four lowercase hex digits.</param>
public sealed record ProductInput(string Path, string Sha256)
{
    public const char Separator = '@';

    public static ProductInput Parse(ReadOnlySpan<char> text)
    {
        var at = text.LastIndexOf(Separator);
        if (at <= 0 || at == text.Length - 1)
        {
            throw new FormatException($"'{text}' is not an input (path@sha256).");
        }

        return new ProductInput(text[..at].ToString(), text[(at + 1)..].ToString());
    }

    public override string ToString() => $"{Path}{Separator}{Sha256}";
}

/// <summary>
/// What every product file says about itself before its rows: which product it is, which inputs
/// it was made from and how they stood, what made it, and how many rows it holds. The orchestrator
/// reads the inputs to decide whether the product is current; a person reads the rest.
///
/// <para>
/// Written as comment lines, <c># name  value</c>, the name padded so the values line up. The four
/// named here are typed; a producer may add lines of its own (<c># tradition</c>, <c># tests</c>),
/// which are kept in <see cref="Notes"/> in their order, and free comment lines, kept in
/// <see cref="Comments"/>. Every path in a header is relative to <c>Resources/</c>.
/// </para>
/// </summary>
public sealed class ProductHeader
{
    public const string ProductName = "product";
    public const string InputsName = "inputs";
    public const string ProducerName = "producer";
    public const string RowsName = "rows";

    /// <summary>Wide enough for the name column of the plan's examples, <c># producer   …</c>.</summary>
    private const int NameWidth = 11;

    private const string InputsJoin = "   ";

    public ProductHeader(string product, IReadOnlyList<ProductInput> inputs, string producer)
    {
        if (product.Contains(' ') || product.Contains('\\'))
        {
            throw new ArgumentException($"A product path has no spaces or backslashes: '{product}'.", nameof(product));
        }

        foreach (var input in inputs)
        {
            if (input.Path.Contains(' ') || input.Path.Contains('\\') || input.Path.Contains(ProductInput.Separator))
            {
                throw new ArgumentException($"An input path has no spaces, backslashes or '@': '{input.Path}'.", nameof(inputs));
            }
        }

        Product = product;
        Inputs = inputs;
        Producer = producer;
    }

    /// <summary>The product's own path, relative to <c>Resources/</c>: <c>Essenthos/texts/KJV/words.tsv</c>; empty in a hand-written file that omits it.</summary>
    public string Product { get; }

    /// <summary>What a hand-edited file writes as its producer.</summary>
    public const string ByHand = "by hand";

    /// <summary>Whether people edit this file: its producer is <see cref="ByHand"/>, or it names none.</summary>
    public bool IsHandWritten => Producer.Length == 0 || Producer.StartsWith(ByHand, StringComparison.Ordinal);

    public IReadOnlyList<ProductInput> Inputs { get; }

    /// <summary>The verb that made it and the assembly it ran in; <see cref="DescribeProducer"/> makes one.</summary>
    public string Producer { get; }

    /// <summary>Null until the rows are written or the file is read.</summary>
    public long? Rows { get; set; }

    public List<KeyValuePair<string, string>> Notes { get; } = [];

    public List<string> Comments { get; } = [];

    /// <summary>
    /// <c>forge convert KJV   (Essenthos.Forge 1.0.0+2b87c172)</c>: the verb as the producer names it,
    /// and the entry assembly with its informational version, which carries the commit.
    /// </summary>
    public static string DescribeProducer(string verb)
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ProductHeader).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "unversioned";
        return $"{verb}   ({assembly.GetName().Name} {version})";
    }

    /// <summary>
    /// The header's lines, each ending in LF. The rows line is right-aligned in a fixed width so a
    /// writer that learns the count last can come back and overwrite the digits in place.
    /// </summary>
    public string Format(long? rows = null)
    {
        var text = new StringBuilder();
        Line(text, ProductName, Product);
        Line(text, InputsName, string.Join(InputsJoin, Inputs));
        Line(text, ProducerName, Producer);
        Line(text, RowsName, RowsField(rows ?? Rows));
        foreach (var (name, value) in Notes)
        {
            Line(text, name, value);
        }

        foreach (var comment in Comments)
        {
            text.Append("# ").Append(comment).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Right-aligned so that any count of rows the corpus will ever hold overwrites the same bytes.</summary>
    public const int RowsWidth = 12;

    public static string RowsField(long? rows) => (rows?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).PadLeft(RowsWidth);

    /// <summary>Where the rows digits start in the formatted header, in characters, which are bytes since the header is ASCII up to there.</summary>
    public int RowsOffset()
    {
        var before = new StringBuilder();
        Line(before, ProductName, Product);
        Line(before, InputsName, string.Join(InputsJoin, Inputs));
        Line(before, ProducerName, Producer);
        return Encoding.UTF8.GetByteCount(before.ToString()) + ("# " + RowsName.PadRight(NameWidth)).Length;
    }

    private static void Line(StringBuilder text, string name, string value) =>
        text.Append("# ").Append(name.PadRight(NameWidth)).Append(value).Append('\n');

    /// <summary>
    /// Reads the comment lines at the top of a file, which the caller has already taken off it. A
    /// line is a named one when it starts with a lowercase word and two or more spaces; anything
    /// else is a free comment. Several <c>inputs</c> lines add up.
    /// </summary>
    public static ProductHeader Parse(IReadOnlyList<string> commentLines)
    {
        string? product = null;
        string? producer = null;
        long? rows = null;
        var inputs = new List<ProductInput>();
        var notes = new List<KeyValuePair<string, string>>();
        var comments = new List<string>();
        foreach (var raw in commentLines)
        {
            var line = raw.StartsWith("# ") ? raw[2..] : raw.TrimStart('#');
            if (!TryNamed(line, out var name, out var value))
            {
                comments.Add(line);
                continue;
            }

            switch (name)
            {
                case ProductName:
                    product = value;
                    break;
                case ProducerName:
                    producer = value;
                    break;
                case RowsName:
                    rows = value.Length == 0 ? null : long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case InputsName:
                    inputs.AddRange(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => ProductInput.Parse(part)));
                    break;
                default:
                    notes.Add(new KeyValuePair<string, string>(name, value));
                    break;
            }
        }

        // A file written by hand may say only `# producer   by hand`, or carry notes alone; what it
        // leaves out is empty rather than an error, and the writers always fill all four.
        var header = new ProductHeader(product ?? string.Empty, inputs, producer ?? string.Empty) { Rows = rows };
        header.Notes.AddRange(notes);
        header.Comments.AddRange(comments);
        return header;
    }

    private static bool TryNamed(string line, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;
        var end = 0;
        while (end < line.Length && (char.IsAsciiLetterLower(line[end]) || (end > 0 && (char.IsAsciiDigit(line[end]) || line[end] == '-'))))
        {
            end++;
        }

        if (end == 0 || end + 2 > line.Length || line[end] != ' ' || line[end + 1] != ' ')
        {
            return false;
        }

        name = line[..end];
        value = line[end..].Trim();
        return true;
    }
}
