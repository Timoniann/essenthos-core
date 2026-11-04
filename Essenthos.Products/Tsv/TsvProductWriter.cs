using System.Globalization;

namespace Essenthos.Core.Products.Tsv;

/// <summary>
/// Writes a product as TSV: the header's comment lines, the column names, then a row per line in
/// <c>COPY</c> escaping, through an <see cref="AtomicFile"/>. The rows count is learned last and
/// written back over the digits the header reserved for it.
/// </summary>
public sealed class TsvProductWriter<T> : IProductWriter<T>, IRowSink
{
    private readonly ProductHeader _header;
    private readonly ProductSchema<T> _schema;
    private readonly AtomicFile _file;
    private readonly StreamWriter _writer;
    private readonly long _rowsOffset;
    private readonly bool _countsRows;
    private int _column;
    private bool _completed;

    /// <param name="countsRows">Write the rows count into the header; false for a file people edit, which then has no rows line.</param>
    public TsvProductWriter(string path, ProductHeader header, ProductSchema<T> schema, bool countsRows = true)
    {
        _header = header;
        _countsRows = countsRows;
        _schema = schema;
        _file = AtomicFile.Create(path);
        _writer = new StreamWriter(_file.Stream, Utf8.NoBom, 1 << 16, leaveOpen: true) { NewLine = "\n" };
        _rowsOffset = header.RowsOffset();
        _writer.Write(header.Format(rows: null, withRows: countsRows));
        _writer.Write(string.Join('\t', schema.Columns.Select(column => column.Name)));
        _writer.Write('\n');
    }

    public string Path => _file.Path;

    public long Rows { get; private set; }

    public void Write(T row)
    {
        _column = 0;
        _schema.Write(row, this);
        _writer.Write('\n');
        Rows++;
    }

    public void Complete()
    {
        if (_completed)
        {
            return;
        }

        _writer.Flush();
        if (_countsRows)
        {
            var stream = _file.Stream;
            var end = stream.Position;
            stream.Position = _rowsOffset;
            stream.Write(System.Text.Encoding.ASCII.GetBytes(ProductHeader.RowsField(Rows)));
            stream.Position = end;
            _header.Rows = Rows;
        }

        _file.Commit();
        _completed = true;
    }

    public void Dispose()
    {
        if (!_completed)
        {
            _writer.Dispose();
        }

        _file.Dispose();
    }

    private void Separate()
    {
        if (_column++ > 0)
        {
            _writer.Write('\t');
        }
    }

    void IRowSink.Text(string? value)
    {
        Separate();
        TsvEscaping.WriteEscaped(_writer, value);
    }

    void IRowSink.Integer(long? value)
    {
        Separate();
        if (value is null)
        {
            _writer.Write(TsvEscaping.Null);
        }
        else
        {
            _writer.Write(value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    void IRowSink.Boolean(bool? value)
    {
        Separate();
        _writer.Write(value switch { null => TsvEscaping.Null, true => "t", false => "f" });
    }

    void IRowSink.Json(string? value)
    {
        Separate();
        TsvEscaping.WriteEscaped(_writer, value);
    }

    void IRowSink.VerseKey(VerseKey? value)
    {
        Separate();
        _writer.Write(value?.ToString() ?? TsvEscaping.Null);
    }

    void IRowSink.WordKey(WordKey? value)
    {
        Separate();
        _writer.Write(value?.Own ?? TsvEscaping.Null);
    }
}
