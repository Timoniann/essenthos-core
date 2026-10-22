using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The verse search a visitor quoting from memory needs: the forms of a word, the joints left out or
/// spelled the other way, the verse holding the words together first, the verse holding most of them
/// when none holds all, and a near spelling offered rather than searched for.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class VerseSearchTests : IAsyncLifetime
{
    private readonly AppDbContext _db;
    private readonly Text _ohienko;
    private readonly Text _english;

    public VerseSearchTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");

        _ohienko = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr",
            (1, 1, ["Промовив", "до", "неї", "Ісус", "Я", "воскресення", "й", "життя"]),
            (1, 2, ["і", "вийдуть", "на", "воскресення", "життя"]),
            (1, 3, ["в", "день", "воскресення", "мертвих"]),
            (1, 4, ["і", "воскресіння", "буде"]),
            (1, 5, ["коли", "хто", "не", "хоче", "працювати", "нехай", "не", "їсть"]),
            (1, 6, ["той", "хто", "сіє"]),
            (1, 7, ["життя", "і", "воскресення", "дав", "Він"]),
            (1, 8, ["життям", "вічним"]),
            (1, 9, ["вийшли", "з", "Рамесесу"]),
            (1, 10, ["і", "прийшли", "до", "Рамесес"]),
            (1, 11, ["їсть", "хліб"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["God", "so", "loved", "the", "world"]),
            (1, 2, ["love", "thy", "neighbour"]),
            (1, 3, ["a", "garden"]));
        _db.SaveChanges();

        foreach (var word in _db.Words.ToList())
        {
            word.NormalisedText = word.Surface.ToLowerInvariant();
        }

        _db.SaveChanges();
    }

    public async Task InitializeAsync() =>
        await new SearchFormLoader(_db, NullLogger<SearchFormLoader>.Instance).Load();

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private Task<SearchResultsResponse> Search(Text text, string query, int skip = 0) =>
        VerseSearch.Search(
            _db, text.Id, text.Slug, text.Language, TypedQuery.Parse(query), new SearchScope(null, null, null),
            SearchWidening.AsFarAsNeeded, skip, 20, CancellationToken.None);

    private static List<int> Verses(SearchResultsResponse found) => [.. found.Items.Select(hit => hit.Verse)];

    [Fact]
    public async Task AWordFindsItsOtherForms()
    {
        (await Search(_ohienko, "життя")).Items.Select(hit => hit.Verse).Should().BeEquivalentTo([1, 2, 7, 8]);
        Verses(await Search(_english, "loved")).Should().BeEquivalentTo([1, 2], "Snowball reads loved and love as one word");
    }

    [Fact]
    public async Task TheWordsSideBySideComeFirstAndAJointIsSpelledEitherWay()
    {
        var found = await Search(_ohienko, "воскресення і життя");

        Verses(found).Should().Equal(1, 2, 7);
        found.Items[0].Match.Should().Be(VerseSearch.Phrase, "the text's «й» is the reader's «і»");
        found.Items[1].Match.Should().Be(VerseSearch.Every);
        found.Terms.Single(term => term.Text == "і").Optional.Should().BeTrue();
    }

    [Fact]
    public async Task NotIsNeverLeftOut() =>
        Verses(await Search(_ohienko, "не їсть")).Should().Equal(5);

    [Fact]
    public async Task WhenNoVerseHoldsEveryWordTheVerseHoldingMostIsOfferedAsOnlyLikeIt()
    {
        var found = await Search(_ohienko, "хто не працює, той нехай і не їсть");

        found.Partial.Should().BeTrue();
        Verses(found).Should().Equal(5);
        found.Items.Should().OnlyContain(hit => hit.Match == VerseSearch.Most);
    }

    [Fact]
    public async Task AQuotedWordIsTheFormTyped()
    {
        Verses(await Search(_ohienko, "\"життя\"")).Should().BeEquivalentTo([1, 2, 7]);
        Verses(await Search(_ohienko, "«воскресення життя»")).Should().Equal(2);
    }

    [Fact]
    public async Task ANearSpellingIsOfferedNotSearched()
    {
        var found = await Search(_ohienko, "воскресіння і життя");

        Verses(found).Should().BeEmpty("no verse holds воскресіння beside життя, and none is matched loosely");
        found.Suggestion.Should().Be("воскресення і життя");
        found.Terms.Single(term => term.Text == "воскресіння").Instead.Should().Be("воскресення");
    }

    [Fact]
    public async Task ANameTheTextDoesNotPrintIsAnsweredWithTheOneItDoes()
    {
        var found = await Search(_ohienko, "Рамсес");

        found.Total.Should().Be(0);
        found.Suggestion.Should().StartWith("Рамесес", "the capital typed is kept");
    }

    [Fact]
    public async Task AWordPrintedOftenEnoughIsNotSecondGuessed() =>
        (await Search(_ohienko, "воскресення")).Suggestion.Should().BeNull();

    [Fact]
    public async Task TheFormsAreCountedOnceAndASecondCountWritesNothing()
    {
        var again = await new SearchFormLoader(_db, NullLogger<SearchFormLoader>.Instance).Load();

        again.Written.Should().Be(0);
        again.Removed.Should().Be(0);
        (await _db.TextForms.Where(f => f.TextId == _ohienko.Id && f.Form == "воскресення").SingleAsync())
            .Occurrences.Should().Be(4);
    }

    [Theory]
    [InlineData("«воскресення життя» Ісус", new[] { "воскресення", "життя", "Ісус" }, new[] { 0, 0, -1 })]
    [InlineData("хто не працює, той", new[] { "хто", "не", "працює", "той" }, new[] { -1, -1, -1, -1 })]
    public void AQueryIsReadIntoWordsAndPhrases(string query, string[] words, int[] phrases)
    {
        var terms = TypedQuery.Parse(query);

        terms.Select(term => term.Text).Should().Equal(words);
        terms.Select(term => term.Phrase ?? -1).Should().Equal(phrases);
    }

    [Fact]
    public void TheRewrittenQueryKeepsItsQuotationMarks() =>
        VerseSearch.Rewritten(TypedQuery.Parse("«воскресіння і» життя"), [new NearSpelling("воскресіння", "воскресення")])
            .Should().Be("\"воскресення і\" життя");
}
