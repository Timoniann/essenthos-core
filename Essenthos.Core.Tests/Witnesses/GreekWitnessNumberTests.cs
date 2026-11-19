using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// Joda of Luke 3:26, whom the critical editions tag with the number of the land of Judah: each edition's corrections file
/// (<c>texts/&lt;SLUG&gt;/corrections.tsv</c>) reads him under the man's number, and the file's row still finds the word and
/// the number the edition prints.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class GreekWitnessNumberReadingTests
{
    private const int Luke = 42;

    [Fact]
    public void EveryCriticalEditionReadsJodaAsTheMan()
    {
        var store = new ProductStore(TestResources.Shipped("."));
        foreach (var edition in new[]
                 {
                     NestleTextSource.Read(TestResources.Nestle1904),
                     TischendorfTextSource.Read(TestResources.TischendorfFolder),
                     WestcottHortTextSource.Read(TestResources.WestcottHortFolder),
                 })
        {
            var slug = edition.Definition.Slug;
            var row = TextCorrections.Read(store, slug).Rows.Should().ContainSingle(r => r.Kind == TextCorrection.Strong, slug).Which;
            row.Own.Should().Be("42.3:26.10");
            row.After.Should().Be("G2455");

            var luke = edition.Books.Single(book => book.CanonicalOrdinal == Luke);
            var joda = Words(luke, 3, 26).Where(word => WordFolding.Fold(word.Surface, "grc") == "ιωδα").Should().ContainSingle(slug).Which;
            row.Before.Should().Be($"{joda.Surface} G2448", slug);
            Words(luke, 1, 39).Select(word => word.StrongNumber).Should().Contain("G2448", slug);
        }
    }

    private static IEnumerable<WordDraft> Words(BookDraft book, int chapter, int verse) =>
        book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse).Words;
}
