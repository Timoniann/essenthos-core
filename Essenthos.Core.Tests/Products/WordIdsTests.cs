using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// A word's id is a function of its key: 52 bits of SHA-256 over the key with its text, the same in every build, below
/// 2^53 so it is an exact JSON number; keys that meet keep the id in key order and the later ones are hashed again, and
/// <c>texts/word-id-collisions</c> lists them for every reader of the words.
/// </summary>
public sealed class WordIdsTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private static WordKey Key(string key) => WordKey.Parse(key);

    [Fact]
    public void AnIdIsTheFirst52BitsOfTheKeysHash()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("KJV 1.1:1.3"));
        var expected = (long)(BinaryPrimitives.ReadUInt64BigEndian(hash) >> 12);

        WordIds.Of(Key("KJV 1.1:1.3")).Should().Be(expected);
        WordIds.Of(Key("KJV 1.1:1.3")).Should().Be(WordIds.Of(new WordKey("KJV", new VerseKey(1, 1, 1), 3)), "the key is the identity");
        WordIds.Of(Key("KJV 1.1:1.3"), 1).Should().NotBe(expected);
        WordIds.Of(Key("SWETE 17.4:17a.2")).Should().BeInRange(0, WordIds.Limit - 1).And.BeLessThan(1L << 53);
    }

    [Fact]
    public void KeysThatMeetKeepTheIdInKeyOrderAndTheRestTakeTheNextFreeAttempt()
    {
        // A hash under which every first attempt meets: 7, then 8, 9, ... by attempt.
        static long Meeting(WordKey key, int attempt) => 7 + attempt;
        var keys = new[] { Key("KJV 1.1:1.2"), Key("BHSA 1.1:1.1"), Key("KJV 1.1:1.1") };

        var rehashed = WordIds.Rehash(keys, id => id == 8, Meeting);

        rehashed.Should().Equal(
            new WordIds.Rehashed(Key("KJV 1.1:1.1"), 2, 9, Key("BHSA 1.1:1.1")),
            new WordIds.Rehashed(Key("KJV 1.1:1.2"), 3, 10, Key("BHSA 1.1:1.1")));
    }

    [Fact]
    public void NoWordTakesIdZero()
    {
        static long Zero(WordKey key, int attempt) => attempt == 0 ? 0 : 40 + attempt;

        WordIds.Rehash([Key("KJV 1.1:1.1")], _ => false, Zero).Should().Equal(new WordIds.Rehashed(Key("KJV 1.1:1.1"), 1, 41, null));
    }

    [Fact]
    public void TheCollisionsAreWrittenOnceAndReadByEveryReaderOfTheWords()
    {
        var store = new ProductStore(_resources);
        foreach (var slug in (string[])["KJV", "BHSA"])
        {
            using var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []);
            writer.WriteAll(Enumerable.Range(1, 3).Select(at =>
                new TextWord(new WordKey(slug, new VerseKey(1, 1, 1), at), $"w{at}", " ", null, null, null, null, null, null, false, null, null, null)));
        }

        var first = WordIdCollisions.Update(store);
        first.Written.Should().BeTrue();
        first.Words.Should().Be(6);
        first.Collisions.Should().BeEmpty();
        store.Save();

        WordIdCollisions.Update(new ProductStore(_resources)).Written.Should().BeFalse("no words product moved");
        WordIdTable.For(new ProductStore(_resources)).Of(Key("BHSA 1.1:1.2")).Should().Be(WordIds.Of(Key("BHSA 1.1:1.2")));

        var table = WordIdTable.Of([new WordIdCollision(Key("KJV 1.1:1.2"), 1, 42, Key("BHSA 1.1:1.1"))]);
        table.Of(Key("KJV 1.1:1.2")).Should().Be(42);
        table.Of(Key("KJV 1.1:1.3")).Should().Be(WordIds.Of(Key("KJV 1.1:1.3")));
    }
}
