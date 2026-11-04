using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Essenthos.Core.Products.Essx;

/// <summary>
/// The fixed parts of an ESSX file. The layout is <c>docs/essx-format.md</c>; what is here is what a
/// reader in any language needs first: the magic, the trailer, the varint, the codecs.
///
/// <para>
/// File: <c>"ESSX" u16 version u16 flags</c>, then the chunks and dictionaries in the order written,
/// then the footer (JSON, UTF-8), then the trailer: <c>u64 footerOffset u64 footerLength "ESSX"
/// sha256</c>. The SHA-256 is over every byte before it. Integers are little-endian.
/// </para>
/// </summary>
public static class EssxLayout
{
    public static ReadOnlySpan<byte> Magic => "ESSX"u8;

    /// <summary>With the dot; the owner named the format. It and <see cref="Magic"/> are the two places a rename touches.</summary>
    public const string Extension = ".essx";

    /// <summary>2: a chunk of a dict column may say <c>"encoding": "text"</c> once the column's dictionary reached its cap.</summary>
    public const ushort Version = 2;

    public const int PreambleLength = 8;

    public const int HashLength = 32;

    /// <summary>footer offset, footer length, magic, hash.</summary>
    public const int TrailerLength = 8 + 8 + 4 + HashLength;

    public const string CodecNone = "none";

    /// <summary>Raw deflate, which a browser decodes natively as <c>deflate-raw</c>.</summary>
    public const string CodecDeflate = "deflate";

    /// <summary>The oldest version this reader reads; version 1 has no per-chunk encoding.</summary>
    public const ushort OldestVersion = 1;

    /// <summary>The footer's JSON, compiled rather than reflected: no warm-up on open, and the tool links with Native AOT.</summary>
    public static EssxJsonContext Json => EssxJsonContext.Default;

    /// <summary>The least whole bytes that hold a value from 0 to <paramref name="range"/>: 0, 1, 2, 3, 4 or 8.</summary>
    public static int Width(ulong range) => range == 0 ? 0 : range < 1UL << 8 ? 1 : range < 1UL << 16 ? 2 : range < 1UL << 24 ? 3 : range < 1UL << 32 ? 4 : 8;

    public static void WriteUnsigned(Span<byte> target, ulong value, int width)
    {
        switch (width)
        {
            case 0: break;
            case 1: target[0] = (byte)value; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)value); break;
            case 3: target[0] = (byte)value; target[1] = (byte)(value >> 8); target[2] = (byte)(value >> 16); break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(target, (uint)value); break;
            default: BinaryPrimitives.WriteUInt64LittleEndian(target, value); break;
        }
    }

    public static ulong ReadUnsigned(ReadOnlySpan<byte> source, int width) => width switch
    {
        0 => 0,
        1 => source[0],
        2 => BinaryPrimitives.ReadUInt16LittleEndian(source),
        3 => source[0] | (ulong)source[1] << 8 | (ulong)source[2] << 16,
        4 => BinaryPrimitives.ReadUInt32LittleEndian(source),
        _ => BinaryPrimitives.ReadUInt64LittleEndian(source),
    };

    public static int BitmapLength(int rows) => (rows + 7) >> 3;

    public static bool Bit(ReadOnlySpan<byte> bitmap, int index) => (bitmap[index >> 3] & (1 << (index & 7))) != 0;

    public static void SetBit(Span<byte> bitmap, int index) => bitmap[index >> 3] |= (byte)(1 << (index & 7));

    public static byte[] Compress(ReadOnlySpan<byte> raw, string codec)
    {
        if (codec != CodecDeflate)
        {
            throw new ArgumentException($"Unknown codec '{codec}'.", nameof(codec));
        }

        using var output = new MemoryStream(raw.Length / 2 + 64);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        return output.ToArray();
    }

    public static byte[] Decompress(byte[] stored, string codec, long rawLength)
    {
        if (codec == CodecNone)
        {
            return stored;
        }

        if (codec != CodecDeflate)
        {
            throw new ProductFormatException($"Unknown codec '{codec}'.");
        }

        var raw = new byte[rawLength];
        using var deflate = new DeflateStream(new MemoryStream(stored, writable: false), CompressionMode.Decompress);
        deflate.ReadExactly(raw);
        return raw;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EssxFooter))]
public sealed partial class EssxJsonContext : JsonSerializerContext;

/// <summary>The footer: everything a reader needs to find and decode the rest, as JSON.</summary>
public sealed class EssxFooter
{
    public string Format { get; set; } = "essx";

    public int Version { get; set; } = EssxLayout.Version;

    public required EssxProductInfo Product { get; set; }

    public List<EssxTableInfo> Tables { get; set; } = [];
}

/// <summary>The <see cref="ProductHeader"/> as the footer carries it.</summary>
public sealed class EssxProductInfo
{
    public required string Product { get; set; }

    public List<EssxInput> Inputs { get; set; } = [];

    public required string Producer { get; set; }

    public long Rows { get; set; }

    public List<string[]>? Notes { get; set; }

    public List<string>? Comments { get; set; }

    public static EssxProductInfo From(ProductHeader header) => new()
    {
        Product = header.Product,
        Inputs = [.. header.Inputs.Select(input => new EssxInput { Path = input.Path, Sha256 = input.Sha256 })],
        Producer = header.Producer,
        Rows = header.Rows ?? 0,
        Notes = header.Notes.Count == 0 ? null : [.. header.Notes.Select(note => new[] { note.Key, note.Value })],
        Comments = header.Comments.Count == 0 ? null : [.. header.Comments],
    };

    public ProductHeader ToHeader()
    {
        var header = new ProductHeader(Product, [.. Inputs.Select(input => new ProductInput(input.Path, input.Sha256))], Producer) { Rows = Rows };
        header.Notes.AddRange((Notes ?? []).Select(note => new KeyValuePair<string, string>(note[0], note[1])));
        header.Comments.AddRange(Comments ?? []);
        return header;
    }
}

public sealed class EssxInput
{
    public required string Path { get; set; }

    public required string Sha256 { get; set; }
}

public sealed class EssxTableInfo
{
    public required string Name { get; set; }

    /// <summary>The producer's own version of the table's shape, from <see cref="ProductSchema{T}"/>.</summary>
    public int SchemaVersion { get; set; }

    public long Rows { get; set; }

    public int RowGroupRows { get; set; }

    public List<EssxColumnInfo> Columns { get; set; } = [];

    public List<EssxPhysicalInfo> Physical { get; set; } = [];

    public List<EssxRowGroupInfo> RowGroups { get; set; } = [];
}

/// <summary>A column as the schema declares it, and the stored columns it is made of.</summary>
public sealed class EssxColumnInfo
{
    public required string Name { get; set; }

    /// <summary>text, integer, boolean, json, versekey, wordkey.</summary>
    public required string Type { get; set; }

    public bool Nullable { get; set; }

    public List<string> Physical { get; set; } = [];
}

/// <summary>A stored column: one chunk per row group, and a dictionary when the encoding has one.</summary>
public sealed class EssxPhysicalInfo
{
    public required string Name { get; set; }

    /// <summary>int, bool, dict, text.</summary>
    public required string Encoding { get; set; }

    public EssxSectionInfo? Dictionary { get; set; }
}

public sealed class EssxRowGroupInfo
{
    public int Rows { get; set; }

    /// <summary>One per physical column, in the table's physical order.</summary>
    public List<EssxChunkInfo> Chunks { get; set; } = [];
}

public sealed class EssxChunkInfo
{
    public long Offset { get; set; }

    public long Length { get; set; }

    public long RawLength { get; set; }

    public string Codec { get; set; } = EssxLayout.CodecNone;

    public int Nulls { get; set; }

    /// <summary>Bytes per value for int and dict chunks; 0 when every value is <see cref="Base"/>.</summary>
    public int Width { get; set; }

    /// <summary>What a stored unsigned value is added to, for int chunks.</summary>
    public long Base { get; set; }

    public long? Min { get; set; }

    public long? Max { get; set; }

    /// <summary>Null when the chunk is in its physical column's encoding; <c>text</c> for a dict column's chunks after its dictionary reached its cap.</summary>
    public string? Encoding { get; set; }
}

public sealed class EssxSectionInfo
{
    public long Offset { get; set; }

    public long Length { get; set; }

    public long RawLength { get; set; }

    public string Codec { get; set; } = EssxLayout.CodecNone;

    public int Count { get; set; }
}

/// <summary>How a file is written: the row group size and whether chunks are compressed.</summary>
public sealed record EssxOptions
{
    public static readonly EssxOptions Default = new();

    /// <summary>Rows per group. A group is the unit of random access and of compression.</summary>
    public int RowGroupRows { get; init; } = 1 << 16;

    /// <summary><see cref="EssxLayout.CodecNone"/> or <see cref="EssxLayout.CodecDeflate"/>.</summary>
    public string Codec { get; init; } = EssxLayout.CodecNone;

    /// <summary>Chunks smaller than this are never compressed.</summary>
    public int CompressFrom { get; init; } = 256;

    /// <summary>
    /// The most distinct strings a dict column keeps. A column whose first group repeats and whose later
    /// groups do not would otherwise hold every string of the table in memory; past the cap its chunks are
    /// written as text.
    /// </summary>
    public int DictionaryLimit { get; init; } = 1 << 20;

    /// <summary>The most characters a dict column's strings hold together before it turns to text.</summary>
    public long DictionaryCharacterLimit { get; init; } = 32L << 20;
}
