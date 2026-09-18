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
/// The encyclopedia's A to Z index, asked of Postgres, because the letter a name is filed under is
/// a regular expression and a case fold the database performs, and the claim under test is that the
/// counts and the filter agree about every name.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityLetterTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public EntityLetterTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        Add(EntityKind.Person, "abraham", "Abraham");
        Add(EntityKind.Person, "aaron", "Aaron");
        Add(EntityKind.Place, "ai", "Ai");
        Add(EntityKind.Person, "the-angel", "the angel of the LORD");
        Add(EntityKind.Place, "heaven", "heaven");
        Add(EntityKind.Place, "kings-highway", "’King’s Highway");
        Add(EntityKind.Place, "zoar", "Zoar");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task EveryLetterIsListedAndTheEmptyOnesAreZero()
    {
        var letters = await EncyclopediaEndpoints.Letters(_db.Entities);

        letters.Letters.Select(l => l.Letter).Should().Equal(
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z");
        Count(letters, "A").Should().Be(3);
        Count(letters, "B").Should().Be(0);
        Count(letters, "Z").Should().Be(1);
        letters.Total.Should().Be(7);
    }

    /// <summary>
    /// A name printed in lower case is filed under its capital, and a letter asked for in lower
    /// case finds it: <em>heaven</em> is an H and <em>the angel of the LORD</em> a T.
    /// </summary>
    [Fact]
    public async Task CaseDoesNotMoveANameToAnotherLetter()
    {
        var letters = await EncyclopediaEndpoints.Letters(_db.Entities);

        Count(letters, "H").Should().Be(1);
        Count(letters, "T").Should().Be(1);
        (await Slugs('h')).Should().Equal("heaven");
        (await Slugs('T')).Should().Equal("the-angel");
    }

    /// <summary>
    /// A name that opens with a quotation mark or an apostrophe is filed under its first letter,
    /// not under a punctuation mark no index offers.
    /// </summary>
    [Fact]
    public async Task PunctuationBeforeTheFirstLetterIsPassedOver()
    {
        var letters = await EncyclopediaEndpoints.Letters(_db.Entities);

        Count(letters, "K").Should().Be(1);
        letters.Letters.Should().OnlyContain(l => l.Letter.Length == 1 && char.IsLetter(l.Letter[0]));
        (await Slugs('k')).Should().Equal("kings-highway");
    }

    /// <summary>The counts a letter shows are the entries asking for it returns, for every letter.</summary>
    [Fact]
    public async Task EachLetterCountsExactlyWhatItsFilterReturns()
    {
        var letters = await EncyclopediaEndpoints.Letters(_db.Entities);

        foreach (var letter in letters.Letters)
        {
            (await Slugs(letter.Letter[0])).Should().HaveCount(letter.Count, $"letter {letter.Letter}");
        }
    }

    [Fact]
    public async Task TheCountsFollowTheKindFilter()
    {
        var letters = await EncyclopediaEndpoints.Letters(_db.Entities.Where(e => e.Kind == EntityKind.Place));

        Count(letters, "A").Should().Be(1);
        Count(letters, "T").Should().Be(0);
        letters.Total.Should().Be(4);
    }

    private static int Count(EntityLettersResponse letters, string letter) =>
        letters.Letters.Single(l => l.Letter == letter).Count;

    private Task<List<string>> Slugs(char letter) =>
        EncyclopediaEndpoints.UnderLetter(_db.Entities, letter)
            .OrderBy(e => e.Slug)
            .Select(e => e.Slug)
            .ToListAsync();

    private void Add(EntityKind kind, string slug, string name) =>
        _db.Entities.Add(new Entity
        {
            Kind = kind,
            Slug = slug,
            Name = name,
            SourceId = $"test:{slug}",
            Source = "test",
        });
}
