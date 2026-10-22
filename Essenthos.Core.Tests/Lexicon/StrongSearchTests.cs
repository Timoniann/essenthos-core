using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Migrations;
using Essenthos.Core.Utils;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

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

    /// <summary>
    /// καταφιλέω holds φιλέω, and its number comes first; a student who typed the word itself
    /// wants the word itself first, then the words it opens.
    /// </summary>
    [Fact]
    public async Task TheWordSpelledExactlyComesFirstThenTheWordsItOpens()
    {
        Entry("G2705", "καταφιλέω", "kataphiléō", "to kiss earnestly", "kiss");
        Entry("G5384", "φίλος", "phílos", "actively, fond, i.e. friendly", "friend");
        await _db.SaveChangesAsync();

        (await Numbers("phileo", "greek")).Should().Equal("G5368", "G2705");
        (await Numbers("φιλεω", null)).Should().Equal("G5368", "G2705");
        (await Numbers("phil", "greek")).Should().Equal("G5368", "G5384", "G2705");
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

    /// <summary>
    /// The folded lemma and transliteration are indexed by an expression a migration wrote down. A
    /// search that folds otherwise is still right, and reads every entry again on every keystroke.
    /// </summary>
    [Theory]
    [InlineData("lemma")]
    [InlineData("transliteration")]
    public void TheSearchFoldsAsItsIndexesWereBuilt(string column)
    {
        DiacriticFolding.Expression(column).Should().Be(
            string.Format(LexiconSearchFindsSpellingsThroughTrigrams.Fold, column),
            "a new fold needs a migration that builds the lexicon's trigram indexes again");
    }

    [Fact]
    public async Task ASpellingIsLookedUpThroughTheIndexes()
    {
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        await using var explain = new NpgsqlCommand(
            "EXPLAIN " + StrongEndpoints.WordSearch.Replace("{0}", "@contains").Replace("{1}", "@exactly").Replace("{2}", "@opening"),
            connection,
            (NpgsqlTransaction)_transaction.GetDbTransaction());
        explain.Parameters.AddWithValue("contains", "%phil%");
        explain.Parameters.AddWithValue("exactly", "phil");
        explain.Parameters.AddWithValue("opening", "phil%");

        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        string.Join('\n', plan).Should()
            .Contain("ix_strong_entry_lemma_folded").And.Contain("ix_strong_entry_transliteration_folded")
            .And.Contain("ix_strong_entry_definition").And.Contain("ix_strong_entry_kjv_definition")
            .And.NotContain("Seq Scan");
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
