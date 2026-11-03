using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// A text's frame file lists only the verses the frame moves, a run of consecutive verses with one shift
/// as one line, and reads back as exactly the addresses it was written from.
/// </summary>
public sealed class FrameFileTests
{
    private static (VerseKey, IReadOnlyList<VerseKey>) Verse(string own, params string[] canonical) =>
        (VerseKey.Parse(own), [.. canonical.Select(key => VerseKey.Parse(key))]);

    [Fact]
    public void APsalmNumberedFromItsTitleIsOneLine()
    {
        var verses = Enumerable.Range(1, 9).Select(verse => Verse($"19.3:{verse}", $"19.3:{verse - 1}"))
            .Append(Verse("19.4:1", "19.4:1"))
            .ToList();

        var rows = FrameFile.Rows(verses);

        rows.Should().ContainSingle();
        rows[0].Should().BeEquivalentTo(new FrameRow(VerseKey.Parse("19.3:1"), 9, [VerseKey.Parse("19.3:0")], "title"));
        rows[0].Verses().Should().BeEquivalentTo(verses.Take(9), options => options.WithStrictOrdering());
    }

    [Fact]
    public void ARunKeepsItsLetterAndStopsAtAnother()
    {
        var rows = FrameFile.Rows([
            Verse("17.1:1a", "17.11:2"), Verse("17.1:2a", "17.11:3"), Verse("17.1:3", "17.11:4"),
            Verse("2.36:8", "2.36:8", "2.39:1"), Verse("2.36:9", "2.36:9", "2.39:2"),
        ]);

        rows.Select(row => (row.Own.ToString(), row.Through, row.Relation)).Should().Equal(
            ("2.36:8", 9, "spans"), ("17.1:1a", 2, "moved"), ("17.1:3", null, "moved"));
    }

    [Fact]
    public void TwoVersesAtOneAddressShareIt()
    {
        var rows = FrameFile.Rows([Verse("76.1:42", "76.1:43"), Verse("76.1:43", "76.1:43")]);

        rows.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FrameRow(VerseKey.Parse("76.1:42"), null, [VerseKey.Parse("76.1:43")], "shares"));
    }

    [Fact]
    public void TheFileReadsBackAsTheVersesItWasWrittenFrom()
    {
        var folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProductStore(folder);
            var rows = FrameFile.Rows([
                Verse("19.9:22", "19.10:1"), Verse("19.9:23", "19.10:2"), Verse("19.12:6", "19.13:5", "19.13:6"),
                Verse("41.9:43-44", "41.9:43", "41.9:44"),
            ]);
            using (var writer = store.Create(FrameFile.Product("TEST"), ProductKind.Machine, FrameRow.Schema, "test", []))
            {
                writer.WriteAll(rows);
            }

            using var reader = store.Open(FrameFile.Product("TEST"), FrameRow.Schema);
            reader.ReadAll().Should().BeEquivalentTo(rows, options => options.WithStrictOrdering());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AVerseTheFileDoesNotListStandsAtItsOwnAddress()
    {
        PlacedVerse[] verses = [new(1, 19, 9, 22, string.Empty, 0), new(2, 19, 9, 21, string.Empty, 0), new(3, 1, 31, 50, "a", 0)];

        CanonicalFrameLoader.Drafts(verses, FrameFile.Rows([Verse("19.9:22", "19.10:1"), Verse("1.31:50a", "1.31:51")]))
            .Should().BeEquivalentTo([
                new ReferenceDraft(1, 19, 10, 1, true),
                new ReferenceDraft(2, 19, 9, 21, true),
                new ReferenceDraft(3, 1, 31, 51, true),
            ]);
    }
}
