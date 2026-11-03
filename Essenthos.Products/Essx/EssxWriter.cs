using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Essenthos.Core.Products.Essx;

/// <summary>
/// Writes an ESSX file: one or more tables, each as row groups of column chunks, string columns
/// through a dictionary shared by the whole table, the footer last. Rows stream in; memory holds
/// one row group per table and the dictionaries. <see cref="Complete"/> writes the dictionaries,
/// the footer and the trailer and moves the file into place; disposing without it leaves the
/// previous file.
/// </summary>
public sealed class EssxWriter : IDisposable
{
    private readonly ProductHeader _header;
    private readonly EssxOptions _options;
    private readonly AtomicFile _file;
    private readonly Output _output;
    private readonly List<ITableWriter> _tables = [];
    private bool _completed;

    private EssxWriter(string path, ProductHeader header, EssxOptions options)
    {
        _header = header;
        _options = options;
        _file = AtomicFile.Create(path);
        _output = new Output(_file.Stream);
        _output.Write(EssxLayout.Magic);
        Span<byte> preamble = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(preamble, EssxLayout.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(preamble[2..], 0);
        _output.Write(preamble);
    }

    public static EssxWriter Create(string path, ProductHeader header, EssxOptions? options = null) =>
        new(path, header, options ?? EssxOptions.Default);

    public string Path => _file.Path;

    /// <summary>Adds a table. Its rows may be written in any order with the other tables' rows.</summary>
    public EssxTableWriter<T> Table<T>(string name, ProductSchema<T> schema)
    {
        if (_tables.Any(table => table.Name == name))
        {
            throw new ArgumentException($"Table '{name}' is already in the file.", nameof(name));
        }

        var table = new EssxTableWriter<T>(name, schema, _options, _output);
        _tables.Add(table);
        return table;
    }

    /// <summary>Finishes every table, writes the footer and the trailer, and moves the file into place.</summary>
    public void Complete()
    {
        if (_completed)
        {
            return;
        }

        var footer = new EssxFooter { Product = EssxProductInfo.From(_header) };
        foreach (var table in _tables)
        {
            footer.Tables.Add(table.Finish());
        }

        footer.Product.Rows = _tables.Count == 0 ? 0 : footer.Tables[0].Rows;
        _header.Rows = footer.Product.Rows;

        var footerOffset = _output.Position;
        var json = JsonSerializer.SerializeToUtf8Bytes(footer, EssxLayout.Json);
        _output.Write(json);

        Span<byte> trailer = stackalloc byte[EssxLayout.TrailerLength - EssxLayout.HashLength];
        BinaryPrimitives.WriteInt64LittleEndian(trailer, footerOffset);
        BinaryPrimitives.WriteInt64LittleEndian(trailer[8..], json.Length);
        EssxLayout.Magic.CopyTo(trailer[16..]);
        _output.Write(trailer);
        _file.Stream.Write(_output.Hash());
        _file.Commit();
        _completed = true;
    }

    public void Dispose() => _file.Dispose();

    /// <summary>The file stream with a running hash and a byte count, which every section goes through.</summary>
    internal sealed class Output(Stream stream)
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long Position { get; private set; }

        public void Write(ReadOnlySpan<byte> bytes)
        {
            stream.Write(bytes);
            _hash.AppendData(bytes);
            Position += bytes.Length;
        }

        public byte[] Hash() => _hash.GetHashAndReset();
    }

    internal interface ITableWriter
    {
        string Name { get; }

        EssxTableInfo Finish();
    }
}

/// <summary>One table of an <see cref="EssxWriter"/>; also a product writer when the file holds one table.</summary>
public sealed class EssxTableWriter<T> : IProductWriter<T>, IRowSink, EssxWriter.ITableWriter
{
    private readonly ProductSchema<T> _schema;
    private readonly EssxOptions _options;
    private readonly EssxWriter.Output _output;
    private readonly ColumnEncoder[] _encoders;
    private readonly List<EssxRowGroupInfo> _groups = [];
    private int _column;
    private int _inGroup;
    private bool _finished;

    internal EssxTableWriter(string name, ProductSchema<T> schema, EssxOptions options, EssxWriter.Output output)
    {
        Name = name;
        _schema = schema;
        _options = options;
        _output = output;
        _encoders = [.. schema.Columns.Select(column => ColumnEncoder.For(column, options))];
    }

    public string Name { get; }

    public string Path => throw new NotSupportedException("A table has no path of its own; the file has.");

    public long Rows { get; private set; }

    public void Write(T row)
    {
        _column = 0;
        _schema.Write(row, this);
        Rows++;
        if (++_inGroup == _options.RowGroupRows)
        {
            FlushGroup();
        }
    }

    /// <summary>Flushes the last group. The file is finished by <see cref="EssxWriter.Complete"/>.</summary>
    public void Complete()
    {
    }

    public void Dispose()
    {
    }

    EssxTableInfo EssxWriter.ITableWriter.Finish()
    {
        if (_finished)
        {
            throw new InvalidOperationException("The table was finished twice.");
        }

        _finished = true;
        if (_inGroup > 0)
        {
            FlushGroup();
        }

        var info = new EssxTableInfo
        {
            Name = Name,
            SchemaVersion = _schema.Version,
            Rows = Rows,
            RowGroupRows = _options.RowGroupRows,
            RowGroups = _groups,
        };
        for (var i = 0; i < _encoders.Length; i++)
        {
            var column = _schema.Columns[i];
            var physical = _encoders[i].Finish(_output);
            info.Columns.Add(new EssxColumnInfo
            {
                Name = column.Name,
                Type = column.Type.ToString().ToLowerInvariant(),
                Nullable = column.Nullable,
                Physical = [.. physical.Select(p => p.Name)],
            });
            info.Physical.AddRange(physical);
        }

        return info;
    }

    private void FlushGroup()
    {
        var group = new EssxRowGroupInfo { Rows = _inGroup };
        foreach (var encoder in _encoders)
        {
            encoder.Flush(_output, group.Chunks);
        }

        _groups.Add(group);
        _inGroup = 0;
    }

    private ColumnEncoder Next() => _encoders[_column++];

    void IRowSink.Text(string? value) => Next().Text(value);
    void IRowSink.Integer(long? value) => Next().Integer(value);
    void IRowSink.Boolean(bool? value) => Next().Boolean(value);
    void IRowSink.Json(string? value) => Next().Text(value);
    void IRowSink.VerseKey(VerseKey? value) => Next().VerseKey(value);
    void IRowSink.WordKey(WordKey? value) => Next().WordKey(value);
}

/// <summary>Buffers one column's values of the current row group and writes them as chunks.</summary>
internal abstract class ColumnEncoder
{
    public static ColumnEncoder For(Column column, EssxOptions options) => column.Type switch
    {
        ColumnType.Text or ColumnType.Json => new StringEncoder(column.Name, options),
        ColumnType.Integer => new IntEncoder(column.Name, options),
        ColumnType.Boolean => new BoolEncoder(column.Name, options),
        ColumnType.VerseKey => new KeyEncoder(column.Name, options, withPosition: false),
        ColumnType.WordKey => new KeyEncoder(column.Name, options, withPosition: true),
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    public virtual void Text(string? value) => throw Mismatch("text");
    public virtual void Integer(long? value) => throw Mismatch("an integer");
    public virtual void Boolean(bool? value) => throw Mismatch("a boolean");
    public virtual void VerseKey(VerseKey? value) => throw Mismatch("a verse key");
    public virtual void WordKey(WordKey? value) => throw Mismatch("a word key");

    public abstract void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks);

    /// <summary>Writes what the column keeps for the whole table (a dictionary) and describes its physical columns.</summary>
    public abstract IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output);

    private ProductFormatException Mismatch(string given) => new($"The schema writes {given} into a column of another type.");

    protected static EssxChunkInfo WriteChunk(EssxWriter.Output output, ReadOnlySpan<byte> raw, EssxOptions options)
    {
        var chunk = new EssxChunkInfo { Offset = output.Position, RawLength = raw.Length, Length = raw.Length };
        if (options.Codec != EssxLayout.CodecNone && raw.Length >= options.CompressFrom)
        {
            var compressed = EssxLayout.Compress(raw, options.Codec);
            if (compressed.Length < raw.Length)
            {
                output.Write(compressed);
                chunk.Length = compressed.Length;
                chunk.Codec = options.Codec;
                return chunk;
            }
        }

        output.Write(raw);
        return chunk;
    }
}

internal sealed class IntEncoder(string name, EssxOptions options) : ColumnEncoder
{
    private readonly List<long> _values = [];
    private readonly List<bool> _valid = [];
    private int _nulls;

    public override void Integer(long? value)
    {
        _values.Add(value ?? 0);
        _valid.Add(value.HasValue);
        if (!value.HasValue)
        {
            _nulls++;
        }
    }

    public override void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks)
    {
        var rows = _values.Count;
        long min = long.MaxValue, max = long.MinValue;
        for (var i = 0; i < rows; i++)
        {
            if (!_valid[i]) continue;
            min = Math.Min(min, _values[i]);
            max = Math.Max(max, _values[i]);
        }

        var any = _nulls < rows;
        if (!any)
        {
            min = max = 0;
        }

        var width = EssxLayout.Width((ulong)(max - min));
        var bitmap = _nulls > 0 ? EssxLayout.BitmapLength(rows) : 0;
        var raw = new byte[bitmap + rows * width];
        for (var i = 0; i < rows; i++)
        {
            if (_valid[i])
            {
                if (bitmap > 0) EssxLayout.SetBit(raw, i);
                EssxLayout.WriteUnsigned(raw.AsSpan(bitmap + i * width), (ulong)(_values[i] - min), width);
            }
        }

        var chunk = WriteChunk(output, raw, options);
        chunk.Nulls = _nulls;
        chunk.Width = width;
        chunk.Base = min;
        chunk.Min = any ? min : null;
        chunk.Max = any ? max : null;
        chunks.Add(chunk);
        _values.Clear();
        _valid.Clear();
        _nulls = 0;
    }

    public override IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output) =>
        [new EssxPhysicalInfo { Name = name, Encoding = "int" }];
}

internal sealed class BoolEncoder(string name, EssxOptions options) : ColumnEncoder
{
    private readonly List<bool> _values = [];
    private readonly List<bool> _valid = [];
    private int _nulls;

    public override void Boolean(bool? value)
    {
        _values.Add(value ?? false);
        _valid.Add(value.HasValue);
        if (!value.HasValue)
        {
            _nulls++;
        }
    }

    public override void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks)
    {
        var rows = _values.Count;
        var bits = EssxLayout.BitmapLength(rows);
        var bitmap = _nulls > 0 ? bits : 0;
        var raw = new byte[bitmap + bits];
        var trues = 0;
        for (var i = 0; i < rows; i++)
        {
            if (!_valid[i]) continue;
            if (bitmap > 0) EssxLayout.SetBit(raw, i);
            if (_values[i])
            {
                EssxLayout.SetBit(raw.AsSpan(bitmap), i);
                trues++;
            }
        }

        var chunk = WriteChunk(output, raw, options);
        chunk.Nulls = _nulls;
        chunk.Min = trues;
        chunks.Add(chunk);
        _values.Clear();
        _valid.Clear();
        _nulls = 0;
    }

    public override IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output) =>
        [new EssxPhysicalInfo { Name = name, Encoding = "bool" }];
}

/// <summary>
/// A string column: through a dictionary when the values repeat, as plain text when they do not.
/// Which is decided on the first row group: a dictionary when at most half the values are distinct
/// or they are short, since a dictionary of a million distinct long strings is the strings again
/// plus an index.
/// </summary>
internal sealed class StringEncoder(string name, EssxOptions options) : ColumnEncoder
{
    private readonly List<string?> _values = [];
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private readonly List<string> _dictionary = [];
    private bool? _dictionaryMode;

    public bool DictionaryMode => _dictionaryMode ?? true;

    public override void Text(string? value) => _values.Add(value);

    public override void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks)
    {
        _dictionaryMode ??= Decide();
        var rows = _values.Count;
        var nulls = _values.Count(value => value is null);
        EssxChunkInfo chunk;
        if (_dictionaryMode.Value)
        {
            var ids = new int[rows];
            var max = 0;
            for (var i = 0; i < rows; i++)
            {
                ids[i] = Id(_values[i]);
                max = Math.Max(max, ids[i]);
            }

            var width = EssxLayout.Width((ulong)max);
            var raw = new byte[rows * width];
            for (var i = 0; i < rows; i++)
            {
                EssxLayout.WriteUnsigned(raw.AsSpan(i * width), (ulong)ids[i], width);
            }

            chunk = WriteChunk(output, raw, options);
            chunk.Width = width;
            chunk.Max = max;
        }
        else
        {
            var bitmap = nulls > 0 ? EssxLayout.BitmapLength(rows) : 0;
            var bytes = 0;
            foreach (var value in _values)
            {
                bytes += value is null ? 0 : Encoding.UTF8.GetByteCount(value);
            }

            var raw = new byte[bitmap + 4 * (rows + 1) + bytes];
            var at = bitmap + 4 * (rows + 1);
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(bitmap), (uint)at);
            for (var i = 0; i < rows; i++)
            {
                if (_values[i] is { } value)
                {
                    if (bitmap > 0) EssxLayout.SetBit(raw, i);
                    at += Encoding.UTF8.GetBytes(value, raw.AsSpan(at));
                }

                BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(bitmap + 4 * (i + 1)), (uint)at);
            }

            chunk = WriteChunk(output, raw, options);
        }

        chunk.Nulls = nulls;
        chunks.Add(chunk);
        _values.Clear();
    }

    private bool Decide()
    {
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        long length = 0;
        var count = 0;
        foreach (var value in _values)
        {
            if (value is null) continue;
            count++;
            length += value.Length;
            distinct.Add(value);
        }

        return count == 0 || distinct.Count * 2 <= count || length <= 16L * count;
    }

    private int Id(string? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (_ids.TryGetValue(value, out var id))
        {
            return id;
        }

        _dictionary.Add(value);
        return _ids[value] = _dictionary.Count;
    }

    public override IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output)
    {
        if (!DictionaryMode)
        {
            return [new EssxPhysicalInfo { Name = name, Encoding = "text" }];
        }

        return [new EssxPhysicalInfo { Name = name, Encoding = "dict", Dictionary = WriteDictionary(output, _dictionary, options) }];
    }

    /// <summary><c>u32 count, u32 offsets[count + 1], bytes</c>; id 1 is the first string, 0 is null.</summary>
    internal static EssxSectionInfo WriteDictionary(EssxWriter.Output output, List<string> values, EssxOptions options)
    {
        var bytes = 0;
        foreach (var value in values)
        {
            bytes += Encoding.UTF8.GetByteCount(value);
        }

        var raw = new byte[4 + 4 * (values.Count + 1) + bytes];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, (uint)values.Count);
        var at = 4 + 4 * (values.Count + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), (uint)at);
        for (var i = 0; i < values.Count; i++)
        {
            at += Encoding.UTF8.GetBytes(values[i], raw.AsSpan(at));
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4 + 4 * (i + 1)), (uint)at);
        }

        var chunk = WriteChunk(output, raw, options);
        return new EssxSectionInfo { Offset = chunk.Offset, Length = chunk.Length, RawLength = chunk.RawLength, Codec = chunk.Codec, Count = values.Count };
    }
}

/// <summary>
/// A key as its parts, each an int or a small dictionary: book, chapter, chapter label, verse,
/// verse label, and the position for a word key. The parts of a text in print order are nearly
/// constant or rising by one, so a chunk of them is a few bytes a row and, compressed, almost none.
/// A null key is a null book; the other parts hold zero.
/// </summary>
internal sealed class KeyEncoder : ColumnEncoder
{
    private readonly IntEncoder _book;
    private readonly IntEncoder _chapter;
    private readonly StringEncoder _chapterLabel;
    private readonly IntEncoder _verse;
    private readonly StringEncoder _verseLabel;
    private readonly IntEncoder? _position;

    public KeyEncoder(string name, EssxOptions options, bool withPosition)
    {
        _book = new IntEncoder($"{name}.book", options);
        _chapter = new IntEncoder($"{name}.chapter", options);
        _chapterLabel = new StringEncoder($"{name}.chapter_label", options);
        _verse = new IntEncoder($"{name}.verse", options);
        _verseLabel = new StringEncoder($"{name}.verse_label", options);
        _position = withPosition ? new IntEncoder($"{name}.position", options) : null;
    }

    private IEnumerable<ColumnEncoder> Parts =>
        _position is null ? [_book, _chapter, _chapterLabel, _verse, _verseLabel] : [_book, _chapter, _chapterLabel, _verse, _verseLabel, _position];

    public override void VerseKey(VerseKey? value)
    {
        if (_position is not null)
        {
            throw new ProductFormatException("The schema writes a verse key into a word key column.");
        }

        Add(value, 0);
    }

    public override void WordKey(WordKey? value)
    {
        if (_position is null)
        {
            throw new ProductFormatException("The schema writes a word key into a verse key column.");
        }

        Add(value?.Verse, value?.Position ?? 0);
    }

    private void Add(VerseKey? verse, int position)
    {
        _book.Integer(verse?.Book);
        _chapter.Integer(verse?.Chapter ?? 0);
        _chapterLabel.Text(verse?.ChapterLabel ?? string.Empty);
        _verse.Integer(verse?.Verse ?? 0);
        _verseLabel.Text(verse?.VerseLabel ?? string.Empty);
        _position?.Integer(position);
    }

    public override void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks)
    {
        foreach (var part in Parts)
        {
            part.Flush(output, chunks);
        }
    }

    public override IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output) =>
        [.. Parts.SelectMany(part => part.Finish(output))];
}
