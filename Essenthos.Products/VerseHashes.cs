using System.Security.Cryptography;
using System.Text;

namespace Essenthos.Core.Products;

/// <summary>
/// The two hashes a text's manifest keeps for every verse. <see cref="Text"/> is over the words as
/// printed, in order; <see cref="Annotation"/> is over what is said about them, in order. A product
/// that cites a verse is valid while the hash it depends on holds: a mapping row pairs printed
/// tokens and watches <see cref="Text"/>; a Strong index or a names projection reads the lemma and
/// the number and watches <see cref="Annotation"/> too. Both are the first sixteen hex digits of a
/// SHA-256, which is enough to tell one version of a verse from another, which is all they do.
/// </summary>
/// <param name="Text">Over <c>surface</c> and <c>trailer</c> of every word.</param>
/// <param name="Annotation">Over <c>lemma</c>, <c>strong</c>, <c>morphology</c> and <c>gloss</c> of every word.</param>
public readonly record struct VerseHashes(string Text, string Annotation)
{
    public const int Length = 16;

    public static VerseHashes Of<T>(IEnumerable<T> words, Func<T, VerseWord> fields)
    {
        var hasher = new VerseHasher();
        foreach (var word in words)
        {
            hasher.Add(fields(word));
        }

        return hasher.Finish();
    }

    public static VerseHashes Of(IEnumerable<VerseWord> words) => Of(words, word => word);
}

/// <summary>What of a word goes into its verse's hashes. Null and empty are different values and hash differently.</summary>
public readonly record struct VerseWord(
    string Surface,
    string Trailer,
    string? Lemma = null,
    string? Strong = null,
    string? Morphology = null,
    string? Gloss = null);

/// <summary>
/// Hashes one verse's words as they are added, in print order. Fields are joined by the unit
/// separator and words closed by the record separator, so that a field moving into the next one or
/// a word boundary moving changes the bytes; a null field is the NUL byte, which no field of the
/// corpus contains, so that null and empty stay apart. Morphology is hashed as the string it is
/// stored as, so the producer writes it in one canonical shape.
/// </summary>
public sealed class VerseHasher : IDisposable
{
    private const byte Unit = 0x1f;
    private const byte Record = 0x1e;
    private const byte Null = 0x00;

    private readonly IncrementalHash _text = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly IncrementalHash _annotation = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private byte[] _buffer = new byte[256];

    public void Add(in VerseWord word)
    {
        Append(_text, word.Surface);
        Separate(_text, Unit);
        Append(_text, word.Trailer);
        Separate(_text, Record);

        Append(_annotation, word.Lemma);
        Separate(_annotation, Unit);
        Append(_annotation, word.Strong);
        Separate(_annotation, Unit);
        Append(_annotation, word.Morphology);
        Separate(_annotation, Unit);
        Append(_annotation, word.Gloss);
        Separate(_annotation, Record);
    }

    /// <summary>The hashes of the words added so far, and the start of the next verse.</summary>
    public VerseHashes Finish()
    {
        Span<byte> digest = stackalloc byte[32];
        _text.GetHashAndReset(digest);
        var text = Convert.ToHexStringLower(digest[..(VerseHashes.Length / 2)]);
        _annotation.GetHashAndReset(digest);
        var annotation = Convert.ToHexStringLower(digest[..(VerseHashes.Length / 2)]);
        return new VerseHashes(text, annotation);
    }

    private void Append(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            Separate(hash, Null);
            return;
        }

        var size = Encoding.UTF8.GetMaxByteCount(value.Length);
        if (_buffer.Length < size)
        {
            _buffer = new byte[Math.Max(size, _buffer.Length * 2)];
        }

        var written = Encoding.UTF8.GetBytes(value, _buffer);
        hash.AppendData(_buffer, 0, written);
    }

    private static void Separate(IncrementalHash hash, byte separator)
    {
        ReadOnlySpan<byte> one = [separator];
        hash.AppendData(one);
    }

    public void Dispose()
    {
        _text.Dispose();
        _annotation.Dispose();
    }
}
