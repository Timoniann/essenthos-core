using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Essenthos.Core.Mapping;

/// <summary>
/// A link's id, derived from the words it joins and nothing else: the pair's ordinal (<c>mapping/pairs</c>) in the top
/// <see cref="OrdinalBits"/> bits below the sign, and the first <see cref="HashBits"/> bits of SHA-256 over
/// <c>pair|from|to</c> under it. The same link has the same id in every build, on every machine, whatever files existed
/// before, so two builds' finals can be joined by id, and no id is carried from a database or an earlier file.
/// <para>
/// Ids are unique by construction across pairs, because each pair has its own ordinal. Inside a pair two shapes may hash
/// alike (under 10⁻⁴ for the largest pair, 1.4 × 10⁻³ for all 22.5 M links together, 2026-11): the merge numbers a pair's
/// links in key order, and a shape whose id is taken is hashed again with <c>|1</c>, <c>|2</c>, … appended until one is
/// free. Only such a shape's id depends on another link: it changes if the link that took its first id goes.
/// </para>
/// </summary>
internal static class LinkIds
{
    public const int HashBits = 51;

    public const int OrdinalBits = 63 - HashBits;

    public const long MaxOrdinal = (1L << OrdinalBits) - 1;

    private const ulong HashMask = (1UL << HashBits) - 1;

    /// <summary>The id of the shape at the given attempt: 0 first, the next one after each collision.</summary>
    public static long Of(long ordinal, string pair, string from, string to, int attempt = 0)
    {
        if (ordinal is < 1 or > MaxOrdinal)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal,
                $"A pair's ordinal must be 1 to {MaxOrdinal} to fit a link id; mapping/pairs needs a wider id layout first.");
        }

        var text = attempt == 0 ? $"{pair}|{from}|{to}" : $"{pair}|{from}|{to}|{attempt}";
        var length = Encoding.UTF8.GetByteCount(text);
        Span<byte> bytes = length <= 1024 ? stackalloc byte[length] : new byte[length];
        Encoding.UTF8.GetBytes(text, bytes);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return (ordinal << HashBits) | (long)(BinaryPrimitives.ReadUInt64BigEndian(hash) >> (64 - HashBits) & HashMask);
    }

    /// <summary>The pair a link id belongs to, by ordinal.</summary>
    public static long OrdinalOf(long id) => id >> HashBits;

    /// <summary>Gives one pair's links their ids, in the order they are asked for, and counts the collisions met.</summary>
    public sealed class Allocator(long ordinal, string pair)
    {
        private readonly HashSet<long> _used = [];

        public int Collisions { get; private set; }

        public long Next(string from, string to)
        {
            for (var attempt = 0; ; attempt++)
            {
                var id = Of(ordinal, pair, from, to, attempt);
                if (_used.Add(id))
                {
                    return id;
                }

                Collisions++;
            }
        }
    }
}
