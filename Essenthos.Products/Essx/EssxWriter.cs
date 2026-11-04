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
        var json = JsonSerializer.SerializeToUtf8Bytes(footer, EssxLayout.Json.EssxFooter);
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
public sealed class EssxTableWriter<T> : IProductWriter<T>, IRowSink, IKeyPartsSink, IDictionarySink, EssxWriter.ITableWriter
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
        EndRow();
    }

    /// <summary>The column encoders in schema order, for a caller in this assembly that hands values over without a row object.</summary>
    internal IReadOnlyList<ColumnEncoder> Encoders => _encoders;

    /// <summary>Ends a row whose every column was handed to <see cref="Encoders"/>.</summary>
    internal void EndRow()
    {
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
    void IDictionarySink.Text(string?[] dictionary, int id) => Next().Text(dictionary, id);
    void IKeyPartsSink.KeyParts(long? book, long chapter, string chapterLabel, long verse, string verseLabel, long position) =>
        Next().KeyParts(book, chapter, chapterLabel, verse, verseLabel, position);
}

/// <summary>
/// Takes a key as its parts. A typed copy out of another ESSX file hands the parts it decoded straight
/// over, without building a key or writing one as text.
/// </summary>
internal interface IKeyPartsSink
{
    void KeyParts(long? book, long chapter, string chapterLabel, long verse, string verseLabel, long position);
}

/// <summary>
/// Takes a string as an id into a dictionary another ESSX file decoded, so a copy maps each of the
/// source's ids once instead of hashing every row's string.
/// </summary>
internal interface IDictionarySink
{
    void Text(string?[] dictionary, int id);
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

    /// <summary>A string as a span of a line read: a dict column that already holds it takes its own instance, so a repeated value costs no allocation.</summary>
    public virtual void Text(ReadOnlySpan<char> value) => Text(value.ToString());

    /// <summary>A string as an id into a source file's dictionary (0 null).</summary>
    public virtual void Text(string?[] dictionary, int id) => Text(dictionary[id]);
    public virtual void Integer(long? value) => throw Mismatch("an integer");
    public virtual void Boolean(bool? value) => throw Mismatch("a boolean");
    public virtual void VerseKey(VerseKey? value) => throw Mismatch("a verse key");
    public virtual void WordKey(WordKey? value) => throw Mismatch("a word key");
    public virtual void KeyParts(long? book, long chapter, string chapterLabel, long verse, string verseLabel, long position) => throw Mismatch("a key");

    public abstract void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks);

    /// <summary>Writes what the column keeps for the whole table (a dictionary) and describes its physical columns.</summary>
    public abstract IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output);

    private ProductFormatException Mismatch(string given) => new($"The schema writes {given} into a column of another type.");

    protected static int InitialCapacity(EssxOptions options) => Math.Clamp(options.RowGroupRows, 16, 1 << 16);

    /// <summary>The encoder's own scratch buffer, grown when a group needs more and reused for every group after.</summary>
    protected static Span<byte> Buffer(ref byte[] buffer, int length, int clear = 0)
    {
        if (buffer.Length < length)
        {
            buffer = new byte[Math.Max(length, buffer.Length * 2)];
        }

        buffer.AsSpan(0, clear).Clear();
        return buffer.AsSpan(0, length);
    }

    protected static void Grow<T>(ref T[] array, int count)
    {
        if (count == array.Length)
        {
            Array.Resize(ref array, array.Length * 2);
        }
    }

    protected static void WriteValues(Span<byte> target, ReadOnlySpan<long> values, long first, int width)
    {
        switch (width)
        {
            case 0:
                break;
            case 1:
                for (var i = 0; i < values.Length; i++) target[i] = (byte)(values[i] - first);
                break;
            case 2:
                for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(target[(2 * i)..], (ushort)(values[i] - first));
                break;
            case 4:
                for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(target[(4 * i)..], (uint)(values[i] - first));
                break;
            default:
                for (var i = 0; i < values.Length; i++) EssxLayout.WriteUnsigned(target[(i * width)..], (ulong)(values[i] - first), width);
                break;
        }
    }

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
    private long[] _values = new long[InitialCapacity(options)];
    private bool[]? _valid;
    private byte[] _raw = [];
    private int _count;
    private int _nulls;

    public override void Integer(long? value) => Add(value.GetValueOrDefault(), value.HasValue);

    public void Add(long value, bool present)
    {
        Grow(ref _values, _count);
        if (!present)
        {
            _nulls++;
            if (_valid is null)
            {
                _valid = new bool[_values.Length];
                Array.Fill(_valid, true, 0, _count);
            }
        }

        if (_valid is not null)
        {
            if (_valid.Length < _values.Length) Array.Resize(ref _valid, _values.Length);
            _valid[_count] = present;
        }

        _values[_count++] = present ? value : 0;
    }

    public override void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks)
    {
        var rows = _count;
        var values = _values.AsSpan(0, rows);
        var nulls = _nulls;
        long min = long.MaxValue, max = long.MinValue;
        for (var i = 0; i < rows; i++)
        {
            if (nulls > 0 && !_valid![i]) continue;
            var value = values[i];
            if (value < min) min = value;
            if (value > max) max = value;
        }

        var any = nulls < rows;
        if (!any)
        {
            min = max = 0;
        }

        if (nulls > 0)
        {
            // A null is stored as min so it costs no width of its own.
            for (var i = 0; i < rows; i++) if (!_valid![i]) values[i] = min;
        }

        var width = EssxLayout.Width((ulong)(max - min));
        var bitmap = nulls > 0 ? EssxLayout.BitmapLength(rows) : 0;
        var raw = Buffer(ref _raw, bitmap + rows * width, clear: bitmap);
        if (bitmap > 0)
        {
            for (var i = 0; i < rows; i++) if (_valid![i]) EssxLayout.SetBit(raw, i);
        }

        WriteValues(raw[bitmap..], values, min, width);
        var chunk = WriteChunk(output, raw, options);
        chunk.Nulls = nulls;
        chunk.Width = width;
        chunk.Base = min;
        chunk.Min = any ? min : null;
        chunk.Max = any ? max : null;
        chunks.Add(chunk);
        _count = 0;
        _nulls = 0;
    }

    public override IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output) =>
        [new EssxPhysicalInfo { Name = name, Encoding = "int" }];
}

internal sealed class BoolEncoder(string name, EssxOptions options) : ColumnEncoder
{
    private sbyte[] _values = new sbyte[InitialCapacity(options)];
    private byte[] _raw = [];
    private int _count;
    private int _nulls;

    public override void Boolean(bool? value)
    {
        Grow(ref _values, _count);
        _values[_count++] = value switch { null => -1, true => 1, false => 0 };
        if (!value.HasValue)
        {
            _nulls++;
        }
    }

    public override void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks)
    {
        var rows = _count;
        var bits = EssxLayout.BitmapLength(rows);
        var bitmap = _nulls > 0 ? bits : 0;
        var raw = Buffer(ref _raw, bitmap + bits, clear: bitmap + bits);
        var trues = 0;
        for (var i = 0; i < rows; i++)
        {
            var value = _values[i];
            if (value < 0) continue;
            if (bitmap > 0) EssxLayout.SetBit(raw, i);
            if (value == 1)
            {
                EssxLayout.SetBit(raw[bitmap..], i);
                trues++;
            }
        }

        var chunk = WriteChunk(output, raw, options);
        chunk.Nulls = _nulls;
        chunk.Min = trues;
        chunks.Add(chunk);
        _count = 0;
        _nulls = 0;
    }

    public override IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output) =>
        [new EssxPhysicalInfo { Name = name, Encoding = "bool" }];
}

/// <summary>
/// A string column: through a dictionary when the values repeat, as plain text when they do not.
/// Which is decided on the first row group: a dictionary when at most half the values are distinct
/// or they are short, since a dictionary of a million distinct long strings is the strings again
/// plus an index. A dictionary stops growing at <see cref="EssxOptions.DictionaryLimit"/> strings or
/// <see cref="EssxOptions.DictionaryCharacterLimit"/> characters: the group that would pass either
/// and every group after it are written as text chunks, so a column whose first group repeats and
/// whose later groups do not never holds the whole table's strings.
/// </summary>
internal sealed class StringEncoder(string name, EssxOptions options) : ColumnEncoder
{
    private string?[] _values = new string?[InitialCapacity(options)];
    private int[] _rowIds = new int[InitialCapacity(options)];
    private byte[] _raw = [];
    private int _count;
    private int _nulls;
    private Dictionary<string, int>? _ids = new(StringComparer.Ordinal);
    private readonly List<string> _dictionary = [];
    private long _dictionaryCharacters;
    private int _groupStart;
    private long _groupCharactersStart;
    private bool? _dictionaryMode;
    private bool _capped;
    private bool _anyDictionaryChunk;

    // Once the first group has decided for a dictionary, ids are given as values arrive, so a value is
    // hashed once; a group that passes the cap is marked and turned to text when it is flushed.
    private bool _eager;
    private bool _overflow;
    private int _maxId;

    // A source file's dictionary and, for each of its ids, the id the string has here (0 not yet seen).
    private string?[]? _source;
    private int[] _sourceIds = [];

    public override void Text(string? value)
    {
        if (_count == _values.Length)
        {
            Array.Resize(ref _values, _values.Length * 2);
            Array.Resize(ref _rowIds, _values.Length);
        }

        _values[_count] = value;
        if (value is null)
        {
            _nulls++;
            _rowIds[_count] = 0;
        }
        else if (_eager && !_overflow)
        {
            _rowIds[_count] = IdOf(value);
        }

        _count++;
    }

    public override void Text(ReadOnlySpan<char> value)
    {
        if (_eager && !_overflow && _ids!.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(value, out var id))
        {
            if (_count == _values.Length)
            {
                Array.Resize(ref _values, _values.Length * 2);
                Array.Resize(ref _rowIds, _values.Length);
            }

            _values[_count] = _dictionary[id - 1];
            _rowIds[_count++] = id;
            if (id > _maxId) _maxId = id;
            return;
        }

        Text(value.ToString());
    }

    public override void Text(string?[] dictionary, int id)
    {
        if (!_eager || _overflow || id == 0)
        {
            Text(dictionary[id]);
            return;
        }

        if (!ReferenceEquals(dictionary, _source))
        {
            _source = dictionary;
            _sourceIds = new int[dictionary.Length];
        }

        var mapped = _sourceIds[id];
        if (mapped == 0)
        {
            Text(dictionary[id]);
            if (!_overflow)
            {
                _sourceIds[id] = _rowIds[_count - 1];
            }

            return;
        }

        if (_count == _values.Length)
        {
            Array.Resize(ref _values, _values.Length * 2);
            Array.Resize(ref _rowIds, _values.Length);
        }

        _values[_count] = _dictionary[mapped - 1];
        _rowIds[_count++] = mapped;
        if (mapped > _maxId) _maxId = mapped;
    }

    private int IdOf(string value)
    {
        if (!_ids!.TryGetValue(value, out var id))
        {
            _dictionary.Add(value);
            _dictionaryCharacters += value.Length;
            id = _ids[value] = _dictionary.Count;
            if (_dictionary.Count > options.DictionaryLimit || _dictionaryCharacters > options.DictionaryCharacterLimit)
            {
                _overflow = true;
                return 0;
            }
        }

        if (id > _maxId) _maxId = id;
        return id;
    }

    public override void Flush(EssxWriter.Output output, List<EssxChunkInfo> chunks)
    {
        EssxChunkInfo chunk;
        if (_eager)
        {
            chunk = _overflow ? Cap(output) : WriteIds(output, _maxId);
        }
        else
        {
            _dictionaryMode ??= Decide();
            chunk = _dictionaryMode.Value && !_capped && TryIds(out var max) ? WriteIds(output, max) : WriteText(output);
        }

        chunk.Nulls = _nulls;
        chunks.Add(chunk);
        Array.Clear(_values, 0, _count);
        _count = 0;
        _nulls = 0;
        _maxId = 0;
        _overflow = false;
        _eager = _dictionaryMode == true && !_capped;
        _groupStart = _dictionary.Count;
        _groupCharactersStart = _dictionaryCharacters;
    }

    /// <summary>Takes the group's new strings out of the dictionary, stops it growing, and writes the group as text.</summary>
    private EssxChunkInfo Cap(EssxWriter.Output output)
    {
        for (var k = _groupStart; k < _dictionary.Count; k++) _ids!.Remove(_dictionary[k]);
        _dictionary.RemoveRange(_groupStart, _dictionary.Count - _groupStart);
        _dictionaryCharacters = _groupCharactersStart;
        _capped = true;
        _ids = null;
        _source = null;
        return WriteText(output);
    }

    /// <summary>Gives every value of the first group its id; false, with the dictionary emptied again, when the group alone passes the cap.</summary>
    private bool TryIds(out int max)
    {
        _maxId = 0;
        for (var i = 0; i < _count && !_overflow; i++)
        {
            _rowIds[i] = _values[i] is { } value ? IdOf(value) : 0;
        }

        max = _maxId;
        if (!_overflow)
        {
            return true;
        }

        for (var k = _groupStart; k < _dictionary.Count; k++) _ids!.Remove(_dictionary[k]);
        _dictionary.RemoveRange(_groupStart, _dictionary.Count - _groupStart);
        _dictionaryCharacters = _groupCharactersStart;
        _capped = true;
        _ids = null;
        return false;
    }

    private EssxChunkInfo WriteIds(EssxWriter.Output output, int max)
    {
        var rows = _count;
        var width = EssxLayout.Width((ulong)max);
        var raw = Buffer(ref _raw, rows * width);
        var ids = _rowIds.AsSpan(0, rows);
        switch (width)
        {
            case 0:
                break;
            case 1:
                for (var i = 0; i < rows; i++) raw[i] = (byte)ids[i];
                break;
            case 2:
                for (var i = 0; i < rows; i++) BinaryPrimitives.WriteUInt16LittleEndian(raw[(2 * i)..], (ushort)ids[i]);
                break;
            default:
                for (var i = 0; i < rows; i++) EssxLayout.WriteUnsigned(raw[(i * width)..], (ulong)ids[i], width);
                break;
        }

        _anyDictionaryChunk = true;
        var chunk = WriteChunk(output, raw, options);
        chunk.Width = width;
        chunk.Max = max;
        return chunk;
    }

    private EssxChunkInfo WriteText(EssxWriter.Output output)
    {
        var rows = _count;
        var bitmap = _nulls > 0 ? EssxLayout.BitmapLength(rows) : 0;
        var header = bitmap + 4 * (rows + 1);
        long characters = 0;
        for (var i = 0; i < rows; i++)
        {
            characters += _values[i]?.Length ?? 0;
        }

        // One encoding pass into room for the worst case; the chunk is cut to what was written.
        var raw = Buffer(ref _raw, checked(header + (int)(characters * 3)), clear: bitmap);
        var at = header;
        BinaryPrimitives.WriteUInt32LittleEndian(raw[bitmap..], (uint)at);
        for (var i = 0; i < rows; i++)
        {
            if (_values[i] is { } value)
            {
                if (bitmap > 0) EssxLayout.SetBit(raw, i);
                at += Encoding.UTF8.GetBytes(value, raw[at..]);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(raw[(bitmap + 4 * (i + 1))..], (uint)at);
        }

        var chunk = WriteChunk(output, raw[..at], options);
        if (_anyDictionaryChunk)
        {
            chunk.Encoding = "text";
        }

        return chunk;
    }

    private bool Decide()
    {
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        long length = 0;
        var count = 0;
        for (var i = 0; i < _count; i++)
        {
            if (_values[i] is not { } value) continue;
            count++;
            length += value.Length;
            distinct.Add(value);
        }

        return count == 0 || distinct.Count * 2 <= count || length <= 16L * count;
    }

    public override IReadOnlyList<EssxPhysicalInfo> Finish(EssxWriter.Output output)
    {
        if (!(_dictionaryMode ?? true) || !_anyDictionaryChunk && _capped)
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

    public override void KeyParts(long? book, long chapter, string chapterLabel, long verse, string verseLabel, long position)
    {
        _book.Add(book.GetValueOrDefault(), book.HasValue);
        _chapter.Add(chapter, true);
        _chapterLabel.Text(chapterLabel);
        _verse.Add(verse, true);
        _verseLabel.Text(verseLabel);
        _position?.Add(position, true);
    }

    private void Add(VerseKey? verse, int position)
    {
        if (verse is { } key)
        {
            KeyParts(key.Book, key.Chapter, key.ChapterLabel, key.Verse, key.VerseLabel, position);
        }
        else
        {
            KeyParts(null, 0, string.Empty, 0, string.Empty, position);
        }
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
