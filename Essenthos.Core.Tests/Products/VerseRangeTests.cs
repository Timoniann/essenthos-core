using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public class VerseRangeTests
{
    [Theory]
    [InlineData("1.1:1..10", "1.1:1", "1.1:10")]
    [InlineData("1.1:31..2:3", "1.1:31", "1.2:3")]
    [InlineData("1.50:26..2.1:1", "1.50:26", "2.1:1")]
    [InlineData("19.4a:1..4b:3", "19.4a:1", "19.4b:3")]
    [InlineData("41.9:43-44..45", "41.9:43-44", "41.9:45")]
    [InlineData("1.31:50..50a", "1.31:50", "1.31:50a")]
    public void ARunIsTwoKeysJoinedByTwoDotsTheSecondAsShortAsTheFirstAllows(string text, string from, string through)
    {
        var range = VerseRange.Parse(text);

        range.From.Should().Be(VerseKey.Parse(from));
        range.Through.Should().Be(VerseKey.Parse(through));
        range.ToString().Should().Be(text);
        range.Single.Should().BeFalse();
    }

    [Fact]
    public void ASingleVerseIsARunOfOneAndAHyphenIsALabelNotARun()
    {
        var single = VerseRange.Parse("1.1:1-10");

        single.Single.Should().BeTrue();
        single.From.Should().Be(new VerseKey(1, 1, 1, "-10"), "IRVBEN prints Mark 9:43-44 as one verse labelled -44");
        single.ToString().Should().Be("1.1:1-10");
        new VerseRange(VerseKey.Parse("1.1:1"), VerseKey.Parse("1.1:1")).ToString().Should().Be("1.1:1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.1:10..1")]
    [InlineData("1.1:1..")]
    [InlineData("..10")]
    [InlineData("1.1:1..a")]
    [InlineData("1.1:1..10..12")]
    [InlineData("1.1:1.. 10")]
    [InlineData("1.1:1-10..")]
    public void RefusesWhatIsNotARun(string text)
    {
        VerseRange.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void ContainsFollowsKeyOrder()
    {
        var range = VerseRange.Parse("1.1:3..2:2");

        range.Contains(VerseKey.Parse("1.1:3")).Should().BeTrue();
        range.Contains(VerseKey.Parse("1.1:31a")).Should().BeTrue();
        range.Contains(VerseKey.Parse("1.2:2")).Should().BeTrue();
        range.Contains(VerseKey.Parse("1.2:3")).Should().BeFalse();
        range.Contains(VerseKey.Parse("1.1:2")).Should().BeFalse();
        range.Contains(VerseKey.Parse("2.1:1")).Should().BeFalse();
    }

    [Fact]
    public void ARangeColumnIsTextInEveryFormat()
    {
        var schema = new ProductSchema<(string Name, VerseRange Where)>()
            .Text("name", r => r.Name)
            .VerseRange("where", r => r.Where)
            .Reads(row => (row.Text(0), row.VerseRange(1)));
        var folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var rows = new[] { ("flood", VerseRange.Parse("1.6:9..9:17")), ("call", VerseRange.Parse("1.12:1")) };
            foreach (var format in new IProductFormat[] { Essenthos.Core.Products.Tsv.TsvFormat.Instance, Essenthos.Core.Products.Esw.EswFormat.Instance })
            {
                var path = Path.Combine(folder, "ranges" + format.Extension);
                using (var writer = format.Create(path, new ProductHeader("Essenthos/test/ranges" + format.Extension, [], "test"), schema))
                {
                    writer.WriteAll(rows);
                }

                using var reader = format.Open(path, schema);
                reader.Columns[1].Type.Should().Be(ColumnType.Text);
                reader.ReadAll().Should().Equal(rows);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
