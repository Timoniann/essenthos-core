using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Open Hebrew Bible's mapping of the Chinese Union Version: that a span takes every running
/// number written after it, that a number printed after bare text belongs to that text, and that the
/// spans are laid onto the corpus's words by their letters even where the file prints more of them.
/// </summary>
public sealed class OpenHebrewCuvTests
{
    /// <summary>Genesis 1:1 and the middle of 1:2, as the file prints them.</summary>
    private const string Genesis =
        "％〈H9002＝〉起初</a>〈H7225＝c1｜2｜E70002〉<sup>S</sup></a>％〈H430＝c1｜4｜E70004〉，　神</a>"
        + "％〈H1254＝c1｜3｜E70003〉創造</a>〈H8804＝〉<sup>T</sup></a>〈H853＝c1｜5｜E70005〉<sup>[S]</sup></a>"
        + "％〈H8415＝c3｜23｜E70016〉，淵</a>面〈H5921＝c3｜21｜E70014〉<sup>[S]</sup></a>〈H6440＝c3｜22｜E70015〉<sup>S</sup></a>上。";

    [Fact]
    public void ASpanTakesEveryNumberWrittenAfterIt()
    {
        var runs = OpenHebrewCuvMapping.Runs(Genesis);

        runs.Select(run => run.Text).Should().Equal("起初", "，　神", "創造", "，淵", "面", "上。");
        runs[0].Positions.Should().Equal(2);
        runs[1].Positions.Should().Equal(4);
        runs[2].Positions.Should().Equal(3, 5);
        runs[4].Positions.Should().Equal(21, 22);
        runs[5].Positions.Should().BeEmpty();
    }

    [Fact]
    public void TheSpansAreLaidOntoTheWordsTheirLettersFallIn()
    {
        var runs = OpenHebrewCuvMapping.Runs(Genesis);

        var placed = OpenHebrewCuvLinkLoader.Place(runs, ["起初", "神", "創造", "淵", "面上"], out var realigned);

        realigned.Should().BeFalse();
        placed.Select(p => (string.Join(',', p.Words), string.Join(',', p.Positions))).Should().Equal(
            ("0", "2"), ("1", "4"), ("2", "3,5"), ("3", "23"), ("4", "21,22"));
    }

    /// <summary>
    /// The file prints FHL's notes inside the verse and the module beside it, so the letters differ;
    /// they are lined up first, and the note's letters carry nothing.
    /// </summary>
    [Fact]
    public void ANoteTheFilePrintsInTheVerseIsSteppedOver()
    {
        var runs = OpenHebrewCuvMapping.Runs(
            "％〈H251＝c1｜10｜E1〉他姪兒</a>（原文是弟兄）％〈H7617＝c1｜11｜E2〉被擄去</a>");

        var placed = OpenHebrewCuvLinkLoader.Place(runs, ["他姪兒", "被擄去"], out var realigned);

        realigned.Should().BeTrue();
        placed.Select(p => (string.Join(',', p.Words), string.Join(',', p.Positions))).Should().Equal(
            ("0", "10"), ("1", "11"));
    }

    /// <summary>Two spans falling in one word of ours are one rendering, with both their numbers.</summary>
    [Fact]
    public void SpansSharingAWordAreOnePlacement()
    {
        var runs = OpenHebrewCuvMapping.Runs("％〈H1＝c1｜1｜E1〉甲</a>％〈H2＝c1｜2｜E2〉乙</a>％〈H3＝c1｜3｜E3〉丙</a>");

        var placed = OpenHebrewCuvLinkLoader.Place(runs, ["甲乙", "丙"], out _);

        placed.Select(p => (string.Join(',', p.Words), string.Join(',', p.Positions))).Should().Equal(
            ("0", "1,2"), ("1", "3"));
    }
}
