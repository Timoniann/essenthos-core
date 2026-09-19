using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which books a search runs over. Three parameters narrow each other, and a combination that can
/// hold no book is refused rather than answered with nothing — an empty page and a contradiction
/// read the same to a caller.
/// </summary>
public class SearchScopeTests
{
    [Fact]
    public void AskingForNothingSearchesEverything()
    {
        var (scope, hint) = SearchScope.Resolve(null, null, null, null);

        hint.Should().BeNull();
        scope!.From.Should().BeNull();
        scope.To.Should().BeNull();
        scope.Books.Should().BeNull();
    }

    [Theory]
    [InlineData("old", 1, 39)]
    [InlineData("New", 40, 66)]
    public void ATestamentIsARangeOverTheCanonicalOrdinal(string testament, int from, int to)
    {
        var (scope, _) = SearchScope.Resolve(null, null, null, testament);

        scope!.From.Should().Be(from);
        scope.To.Should().Be(to);
        scope.Books.Should().BeNull();
    }

    /// <summary>The deuterocanon is in neither testament, so it is reached by naming its books.</summary>
    [Fact]
    public void ATestamentReachesNoDeuterocanonicalBook()
    {
        var (scope, _) = SearchScope.Resolve(null, null, null, "old");

        scope!.To.Should().Be(39);
    }

    [Fact]
    public void ARangeIsResolvedFromEitherNameOrSlug()
    {
        var (scope, hint) = SearchScope.Resolve(null, "matthew", "john", null);

        hint.Should().BeNull();
        scope!.From.Should().Be(40);
        scope.To.Should().Be(43);
    }

    [Fact]
    public void ARangeAndATestamentNarrowEachOther()
    {
        var (scope, _) = SearchScope.Resolve(null, "genesis", null, "new");

        scope!.From.Should().Be(40, "the testament's lower bound is the tighter of the two");
        scope.To.Should().Be(66);
    }

    [Fact]
    public void BooksAreNamedOneByOneAndKeptInTheOrderTheyWereAsked()
    {
        var (scope, hint) = SearchScope.Resolve("psalms, 1-samuel ,psalms", null, null, null);

        hint.Should().BeNull();
        scope!.Books.Should().Equal(19, 9);
        scope.From.Should().BeNull();
        scope.To.Should().BeNull();
    }

    [Fact]
    public void ABookOutsideTheTestamentIsDroppedFromTheList()
    {
        var (scope, _) = SearchScope.Resolve("genesis,matthew", null, null, "new");

        scope!.Books.Should().Equal(40);
    }

    [Fact]
    public void ACombinationNoBookSatisfiesIsRefusedRatherThanAnsweredEmpty()
    {
        var (scope, hint) = SearchScope.Resolve("genesis", null, null, "new");

        scope.Should().BeNull();
        hint.Should().Contain("narrow each other");
    }

    [Fact]
    public void ARangeThatRunsBackwardsIsRefused()
    {
        var (scope, hint) = SearchScope.Resolve(null, "john", "matthew", null);

        scope.Should().BeNull();
        hint.Should().Contain("canonical order");
    }

    [Theory]
    [InlineData("gospels", null, null)]
    [InlineData(null, "gospel-of-luke", null)]
    [InlineData(null, null, "middle")]
    public void AnUnknownBookOrTestamentSaysWhatIsAccepted(string? books, string? from, string? testament)
    {
        var (scope, hint) = SearchScope.Resolve(books, from, null, testament);

        scope.Should().BeNull();
        hint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TheWideningIsAskedForRatherThanArrivedAt()
    {
        SearchScope.ResolveWidening(null).Should().Be((SearchWidening.AsFarAsNeeded, (string?)null));
        SearchScope.ResolveWidening("whole").Should().Be((SearchWidening.WholeWordOnly, (string?)null));
        SearchScope.ResolveWidening("substring").Should().Be((SearchWidening.PartOfAWord, (string?)null));
    }

    [Fact]
    public void AnUnknownWayOfMatchingNamesTheOnesThereAre()
    {
        var (_, hint) = SearchScope.ResolveWidening("fuzzy");

        hint.Should().Contain("'whole'").And.Contain("'substring'");
    }
}
