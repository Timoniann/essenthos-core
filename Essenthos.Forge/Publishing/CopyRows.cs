using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Essenthos.Core.Publishing;

/// <summary>
/// A row as <c>COPY … TO STDOUT</c> writes it in text format, which is how a pg_dump archive holds every
/// row, read as the text Postgres gives the same row as a record: <c>ROW(a, b, …)::text</c> over the
/// columns the COPY names, in its order. The MD5 of that text is a row's hash here and on a server alike,
/// so a sum of hashes taken over a dumped table equals the sum a server takes over the table it restored
/// or patched (<see cref="TableSum.Sql"/>), and two archives can be compared row by row without a database.
/// </summary>
internal sealed class CopyRows
{
    private byte[] _record = new byte[4096];

    /// <summary>The MD5 of the row's record text, as the server's <c>md5(ROW(…)::text)</c> would give it.</summary>
    public UInt128 Hash(ReadOnlySpan<byte> line)
    {
        var length = RecordText(line);
        Span<byte> digest = stackalloc byte[16];
        MD5.HashData(_record.AsSpan(0, length), digest);
        return BinaryPrimitives.ReadUInt128BigEndian(digest);
    }

    /// <summary>The record text of the last row hashed or converted, for tests and messages.</summary>
    public string Text(ReadOnlySpan<byte> line) => Encoding.UTF8.GetString(_record, 0, RecordText(line));

    /// <summary>
    /// Writes the row's record text into the buffer and returns its length: each field de-escaped as COPY
    /// reads it, then quoted as <c>record_out</c> quotes it, joined by commas inside parentheses. Works on
    /// bytes, which is safe for UTF-8: no byte of a multi-byte character is ever an ASCII one.
    /// </summary>
    private int RecordText(ReadOnlySpan<byte> line)
    {
        // A field at worst doubles (every byte a quote or backslash) and gains two quotes and a comma.
        var most = 2 * line.Length + 3 * (line.Count((byte)'\t') + 1) + 2;
        if (_record.Length < most)
        {
            _record = new byte[Math.Max(most, _record.Length * 2)];
        }

        var output = _record.AsSpan();
        var at = 0;
        output[at++] = (byte)'(';
        var start = 0;
        var first = true;
        while (true)
        {
            var tab = line[start..].IndexOf((byte)'\t');
            var field = tab < 0 ? line[start..] : line.Slice(start, tab);
            if (!first)
            {
                output[at++] = (byte)',';
            }

            first = false;
            at = AppendField(field, output, at);
            if (tab < 0)
            {
                break;
            }

            start += tab + 1;
        }

        output[at++] = (byte)')';
        return at;
    }

    private static int AppendField(ReadOnlySpan<byte> field, Span<byte> output, int at)
    {
        // COPY's null; record_out writes nothing for it.
        if (field.Length == 2 && field[0] == '\\' && field[1] == 'N')
        {
            return at;
        }

        // The value is de-escaped first into the tail of the buffer the record text has not reached, so
        // quoting can be decided over the real bytes; the tail is at least twice the field's length.
        var tail = output.Length - field.Length;
        var length = Unescape(field, output[tail..]);
        var value = output.Slice(tail, length);
        var quote = length == 0;
        foreach (var b in value)
        {
            if (b is (byte)'"' or (byte)'\\' or (byte)'(' or (byte)')' or (byte)',' or (byte)' ' or (byte)'\t' or (byte)'\n'
                or (byte)'\r' or (byte)'\v' or (byte)'\f')
            {
                quote = true;
                break;
            }
        }

        if (!quote)
        {
            value.CopyTo(output[at..]);
            return at + length;
        }

        // Quoting can grow the value past where its de-escaped copy starts, so it is copied out first.
        Span<byte> copy = length <= 1024 ? stackalloc byte[length] : new byte[length];
        value.CopyTo(copy);
        output[at++] = (byte)'"';
        foreach (var b in copy)
        {
            if (b is (byte)'"' or (byte)'\\')
            {
                output[at++] = b;
            }

            output[at++] = b;
        }

        output[at++] = (byte)'"';
        return at;
    }

    /// <summary>COPY text's escapes, as <c>CopyReadAttributesText</c> reads them.</summary>
    private static int Unescape(ReadOnlySpan<byte> field, Span<byte> into)
    {
        var at = 0;
        for (var i = 0; i < field.Length; i++)
        {
            var b = field[i];
            if (b != '\\' || i + 1 == field.Length)
            {
                into[at++] = b;
                continue;
            }

            var next = field[++i];
            switch (next)
            {
                case >= (byte)'0' and <= (byte)'7':
                {
                    var value = next - '0';
                    for (var digits = 1; digits < 3 && i + 1 < field.Length && field[i + 1] is >= (byte)'0' and <= (byte)'7'; digits++)
                    {
                        value = (value << 3) + (field[++i] - '0');
                    }

                    into[at++] = (byte)value;
                    break;
                }

                case (byte)'x' when i + 1 < field.Length && IsHex(field[i + 1]):
                {
                    var value = Hex(field[++i]);
                    if (i + 1 < field.Length && IsHex(field[i + 1]))
                    {
                        value = (value << 4) + Hex(field[++i]);
                    }

                    into[at++] = (byte)value;
                    break;
                }

                case (byte)'b': into[at++] = (byte)'\b'; break;
                case (byte)'f': into[at++] = (byte)'\f'; break;
                case (byte)'n': into[at++] = (byte)'\n'; break;
                case (byte)'r': into[at++] = (byte)'\r'; break;
                case (byte)'t': into[at++] = (byte)'\t'; break;
                case (byte)'v': into[at++] = (byte)'\v'; break;
                default: into[at++] = next; break;
            }
        }

        return at;
    }

    private static bool IsHex(byte b) => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F';

    private static int Hex(byte b) => b <= '9' ? b - '0' : (b | 0x20) - 'a' + 10;
}

/// <summary>
/// A table's rows summed as a set: how many, and the sums of the two halves of every row's MD5 read as
/// signed 64-bit numbers. Order-free, and a row that appears twice counts twice, so two tables with the
/// same sum hold the same rows as far as 128 bits can tell.
/// </summary>
internal readonly record struct TableSum(long Rows, Int128 High, Int128 Low)
{
    public TableSum Add(UInt128 hash) => new(
        Rows + 1,
        High + (long)(ulong)(hash >> 64),
        Low + (long)(ulong)hash);

    public string Describe() => string.Create(CultureInfo.InvariantCulture, $"{Rows} rows, sums {High} {Low}");

    /// <summary>
    /// The statement that computes the same sum over a table on a server: <paramref name="columns"/> as the
    /// COPY names them. The session must render values as pg_dump's does (<see cref="ReleaseDifference.Settings"/>).
    /// </summary>
    public static string Sql(string table, string columns) =>
        "SELECT count(*), coalesce(sum(('x' || substr(row_md5, 1, 16))::bit(64)::bigint), 0), coalesce(sum(('x' || substr(row_md5, 17, 16))::bit(64)::bigint), 0)" +
        $" FROM (SELECT md5(ROW({columns})::text) AS row_md5 FROM {table}) AS rows";
}
