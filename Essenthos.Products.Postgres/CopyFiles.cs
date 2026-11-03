using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Essenthos.Core.Products.Esw;
using Essenthos.Core.Products.Tsv;
using NpgsqlTypes;

namespace Essenthos.Core.Products.Postgres;

/// <summary>
/// Writes what <c>COPY ... FROM '&lt;file&gt;'</c> reads, from an ESW table: the text format a TSV
/// product already is, or the binary format, which the server takes without parsing a number or
/// a quote. For a database on the same machine as the files this is the fastest way in: the
/// server reads the file itself and nothing crosses a client connection.
/// </summary>
public static class CopyFiles
{
    /// <summary><c>PGCOPY\n\377\r\n\0</c>, as bytes: a u8 literal would write the 0xff as two UTF-8 bytes.</summary>
    private static ReadOnlySpan<byte> Signature => [0x50, 0x47, 0x43, 0x4F, 0x50, 0x59, 0x0A, 0xFF, 0x0D, 0x0A, 0x00];

    /// <summary>
    /// Appends the table's rows to a binary COPY stream. <paramref name="first"/> writes the file
    /// header before them and <paramref name="last"/> the trailer after, so several tables or files
    /// go into one stream.
    /// </summary>
    public static long WriteBinary(Stream output, string eswPath, IReadOnlyList<CopyColumn> columns, IReadOnlyDictionary<string, NpgsqlDbType> types, bool first = true, bool last = true, string? sourceTable = null)
    {
        using var file = EswReader.Open(eswPath);
        var table = sourceTable is null ? file.Tables[0] : file.Table(sourceTable);
        var plan = columns.Select(column => (column, Index: column.Source is null ? -1 : IndexOfColumn(table, column.Source), Type: types[column.Target])).ToArray();
        var writer = new BinaryWriter(output);
        if (first)
        {
            writer.Write(Signature);
            writer.Write(new byte[8]);
        }

        var buffer = new byte[1 << 16];
        long rows = 0;
        for (var g = 0; g < table.RowGroups; g++)
        {
            var group = table.ReadGroup(g);
            var data = plan.Select(bound => bound.Index < 0 ? null : group.Columns[bound.Index]).ToArray();
            for (var i = 0; i < group.Rows; i++)
            {
                WriteInt16(writer, (short)plan.Length);
                for (var c = 0; c < plan.Length; c++)
                {
                    var (column, _, type) = plan[c];
                    if (column.Constant is { } constant)
                    {
                        WriteText(writer, constant, ref buffer, type);
                        continue;
                    }

                    var source = data[c]!;
                    if (source.IsNull(i))
                    {
                        WriteInt32(writer, -1);
                        continue;
                    }

                    switch (source)
                    {
                        case KeyData key when column.Part is 2 or 4:
                            WriteText(writer, (column.Part == 2 ? key.ChapterLabel.Text(i) : key.VerseLabel.Text(i)) ?? string.Empty, ref buffer, type);
                            break;
                        case KeyData key:
                            WriteInteger(writer, column.Part switch { 0 => key.Book.Values[i], 1 => key.Chapter.Values[i], 3 => key.Verse.Values[i], _ => key.Position!.Values[i] }, type);
                            break;
                        case IntData integers:
                            WriteInteger(writer, integers.Values[i], type);
                            break;
                        case BoolData booleans:
                            WriteInt32(writer, 1);
                            writer.Write(booleans.Values[i] ? (byte)1 : (byte)0);
                            break;
                        default:
                            WriteText(writer, source.Text(i)!, ref buffer, type);
                            break;
                    }
                }

                rows++;
            }
        }

        if (last)
        {
            WriteInt16(writer, -1);
        }

        writer.Flush();
        return rows;
    }

    /// <summary>Appends the table's rows as COPY text lines, which is the body of a TSV product.</summary>
    public static long WriteText(TextWriter output, string eswPath, IReadOnlyList<CopyColumn> columns, string? sourceTable = null)
    {
        using var file = EswReader.Open(eswPath);
        var table = sourceTable is null ? file.Tables[0] : file.Table(sourceTable);
        var plan = columns.Select(column => (column, Index: column.Source is null ? -1 : IndexOfColumn(table, column.Source))).ToArray();
        long rows = 0;
        for (var g = 0; g < table.RowGroups; g++)
        {
            var group = table.ReadGroup(g);
            var data = plan.Select(bound => bound.Index < 0 ? null : group.Columns[bound.Index]).ToArray();
            for (var i = 0; i < group.Rows; i++)
            {
                for (var c = 0; c < plan.Length; c++)
                {
                    if (c > 0) output.Write('\t');
                    var (column, _) = plan[c];
                    string? value;
                    if (column.Constant is { } constant) value = constant;
                    else if (data[c] is KeyData key) value = key.IsNull(i) ? null : KeyPart(key, column.Part, i);
                    else value = data[c]!.Text(i);
                    TsvEscaping.WriteEscaped(output, value);
                }

                output.Write('\n');
                rows++;
            }
        }

        return rows;
    }

    internal static string KeyPart(KeyData key, int part, int row) => part switch
    {
        0 => key.Book.Values[row].ToString(CultureInfo.InvariantCulture),
        1 => key.Chapter.Values[row].ToString(CultureInfo.InvariantCulture),
        2 => key.ChapterLabel.Text(row) ?? string.Empty,
        3 => key.Verse.Values[row].ToString(CultureInfo.InvariantCulture),
        4 => key.VerseLabel.Text(row) ?? string.Empty,
        _ => key.Position!.Values[row].ToString(CultureInfo.InvariantCulture),
    };

    private static int IndexOfColumn(EswTable table, string name)
    {
        for (var i = 0; i < table.Columns.Count; i++)
        {
            if (table.Columns[i].Name == name) return i;
        }

        throw new ArgumentException($"The file has no column '{name}'.");
    }

    private static void WriteInt16(BinaryWriter writer, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteInt32(BinaryWriter writer, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void WriteInteger(BinaryWriter writer, long value, NpgsqlDbType type)
    {
        Span<byte> bytes = stackalloc byte[8];
        switch (type)
        {
            case NpgsqlDbType.Smallint:
                WriteInt32(writer, 2);
                BinaryPrimitives.WriteInt16BigEndian(bytes, checked((short)value));
                writer.Write(bytes[..2]);
                break;
            case NpgsqlDbType.Integer:
                WriteInt32(writer, 4);
                BinaryPrimitives.WriteInt32BigEndian(bytes, checked((int)value));
                writer.Write(bytes[..4]);
                break;
            case NpgsqlDbType.Bigint:
                WriteInt32(writer, 8);
                BinaryPrimitives.WriteInt64BigEndian(bytes, value);
                writer.Write(bytes);
                break;
            default:
                var text = Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture));
                WriteInt32(writer, text.Length);
                writer.Write(text);
                break;
        }
    }

    /// <summary>Text, or jsonb, which in binary is the version byte 1 followed by the text.</summary>
    private static void WriteText(BinaryWriter writer, string value, ref byte[] buffer, NpgsqlDbType type)
    {
        var size = Encoding.UTF8.GetMaxByteCount(value.Length);
        if (buffer.Length < size)
        {
            buffer = new byte[Math.Max(size, buffer.Length * 2)];
        }

        var length = Encoding.UTF8.GetBytes(value, buffer);
        if (type == NpgsqlDbType.Jsonb)
        {
            WriteInt32(writer, length + 1);
            writer.Write((byte)1);
        }
        else
        {
            WriteInt32(writer, length);
        }

        writer.Write(buffer, 0, length);
    }
}
