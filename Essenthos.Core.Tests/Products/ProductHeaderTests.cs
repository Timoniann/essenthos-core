using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public class ProductHeaderTests
{
    private static readonly string Sha = new('a', 64);
    private static readonly string Sha2 = new('b', 64);

    [Fact]
    public void WritesThePlansFourLinesWithTheValuesAligned()
    {
        var header = new ProductHeader(
            "Essenthos/texts/KJV/words.tsv",
            [new ProductInput("bible4u/kjv.zip", Sha), new ProductInput("Essenthos/texts/KJV/corrections.tsv", Sha2)],
            "forge convert KJV   (Essenthos.Forge 1.0)");

        header.Format(rows: 932131).Should().Be(
            $"# product    Essenthos/texts/KJV/words.tsv\n" +
            $"# inputs     bible4u/kjv.zip@{Sha}   Essenthos/texts/KJV/corrections.tsv@{Sha2}\n" +
            $"# producer   forge convert KJV   (Essenthos.Forge 1.0)\n" +
            $"# rows             932131\n");
    }

    [Fact]
    public void ParsesWhatItWritesNotesAndFreeCommentsIncluded()
    {
        var header = new ProductHeader("Essenthos/texts/NPU2022/frame.tsv", [new ProductInput("Versification/TVTMS.txt", Sha)], "forge frame NPU2022");
        header.Notes.Add(new("tradition", "original; books: 19=septuagint"));
        header.Notes.Add(new("tests", "Psalm 9 has 39 verses; Psalm 3 has 9 verses and its first holds the title"));
        header.Comments.Add("A verse not listed stands at its own address.");
        header.Rows = 159;

        var lines = header.Format().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var parsed = ProductHeader.Parse(lines);

        parsed.Product.Should().Be(header.Product);
        parsed.Inputs.Should().Equal(header.Inputs);
        parsed.Producer.Should().Be(header.Producer);
        parsed.Rows.Should().Be(159);
        parsed.Notes.Should().Equal(header.Notes);
        parsed.Comments.Should().Equal("A verse not listed stands at its own address.");
        parsed.Format().Should().Be(header.Format());
    }

    [Fact]
    public void InputsMayWrapOverSeveralLines()
    {
        var parsed = ProductHeader.Parse(
        [
            "# product    Essenthos/mapping/KJV-BHSA/final.tsv",
            $"# inputs     Essenthos/mapping/KJV-BHSA/stated.tsv@{Sha}",
            $"# inputs     Essenthos/mapping/KJV-BHSA/aligner.tsv@{Sha2}",
            "# producer   forge merge KJV BHSA",
            "# rows       ",
        ]);

        parsed.Inputs.Select(input => input.Path).Should().Equal("Essenthos/mapping/KJV-BHSA/stated.tsv", "Essenthos/mapping/KJV-BHSA/aligner.tsv");
        parsed.Rows.Should().BeNull("the writer had not come back to it");
    }

    [Fact]
    public void TheRowsFieldIsOverwrittenInPlaceSoTheOffsetIsFixedBeforeTheRowsAreKnown()
    {
        var header = new ProductHeader("Essenthos/a/b.tsv", [], "test");
        var blank = header.Format(rows: null);
        var filled = header.Format(rows: 42);

        blank.Length.Should().Be(filled.Length);
        var offset = header.RowsOffset();
        filled.Substring(offset, ProductHeader.RowsWidth).Should().Be("          42");
        blank.Substring(offset, ProductHeader.RowsWidth).Should().Be(new string(' ', ProductHeader.RowsWidth));
    }

    [Fact]
    public void AHeaderWithoutProductOrProducerIsRefused()
    {
        var parse = () => ProductHeader.Parse(["# rows       3"]);

        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void PathsWithSpacesOrBackslashesAreRefusedSinceTheInputsLineSplitsOnSpace()
    {
        var spaced = () => new ProductHeader("Essenthos/a b.tsv", [], "test");
        var slashed = () => new ProductHeader("Essenthos/a.tsv", [new ProductInput(@"bible4u\kjv.zip", Sha)], "test");

        spaced.Should().Throw<ArgumentException>();
        slashed.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheProducerNamesTheVerbAndTheAssembly()
    {
        ProductHeader.DescribeProducer("forge convert KJV").Should().StartWith("forge convert KJV   (").And.EndWith(")");
    }
}
