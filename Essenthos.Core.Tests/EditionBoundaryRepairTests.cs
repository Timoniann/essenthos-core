using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Swete;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

[Trait(TestCategory.Name, TestCategory.Corpus)]
public class EditionBoundaryRepairTests
{
    [Theory]
    [InlineData(67, 6, 1, "BAR")]
    [InlineData(72, 1, 1, "SIR")]
    [InlineData(78, 1, 1, "BEL")]
    public void ApocryphaVersesContainOnlyTheirPrintedText(int book, int chapter, int verse, string code)
    {
        var folder = TestResources.Folder(DeuterocanonTextSource.KingJamesFolder);
        var file = Directory.GetFiles(folder, "*.usfm").Single(path =>
            File.ReadAllText(path).StartsWith($"\\id {code}", StringComparison.Ordinal));
        var printed = UsfmReader.Read(File.ReadAllText(file), editorialHeadings: true);
        printed.Book.Should().Be(code);
        var expected = printed.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse);
        var source = DeuterocanonTextSource.Extend(
            Bible4uTextSource.Read(TestResources.Bible4u(Sources.KingJamesSlug), Sources.KingJamesSlug),
            TestResources.Folder(string.Empty));
        var actualBook = source.Books.Single(b => b.CanonicalOrdinal == book);
        actualBook.CanonicalOrdinal.Should().Be(book);
        var actual = actualBook.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse);
        actual.Words.Select(w => w.Surface + w.Trailer).Should()
            .Equal(expected.Words.Select(w => w.Surface + w.Trailer));
    }

    [Fact]
    public void AnEpistleSubscriptionRemainsWhenApocryphaIsExtended()
    {
        var original = Bible4uTextSource.Read(TestResources.Bible4u(Sources.KingJamesSlug), Sources.KingJamesSlug);
        var extended = DeuterocanonTextSource.Extend(original, TestResources.Folder(string.Empty));
        extended.Books.Single(b => b.CanonicalOrdinal == 57).Should()
            .BeEquivalentTo(original.Books.Single(b => b.CanonicalOrdinal == 57), options => options.Excluding(b => b.Position));
        UsfmReader.Read("\\id PHM\n\\c 1\n\\v 25 Amen.\n\\s1 Written from Rome to Philemon.")
            .Chapters.Single().Verses.Single().Words.Select(w => w.Surface).Should()
            .Equal("Amen", "Written", "from", "Rome", "to", "Philemon");
    }

    [Fact]
    public void SwetesLastVerseOfSecondSamuelNineteenHasItsOwnAddress()
    {
        var source = SweteTextSource.Read(TestResources.SweteFolder);
        var book = source.Books.Single(b => b.CanonicalOrdinal == 10);
        book.CanonicalOrdinal.Should().Be(10);
        var chapter = book.Chapters.Single(c => c.Number == 19);
        chapter.Verses.Select(v => v.Number).Should().Equal(Enumerable.Range(1, 43));
        var before = chapter.Verses.Single(v => v.Number == 42);
        var last = chapter.Verses.Single(v => v.Number == 43);
        string Text(VerseDraft value) => string.Concat(value.Words.Select(w => w.Surface + w.Trailer)).TrimEnd();
        Text(before).Should().EndWith("ἡμῖν;");
        Text(last).Should().StartWith("καὶ ἀπεκρίθη ἀνὴρ Ἰσραὴλ");
        Text(last).Should().Contain("ὑπὲρ τὸν λόγον ἀνδρὸς Ἰσραήλ.");
        var frame = TvtmsReader.Read(TestResources.Tvtms).Frame(Versification.Septuagint,
            EditionShape.Of(source.Books.SelectMany(b => b.Chapters.SelectMany(c => c.Verses.Select(v =>
                (b.CanonicalOrdinal, c.Number, v.Number, v.Label, v.Words.Sum(w => w.Surface.Length)))))));
        foreach (var number in new[] { 1, 42, 43 })
            frame.Resolve(10, 19, number).Should().Equal(new CanonicalReference(10, 19, number));
        frame.Resolve(10, 18, 33).Should().Equal(new CanonicalReference(10, 18, 33));
    }
}
