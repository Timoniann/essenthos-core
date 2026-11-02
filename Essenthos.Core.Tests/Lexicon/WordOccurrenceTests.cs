using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// The two lists a pressed word leads to: every place the text prints it, and every place its original
/// stands with the phrase the text renders each by. Asked of Postgres, because the claims are that a
/// list adds up to what its breakdown counts, and that a place no link reaches stays visible as one.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WordOccurrenceTests : IDisposable
{
    private const string Agape = "G26";

    /// <summary>An inferred link has to say how sure it is; the value plays no part here.</summary>
    private const double InferredConfidence = 0.5;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _greek;
    private readonly Text _english;

    public WordOccurrenceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        _greek = Corpus.Add(_db, "NESTLE", TextKind.CriticalEdition, "grc",
            (1, 1, ["ἀγάπη", "ἀγάπη"]),
            (1, 2, ["ἀγάπην"]),
            (1, 3, ["ἀγάπης"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (1, 1, ["love", "and", "charity"]),
            (1, 2, ["thy", "love"]),
            (1, 3, ["Loved", "nothing"]));
        _db.SaveChanges();

        foreach (var word in _db.Words.Local.Where(w => w.TextId == _english.Id))
        {
            word.NormalisedText = word.Surface.ToLowerInvariant();
        }

        foreach (var word in _db.Words.Local.Where(w => w.TextId == _greek.Id))
        {
            word.StrongNumber = Agape;
        }

        Renders((1, 1, 1), (1, 1, [1]));
        Renders((1, 1, 2), (1, 1, [3]));
        Renders((1, 2, 1), (1, 2, [1, 2]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task EveryPlaceOfTheSpellingsIsListedInOrderAndCountedWhole()
    {
        var page = await RenderingEndpoints.Occurrences(_db, _english.Id, "love", ["love", "loved"], 0, 2, default);

        page.Total.Should().Be(3);
        page.Items.Select(item => (item.Verse, item.Text)).Should().Equal((1, "love"), (2, "love"));

        var next = await RenderingEndpoints.Occurrences(_db, _english.Id, "love", ["love", "loved"], 2, 2, default);
        next.Items.Single().Text.Should().Be("Loved");
    }

    [Fact]
    public async Task ASpellingTheTextDoesNotPrintIsAnEmptyList()
    {
        var page = await RenderingEndpoints.Occurrences(_db, _english.Id, "zzz", [], null, null, default);

        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    /// <summary>The phrase is the whole of what stands for the word, as the renderings count it.</summary>
    [Fact]
    public async Task EachWordOfTheOriginalCarriesThePhraseItIsRenderedBy()
    {
        var ids = Originals();

        var phrases = await StrongRenderingCounts.PhrasesOf(_db, _english.Id, ids, default);

        phrases[ids[0]].Should().Be("love");
        phrases[ids[1]].Should().Be("charity");
        phrases[ids[2]].Should().Be("thy love");
    }

    /// <summary>A word no link reaches has no phrase: nothing has said what stands for it, or that nothing does.</summary>
    [Fact]
    public async Task AWordNoLinkReachesHasNoPhraseAndIsListedAsNotRendered()
    {
        var ids = Originals();

        (await StrongRenderingCounts.PhrasesOf(_db, _english.Id, ids, default)).Should().NotContainKey(ids[3]);
        (await StrongRenderingCounts.NotRendered(_db, Agape, _english.Id, _greek.Id, default)).Should().Equal(ids[3]);
    }

    [Fact]
    public async Task APageOfNothingAsksNothing() =>
        (await StrongRenderingCounts.PhrasesOf(_db, _english.Id, [], default)).Should().BeEmpty();

    /// <summary>What a list of a word's places quotes marks every form it was listed for, in that verse.</summary>
    [Fact]
    public async Task AVerseMarksTheWordsPrintedSo()
    {
        _db.In(_english, 1);
        _db.SaveChanges();

        var verses = await VerseEndpoints.Read(
            _db, _english.Id, [1_001_001, 1_001_003], null, default, printed: ["love", "loved"]);

        verses[0].Marks.Should().Equal(new VerseMarkResponse(0, 4));
        verses[1].Marks.Should().Equal(new VerseMarkResponse(0, 5));
    }

    /// <summary>
    /// The words of the text a search counted are the words of the text: the forms are read from what
    /// the load counted where it counted, and from the words where it has not, and agree.
    /// </summary>
    [Fact]
    public async Task FormsReadFromTheCountedFormsAgreeWithTheWords()
    {
        var counted = await WordForms.Spellings(_db, _english.Id, default);
        counted.Select(form => form.Text).Should().Contain(["love", "loved", "charity"]);
        counted.Single(form => form.Text == "love").Count.Should().Be(2);

        _db.TextForms.Add(new TextForm { TextId = _english.Id, Form = "love", Stem = "love", Occurrences = 2 });
        _db.SaveChanges();

        (await WordForms.Spellings(_db, _english.Id, default)).Should().Equal(new WordForm("love", 2));
    }

    private List<long> Originals() =>
        [.. _db.Words.Local
            .Where(w => w.TextId == _greek.Id)
            .OrderBy(w => w.VerseId).ThenBy(w => w.Position)
            .Select(w => w.Id)];

    private void Renders((int Chapter, int Verse, int Position) original, (int Chapter, int Verse, int[] Positions) english)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _greek.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = InferredConfidence,
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

        _db.LinkWords.Add(new LinkWord
        {
            Link = link,
            Word = _db.WordAt(_greek, original.Chapter, original.Verse, original.Position),
            Side = LinkSide.To,
        });
    }
}
