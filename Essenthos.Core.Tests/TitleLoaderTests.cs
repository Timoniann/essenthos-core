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
/// The names the owner ruled are titles borne by possibly more than one man — Abimelech of Gerar,
/// Phicol, Ahuzzath — against the decision the corpus actually ships.
///
/// <para>
/// The records are set up as the loaded corpus holds them: persons, with a dataset's testimony and a
/// reading's claim, Isaac's king split off as a second person, and Gideon's son beside them under
/// the same name. What is under test is that the decision changes the first three and nothing else.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TitleLoaderTests : IDisposable
{
    private const string Dataset = "BibleData, a test";

    private const string Reading = "Essenthos, from a reading, a test";

    private readonly AppDbContext _db;
    private readonly TitleLoader _loader;
    private readonly TitleDecision _decision = SenseReadingFiles.Titles();

    public TitleLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new TitleLoader(_db, NullLogger<TitleLoader>.Instance);

        foreach (var slug in new[] { "abimelech", "phicol", "ahuzzath", "abimelech-4", "abimelech-2", "achish" })
        {
            _db.Entities.Add(new Entity
            {
                Kind = EntityKind.Person,
                Slug = slug,
                Name = "a name",
                Distinguisher = "one man",
                Sex = "male",
                SourceId = slug,
                Source = Reading,
                Claims =
                [
                    new EntityClaim { Method = LinkMethod.StatedBySource, Source = Dataset, Note = "holds this man" },
                    new EntityClaim { Method = LinkMethod.ModelReading, Confidence = 0.9, Source = Reading, Note = "this bearer" },
                ],
            });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    /// <summary>
    /// Each record the decision names is a title, says what the decision says in its line and notes,
    /// credits the decision, and keeps the dataset's testimony while losing the reading that made it
    /// one man.
    /// </summary>
    [Fact]
    public async Task ARecordTheOwnerRuledATitleIsHeldAsOne()
    {
        var outcome = await _loader.Load();

        outcome.Retitled.Should().Be(_decision.Titles.Count);

        foreach (var title in _decision.Titles)
        {
            var record = await _db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == title.Slug);

            record.Kind.Should().Be(EntityKind.Title);
            record.Name.Should().Be(title.Name);
            record.Distinguisher.Should().Be(title.Distinguisher);
            record.Notes.Should().Be(title.Notes);
            record.Sex.Should().BeNull("a title is borne by whoever holds the office");
            record.Source.Should().Be(_decision.Source);
            record.Claims.Select(c => (c.Method, c.Source)).Should().BeEquivalentTo(
                new[] { (LinkMethod.StatedBySource, Dataset), (LinkMethod.Manual, _decision.Source) });
            record.Claims.Single(c => c.Method == LinkMethod.Manual).Note.Should().Be(title.Why);
        }
    }

    /// <summary>
    /// What the decision leaves open is on the record: every reading of who bore the name, with its
    /// reason, and a link where the encyclopedia holds the man.
    /// </summary>
    [Fact]
    public async Task WhatTheDecisionLeavesOpenIsOnTheRecord()
    {
        await _loader.Load();

        var abimelech = await _db.Entities
            .Include(e => e.Alternatives).ThenInclude(a => a.Alternative)
            .SingleAsync(e => e.Slug == "abimelech");

        abimelech.Alternatives.Should().HaveCount(
            _decision.Titles.Single(t => t.Slug == "abimelech").Alternatives!.Count);
        abimelech.Alternatives.Should().ContainSingle(a => a.Alternative != null)
            .Which.Alternative!.Slug.Should().Be("achish");
        abimelech.Alternatives.Should().OnlyContain(a => a.Reason.Length > 0 && a.Source == _decision.Source);
        abimelech.Alternatives.Where(a => a.Alternative == null)
            .Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Describes));
    }

    /// <summary>
    /// The second Abimelech a reading split off for Genesis 26 is withdrawn, and Gideon's son, whose
    /// own name it was, is not touched.
    /// </summary>
    [Fact]
    public async Task TheRecordForOneBearerGoesAndGideonsSonStays()
    {
        var outcome = await _loader.Load();

        outcome.Retired.Should().Be(1);
        (await _db.Entities.AnyAsync(e => e.Slug == "abimelech-4")).Should().BeFalse();

        var gideons = await _db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == "abimelech-2");
        gideons.Kind.Should().Be(EntityKind.Person);
        gideons.Distinguisher.Should().Be("one man");
        gideons.Claims.Should().HaveCount(2);

        (await _db.Entities.SingleAsync(e => e.Slug == "achish")).Kind.Should().Be(EntityKind.Person);
    }

    /// <summary>The startup pipeline runs on every boot, and a second boot writes nothing (RUL-0005).</summary>
    [Fact]
    public async Task ASecondBootWritesNothing()
    {
        (await _loader.Load()).AlreadyLoaded.Should().BeFalse();
        var claims = await _db.EntityClaims.CountAsync();
        var alternatives = await _db.EntityAlternatives.CountAsync();

        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.EntityClaims.CountAsync()).Should().Be(claims);
        (await _db.EntityAlternatives.CountAsync()).Should().Be(alternatives);
    }

    /// <summary>
    /// A corpus whose encyclopedia is not loaded has nothing to hold as a title, and says so rather
    /// than calling the step done.
    /// </summary>
    [Fact]
    public async Task ACorpusWithoutTheRecordsIsNotCalledLoaded()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        var outcome = await _loader.Load();

        outcome.AlreadyLoaded.Should().BeFalse();
        outcome.Missing.Should().Be(_decision.Titles.Count);
    }

    /// <summary>
    /// The words the decision settles name only the records it holds as titles, so a word is never
    /// credited to the decision and annotated to somebody it did not rule on.
    /// </summary>
    [Fact]
    public void TheWordsTheDecisionSettlesNameItsTitles()
    {
        var rulings = SenseReadingFiles.TitleRulings();

        rulings.Source.Should().Be(_decision.Source);
        rulings.Rulings.Should().OnlyContain(r =>
            r.Create == null && _decision.Titles.Any(t => t.Slug == r.Existing));
    }
}
