using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a lexicon card says about a page of numbers at once: how often each stands in the corpus,
/// and the commonest phrases a translation puts there. Asked of Postgres, because the renderings
/// are one ranked SQL query and the claim is that answering forty numbers together gives each of
/// them what the entry page gives it alone.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongCardTests : IDisposable
{
    private const string God = "H430";
    private const string Create = "H1254";
    private const string Love = "G26";

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _hebrew;
    private readonly Text _english;
    private readonly Text _greek;

    public StrongCardTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["בָּרָא", "אֱלֹהִים", "אֱלֹהֶיךָ"]),
            (1, 2, ["אֱלֹהִים"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "en",
            (1, 1, ["created", "God", "thy", "God"]),
            (1, 2, ["God"]));

        // A witness that carries the number and is joined to nothing the King James reads.
        _greek = Corpus.Add(_db, "lxx", TextKind.Translation, "grc", (1, 1, ["θεός"]));
        _db.SaveChanges();

        Tag(_hebrew, 1, 1, 1, Create);
        Tag(_hebrew, 1, 1, 2, God);
        Tag(_hebrew, 1, 1, 3, God);
        Tag(_hebrew, 1, 2, 1, God);
        Tag(_greek, 1, 1, 1, God);

        Renders((1, 1, [1]), (1, 1, [1]));
        Renders((1, 1, [2]), (1, 1, [2]));
        Renders((1, 1, [3]), (1, 1, [3, 4]));
        Renders((1, 2, [1]), (1, 2, [1]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// A link is counted as the phrase it renders, commonest first: <em>god</em> twice and <em>thy
    /// god</em> once, never <em>thy</em> on its own.
    /// </summary>
    [Fact]
    public async Task EachNumberGetsItsOwnPhrasesCommonestFirst()
    {
        var renderings = await StrongEndpoints.Renderings(_db, [God, Create, Love], _english.Id, 3, default);

        renderings[God].Should().Equal(
            new StrongRenderingResponse("god", 2),
            new StrongRenderingResponse("thy god", 1));
        renderings[Create].Should().Equal(new StrongRenderingResponse("created", 1));
    }

    [Fact]
    public async Task ANumberTheTranslationRendersNowhereHasNoPhrases()
    {
        var renderings = await StrongEndpoints.Renderings(_db, [God, Love], _english.Id, 3, default);

        renderings.Should().NotContainKey(Love);
    }

    [Fact]
    public async Task TheLimitIsPerNumberNotPerPage()
    {
        var renderings = await StrongEndpoints.Renderings(_db, [God, Create], _english.Id, 1, default);

        renderings[God].Should().Equal(new StrongRenderingResponse("god", 2));
        renderings[Create].Should().Equal(new StrongRenderingResponse("created", 1));
    }

    /// <summary>
    /// A page of numbers answers each of them exactly as asking for it alone does, which is what the
    /// entry page does — so a card and the page it opens cannot disagree.
    /// </summary>
    [Fact]
    public async Task APageAnswersEachNumberAsTheEntryPageDoes()
    {
        var together = await StrongEndpoints.Renderings(_db, [God, Create], _english.Id, 3, default);

        foreach (var number in new[] { God, Create })
        {
            var alone = await StrongEndpoints.Renderings(_db, [number], _english.Id, 3, default);
            together[number].Should().Equal(alone[number]);
        }
    }

    /// <summary>
    /// Occurrences count every witness that tags the number, whether or not the translation quoted
    /// on the card is joined to it.
    /// </summary>
    [Fact]
    public async Task OccurrencesCountEveryWitness()
    {
        var occurrences = await StrongEndpoints.Occurrences(_db, [God, Create, Love], default);

        occurrences[God].Should().Be(4);
        occurrences[Create].Should().Be(1);
        occurrences.Should().NotContainKey(Love);
    }

    /// <summary>
    /// The load counts every number at once, from the links rather than from the words, and each
    /// number comes out as a page of them would count it.
    /// </summary>
    [Fact]
    public async Task CountingEveryNumberAnswersAsCountingAPage()
    {
        var every = await StrongRenderingCounts.Count(_db, _english.Id, null, 3, default);
        var page = await StrongRenderingCounts.Count(_db, _english.Id, [God, Create, Love], 3, default);

        every.Should().Equal(page);
    }

    /// <summary>
    /// A text the load has counted is read from what it counted, and a list deeper than the load
    /// keeps is still counted as it is asked.
    /// </summary>
    [Fact]
    public async Task ACountedTextIsReadFromWhatTheLoadCounted()
    {
        _db.StrongRenderings.Add(new StrongRendering
        {
            StrongNumber = God, TextId = _english.Id, Rank = 1, Phrase = "as the load counted it", Uses = 9,
        });
        await _db.SaveChangesAsync();

        var kept = await StrongEndpoints.Renderings(_db, [God, Create], _english.Id, StrongRenderingCounts.CardRenderings, default);
        kept[God].Should().Equal(new StrongRenderingResponse("as the load counted it", 9));
        kept.Should().NotContainKey(Create);

        var deeper = await StrongEndpoints.Renderings(_db, [God], _english.Id, StrongRenderingCounts.CardRenderings + 1, default);
        deeper[God][0].Should().Be(new StrongRenderingResponse("god", 2));
    }

    [Fact]
    public async Task ATextsNeighboursAreTheTextsLinkedToItEitherWay()
    {
        (await LinkedOriginals.Neighbours(_db, _english.Id, default)).Should().Equal(_hebrew.Id);
        (await LinkedOriginals.Neighbours(_db, _hebrew.Id, default)).Should().Equal(_english.Id);
        (await LinkedOriginals.Neighbours(_db, _greek.Id, default)).Should().BeEmpty();
    }

    private void Tag(Text text, int chapter, int verse, int position, string number) =>
        _db.WordAt(text, chapter, verse, position).StrongNumber = number;

    private void Renders(
        (int Chapter, int Verse, int[] Positions) hebrew,
        (int Chapter, int Verse, int[] Positions) english)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);

        foreach (var position in english.Positions)
        {
            _db.LinkWords.Add(new LinkWord
            {
                Link = link,
                Word = _db.WordAt(_english, english.Chapter, english.Verse, position),
                Side = LinkSide.From,
            });
        }

        foreach (var position in hebrew.Positions)
        {
            _db.LinkWords.Add(new LinkWord
            {
                Link = link,
                Word = _db.WordAt(_hebrew, hebrew.Chapter, hebrew.Verse, position),
                Side = LinkSide.To,
            });
        }
    }
}
