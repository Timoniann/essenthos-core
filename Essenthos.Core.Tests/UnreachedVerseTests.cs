using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A witness that gained Strong numbers after a translation was linked to it by them: the verses no
/// link reached are matched now, and the ones already linked are left as their first run drew them.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class UnreachedVerseTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TaggedTextLinkLoader _loader;
    private readonly Text _german;
    private readonly Text _hebrew;

    public UnreachedVerseTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _loader = new TaggedTextLinkLoader(_db, NullLogger<TaggedTextLinkLoader>.Instance);

        _german = Corpus.Add(_db, "LUTH1912", TextKind.Translation, "deu",
            (1, 1, ["Am", "Anfang", "schuf", "Gott"]),
            (1, 2, ["Und", "die", "Erde", "war"]));
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא", "אֱלֹהִים"]),
            (1, 2, ["וְ", "הָ", "אָרֶץ", "הָיְתָה"]));
        _db.SaveChanges();

        Tag(_german, 1, [null, "H7225", "H1254", "H430"]);
        Tag(_german, 2, [null, null, "H776", "H1961"]);
        Tag(_hebrew, 1, ["H9003", "H7225", "H1254", "H430"]);
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    private void Tag(Text text, int verse, string?[] numbers)
    {
        var words = _db.Words.Where(w => w.TextId == text.Id && w.Verse!.Number == verse).OrderBy(w => w.Position).ToList();
        for (var at = 0; at < numbers.Length; at++)
        {
            words[at].StrongNumber = numbers[at];
        }

        _db.SaveChanges();
    }

    private Task<List<long>> LinkIds() =>
        _db.Links.Where(l => l.FromTextId == _german.Id && l.ToTextId == _hebrew.Id).Select(l => l.Id).ToListAsync();

    [Fact]
    public async Task TheVersesNothingReachedAreMatchedAndTheRestAreLeftAlone()
    {
        await _loader.Load(_german.Slug, _hebrew.Slug, null);
        var first = await LinkIds();
        first.Should().HaveCount(3);

        // BHSA's verse gains the numbers it lacked.
        Tag(_hebrew, 2, ["H9002", "H9009", "H776", "H1961"]);
        (await _loader.Load(_german.Slug, _hebrew.Slug, null)).AlreadyLoaded.Should().BeTrue();

        var unreached = await _loader.Unreached(_german.Slug, _hebrew.Slug, null);
        var secondVerse = await _db.Words.Where(w => w.TextId == _german.Id && w.Verse!.Number == 2).Select(w => w.Id).ToListAsync();
        unreached.Should().BeEquivalentTo(secondVerse);

        await _loader.Load(_german.Slug, _hebrew.Slug, null, default, unreached);

        var now = await LinkIds();
        now.Should().HaveCount(5).And.Contain(first);
        var erde = await _db.Words.SingleAsync(w => w.TextId == _german.Id && w.Surface == "Erde");
        (await _db.LinkWords.AnyAsync(lw => lw.WordId == erde.Id && lw.Link!.ToTextId == _hebrew.Id)).Should().BeTrue();

        (await _loader.Unreached(_german.Slug, _hebrew.Slug, null)).Should().BeEmpty();
    }
}
