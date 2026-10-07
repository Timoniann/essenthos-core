using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Searching a text printed without spaces: a reader's word is two of the tagger's rows (天地) or part
/// of one (女人 in 於是女人), and is found in the line as printed, never across its punctuation.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ChineseSearchTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _union;

    public ChineseSearchTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _union = Corpus.Add(_db, "CUV", TextKind.Translation, "zho",
            (1, 1, ["起初", "神", "創造", "天", "地"]),
            (3, 6, ["於是女人", "見", "那棵樹"]),
            (3, 7, ["天", "也", "地"]));
        _db.SaveChanges();

        foreach (var word in _db.Words.Where(w => w.TextId == _union.Id).ToList())
        {
            word.NormalisedText = word.Surface;
            word.Trailer = string.Empty;
        }

        _db.SaveChanges();
        Printed(1, 1, "起初神創造天地", 1, 5);
        Printed(3, 6, "於是女人見那棵樹", 1, 3);
        // 3:7 prints a mark of punctuation after 天, so it is a run of its own and 也地 the next.
        _db.WordAt(_union, 3, 7, 1).Trailer = "，";
        Printed(3, 7, "也地", 2, 3);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    private void Printed(int chapter, int verse, string run, int first, int last)
    {
        for (var position = first; position <= last; position++)
        {
            _db.WordAt(_union, chapter, verse, position).GraphicalText = run;
        }
    }

    private async Task<List<(int Chapter, int Verse)>> Found(string characters)
    {
        var ids = await SearchEndpoints.VersesPrinting(_db.Words.Where(w => w.TextId == _union.Id), characters).ToListAsync();
        return [.. (await _db.Verses.Where(v => ids.Contains(v.Id)).ToListAsync()).Select(v => (v.ChapterNumber, v.Number)).Order()];
    }

    [Fact]
    public async Task AWordTheTaggerSplitIsFound() =>
        (await Found("天地")).Should().Equal((1, 1));

    [Fact]
    public async Task PartOfAWordTheTaggerJoinedIsFound() =>
        (await Found("女人")).Should().Equal((3, 6));

    [Fact]
    public async Task ACharacterAloneIsFoundWhereverItStands() =>
        (await Found("天")).Should().Equal((1, 1), (3, 7));

    [Fact]
    public void TheWordsMarkedAreTheOnesTheCharactersFallIn()
    {
        SearchEndpoints.CharactersMatched([("起初", ""), ("神", ""), ("創造", ""), ("天", ""), ("地", "")], ["天地"])
            .Should().BeEquivalentTo([3, 4]);
        SearchEndpoints.CharactersMatched([("於是女人", ""), ("見", "")], ["女人"]).Should().BeEquivalentTo([0]);
        SearchEndpoints.CharactersMatched([("天", "，"), ("地", "")], ["天地"]).Should().BeEmpty(
            "a term is not found across a mark of punctuation");
    }
}
