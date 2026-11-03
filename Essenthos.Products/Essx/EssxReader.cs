using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Essenthos.Core.Products.Essx;

/// <summary>
/// Reads an ESSX file: the trailer, the footer, then whatever is asked for, by seeking. Nothing is
/// decoded until a table's rows or a row group is read, so opening a file and reading one verse's
/// group costs the footer and that group's chunks. <see cref="Verify"/> hashes the whole file once.
/// </summary>
public sealed class EssxReader : IDisposable
{
    private readonly SafeFileHandle _handle;

    private EssxReader(string path, SafeFileHandle handle, EssxFooter footer, long length)
    {
        Path = path;
        _handle = handle;
        Footer = footer;
        Length = length;
        Tables = [.. footer.Tables.Select(table => new EssxTable(this, table))];
    }

    public static EssxReader Open(string path)
    {
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            var length = RandomAccess.GetLength(handle);
            if (length < EssxLayout.PreambleLength + EssxLayout.TrailerLength)
            {
                throw new ProductFormatException($"{path}: too short to be an ESSX file.");
            }

            Span<byte> trailer = stackalloc byte[EssxLayout.TrailerLength];
            RandomAccess.Read(handle, trailer, length - EssxLayout.TrailerLength);
            Span<byte> preamble = stackalloc byte[EssxLayout.PreambleLength];
            RandomAccess.Read(handle, preamble, 0);
            if (!preamble[..4].SequenceEqual(EssxLayout.Magic) || !trailer.Slice(16, 4).SequenceEqual(EssxLayout.Magic))
            {
                throw new ProductFormatException($"{path}: not an ESSX file (no magic at the start and the end).");
            }

            var version = BinaryPrimitives.ReadUInt16LittleEndian(preamble[4..]);
            if (version > EssxLayout.Version)
            {
                throw new ProductFormatException($"{path}: ESSX version {version} is newer than this reader ({EssxLayout.Version}).");
            }

            var footerOffset = BinaryPrimitives.ReadInt64LittleEndian(trailer);
            var footerLength = BinaryPrimitives.ReadInt64LittleEndian(trailer[8..]);
            if (footerOffset < EssxLayout.PreambleLength || footerLength < 2 || footerOffset + footerLength != length - EssxLayout.TrailerLength)
            {
                throw new ProductFormatException($"{path}: the trailer does not point at a footer inside the file.");
            }

            var json = new byte[footerLength];
            RandomAccess.Read(handle, json, footerOffset);
            var footer = JsonSerializer.Deserialize<EssxFooter>(json, EssxLayout.Json)
                         ?? throw new ProductFormatException($"{path}: empty footer.");
            return new EssxReader(path, handle, footer, length);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public string Path { get; }

    public long Length { get; }

    public EssxFooter Footer { get; }

    public ProductHeader Header => Footer.Product.ToHeader();

    public IReadOnlyList<EssxTable> Tables { get; }

    public EssxTable Table(string name) =>
        Tables.FirstOrDefault(table => table.Name == name)
        ?? throw new ProductFormatException($"{Path}: no table '{name}'; the tables are {string.Join(", ", Tables.Select(table => table.Name))}.");

    /// <summary>The SHA-256 the trailer carries.</summary>
    public byte[] StoredHash()
    {
        var hash = new byte[EssxLayout.HashLength];
        RandomAccess.Read(_handle, hash, Length - EssxLayout.HashLength);
        return hash;
    }

    /// <summary>Hashes every byte before the trailer's hash and compares; false when the file is not what was written.</summary>
    public bool Verify()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        var remaining = Length - EssxLayout.HashLength;
        long at = 0;
        while (remaining > 0)
        {
            var read = RandomAccess.Read(_handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, remaining)), at);
            if (read == 0)
            {
                return false;
            }

            hash.AppendData(buffer, 0, read);
            at += read;
            remaining -= read;
        }

        return hash.GetHashAndReset().AsSpan().SequenceEqual(StoredHash());
    }

    internal byte[] Read(long offset, long length, string codec, long rawLength)
    {
        if (offset < EssxLayout.PreambleLength || offset + length > Length - EssxLayout.TrailerLength)
        {
            throw new ProductFormatException($"{Path}: a chunk at {offset}+{length} lies outside the data.");
        }

        var stored = new byte[length];
        var read = RandomAccess.Read(_handle, stored, offset);
        if (read != length)
        {
            throw new ProductFormatException($"{Path}: short read at {offset}.");
        }

        return codec == EssxLayout.CodecNone ? stored : EssxLayout.Decompress(stored, codec, rawLength);
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>One table: its columns, its row groups, and the rows decoded a group at a time.</summary>
public sealed class EssxTable
{
    private readonly EssxReader _reader;
    private readonly Dictionary<string, int> _physicalIndex;
    private readonly string?[]?[] _dictionaries;

    internal EssxTable(EssxReader reader, EssxTableInfo info)
    {
        _reader = reader;
        Info = info;
        Columns = [.. info.Columns.Select(column => new Column(column.Name, Enum.Parse<ColumnType>(column.Type, ignoreCase: true), column.Nullable))];
        _physicalIndex = info.Physical.Select((physical, index) => (physical.Name, index)).ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);
        _dictionaries = new string?[]?[info.Physical.Count];
    }

    public EssxTableInfo Info { get; }

    public string Name => Info.Name;

    public long RowCount => Info.Rows;

    public IReadOnlyList<Column> Columns { get; }

    public int RowGroups => Info.RowGroups.Count;

    /// <summary>The strings of a dictionary column, index 0 null; one instance per distinct value, shared by every row.</summary>
    public string?[] Dictionary(string physicalName)
    {
        var index = _physicalIndex[physicalName];
        return _dictionaries[index] ??= ReadDictionary(Info.Physical[index]);
    }

    private string?[] ReadDictionary(EssxPhysicalInfo physical)
    {
        var section = physical.Dictionary ?? throw new ProductFormatException($"{_reader.Path}: column '{physical.Name}' has no dictionary.");
        var raw = _reader.Read(section.Offset, section.Length, section.Codec, section.RawLength);
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(raw);
        if (count != section.Count)
        {
            throw new ProductFormatException($"{_reader.Path}: dictionary of '{physical.Name}' says {count} strings, the footer {section.Count}.");
        }

        var values = new string?[count + 1];
        var offsets = raw.AsSpan(4);
        for (var i = 0; i < count; i++)
        {
            var start = (int)BinaryPrimitives.ReadUInt32LittleEndian(offsets[(4 * i)..]);
            var end = (int)BinaryPrimitives.ReadUInt32LittleEndian(offsets[(4 * (i + 1))..]);
            values[i + 1] = Encoding.UTF8.GetString(raw, start, end - start);
        }

        return values;
    }

    /// <summary>Decodes one row group whole: every column of it, as arrays.</summary>
    public EssxRowGroup ReadGroup(int index)
    {
        var group = Info.RowGroups[index];
        var columns = new EssxColumnData[Columns.Count];
        for (var i = 0; i < Columns.Count; i++)
        {
            columns[i] = EssxColumnData.Decode(this, Info.Columns[i], group);
        }

        return new EssxRowGroup(this, index, group.Rows, columns);
    }

    /// <summary>The groups whose chunk statistics admit the predicate, for a read that needs some verses rather than the file.</summary>
    public IEnumerable<int> GroupsWhere(string physicalName, Func<long?, long?, bool> minMaxAdmits)
    {
        var index = _physicalIndex[physicalName];
        for (var g = 0; g < Info.RowGroups.Count; g++)
        {
            var chunk = Info.RowGroups[g].Chunks[index];
            if (minMaxAdmits(chunk.Min, chunk.Max))
            {
                yield return g;
            }
        }
    }

    internal byte[] ReadChunk(EssxRowGroupInfo group, string physicalName, out EssxChunkInfo chunk)
    {
        chunk = group.Chunks[_physicalIndex[physicalName]];
        return _reader.Read(chunk.Offset, chunk.Length, chunk.Codec, chunk.RawLength);
    }

    /// <summary>Every row, group after group. The row object is reused; take what is needed before moving on.</summary>
    public IEnumerable<EssxRow> Rows()
    {
        for (var g = 0; g < Info.RowGroups.Count; g++)
        {
            var group = ReadGroup(g);
            var row = new EssxRow(group);
            for (var i = 0; i < group.Rows; i++)
            {
                row.Index = i;
                yield return row;
            }
        }
    }

    public IEnumerable<T> Rows<T>(ProductSchema<T> schema)
    {
        schema.Check([.. Columns.Select(column => column.Name)], _reader.Path);
        foreach (var row in Rows())
        {
            yield return schema.Read(row);
        }
    }
}

/// <summary>One decoded row group.</summary>
public sealed class EssxRowGroup(EssxTable table, int index, int rows, EssxColumnData[] columns)
{
    public EssxTable Table { get; } = table;

    public int Index { get; } = index;

    public int Rows { get; } = rows;

    public IReadOnlyList<EssxColumnData> Columns { get; } = columns;

    public EssxColumnData Column(string name) => Columns[Table.Columns.ToList().FindIndex(column => column.Name == name)];
}

/// <summary>A logical column of one row group, decoded: typed arrays a loader reads straight.</summary>
public abstract class EssxColumnData
{
    public abstract bool IsNull(int row);

    public abstract string? Text(int row);

    public virtual long? Integer(int row) => Text(row) is { } text ? long.Parse(text, CultureInfo.InvariantCulture) : null;

    public virtual bool? Boolean(int row) => Text(row) switch { null => null, "t" => true, _ => false };

    public virtual VerseKey? VerseKey(int row) => Text(row) is { } text ? Products.VerseKey.Parse(text) : null;

    public virtual WordKey? WordKey(int row, string text) => Text(row) is { } written ? Products.WordKey.Parse(written, text) : null;

    internal static EssxColumnData Decode(EssxTable table, EssxColumnInfo column, EssxRowGroupInfo group) => column.Type switch
    {
        "integer" => IntData.Read(table, group, column.Physical[0]),
        "boolean" => BoolData.Read(table, group, column.Physical[0]),
        "text" or "json" => StringData(table, group, column.Physical[0]),
        "versekey" => new KeyData(table, group, column.Physical, withPosition: false),
        "wordkey" => new KeyData(table, group, column.Physical, withPosition: true),
        _ => throw new ProductFormatException($"Unknown column type '{column.Type}'."),
    };

    private static EssxColumnData StringData(EssxTable table, EssxRowGroupInfo group, string physical)
    {
        var encoding = table.Info.Physical.First(p => p.Name == physical).Encoding;
        return encoding == "dict" ? DictData.Read(table, group, physical) : TextData.Read(table, group, physical);
    }

    internal static bool[]? Validity(ReadOnlySpan<byte> raw, int rows, int nulls, out int bitmapLength)
    {
        bitmapLength = nulls > 0 ? EssxLayout.BitmapLength(rows) : 0;
        if (nulls == 0)
        {
            return null;
        }

        var valid = new bool[rows];
        for (var i = 0; i < rows; i++)
        {
            valid[i] = EssxLayout.Bit(raw, i);
        }

        return valid;
    }
}

public sealed class IntData(long[] values, bool[]? valid) : EssxColumnData
{
    public long[] Values { get; } = values;

    /// <summary>Null when every value is present.</summary>
    public bool[]? Valid { get; } = valid;

    public override bool IsNull(int row) => Valid is not null && !Valid[row];

    public override string? Text(int row) => IsNull(row) ? null : Values[row].ToString(CultureInfo.InvariantCulture);

    public override long? Integer(int row) => IsNull(row) ? null : Values[row];

    internal static IntData Read(EssxTable table, EssxRowGroupInfo group, string physical)
    {
        var raw = table.ReadChunk(group, physical, out var chunk);
        var valid = Validity(raw, group.Rows, chunk.Nulls, out var bitmap);
        var values = new long[group.Rows];
        var width = chunk.Width;
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = chunk.Base + (long)EssxLayout.ReadUnsigned(raw.AsSpan(bitmap + i * width), width);
        }

        return new IntData(values, valid);
    }
}

public sealed class BoolData(bool[] values, bool[]? valid) : EssxColumnData
{
    public bool[] Values { get; } = values;

    public bool[]? Valid { get; } = valid;

    public override bool IsNull(int row) => Valid is not null && !Valid[row];

    public override string? Text(int row) => IsNull(row) ? null : Values[row] ? "t" : "f";

    public override bool? Boolean(int row) => IsNull(row) ? null : Values[row];

    internal static BoolData Read(EssxTable table, EssxRowGroupInfo group, string physical)
    {
        var raw = table.ReadChunk(group, physical, out var chunk);
        var valid = Validity(raw, group.Rows, chunk.Nulls, out var bitmap);
        var values = new bool[group.Rows];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = EssxLayout.Bit(raw.AsSpan(bitmap), i);
        }

        return new BoolData(values, valid);
    }
}

/// <summary>A dictionary column: ids into the table's dictionary, 0 null; the strings are shared instances.</summary>
public sealed class DictData(int[] ids, string?[] dictionary) : EssxColumnData
{
    public int[] Ids { get; } = ids;

    public string?[] Dictionary { get; } = dictionary;

    public override bool IsNull(int row) => Ids[row] == 0;

    public override string? Text(int row) => Dictionary[Ids[row]];

    internal static DictData Read(EssxTable table, EssxRowGroupInfo group, string physical)
    {
        var raw = table.ReadChunk(group, physical, out var chunk);
        var ids = new int[group.Rows];
        var width = chunk.Width;
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = (int)EssxLayout.ReadUnsigned(raw.AsSpan(i * width), width);
        }

        var dictionary = table.Dictionary(physical);
        if (chunk.Max is { } max && max >= dictionary.Length)
        {
            throw new ProductFormatException($"Column '{physical}' refers to id {max} in a dictionary of {dictionary.Length - 1}.");
        }

        return new DictData(ids, dictionary);
    }
}

public sealed class TextData(string?[] values) : EssxColumnData
{
    public string?[] Values { get; } = values;

    public override bool IsNull(int row) => Values[row] is null;

    public override string? Text(int row) => Values[row];

    internal static TextData Read(EssxTable table, EssxRowGroupInfo group, string physical)
    {
        var raw = table.ReadChunk(group, physical, out var chunk);
        var valid = Validity(raw, group.Rows, chunk.Nulls, out var bitmap);
        var values = new string?[group.Rows];
        var offsets = raw.AsSpan(bitmap);
        for (var i = 0; i < values.Length; i++)
        {
            if (valid is not null && !valid[i]) continue;
            var start = (int)BinaryPrimitives.ReadUInt32LittleEndian(offsets[(4 * i)..]);
            var end = (int)BinaryPrimitives.ReadUInt32LittleEndian(offsets[(4 * (i + 1))..]);
            values[i] = Encoding.UTF8.GetString(raw, start, end - start);
        }

        return new TextData(values);
    }
}

/// <summary>A key column from its parts.</summary>
public sealed class KeyData : EssxColumnData
{
    private readonly bool _withPosition;

    internal KeyData(EssxTable table, EssxRowGroupInfo group, List<string> physical, bool withPosition)
    {
        _withPosition = withPosition;
        Book = IntData.Read(table, group, physical[0]);
        Chapter = IntData.Read(table, group, physical[1]);
        ChapterLabel = DictData.Read(table, group, physical[2]);
        Verse = IntData.Read(table, group, physical[3]);
        VerseLabel = DictData.Read(table, group, physical[4]);
        Position = withPosition ? IntData.Read(table, group, physical[5]) : null;
    }

    public IntData Book { get; }
    public IntData Chapter { get; }
    public DictData ChapterLabel { get; }
    public IntData Verse { get; }
    public DictData VerseLabel { get; }
    public IntData? Position { get; }

    public override bool IsNull(int row) => Book.IsNull(row);

    public override VerseKey? VerseKey(int row) =>
        IsNull(row) ? null : new VerseKey((int)Book.Values[row], (int)Chapter.Values[row], ChapterLabel.Text(row) ?? "", (int)Verse.Values[row], VerseLabel.Text(row) ?? "");

    public override WordKey? WordKey(int row, string text) =>
        VerseKey(row) is { } verse ? new WordKey(text, verse, (int)(Position ?? throw new ProductFormatException("A verse key column has no position.")).Values[row]) : null;

    public override string? Text(int row)
    {
        if (VerseKey(row) is not { } verse)
        {
            return null;
        }

        return _withPosition ? $"{verse}.{Position!.Values[row]}" : verse.ToString();
    }
}

/// <summary>A row of a decoded group, as the schema reads it.</summary>
public sealed class EssxRow(EssxRowGroup group) : ProductRow(group.Table.Columns)
{
    public EssxRowGroup Group { get; } = group;

    public int Index { get; set; }

    public override bool IsNull(int column) => Group.Columns[column].IsNull(Index);

    public override string? TextOrNull(int column) => Group.Columns[column].Text(Index);

    public override long? IntegerOrNull(int column) => Group.Columns[column].Integer(Index);

    public override bool? BooleanOrNull(int column) => Group.Columns[column].Boolean(Index);

    public override VerseKey? VerseKeyOrNull(int column) => Group.Columns[column].VerseKey(Index);

    public override WordKey? WordKeyOrNull(int column, string text) => Group.Columns[column].WordKey(Index, text);
}
