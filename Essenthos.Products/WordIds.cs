using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Essenthos.Core.Products;

/// <summary>
/// A word's id, derived from its key and nothing else: the first <see cref="Bits"/> bits of SHA-256 over the key as files
/// write it with its text, <c>KJV 1.1:1.3</c>. The same word has the same id in every build and on every machine, so no id
/// is carried in a file or taken from a database, and a word whose key is gone simply has no id any more.
/// <para>
/// Fifty-two bits keep every id below 2^53, so it travels as an exact JSON number. Two keys of the corpus may still hash
/// alike (about 0.1 expected over 30 million words): the corpus's keys are taken in key order, the first keeps its id, and a
/// later one is hashed again with <c>|1</c>, <c>|2</c>, … appended until it reaches an id no word holds
/// (<see cref="Rehash"/>). 0 is never an id. Only such a key's id depends on another word, and the build lists every one.
/// </para>
/// </summary>
public static class WordIds
{
    public const int Bits = 52;

    /// <summary>One above the highest id: every id is 1 to <c>Limit - 1</c>.</summary>
    public const long Limit = 1L << Bits;

    /// <summary>The id of the key at the given attempt: 0 first, the next one after each collision. May be 0, which no word takes.</summary>
    public static long Of(WordKey key, int attempt = 0)
    {
        var text = attempt == 0 ? key.ToString() : $"{key}|{attempt}";
        var length = Encoding.UTF8.GetByteCount(text);
        Span<byte> bytes = length <= 512 ? stackalloc byte[length] : new byte[length];
        Encoding.UTF8.GetBytes(text, bytes);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return (long)(BinaryPrimitives.ReadUInt64BigEndian(hash) >> (64 - Bits));
    }

    /// <summary>A key whose first id another key took (<see cref="With"/>, null where the first id was 0), and the id it holds.</summary>
    public sealed record Rehashed(WordKey Key, int Attempt, long Id, WordKey? With);

    /// <summary>
    /// The ids of the contested keys: every key whose first id is 0 or shared with another key of the corpus.
    /// <paramref name="firstTaken"/> answers whether any word of the corpus has a given first id. In each group of keys
    /// sharing an id the first in key order keeps it; the others, in key order, take the first attempt whose id is not 0,
    /// not any word's first id and not given to an earlier contested key.
    /// </summary>
    public static List<Rehashed> Rehash(IEnumerable<WordKey> contested, Func<long, bool> firstTaken) => Rehash(contested, firstTaken, Of);

    /// <summary>The rule over any hash, so that a test can make keys meet.</summary>
    internal static List<Rehashed> Rehash(IEnumerable<WordKey> contested, Func<long, bool> firstTaken, Func<WordKey, int, long> of)
    {
        var given = new HashSet<long>();
        var rehashed = new List<Rehashed>();
        foreach (var group in contested.Distinct().GroupBy(key => of(key, 0)).OrderBy(group => group.Min()))
        {
            var keys = group.Order().ToList();
            WordKey? keeper = group.Key == 0 ? null : keys[0];
            foreach (var key in keys.Skip(keeper is null ? 0 : 1))
            {
                for (var attempt = 1; ; attempt++)
                {
                    var id = of(key, attempt);
                    if (id != 0 && !firstTaken(id) && given.Add(id))
                    {
                        rehashed.Add(new Rehashed(key, attempt, id, keeper));
                        break;
                    }
                }
            }
        }

        return rehashed;
    }
}
