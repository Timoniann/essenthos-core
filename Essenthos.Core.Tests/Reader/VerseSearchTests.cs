using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Indexes;
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
            (1, 11, ["їсть", "хліб"]),
            (1, 12, ["освітив", "землю"]),
            (1, 13, ["світильник", "горить"]),
            (1, 14, ["щоб", "світити", "на", "землю"]),
            (1, 15, ["світу", "цього"]),
            (1, 16, ["Бог", "так", "полюбив", "світ"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["God", "so", "loved", "the", "world"]),
            (1, 2, ["love", "thy", "neighbour"]),
            (1, 3, ["a", "garden"]),
            (1, 4, ["delight", "thyself"]),
            (1, 5, ["lightning", "came"]),
            (1, 6, ["lights", "in", "the", "firmament"]),
            (1, 7, ["the", "light", "shineth"]));
        _db.SaveChanges();

        foreach (var word in _db.Words.ToList())
        {
            word.NormalisedText = word.Surface.ToLowerInvariant();
        }

        _db.SaveChanges();
    }

    /// <summary>
    /// <c>text_form</c> as the build fills it from each text's forms index (<see cref="TextIndexes.Forms"/>,
    /// <see cref="IndexLoad"/>): every form the text prints with how often, stemmed here or by the database's dictionary.
    /// </summary>
    public async Task InitializeAsync()
    {
        var texts = await _db.Texts.AsNoTracking().ToListAsync();
        foreach (var text in texts)
        {
            var forms = await _db.Words.Where(w => w.TextId == text.Id && w.NormalisedText != null && w.NormalisedText != "")
                .GroupBy(w => w.NormalisedText!).Select(g => new { Form = g.Key, Occurrences = g.Count() }).ToListAsync();
            _db.TextForms.AddRange(forms.Select(form => new TextForm
            {
                TextId = text.Id,
                Form = form.Form,
                Occurrences = form.Occurrences,
                Stem = TextIndexes.Stem(form.Form, text.Language) ?? form.Form,
            }));
        }

        await _db.SaveChangesAsync();
        foreach (var language in texts.Select(text => text.Language).Distinct())
        {
            if (SearchWords.Dictionary(language) is { } dictionary)
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE text_form f SET stem = coalesce(nullif((ts_lexize({0}::regdictionary, f.form))[1], ''), f.form) "
                    + "FROM text t WHERE t.id = f.text_id AND t.language = {1}",
                    dictionary, language);
            }
        }
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    private async Task<SearchResultsResponse> Search(
        Text text, string query, int skip = 0, SearchWidening widening = SearchWidening.AsFarAsNeeded) =>
        await VerseSearch.Search(
            _db, text.Id, text.Slug, text.Language, TypedQuery.Parse(query), new SearchScope(null, null, null),
            widening, skip, 20, CancellationToken.None,
            text.Language is "ukr" or "rus" ? new FormIndex(text.Language, await WordForms.Spellings(_db, text.Id, CancellationToken.None)) : null);

    private static List<int> Verses(SearchResultsResponse found) => [.. found.Items.Select(hit => hit.Verse)];

    /// <summary>
    /// A query of no word asks the database nothing it cannot read: every text, counted or not, answers an
    /// empty page.
    /// </summary>
    [Theory]
    [InlineData("—")]
    [InlineData("« »")]
    [InlineData("…")]
    public async Task NoWordFindsNoVerse(string query)
    {
        foreach (var text in new[] { _ohienko, _english })
        {
            var found = await Search(text, query);
            found.Total.Should().Be(0);
            found.Items.Should().BeEmpty();
            found.Terms.Should().BeEmpty();

            (await VerseSearch.Search(
                _db, text.Id, text.Slug, text.Language, [], new SearchScope(null, null, null),
                SearchWidening.AsFarAsNeeded, 0, 20, CancellationToken.None)).Total.Should().Be(0);
        }
    }

    /// <summary>
    /// A hit names its verse by the text's own number and by the shared frame's, which the reader's
    /// addresses are written in: the Hebrew Psalms run a verse ahead of the English, so a link by the
    /// own number opens the verse after, or one the frame does not have.
    /// </summary>
    [Fact]
    public async Task AHitCarriesTheFramesAddressBesideTheTextsOwnNumber()
    {
        var placed = await _db.VerseReferences.SingleAsync(r => r.Verse!.TextId == _english.Id && r.Verse.Number == 3);
        placed.CanonicalVerse = 9;
        await _db.SaveChangesAsync();

        var hit = (await Search(_english, "garden")).Items.Single();

        (hit.Chapter, hit.Verse, hit.CanonicalChapter, hit.CanonicalVerse).Should().Be((1, 3, 1, 9));
    }

    [Fact]
    public async Task AWordFindsItsOtherForms()
    {
        (await Search(_ohienko, "життя")).Items.Select(hit => hit.Verse).Should().BeEquivalentTo([1, 2, 7, 8]);
        Verses(await Search(_english, "loved")).Should().BeEquivalentTo([1, 2], "Snowball reads loved and love as one word");
    }

    /// <summary>
    /// A reader who knows part of a word finds the words holding it, after the word itself: «світ»
    /// (world) first, then its own forms, then the words beginning with it — the verb «світити» (to
    /// shine) the stemmer lands on the noun, «світильник» — and last a word with it inside.
    /// </summary>
    [Fact]
    public async Task TheWordTypedComesFirstThenItsFormsThenTheWordsHoldingIt()
    {
        var found = await Search(_ohienko, "світ");

        Verses(found).Should().Equal(16, 15, 13, 14, 12);
        found.Items[^1].Snippet.Should().Contain("<em>освітив</em>");
        found.Terms.Single().Matching.Should().Be("stemmed");
    }

    [Fact]
    public async Task AnEnglishWordRanksTheSameWay() =>
        Verses(await Search(_english, "light")).Should().Equal(7, 6, 5, 4);

    [Fact]
    public async Task WholeWordsOnlyLeavesOutTheWordsMerelyHoldingIt() =>
        Verses(await Search(_ohienko, "світ", widening: SearchWidening.WholeWordOnly)).Should().Equal(16, 15, 14);

    [Fact]
    public async Task PartOfAWordAloneIsFoundAndSaidToBe()
    {
        var found = await Search(_ohienko, "світильн");

        Verses(found).Should().Equal(13);
        found.Terms.Single().Matching.Should().Be("substring");
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
