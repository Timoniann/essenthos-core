using System.Security.Cryptography;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public sealed class InputHasherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public InputHasherTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bible4u"));
        Directory.CreateDirectory(Path.Combine(_root, "Nestle1904", "sub"));
        File.WriteAllText(Path.Combine(_root, "bible4u", "kjv.xml"), "<kjv/>");
        File.WriteAllText(Path.Combine(_root, "Nestle1904", "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "Nestle1904", "sub", "b.txt"), "b");
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void AFilesHashIsItsSha256AndItsPathIsWrittenWithForwardSlashes()
    {
        var hasher = new InputHasher(_root);

        var input = hasher.Input(@"bible4u\kjv.xml");

        input.Should().Be(new ProductInput("bible4u/kjv.xml", Sha256("<kjv/>")));
        hasher.Hashed.Should().Be(1);
    }

    [Fact]
    public void AFileWithTheSameSizeAndWriteTimeIsNotReadAgainAndRehashReadsIt()
    {
        var path = Path.Combine(_root, "bible4u", "kjv.xml");
        var written = File.GetLastWriteTimeUtc(path);
        var first = new InputHasher(_root);
        var before = first.Hash("bible4u/kjv.xml");
        first.Save();

        File.WriteAllText(path, "<KJV/>");
        File.SetLastWriteTimeUtc(path, written);

        new InputHasher(_root).Hash("bible4u/kjv.xml").Should().Be(before, "the memory says nothing moved");
        var rehash = new InputHasher(_root) { Rehash = true };
        rehash.Hash("bible4u/kjv.xml").Should().Be(Sha256("<KJV/>"));
        rehash.Hashed.Should().Be(1);
    }

    [Fact]
    public void AFileWhoseSizeOrWriteTimeMovedIsReadAgain()
    {
        var path = Path.Combine(_root, "bible4u", "kjv.xml");
        var first = new InputHasher(_root);
        first.Hash("bible4u/kjv.xml");
        first.Save();

        File.WriteAllText(path, "<kjv></kjv>");
        var second = new InputHasher(_root);

        second.Hash("bible4u/kjv.xml").Should().Be(Sha256("<kjv></kjv>"));
        second.Hashed.Should().Be(1);
    }

    [Fact]
    public void TheMemoryIsAProductFileUnderBuild()
    {
        var hasher = new InputHasher(_root);
        hasher.Hash("bible4u/kjv.xml");
        hasher.Save();

        var cache = Path.Combine(_root, "Essenthos", "build", "hashes.tsv");
        File.Exists(cache).Should().BeTrue();
        File.ReadAllText(cache).Should().Contain("\nbible4u/kjv.xml\t6\t");
    }

    [Fact]
    public void AFolderHashesAsItsFilesAndMovesWhenOneIsChangedAddedOrRemoved()
    {
        var hasher = new InputHasher(_root);
        var before = hasher.Hash("Nestle1904");

        File.WriteAllText(Path.Combine(_root, "Nestle1904", "sub", "b.txt"), "B");
        var changed = new InputHasher(_root).Hash("Nestle1904");
        File.WriteAllText(Path.Combine(_root, "Nestle1904", "c.txt"), "c");
        var added = new InputHasher(_root).Hash("Nestle1904");
        File.Delete(Path.Combine(_root, "Nestle1904", "a.txt"));
        var removed = new InputHasher(_root).Hash("Nestle1904");

        new[] { before, changed, added, removed }.Should().OnlyHaveUniqueItems();
        before.Should().Be(Sha256($"Nestle1904/a.txt\t{Sha256("a")}\nNestle1904/sub/b.txt\t{Sha256("b")}\n"));
    }

    [Fact]
    public void AnInputOutsideTheRootOrMissingIsRefused()
    {
        var hasher = new InputHasher(_root);

        var outside = () => hasher.Hash("../x");
        var missing = () => hasher.Hash("bible4u/none.xml");
        var rooted = () => hasher.Hash(_root);

        outside.Should().Throw<ArgumentException>();
        missing.Should().Throw<FileNotFoundException>();
        rooted.Should().Throw<ArgumentException>();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
