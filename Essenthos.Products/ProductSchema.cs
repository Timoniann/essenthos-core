using System.Globalization;

namespace Essenthos.Core.Products;

/// <summary>
/// The types a column may have. The TSV writes each as text; a binary format stores each as what it
/// is. <see cref="Json"/> is text the writer passes through unread, kept apart so a tool can show it
/// as a document and a loader can type the column <c>jsonb</c>.
/// </summary>
public enum ColumnType
{
    Text,
    Integer,
    Boolean,
    Json,
}

public sealed record Column(string Name, ColumnType Type, bool Nullable = false)
{
    public override string ToString() => $"{Name}:{Type.ToString().ToLowerInvariant()}{(Nullable ? "?" : "")}";
}

/// <summary>Where a schema puts one row's values, in column order; a format implements it.</summary>
public interface IRowSink
{
    void Text(string? value);
    void Integer(long? value);
    void Boolean(bool? value);
    void Json(string? value);
}

/// <summary>
/// One row as a reader hands it over: values by column index, typed on request. A reader may reuse
/// the row object from one row to the next, so what is wanted is taken out of it before moving on.
/// </summary>
public abstract class ProductRow
{
    protected ProductRow(IReadOnlyList<Column> columns)
    {
        Columns = columns;
    }

    public IReadOnlyList<Column> Columns { get; }

    public int Count => Columns.Count;

    public int IndexOf(string column)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Name == column)
            {
                return i;
            }
        }

        throw new ArgumentException($"No column '{column}'; the columns are {string.Join(", ", Columns.Select(c => c.Name))}.", nameof(column));
    }

    public abstract bool IsNull(int column);

    public abstract string? TextOrNull(int column);

    public string Text(int column) => TextOrNull(column) ?? throw Missing(column);

    public virtual long? IntegerOrNull(int column)
    {
        var text = TextOrNull(column);
        if (text is null)
        {
            return null;
        }

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ProductFormatException($"Column '{Columns[column].Name}' holds '{text}', not an integer.");
    }

    public long Integer(int column) => IntegerOrNull(column) ?? throw Missing(column);

    public int Int32(int column) => checked((int)Integer(column));

    public int? Int32OrNull(int column) => IntegerOrNull(column) is { } value ? checked((int)value) : null;

    public virtual bool? BooleanOrNull(int column)
    {
        var text = TextOrNull(column);
        return text switch
        {
            null => null,
            "t" => true,
            "f" => false,
            _ => throw new ProductFormatException($"Column '{Columns[column].Name}' holds '{text}', not t or f."),
        };
    }

    public bool Boolean(int column) => BooleanOrNull(column) ?? throw Missing(column);

    public string? JsonOrNull(int column) => TextOrNull(column);

    public string Json(int column) => Text(column);

    public string? TextOrNull(string column) => TextOrNull(IndexOf(column));
    public string Text(string column) => Text(IndexOf(column));
    public long? IntegerOrNull(string column) => IntegerOrNull(IndexOf(column));
    public long Integer(string column) => Integer(IndexOf(column));
    public int Int32(string column) => Int32(IndexOf(column));
    public bool Boolean(string column) => Boolean(IndexOf(column));

    /// <summary>Every value as text, null where the value is null; what a converter or a viewer takes.</summary>
    public string?[] ToTexts()
    {
        var texts = new string?[Count];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = TextOrNull(i);
        }

        return texts;
    }

    private ProductFormatException Missing(int column) =>
        new($"Column '{Columns[column].Name}' is null where a value was required.");
}

/// <summary>
/// How a row type maps onto columns: a getter per column for writing, and one function from a
/// <see cref="ProductRow"/> for reading. Declared once beside the row type and handed to whichever
/// format writes or reads the product, so a producer's code names columns and never a format.
/// </summary>
public sealed class ProductSchema<T>
{
    private readonly List<Column> _columns = [];
    private readonly List<Action<T, IRowSink>> _writers = [];
    private Func<ProductRow, T>? _read;

    public IReadOnlyList<Column> Columns => _columns;

    public ProductSchema<T> Text(string name, Func<T, string> get) =>
        Add(new Column(name, ColumnType.Text), (row, sink) => sink.Text(get(row) ?? throw NullIn(name)));

    public ProductSchema<T> OptionalText(string name, Func<T, string?> get) =>
        Add(new Column(name, ColumnType.Text, Nullable: true), (row, sink) => sink.Text(get(row)));

    public ProductSchema<T> Integer(string name, Func<T, long> get) =>
        Add(new Column(name, ColumnType.Integer), (row, sink) => sink.Integer(get(row)));

    public ProductSchema<T> OptionalInteger(string name, Func<T, long?> get) =>
        Add(new Column(name, ColumnType.Integer, Nullable: true), (row, sink) => sink.Integer(get(row)));

    public ProductSchema<T> Boolean(string name, Func<T, bool> get) =>
        Add(new Column(name, ColumnType.Boolean), (row, sink) => sink.Boolean(get(row)));

    public ProductSchema<T> OptionalBoolean(string name, Func<T, bool?> get) =>
        Add(new Column(name, ColumnType.Boolean, Nullable: true), (row, sink) => sink.Boolean(get(row)));

    public ProductSchema<T> Json(string name, Func<T, string> get) =>
        Add(new Column(name, ColumnType.Json), (row, sink) => sink.Json(get(row) ?? throw NullIn(name)));

    public ProductSchema<T> OptionalJson(string name, Func<T, string?> get) =>
        Add(new Column(name, ColumnType.Json, Nullable: true), (row, sink) => sink.Json(get(row)));

    /// <summary>A verse key, written as <c>1.31:50a</c>.</summary>
    public ProductSchema<T> VerseKey(string name, Func<T, VerseKey> get) =>
        Text(name, row => get(row).ToString());

    /// <summary>A word key of the file's own text, written as <c>1.1:1.3</c>.</summary>
    public ProductSchema<T> OwnWordKey(string name, Func<T, WordKey> get) =>
        Text(name, row => get(row).Own);

    public ProductSchema<T> Reads(Func<ProductRow, T> read)
    {
        _read = read;
        return this;
    }

    public void Write(T row, IRowSink sink)
    {
        foreach (var writer in _writers)
        {
            writer(row, sink);
        }
    }

    public T Read(ProductRow row) =>
        (_read ?? throw new InvalidOperationException("The schema has no reader; call Reads(...)."))(row);

    /// <summary>The file's columns must be the schema's, by name and in order; the message names the first difference.</summary>
    public void Check(IReadOnlyList<string> fileColumns, string path)
    {
        var count = Math.Max(fileColumns.Count, _columns.Count);
        for (var i = 0; i < count; i++)
        {
            var expected = i < _columns.Count ? _columns[i].Name : "(end)";
            var found = i < fileColumns.Count ? fileColumns[i] : "(end)";
            if (expected != found)
            {
                throw new ProductFormatException(
                    $"{path}: column {i + 1} is '{found}' where the schema expects '{expected}'. " +
                    $"File: {string.Join(", ", fileColumns)}. Schema: {string.Join(", ", _columns.Select(c => c.Name))}.");
            }
        }
    }

    private ProductSchema<T> Add(Column column, Action<T, IRowSink> write)
    {
        if (_columns.Any(existing => existing.Name == column.Name))
        {
            throw new ArgumentException($"Column '{column.Name}' is declared twice.");
        }

        _columns.Add(column);
        _writers.Add(write);
        return this;
    }

    private static ProductFormatException NullIn(string column) =>
        new($"Column '{column}' is not nullable and a row has null in it.");
}

public sealed class ProductFormatException(string message) : FormatException(message);
