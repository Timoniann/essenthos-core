using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Publishing;

/// <summary>
/// A pg_dump archive in the custom format (<c>-Fc</c>), read directly: its table of contents, and any
/// table's rows as COPY text, decompressed from the offset the table of contents records. Reading the
/// file here rather than through pg_restore in a container matters for size: Docker Desktop passes a
/// container's output to Windows at a few megabytes a second, and an archive holds 20 GB of rows.
///
/// The layout is pg_dump's (pg_backup_archiver.c, pg_backup_custom.c) for archive version 1.14 to 1.16:
/// integers as a sign byte and <c>intSize</c> little-endian bytes, strings as a length and bytes, data as a
/// block type, the entry's dump id, and length-prefixed chunks of one zlib stream ending in a zero length.
/// Any other version, or compression other than none or gzip, is refused rather than guessed at.
/// </summary>
internal sealed partial class DumpArchive
{
    private const byte BlockData = 1;
    private const byte OffsetSet = 2;

    private readonly string _path;
    private readonly int _intSize;
    private readonly int _offsetSize;
    private readonly bool _compressed;

    private DumpArchive(string path, int intSize, int offsetSize, bool compressed, IReadOnlyList<Entry> entries)
    {
        _path = path;
        _intSize = intSize;
        _offsetSize = offsetSize;
        _compressed = compressed;
        Entries = entries;
    }

    /// <summary>An entry of the table of contents: what pg_restore -l lists, with its SQL and where its data is.</summary>
    /// <param name="Section">pg_dump's section: 1 none (comments, ACLs), 2 pre-data, 3 data, 4 post-data.</param>
    internal sealed record Entry(int DumpId, string Kind, string Schema, string Name, string Definition, string Copy, long? DataAt, int Section = 0);

    public const int PostData = 4;

    public IReadOnlyList<Entry> Entries { get; }

    public static DumpArchive Open(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var (reader, compressed, _, toc) = ReadToc(file, path);
        return new DumpArchive(path, reader.IntSize, reader.OffsetSize, compressed, [.. toc.Select(placed => placed.Entry)]);
    }

    /// <summary>
    /// Writes the archive at <paramref name="path"/> again as <paramref name="destination"/> without the entries
    /// <paramref name="omit"/> picks, which may hold no rows: the table of contents loses them, every data offset moves
    /// back by as many bytes, no entry kept names one left out among its dependencies, and the data blocks are copied as
    /// they are. pg_restore reads the result as it reads what pg_dump writes. The names of the entries left out.
    /// </summary>
    public static IReadOnlyList<string> Omit(string path, string destination, Func<Entry, bool> omit)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var (reader, _, countAt, toc) = ReadToc(input, path);
        var tocEnd = input.Position;
        var left = toc.Where(placed => omit(placed.Entry)).ToList();
        if (left.FirstOrDefault(placed => placed.Entry.DataAt is not null || placed.Entry.Kind is "TABLE DATA") is { } rows)
        {
            throw new InvalidOperationException($"{rows.Entry.Kind} {rows.Entry.Name} holds rows; an archive is written again only without entries that hold none.");
        }

        var gone = left.Select(placed => placed.Entry.DumpId.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        var kept = toc.Where(placed => !omit(placed.Entry))
            .Select(placed => (Placed: placed, Dependencies: placed.Dependencies.Where(dependency => !gone.Contains(dependency)).ToList()))
            .ToList();
        var writer = new Writer(reader.IntSize, reader.OffsetSize);
        var size = countAt + writer.IntBytes + kept.Sum(entry =>
            entry.Placed.DependenciesAt - entry.Placed.Start
            + entry.Dependencies.Sum(dependency => writer.IntBytes + Encoding.UTF8.GetByteCount(dependency)) + writer.IntBytes
            + 1 + reader.OffsetSize);
        var moved = size - tocEnd;

        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        Copy(input, output, 0, countAt);
        writer.Int(output, kept.Count);
        foreach (var (placed, dependencies) in kept)
        {
            Copy(input, output, placed.Start, placed.DependenciesAt - placed.Start);
            foreach (var dependency in dependencies)
            {
                writer.String(output, dependency);
            }

            writer.String(output, null);
            output.WriteByte(placed.Flag);
            writer.Offset(output, placed.Flag == OffsetSet ? placed.Offset + moved : placed.Offset);
        }

        if (output.Position != size)
        {
            throw new InvalidOperationException($"The table of contents of {destination} came out {output.Position} bytes long, not {size}.");
        }

        Copy(input, output, tocEnd, input.Length - tocEnd);
        return [.. left.Select(placed => placed.Entry.Name)];
    }

    private static void Copy(FileStream input, Stream output, long from, long count)
    {
        input.Position = from;
        var buffer = new byte[1 << 20];
        while (count > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
            {
                throw new EndOfStreamException("The archive ended before the bytes it was to copy.");
            }

            output.Write(buffer, 0, read);
            count -= read;
        }
    }

    /// <summary>An entry of the table of contents and where its bytes lie: from its start to its dependencies, then the dependencies, then its data offset.</summary>
    private sealed record Placed(Entry Entry, long Start, long DependenciesAt, IReadOnlyList<string> Dependencies, byte Flag, long Offset);

    private static (Reader Reader, bool Compressed, long CountAt, List<Placed> Toc) ReadToc(FileStream file, string path)
    {
        var reader = new Reader(file);
        if (Encoding.ASCII.GetString(reader.Bytes(5)) != "PGDMP")
        {
            throw new InvalidDataException($"{path} is not a pg_dump custom-format archive.");
        }

        var (major, minor) = (reader.Byte(), reader.Byte());
        reader.Byte();
        if (major != 1 || minor is < 14 or > 16)
        {
            throw new InvalidDataException($"{path} is archive version {major}.{minor}; this reads 1.14 to 1.16.");
        }

        reader.IntSize = reader.Byte();
        reader.OffsetSize = reader.Byte();
        if (reader.Byte() != 1)
        {
            throw new InvalidDataException($"{path} is not in the custom format.");
        }

        bool compressed;
        if (minor >= 15)
        {
            compressed = reader.Byte() switch
            {
                0 => false,
                1 => true,
                var other => throw new InvalidDataException($"{path} is compressed with algorithm {other}; this reads none or gzip."),
            };
        }
        else
        {
            compressed = reader.Int() != 0;
        }

        for (var i = 0; i < 7; i++)
        {
            reader.Int();
        }

        reader.String();
        reader.String();
        reader.String();

        var countAt = file.Position;
        var count = reader.Int();
        var entries = new List<Placed>(count);
        for (var i = 0; i < count; i++)
        {
            var start = file.Position;
            var dumpId = reader.Int();
            reader.Int();
            reader.String();
            reader.String();
            var tag = reader.String() ?? string.Empty;
            var kind = reader.String() ?? string.Empty;
            var section = reader.Int();
            var definition = reader.String() ?? string.Empty;
            reader.String();
            var copy = reader.String() ?? string.Empty;
            var schema = reader.String() ?? string.Empty;
            reader.String();
            reader.String();
            if (minor >= 16)
            {
                reader.Int();
            }

            reader.String();
            reader.String();
            var dependenciesAt = file.Position;
            var dependencies = new List<string>();
            while (reader.String() is { } dependency)
            {
                dependencies.Add(dependency);
            }

            var flag = reader.Byte();
            var offset = reader.Offset();
            entries.Add(new Placed(new Entry(dumpId, kind, schema, tag, definition, copy, flag == OffsetSet ? offset : null, section),
                start, dependenciesAt, dependencies, flag, offset));
        }

        return (reader, compressed, countAt, entries);
    }

    /// <summary>The tables of schema public whose rows the archive holds.</summary>
    public IReadOnlyList<Entry> Tables => Entries.Where(entry => entry.Kind == "TABLE DATA" && entry.Schema == "public").ToList();

    /// <summary>
    /// Every entry that is not data: the schema, which a restore creates before and after the rows. The
    /// database's own entries name the database dumped and are restored only with <c>--create</c>, which
    /// no publication uses.
    /// </summary>
    public IReadOnlyList<Entry> Definitions =>
        Entries.Where(entry => entry.Kind is not ("TABLE DATA" or "SEQUENCE SET" or "DATABASE" or "DATABASE PROPERTIES")).ToList();

    /// <summary>Every sequence's value as the archive sets it: its <c>setval</c> statements.</summary>
    public IReadOnlyList<string> SequenceValues =>
        Entries.Where(entry => entry.Kind == "SEQUENCE SET").Select(entry => entry.Definition.Trim()).ToList();

    /// <summary>The primary key of every table, as the column list pg_dump writes in its constraint.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PrimaryKeys()
    {
        var keys = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var entry in Entries.Where(entry => entry.Kind == "CONSTRAINT"))
        {
            if (PrimaryKey().Match(entry.Definition) is { Success: true } match)
            {
                keys[match.Groups["table"].Value] = Columns(match.Groups["columns"].Value);
            }
        }

        return keys;
    }

    /// <summary>The columns of a COPY header or a key, split on the commas between identifiers.</summary>
    public static IReadOnlyList<string> Columns(string list)
    {
        var columns = new List<string>();
        var quoted = false;
        var start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            if (list[i] == '"')
            {
                quoted = !quoted;
            }
            else if (list[i] == ',' && !quoted)
            {
                columns.Add(list[start..i].Trim());
                start = i + 1;
            }
        }

        columns.Add(list[start..].Trim());
        return columns;
    }

    /// <summary>
    /// One table's rows: <paramref name="header"/> gets the table and columns of its COPY statement,
    /// then <paramref name="row"/> each row as COPY text, without its newline.
    /// </summary>
    public void Rows(string table, Action<string, string> header, RowAction row)
    {
        var entry = Tables.FirstOrDefault(entry => entry.Name == table)
            ?? throw new InvalidDataException($"{_path} holds no rows of {table}.");
        if (Header().Match(entry.Copy.Trim()) is not { Success: true } copy)
        {
            throw new InvalidDataException($"{_path} has a COPY statement for {table} this does not read: {entry.Copy}");
        }

        if (entry.DataAt is not { } at)
        {
            throw new InvalidDataException($"{_path} records no offset for the rows of {table}; it was not written to a file pg_dump could seek in.");
        }

        header(copy.Groups["table"].Value, copy.Groups["columns"].Value);
        using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        file.Position = at;
        var reader = new Reader(file) { IntSize = _intSize, OffsetSize = _offsetSize };
        if (reader.Byte() != BlockData || reader.Int() != entry.DumpId)
        {
            throw new InvalidDataException($"{_path} has no data block of {table} at offset {at}.");
        }

        using var chunks = new Chunks(reader);
        using Stream data = _compressed ? new ZLibStream(chunks, CompressionMode.Decompress) : chunks;
        Lines(data, row);
    }

    private static void Lines(Stream stream, RowAction row)
    {
        var buffer = new byte[1 << 20];
        var filled = 0;
        while (true)
        {
            var read = stream.Read(buffer, filled, buffer.Length - filled);
            filled += read;
            var span = buffer.AsSpan(0, filled);
            var start = 0;
            while (span[start..].IndexOf((byte)'\n') is var newline and >= 0)
            {
                // pg_dump ends a table's data with COPY's own end marker and two blank lines.
                if (span.Slice(start, newline).SequenceEqual("\\."u8))
                {
                    return;
                }

                row(span.Slice(start, newline));
                start += newline + 1;
            }

            if (read == 0)
            {
                throw new InvalidDataException("A table's rows end without COPY's end marker.");
            }

            var rest = filled - start;
            if (start == 0 && rest == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }
            else
            {
                Buffer.BlockCopy(buffer, start, buffer, 0, rest);
            }

            filled = rest;
        }
    }

    // "COPY public.word (id, text_id, \"position\") FROM stdin;"
    [GeneratedRegex(@"^COPY (?<table>\S+) \((?<columns>.*)\) FROM stdin;$")]
    private static partial Regex Header();

    [GeneratedRegex(@"^ALTER TABLE ONLY public\.(?<table>\S+)\s+ADD CONSTRAINT \S+ PRIMARY KEY \((?<columns>[^)]*)\);", RegexOptions.Singleline)]
    private static partial Regex PrimaryKey();

    /// <summary>pg_dump's integers, offsets and strings.</summary>
    private sealed class Reader(Stream stream)
    {
        public int IntSize { get; set; } = 4;

        public int OffsetSize { get; set; } = 8;

        public Stream Stream => stream;

        public byte Byte() => stream.ReadByte() is var b and >= 0 ? (byte)b : throw new EndOfStreamException();

        public byte[] Bytes(int count)
        {
            var bytes = new byte[count];
            stream.ReadExactly(bytes);
            return bytes;
        }

        public int Int()
        {
            var negative = Byte() != 0;
            long value = 0;
            for (var i = 0; i < IntSize; i++)
            {
                value |= (long)Byte() << (8 * i);
            }

            return (int)(negative ? -value : value);
        }

        public long Offset()
        {
            long value = 0;
            for (var i = 0; i < OffsetSize; i++)
            {
                value |= (long)Byte() << (8 * i);
            }

            return value;
        }

        public string? String()
        {
            var length = Int();
            return length < 0 ? null : length == 0 ? string.Empty : Encoding.UTF8.GetString(Bytes(length));
        }
    }

    /// <summary>pg_dump's integers, offsets and strings, written as <see cref="Reader"/> reads them.</summary>
    private sealed class Writer(int intSize, int offsetSize)
    {
        public int IntBytes => 1 + intSize;

        public void Int(Stream stream, int value)
        {
            stream.WriteByte(value < 0 ? (byte)1 : (byte)0);
            var magnitude = Math.Abs((long)value);
            for (var i = 0; i < intSize; i++)
            {
                stream.WriteByte((byte)(magnitude >> (8 * i)));
            }
        }

        public void Offset(Stream stream, long value)
        {
            for (var i = 0; i < offsetSize; i++)
            {
                stream.WriteByte((byte)(value >> (8 * i)));
            }
        }

        public void String(Stream stream, string? value)
        {
            if (value is null)
            {
                Int(stream, -1);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            Int(stream, bytes.Length);
            stream.Write(bytes);
        }
    }

    /// <summary>A data block's length-prefixed chunks, read as one stream that ends at the zero-length chunk.</summary>
    private sealed class Chunks(Reader reader) : Stream
    {
        private int _left;
        private bool _ended;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (_left == 0)
            {
                if (_ended)
                {
                    return 0;
                }

                _left = reader.Int();
                if (_left == 0)
                {
                    _ended = true;
                    return 0;
                }
            }

            var read = reader.Stream.Read(buffer[..Math.Min(buffer.Length, _left)]);
            if (read == 0)
            {
                throw new EndOfStreamException("A data block ends inside a chunk.");
            }

            _left -= read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal delegate void RowAction(ReadOnlySpan<byte> line);
