using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The King James' italics, read from the words its stated mappings say render nothing.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EditionMarkTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _english;
    private readonly Text _hebrew;

    public EditionMarkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _hebrew = Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo", (1, 1, ["בָּרָא"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["God", "did", "verily", "create"]));
        _db.SaveChanges();
        Supplied(2);
        Supplied(3);
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    private void Supplied(int position)
    {
        var link = new Link
        {
            FromTextId = _english.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Expands,
            Method = LinkMethod.StatedBySource,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_english, 1, 1, position), Side = LinkSide.From });
        _db.SaveChanges();
    }

    private Task<IReadOnlyList<EditionMarkOutcome>> Mark() =>
        new EditionMarkLoader(_db, NullLogger<EditionMarkLoader>.Instance).Mark(Path.Combine(Path.GetTempPath(), "absent"), default);

    private async Task<List<(long Group, long Word)>> Groups() =>
        (await _db.WordGroupWords
            .Where(m => m.WordGroup!.TextId == _english.Id)
            .OrderBy(m => m.WordId)
            .Select(m => new { m.WordGroupId, m.WordId })
            .ToListAsync())
        .Select(m => (m.WordGroupId, m.WordId))
        .ToList();

    /// <summary>
    /// A run of supplied words is one span, and marking them again as they are leaves the span as it
    /// is, its id included; a span that changed is written again.
    /// </summary>
    [Fact]
    public async Task MarkingTheSameItalicsAgainLeavesThemAndAChangeIsWritten()
    {
        await Mark();
        var first = await Groups();
        first.Select(m => m.Group).Distinct().Should().ContainSingle();
        first.Select(m => m.Word).Should().Equal(_db.WordAt(_english, 1, 1, 2).Id, _db.WordAt(_english, 1, 1, 3).Id);

        await Mark();
        (await Groups()).Should().Equal(first);

        Supplied(4);
        await Mark();
        var after = await Groups();
        after.Should().HaveCount(3);
        after.Select(m => m.Group).Distinct().Should().ContainSingle().Which.Should().NotBe(first[0].Group);
    }
}
