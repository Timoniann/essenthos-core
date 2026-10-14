using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the lexicon's search box answers to the two things a student types into it: a Strong
/// number, and the plain spelling of a word. Asked of Postgres, because the accents are folded
/// there.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongSearchTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public StrongSearchTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();

        Entry("G26", "ἀγάπη", "agápē", "love, i.e. affection or benevolence", "(feast of) charity");
        Entry("G27", "ἀγαπητός", "agapētós", "beloved", "(dearly, well) beloved");
        Entry("G2647", "καταλύω", "katalýō", "to demolish; specially (compare G2646) to halt for the night",
            "destroy, dissolve");
        Entry("G5368", "φιλέω", "philéō", "to be a friend to; while G25 is wider", "kiss, love");
        Entry("H5368", "נְקַשׁ", "nᵉqash", "to knock", "smote");
        Entry("H430", "אֱלֹהִים", "ʼĕlôhîym", "gods in the ordinary sense", "God");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Theory]
    [InlineData("G5368", null)]
    [InlineData("g5368", null)]
    [InlineData(" G05368 ", null)]
    [InlineData("5368", "greek")]
    [InlineData("5368", "G")]
    public async Task ANumberFindsItsEntry(string query, string? language)
    {
        (await Numbers(query, language)).Should().Equal("G5368");
    }

    /// <summary>
    /// G2647's definition says "compare G2646", which holds G26 as a run of letters; a number is
    /// not a run of letters.
    /// </summary>
    [Fact]
    public async Task ANumberIsNotFoundInsideAnotherNumber()
    {
        (await Numbers("G26", null)).Should().Equal("G26");
    }

    [Fact]
    public async Task BareDigitsWithNoLanguageFindBothLanguages()
    {
        (await Numbers("5368", null)).Should().BeEquivalentTo("H5368", "G5368");
    }

    [Theory]
    [InlineData("phileo", "G5368")]
    [InlineData("PHILEO", "G5368")]
    [InlineData("φιλεω", "G5368")]
    [InlineData("elohiym", "H430")]
    public async Task APlainSpellingFindsTheAccentedWord(string query, string number)
    {
        (await Numbers(query, null)).Should().Equal(number);
    }

    [Fact]
    public async Task APlainSpellingFindsEveryWordItIsPartOf()
    {
        (await Numbers("agape", "greek")).Should().BeEquivalentTo("G26", "G27");
    }

    [Fact]
    public async Task GlossesAreStillSearched()
    {
        (await Numbers("demolish", null)).Should().Equal("G2647");
        (await Numbers("kiss", "greek")).Should().Equal("G5368");
        (await Numbers("kiss", "hebrew")).Should().BeEmpty();
    }

    [Fact]
    public async Task AWildcardIsTypedNotObeyed()
    {
        (await Numbers("%", null)).Should().BeEmpty();
    }

    private async Task<List<string>> Numbers(string query, string? language)
    {
        return await StrongEndpoints.Matching(_db, query, language)
            .Select(e => e.StrongNumber)
            .ToListAsync();
    }

    private void Entry(string number, string lemma, string transliteration, string definition, string kjv)
    {
        _db.StrongEntries.Add(new StrongEntry
        {
            StrongNumber = number,
            Lemma = lemma,
            Transliteration = transliteration,
            Definition = definition,
            KjvDefinition = kjv,
        });
    }
}
