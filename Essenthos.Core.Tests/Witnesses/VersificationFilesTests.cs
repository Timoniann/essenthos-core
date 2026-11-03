using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// The versification rules that are this project's live in <c>Resources/Essenthos/versification/</c>, each
/// with what was read to write it. A rule nobody can trace back to a reading is one nobody can check.
/// </summary>
public sealed class VersificationFilesTests
{
    private const int Sirach = 72;

    private static VersificationData Data => VersificationData.Of(TestResources.Shipped("."));

    [Fact]
    public void EveryCorrectionSaysWhatWasRead()
    {
        Data.Rows(VersificationData.CorrectionsProduct,
                "scheme", "data-source", "data-standard", "source", "standard", "tests", "why")
            .Should().OnlyContain(line => line.Optional("why") != null);
    }

    [Fact]
    public void EverySupplementedPassageSaysWhatWasRead()
    {
        var lines = Data.Rows(VersificationData.SupplementsProduct,
            "passage", "scheme", "tradition", "joins", "source", "standard", "tests", "why");

        lines.GroupBy(line => (line["scheme"], line["passage"]))
            .Should().OnlyContain(passage => passage.First().Optional("why") != null);
        lines.Select(line => (line["scheme"], line["passage"])).Distinct().Count()
            .Should().Be(Data.Supplements.Count);
    }

    [Fact]
    public void EveryEditionReadingSaysWhatWasRead()
    {
        Data.Rows(VersificationData.EditionReadingsProduct, "edition", "source", "standard", "tests", "why")
            .GroupBy(line => line["edition"])
            .Should().OnlyContain(edition => edition.First().Optional("why") != null);
    }

    [Fact]
    public void EveryBookTraditionSaysWhatWasRead()
    {
        Data.Rows(VersificationData.BookTraditionsProduct, "text", "book", "tradition", "why")
            .Should().OnlyContain(line => line.Optional("why") != null);
    }

    /// <summary>
    /// The Ge'ez divides Sirach 30-36 verse for verse as Swete does, in the Greek manuscripts' order, so
    /// each of its verses there stands where the verse of Swete's it prints stands. Its rows are Swete's
    /// under its own numbers and have to move with them.
    /// </summary>
    [Fact]
    [Trait(TestCategory.Name, TestCategory.Corpus)]
    public void TheGeezSirachStandsWhereSwetesVersesDo()
    {
        var additions = TestResources.Rules.Additions;
        var swete = Data.Editions.For(Shape(
            (17, 4, 1, ["", "a", "b", "c"]), (17, 1, 17, ["", "a"]), (17, 4, 2, [""])), additions)!;
        var geez = Data.Editions.For(Shape(
            (27, 3, 100, [""]), (27, 13, 1, [""])), additions)!;

        (int Geez, int Swete, int First, int Last)[] chapters =
        [
            (30, 33, 25, 40), (31, 34, 1, 31), (32, 35, 1, 26), (33, 36, 1, 13), (34, 31, 1, 31),
            (35, 32, 1, 24), (36, 33, 1, 16), (36, 36, 17, 31),
        ];
        foreach (var (geezChapter, sweteChapter, first, last) in chapters)
        {
            for (var verse = first; verse <= last; verse++)
            {
                var own = new CanonicalReference(Sirach, sweteChapter, verse);
                var expected = swete.GetValueOrDefault(new PrintedAddress(own, string.Empty))
                               ?? swete.GetValueOrDefault(new PrintedAddress(own, "a"))
                               ?? [own];
                geez[new PrintedAddress(new CanonicalReference(Sirach, geezChapter, verse), string.Empty)]
                    .Should().Equal(expected, $"the Ge'ez Sirach {geezChapter}:{verse} prints Swete's {sweteChapter}:{verse}");
            }
        }
    }

    private static EditionShape Shape(params (int Book, int Chapter, int Verse, string[] Labels)[] verses) =>
        EditionShape.Of(verses.SelectMany(verse => verse.Labels.Select(label =>
            (verse.Book, verse.Chapter, verse.Verse, label, 10))));
}
