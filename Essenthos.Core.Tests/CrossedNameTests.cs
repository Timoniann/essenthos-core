using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// 1 Kings 7:13, where the Synodal's printed numbering puts Hiram's number on <em>Тира</em> and
/// Tyre's on <em>Хирама</em>, because the Hebrew names the man before the city and the Russian names
/// the city before the man.
///
/// What tells them apart is the word's own spelling against the forms the encyclopedia records, so
/// each case here is one thing that has to be true of those forms before two words are taken to hold
/// each other's names.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CrossedNameTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CrossedNameLoader _loader;
    private readonly Text _russian;
    private readonly Entity _hiram;
    private readonly Entity _tyre;

    public CrossedNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new CrossedNameLoader(_db, NullLogger<CrossedNameLoader>.Instance);

        _russian = Corpus.Add(_db, "RUSV", TextKind.Translation, "rus",
            (7, 13, ["взял", "из", "Тира", "Хирама"]));
        _db.SaveChanges();
        Fold();

        _hiram = Record("hiram-2", "Hiram", ("nominative", "Хирам"), ("genitive", "Хирама"));
        _tyre = Record("tyre", "Tyre", ("nominative", "Тир"), ("genitive", "Тира"));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    /// <summary>
    /// The searchable form the folding pass fills, which is the left side of every comparison here.
    /// </summary>
    private void Fold()
    {
        foreach (var word in _db.Words.Where(w => w.TextId == _russian.Id))
        {
            word.NormalisedText = word.Surface.ToLowerInvariant();
        }

        _db.SaveChanges();
    }

    private Entity Record(string slug, string name, params (string Case, string Form)[] forms)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();

        foreach (var (grammaticalCase, form) in forms)
        {
            _db.EntityNameForms.Add(new EntityNameForm
            {
                Entity = entity, Language = "rus", GrammaticalCase = grammaticalCase, Form = form,
                Method = LinkMethod.Manual, Source = "a test",
            });
        }

        return entity;
    }

    private Word Word(int position) => _db.WordAt(_russian, 7, 13, position);

    private void Names(int position, Entity entity, double confidence, LinkMethod method = LinkMethod.StrongNumber) =>
        _db.WordEntities.Add(new WordEntity
        {
            Word = Word(position), Entity = entity, Method = method, Confidence = confidence,
            Source = "the numbering of the Synodal",
            Note = "through BHSA word 1, linked by strong-number",
        });

    private async Task<Dictionary<long, WordEntity>> Named() =>
        await _db.WordEntities.AsNoTracking().Include(a => a.Entity).ToDictionaryAsync(a => a.WordId);

    [Fact]
    public async Task TwoWordsHoldingEachOthersNamesAreCrossedBack()
    {
        Names(3, _hiram, 0.89);
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        var named = await Named();
        named[Word(3).Id].Entity!.Slug.Should().Be("tyre");
        named[Word(4).Id].Entity!.Slug.Should().Be("hiram-2");
        outcome.Pairs.Should().Be(1);
        outcome.Rewritten.Should().Be(2);
    }

    /// <summary>
    /// The confidence travels with the name and is not invented: <em>Тира</em> is as sure of Tyre
    /// as the row that said Tyre was, and no surer.
    /// </summary>
    [Fact]
    public async Task TheRewrittenRowIsAsSureAsTheAnswerItTook()
    {
        Names(3, _hiram, 0.89);
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();

        await _loader.Load();

        var named = await Named();
        named[Word(3).Id].Confidence.Should().Be(0.72);
        named[Word(4).Id].Confidence.Should().Be(0.89);
        named[Word(3).Id].Method.Should().Be(LinkMethod.Lexical, "the word's own form is what decided it");
        named[Word(3).Id].Source.Should().Be(CrossedNameLoader.Source);
    }

    /// <summary>What was overturned travels in the note beside the answer that replaced it.</summary>
    [Fact]
    public async Task TheAnswerItReplacedIsNamedInTheNote()
    {
        Names(3, _hiram, 0.89);
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();

        await _loader.Load();

        var named = await Named();
        named[Word(3).Id].Note.Should().Contain("the numbering of the Synodal").And.Contain("Hiram");
        (await _db.WordEntityClaims.CountAsync(c => c.Source == CrossedNameLoader.Source)).Should().Be(2);
    }

    /// <summary>
    /// A word that does spell the name it was given is the ordinary case, whatever else stands in
    /// the verse.
    /// </summary>
    [Fact]
    public async Task WordsThatSpellTheirOwnNamesAreLeftAlone()
    {
        Names(3, _tyre, 0.72);
        Names(4, _hiram, 0.89);
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        var named = await Named();
        named[Word(3).Id].Entity!.Slug.Should().Be("tyre");
        named[Word(4).Id].Entity!.Slug.Should().Be("hiram-2");
        outcome.Pairs.Should().Be(0);
    }

    /// <summary>
    /// Running it twice must not swing the names back: after the first pass each word spells what it
    /// is called, which is exactly what the rule asks.
    /// </summary>
    [Fact]
    public async Task CrossingBackTwiceIsCrossingBackOnce()
    {
        Names(3, _hiram, 0.89);
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();

        await _loader.Load();
        var again = await _loader.Load();

        var named = await Named();
        named[Word(3).Id].Entity!.Slug.Should().Be("tyre");
        again.Pairs.Should().Be(0);
    }

    /// <summary>
    /// A carry reaches these words again from the same crossed links and writes the crossed answers
    /// back beside the corrected ones. The next pass takes them back and leaves one answer a word.
    /// </summary>
    [Fact]
    public async Task ACrossedAnswerWrittenAgainIsTakenBackAgain()
    {
        Names(3, _hiram, 0.89);
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();
        await _loader.Load();

        Names(3, _hiram, 0.89);
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();
        var again = await _loader.Load();

        var rows = await _db.WordEntities.AsNoTracking().Include(a => a.Entity).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Single(a => a.WordId == Word(3).Id).Entity!.Slug.Should().Be("tyre");
        rows.Single(a => a.WordId == Word(4).Id).Entity!.Slug.Should().Be("hiram-2");
        again.Pairs.Should().Be(1);
        (await _db.WordEntityClaims.CountAsync(c => c.Source == CrossedNameLoader.Source)).Should().Be(2);
    }

    /// <summary>
    /// A ruling carries no confidence, and a spelling does not overturn somebody's decision about
    /// one word.
    /// </summary>
    [Fact]
    public async Task ARulingIsNotCrossed()
    {
        _db.WordEntities.Add(new WordEntity
        {
            Word = Word(3), Entity = _hiram, Method = LinkMethod.Manual, Confidence = null,
            Source = "a ruling",
        });
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        (await Named())[Word(3).Id].Entity!.Slug.Should().Be("hiram-2");
        outcome.Pairs.Should().Be(0);
    }

    /// <summary>
    /// Only an exact form counts. <em>Тире</em> is a form of Tyre the record does not hold, and a
    /// rule that guessed at the ending would be answering a question it cannot see the evidence for.
    /// </summary>
    [Fact]
    public async Task AFormTheEncyclopediaDoesNotHoldSaysNothing()
    {
        Word(3).Surface = "Тире";
        Word(3).NormalisedText = "тире";
        Names(3, _hiram, 0.89);
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        (await Named())[Word(3).Id].Entity!.Slug.Should().Be("hiram-2");
        outcome.Pairs.Should().Be(0);
    }

    /// <summary>
    /// A word standing in two such pairs is left alone, and so is everything it pairs with: which of
    /// them it belongs to is the question the rule has no answer for.
    /// </summary>
    [Fact]
    public async Task APairEitherOfWhoseWordsStandsInASecondIsLeftAlone()
    {
        var second = Record("tyre-2", "Tyre", ("genitive", "Тира"));
        Word(2).Surface = "Хирам";
        Word(2).NormalisedText = "хирам";
        Names(2, second, 0.60);
        Names(3, _hiram, 0.89);
        Names(4, _tyre, 0.72);
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        var named = await Named();
        named[Word(3).Id].Entity!.Slug.Should().Be("hiram-2");
        named[Word(4).Id].Entity!.Slug.Should().Be("tyre");
        outcome.Pairs.Should().Be(0);
    }
    /// <summary>
    /// 1 Chronicles 27:31 in the Ukrainian, <em>гаґрянин Язіз</em>: the man's name on the gentilic and
    /// the Hagrites on the man. <em>Язіз</em> is exactly the man's recorded form, and the encyclopedia
    /// holds no Ukrainian form of the Hagrites for the gentilic to be measured against.
    /// </summary>
    [Fact]
    public async Task NeighboursCrossedWhereOnlyOneOfTheNamesHasAFormAreCrossedBack()
    {
        var jaziz = Record("jaziz", "Jaziz", ("nominative", "Язіз"));
        var hagrites = new Entity
        {
            Kind = EntityKind.People, Slug = "hagarites", Name = "Hagrites", SourceId = "hagarites",
            Source = "a test",
        };
        _db.Entities.Add(hagrites);
        Word(1).Surface = "гаґрянин";
        Word(1).NormalisedText = "гаґрянин";
        Word(2).Surface = "Язіз";
        Word(2).NormalisedText = "язіз";
        await _db.SaveChangesAsync();
        Names(1, jaziz, 0.97);
        Names(2, hagrites, 0.88);
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        var named = await Named();
        named[Word(1).Id].Entity!.Slug.Should().Be("hagarites");
        named[Word(2).Id].Entity!.Slug.Should().Be("jaziz");
        outcome.Pairs.Should().Be(1);
    }

    /// <summary>
    /// The same shape where the name given back has forms and none of them is the word: a form that
    /// does not match is evidence, and it says no.
    /// </summary>
    [Fact]
    public async Task NeighboursWhoseOtherNameHasFormsThatDoNotMatchAreLeftAlone()
    {
        Word(1).Surface = "Тирянин";
        Word(1).NormalisedText = "тирянин";
        Word(2).Surface = "Хирам";
        Word(2).NormalisedText = "хирам";
        await _db.SaveChangesAsync();
        Names(1, _hiram, 0.9);
        Names(2, _tyre, 0.8);
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load();

        (await Named())[Word(1).Id].Entity!.Slug.Should().Be("hiram-2");
        outcome.Pairs.Should().Be(0);
    }
}
