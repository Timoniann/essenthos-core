using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// Row ids depend on what the row is, so two builds of the same files number every row alike and a release's difference
/// carries only the rows a change touched: a verse's id is its text, book, chapter and place in the chapter; a record's id
/// keeps slug order and moves only near a slug added beside it; anything else is a hash of its key.
/// </summary>
public sealed class RowIdsTests
{
    [Fact]
    public void AVersesIdIsItsTextBookChapterAndPlaceAndOrdersAsTheKeysDo()
    {
        RowIds.Verse(1, 1, 1, 0).Should().Be((((1 << 7 | 1) << 8 | 1) << 9) | 0);
        RowIds.Chapter(3, 40, 5).Should().Be(RowIds.Verse(3, 40, 5, 0) >> 9);
        RowIds.Book(3, 40).Should().Be(RowIds.Chapter(3, 40, 5) >> 8);

        int[] ordered =
        [
            RowIds.Verse(1, 1, 1, 0), RowIds.Verse(1, 1, 1, 511), RowIds.Verse(1, 1, 2, 0), RowIds.Verse(1, 2, 0, 0),
            RowIds.Verse(1, 93, 151, 175), RowIds.Verse(2, 1, 1, 0), RowIds.Verse(RowIds.Texts, RowIds.Books, RowIds.Chapters, 511),
        ];
        ordered.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems().And.OnlyContain(id => id > 0);
    }

    [Fact]
    public void AnAddressThatDoesNotFitIsRefusedRatherThanWrapped()
    {
        FluentActions.Invoking(() => RowIds.Verse(RowIds.Texts + 1, 1, 1, 0)).Should().Throw<InvalidOperationException>().WithMessage("*text id*");
        FluentActions.Invoking(() => RowIds.Verse(1, 128, 1, 0)).Should().Throw<InvalidOperationException>().WithMessage("*book ordinal*");
        FluentActions.Invoking(() => RowIds.Verse(1, 1, 256, 0)).Should().Throw<InvalidOperationException>().WithMessage("*chapter*");
        FluentActions.Invoking(() => RowIds.Verse(1, 1, 1, RowIds.VersesPerChapter)).Should().Throw<InvalidOperationException>().WithMessage("*place*");
    }

    [Fact]
    public void ARecordsIdKeepsSlugOrderAndASlugAddedMovesOnlyItsNeighbours()
    {
        string[] slugs = ["aaron", "abel", "abraham", "adam", "adam-2", "zechariah-1", "zechariah-10", "zechariah-2", "zion"];
        var ids = RowIds.Ordered(slugs);
        ids.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems().And.OnlyContain(id => id > 0);
        RowIds.Ordered(slugs).Should().Equal(ids, "the slugs are the identity");

        string[] added = ["aaron", "abel", "abiathar", "abraham", "adam", "adam-2", "zechariah-1", "zechariah-10", "zechariah-11", "zechariah-2", "zion"];
        var after = RowIds.Ordered(added);
        var before = slugs.Zip(ids).ToDictionary(pair => pair.First, pair => pair.Second);
        var moved = added.Zip(after).Where(pair => before.TryGetValue(pair.First, out var id) && id != pair.Second).Select(pair => pair.First).ToList();
        moved.Should().BeSubsetOf(["zechariah-2"], "only a slug spread to the same place as the one added moves, never every slug after it");
    }

    [Fact]
    public void AHashedIdIsTheKeysOwnAndAKeyThatMeetsAnotherTakesTheNextAttempt()
    {
        var first = new RowIds.Hashed(20);
        var again = new RowIds.Hashed(20);
        first.Of("KJV-BHSA|1.1:1|1.1:1").Should().Be(again.Of("KJV-BHSA|1.1:1|1.1:1")).And.Be(RowIds.Hash("KJV-BHSA|1.1:1|1.1:1", 20));

        var taken = RowIds.Hash("a source", 20);
        var held = new RowIds.Hashed(20, id => id == taken);
        held.Of("a source").Should().Be(RowIds.Hash("a source", 20, 1), "a held id is never given again");
        held.Of("a source").Should().NotBe(RowIds.Hash("a source", 20, 1), "the same key asked twice is two rows").And.BeInRange(1, (1 << 20) - 1);
    }
}
