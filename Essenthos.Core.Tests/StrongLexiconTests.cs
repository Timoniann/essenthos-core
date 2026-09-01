using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The lexicon, and the two counts it exists to produce. Loading a dictionary is small; being able
/// to say how much of the corpus it explains is the part that was missing, and it is the measure
/// everything leaning on Strong numbers rests on.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongLexiconTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly StrongLexiconLoader _loader;
    private readonly Text _hebrew;

    public StrongLexiconTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _loader = new StrongLexiconLoader(_db, NullLogger<StrongLexiconLoader>.Instance);

        _hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["בְּ", "אֱלֹהִים", "רֵאשִׁית"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// A word whose number no entry answers is a word the corpus cannot explain, and it is counted.
    /// </summary>
    [Fact]
    public async Task AWordWhoseNumberNoEntryAnswersIsCounted()
    {
        Number(1, "H9003");
        Number(2, "H430");
        Number(3, "H7225");
        Entry("H430");

        var (unresolved, _) = await _loader.Coverage();

        unresolved.Should().Be(1);
    }

    /// <summary>
    /// ETCBC numbers the conjunction, the article and the inseparable prepositions in the H9000
    /// range. Strong never catalogued them, because a concordance has nothing to say about a
    /// letter. Counting them as missing entries misreports this corpus by 21%.
    /// </summary>
    [Fact]
    public async Task APrefixMorphemeIsNotAMissingEntry()
    {
        Number(1, "H9003");
        Number(2, "H9000");
        Number(3, "H9009");

        var (unresolved, _) = await _loader.Coverage();

        unresolved.Should().Be(0);
    }

    /// <summary>
    /// An entry nothing points at is not an error either — the concordance covers the whole Bible
    /// and these texts are not all of it — but it is worth knowing, and it is the direction nobody
    /// looks.
    /// </summary>
    [Fact]
    public async Task AnEntryNoWordPointsAtIsCountedSeparately()
    {
        Number(1, "H430");
        Entry("H430");
        Entry("H1254");

        var (unresolved, unused) = await _loader.Coverage();

        unresolved.Should().Be(0);
        unused.Should().Be(1);
    }

    private void Number(int position, string strong)
    {
        _db.WordAt(_hebrew, 1, 1, position).StrongNumber = strong;
        _db.SaveChanges();
    }

    private void Entry(string strong)
    {
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = strong, Lemma = strong, Definition = "a gloss" });
        _db.SaveChanges();
    }
}
