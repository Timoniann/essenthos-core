using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Essenthos.Core.Products;

/// <summary>
/// Row ids that depend on what the row is, not on how many rows were written before it, so that two builds of the same
/// files number every row alike and a change moves only the rows it touches. A release is sent to a server as the rows
/// that differ from the one it holds; an id handed out in insertion order renumbers every row after the first one added
/// or removed, and the difference then carries the whole table.
/// <para>
/// Books, chapters and verses are numbered from their place in their text, in the order of their keys, so ordering by id
/// is still reading order within a text: <see cref="Book"/>, <see cref="Chapter"/>, <see cref="Verse"/>. Anything else is
/// numbered by a hash of its key (<see cref="Hashed"/>).
/// </para>
/// </summary>
public static class RowIds
{
    /// <summary>The highest text id a book, chapter or verse id has room for.</summary>
    public const int Texts = 127;

    /// <summary>The highest canonical book ordinal a book, chapter or verse id has room for.</summary>
    public const int Books = 127;

    /// <summary>The highest chapter number a chapter or verse id has room for.</summary>
    public const int Chapters = 255;

    /// <summary>The most verses one chapter of one text may hold.</summary>
    public const int VersesPerChapter = 512;

    /// <summary>A book's id: its text's id and its canonical ordinal.</summary>
    public static int Book(int text, int ordinal) => (Check(text, 1, Texts, "text id") << 7) | Check(ordinal, 0, Books, "book ordinal");

    /// <summary>A chapter's id: its book's and its number.</summary>
    public static int Chapter(int text, int ordinal, int chapter) => (Book(text, ordinal) << 8) | Check(chapter, 0, Chapters, "chapter number");

    /// <summary>
    /// A verse's id: its chapter's, and its place among the chapter's verses in key order (0 for the first). A verse added to a
    /// chapter moves only the verses after it in that chapter.
    /// </summary>
    public static int Verse(int text, int ordinal, int chapter, int place) =>
        (Chapter(text, ordinal, chapter) << 9) | Check(place, 0, VersesPerChapter - 1, "verse's place in its chapter");

    /// <summary>
    /// The id of a key at the given attempt: the first <paramref name="bits"/> bits of SHA-256 over the key, with <c>|attempt</c>
    /// appended after the first. May be 0, which no row takes.
    /// </summary>
    public static long Hash(string key, int bits, int attempt = 0)
    {
        if (bits is < 8 or > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), bits, "An id is 8 to 63 bits.");
        }

        var text = attempt == 0 ? key : $"{key}|{attempt}";
        var length = Encoding.UTF8.GetByteCount(text);
        Span<byte> bytes = length <= 512 ? stackalloc byte[length] : new byte[length];
        Encoding.UTF8.GetBytes(text, bytes);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return (long)(BinaryPrimitives.ReadUInt64BigEndian(hash) >> (64 - bits));
    }

    /// <summary>
    /// Ids for keys from a hash of each, handed out one key at a time: a key takes the first attempt whose id is not 0 and not
    /// taken, by an earlier key or by <paramref name="held"/>. Only a key whose hash meets another's depends on the order the
    /// keys come in.
    /// </summary>
    public sealed class Hashed(int bits, Func<long, bool>? held = null)
    {
        private readonly HashSet<long> _given = [];

        public int Count => _given.Count;

        public long Of(string key)
        {
            if (bits < 63 && _given.Count >= (1L << bits) / 2)
            {
                throw new InvalidOperationException($"More than {(1L << bits) / 2} ids asked of {bits} bits; the id needs more room.");
            }

            for (var attempt = 0; ; attempt++)
            {
                var id = Hash(key, bits, attempt);
                if (id != 0 && !(held?.Invoke(id) ?? false) && _given.Add(id))
                {
                    return id;
                }
            }
        }
    }

    /// <summary>The room left above the spread keys, for the ids pushed up by keys that spread alike.</summary>
    private const int OrderedHeadroom = 1 << 20;

    /// <summary>
    /// Positive ids for keys given in their order, increasing in that order, each as near as the order allows to where the key
    /// falls among all possible keys (<see cref="Spread"/>). So ordering by id is ordering by key, and a key added or removed
    /// moves only the keys after it that spread to the same place or within reach of it, not every key after it.
    /// </summary>
    public static int[] Ordered(IReadOnlyList<string> keys)
    {
        var ids = new int[keys.Count];
        long previous = 0;
        for (var i = 0; i < keys.Count; i++)
        {
            var id = Math.Max(Spread(keys[i]), previous + 1);
            ids[i] = id <= int.MaxValue
                ? (int)id
                : throw new InvalidOperationException($"Too many keys spread near the end of the ids for '{keys[i]}' to have one.");
            previous = id;
        }

        return ids;
    }

    /// <summary>
    /// Where a key falls among all keys in ordinal order, as a number from 1 to <c>int.MaxValue - OrderedHeadroom</c>: its first
    /// characters read as the digits of a fraction (end of key, '-', digits, letters, anything else).
    /// </summary>
    internal static long Spread(string key)
    {
        const int digits = 40;
        double fraction = 0, scale = 1;
        for (var i = 0; i < Math.Min(key.Length, 12); i++)
        {
            var c = key[i];
            var digit = c switch
            {
                '-' => 1,
                >= '0' and <= '9' => 2 + (c - '0'),
                >= 'a' and <= 'z' => 12 + (c - 'a'),
                _ => digits - 1,
            };
            scale /= digits;
            fraction += digit * scale;
        }

        return 1 + (long)(fraction * (int.MaxValue - OrderedHeadroom - 1));
    }

    private static int Check(int value, int least, int most, string what) =>
        value >= least && value <= most
            ? value
            : throw new InvalidOperationException($"A {what} of {value} does not fit a row id, which has room for {least} to {most}.");
}
