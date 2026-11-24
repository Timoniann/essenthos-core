using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>The Peshitta and the three Targums as fetched by fetch-etcbc and fetch-sefaria-targums (TSK-0230, TSK-0231).</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SemiticWitnessesTests
{
    private static IEnumerable<VerseDraft> Verses(TextSource source) =>
        source.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses);

    private static VerseDraft Verse(TextSource source, int book, int chapter, int verse) =>
        source.Books.Single(b => b.CanonicalOrdinal == book).Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse);

    [Fact]
    public void ThePeshittaHoldsItsBooksWithTheHebrewNumbering()
    {
        var peshitta = PeshittaTextSource.Read(TestResources.Folder(PeshittaTextSource.Folder));

        (peshitta.Books.Count, Verses(peshitta).Count()).Should().Be((57, 29_503));
        Verse(peshitta, 1, 1, 1).Words.Select(word => word.Surface).Should().StartWith(["ܒܪܫܝܬ", "ܒܪܐ"]);

        // A psalm's title is not printed: Psalm 3 starts at the Hebrew's verse 2.
        peshitta.Books.Single(b => b.CanonicalOrdinal == 19).Chapters.Single(c => c.Number == 3).Verses[0].Number.Should().Be(2);

        // "Concerning Moab", the edition's verse 0, stands where the Hebrew prints it, at the head of verse 1.
        Verse(peshitta, 24, 48, 1).Words[0].Surface.Should().Be("ܥܠ");
        Verses(peshitta).Should().NotContain(verse => verse.Number == 0);

        // Psalm 151 is its own book, chapter 1; 1 Maccabees is witness B, whole to chapter 16.
        Verse(peshitta, 82, 1, 1).Words.Should().NotBeEmpty();
        peshitta.Books.Single(b => b.CanonicalOrdinal == 73).Chapters.Should().HaveCount(16);
    }

    [Fact]
    public void EachTargumHoldsItsBooksAndNothingElse()
    {
        var folder = TestResources.Folder(TargumTextSource.Folder);
        var onkelos = TargumTextSource.Read(folder, TargumTextSource.Onkelos);
        var jonathan = TargumTextSource.Read(folder, TargumTextSource.Jonathan);
        var writings = TargumTextSource.Read(folder, TargumTextSource.OfTheWritings);

        (onkelos.Books.Count, Verses(onkelos).Count()).Should().Be((5, 5_846));
        (jonathan.Books.Count, Verses(jonathan).Count()).Should().Be((21, 9_296));
        (writings.Books.Count, Verses(writings).Count()).Should().Be((8, 5_257));
        // The points stay in the order the source writes them; the letters are what this checks.
        string.Join(' ', Verse(onkelos, 1, 1, 1).Words.Select(word => string.Concat(word.Surface.Where(c => c is >= 'א' and <= 'ת'))))
            .Should().Be("בקדמין ברא יי ית שמיא וית ארעא");
    }

    [Fact]
    public void ASofPasuqGoesToTheTrailerAndMarkupIsTakenOut()
    {
        var words = TargumTextSource.Words("<small>[ת״‎א]</small> פְּתָחוּ עַמְמַיָא תַּרְעֵיכוֹן :");

        words.Select(word => word.Surface).Should().Equal("[ת״א]", "פְּתָחוּ", "עַמְמַיָא", "תַּרְעֵיכוֹן");
        words[^1].Trailer.Should().Be(":");
    }
}
