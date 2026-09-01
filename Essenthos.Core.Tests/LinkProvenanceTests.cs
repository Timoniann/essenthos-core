using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A guess may not be stored looking like a sourced claim. That is a rule this project's whole
/// claim rests on, so it is a constraint the database holds rather than a convention each loader is
/// trusted to keep.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LinkProvenanceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _from;
    private readonly Text _to;

    public LinkProvenanceTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _from = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (1, 1, ["רֵאשִׁית"]));
        _to = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (1, 1, ["beginning"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void ACorrespondenceASourceStatesCarriesNoConfidence()
    {
        Saving(LinkMethod.StatedBySource, confidence: null, source: "mapping/kjv-bhs.txt")
            .Should().NotThrow();
    }

    [Fact]
    public void ACorrespondenceASourceStatesMayNotBeGivenOne()
    {
        Saving(LinkMethod.StatedBySource, confidence: 0.9, source: "mapping/kjv-bhs.txt")
            .Should().Throw<DbUpdateException>();
    }

    [Theory]
    [InlineData(LinkMethod.StrongNumber)]
    [InlineData(LinkMethod.Lexical)]
    [InlineData(LinkMethod.Aligner)]
    public void ACorrespondenceAProcessInferredMustCarryOne(LinkMethod method)
    {
        Saving(method, confidence: null, source: "an aligner, v1").Should().Throw<DbUpdateException>();
        Saving(method, confidence: 0.62, source: "an aligner, v1").Should().NotThrow();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ConfidenceIsAProbability(double confidence)
    {
        Saving(LinkMethod.Aligner, confidence, source: "an aligner, v1").Should().Throw<DbUpdateException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EveryCorrespondenceNamesWhatProducedIt(string source)
    {
        Saving(LinkMethod.Manual, confidence: null, source).Should().Throw<DbUpdateException>();
    }

    /// <summary>
    /// The stored spelling is the word, not the ordinal. Every measurement in this project was
    /// taken by hand in psql, and a column of integers is a column nobody reads.
    /// </summary>
    [Fact]
    public void RelationAndMethodAreStoredAsWords()
    {
        var link = new Link
        {
            FromTextId = _from.Id,
            ToTextId = _to.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "mapping/kjv-bhs.txt",
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        var stored = _db.Database
            .SqlQuery<string>($"select relation || ' / ' || method as \"Value\" from link where id = {link.Id}")
            .Single();

        stored.Should().Be("renders / stated-by-source");
    }

    /// <summary>
    /// Each attempt runs inside a savepoint. A statement Postgres rejects aborts the whole
    /// transaction, so without one the first expected failure would take every later assertion in
    /// the test with it.
    /// </summary>
    private Action Saving(LinkMethod method, double? confidence, string source) => () =>
    {
        var link = new Link
        {
            FromTextId = _from.Id,
            ToTextId = _to.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Source = source,
        };
        _db.Links.Add(link);

        const string savepoint = "attempt";
        _transaction.CreateSavepoint(savepoint);
        try
        {
            _db.SaveChanges();
            _transaction.ReleaseSavepoint(savepoint);
        }
        catch
        {
            _transaction.RollbackToSavepoint(savepoint);
            throw;
        }
        finally
        {
            _db.Entry(link).State = EntityState.Detached;
        }
    };
}
