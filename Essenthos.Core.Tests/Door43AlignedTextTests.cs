using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The texts read from Door43's aligned releases on disk: every book the release holds, numbered as
/// the English numbers it, with none of the alignment left in the words.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class Door43AlignedTextTests(ITestOutputHelper output)
{
    /// <summary>A New Testament numbered as the English numbers it, give or take a verse its edition omits.</summary>
    private const int NewTestament = 7_800;

    public static TheoryData<string> Folders
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var folder in Door43TextSource.Definitions.Keys)
            {
                data.Add(folder);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void TheReleaseReadsWithItsWordsBare(string folder)
    {
        var path = TestResources.Folder(Path.Combine("Door43", folder));
        var source = Door43TextSource.Read(path);

        var verses = source.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses).ToList();
        var words = verses.SelectMany(verse => verse.Words).ToList();
        output.WriteLine($"{folder}: {source.Books.Count} books, {verses.Count} verses, {words.Count} words");

        source.Books.Should().HaveCount(Directory.GetFiles(path, "*.usfm").Length);
        source.Books.Where(book => book.CanonicalOrdinal >= 40).SelectMany(book => book.Chapters)
            .Sum(chapter => chapter.Verses.Count).Should().BeGreaterThan(NewTestament);
        // A backslash is a marker the reader left behind. A bar is not tested for: the Urdu writes its
        // full stop as one, sometimes with no space before the next word.
        words.Where(word => word.Surface.Length == 0 || $"{word.Surface}{word.Trailer}".Contains('\\'))
            .Select(word => $"[{word.Surface}][{word.Trailer}]").Take(5).Should().BeEmpty();
    }

    [Theory]
    [InlineData("bn_irv", "যীশু")]
    [InlineData("as_irv", "যীচু")]
    public void TheIndianRevisedVersionNamesJesus(string folder, string jesus) =>
        Door43TextSource.Read(TestResources.Folder(Path.Combine("Door43", folder))).Books
            .SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words)
            .Should().Contain(word => word.Surface == jesus);
}
