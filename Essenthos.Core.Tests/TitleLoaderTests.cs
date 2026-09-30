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
/// Phicol, Ahuzzath, the Rabshakeh — and the titles no dataset holds as anybody, Pharaoh and the
/// high priest among them, against the decision the corpus actually ships.
///
/// <para>
/// The records are set up as the loaded corpus holds them: persons, with a dataset's testimony and a
/// reading's claim, Isaac's king split off as a second person, Gideon's son beside them under the
/// same name, and every person the file names as a bearer. What is under test is that the decision
/// changes the records it names, writes the titles it brings, joins the bearers at their verses and
/// touches nothing else.
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

    /// <summary>
    /// The titles the file writes itself. One of them can bear another — the anointed priest is the
    /// high priest — and such a bearer is a record the loader writes, not one a dataset holds.
    /// </summary>
    private HashSet<string> Written =>
        [.. _decision.Titles.Where(title => title.Names is not null).Select(title => title.Slug)];

    public TitleLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new TitleLoader(_db, NullLogger<TitleLoader>.Instance);

        var held = _decision.Titles.Where(title => title.Names is null).Select(title => title.Slug)
            .Concat(_decision.Titles.SelectMany(title => title.Bearers ?? []).Select(bearer => bearer.Slug)
                .Where(slug => !Written.Contains(slug)))
            .Concat(["abimelech-4", "abimelech-2", "achish"])
            .Distinct();

        foreach (var slug in held)
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
        outcome.Missing.Should().Be(0);

        foreach (var title in _decision.Titles.Where(title => title.Names is null))
        {
            var record = await _db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == title.Slug);
            var source = title.Source ?? _decision.Source;

            record.Kind.Should().Be(EntityKind.Title);
            record.Name.Should().Be(title.Name);
            record.Distinguisher.Should().Be(title.Distinguisher);
            record.Notes.Should().Be(title.Notes);
            record.Sex.Should().BeNull("a title is borne by whoever holds the office");
            record.Source.Should().Be(source);
            record.Claims.Select(c => (c.Method, c.Source)).Should().BeEquivalentTo(
                new[] { (LinkMethod.StatedBySource, Dataset), (LinkMethod.Manual, source) });
            record.Claims.Single(c => c.Method == LinkMethod.Manual).Note.Should().Be(title.Why);
        }
    }

    /// <summary>
    /// Pharaoh is eight persons in the dataset and no record for the word, and the high priest is an
    /// office, not anybody's name. Each is written as a record of this corpus's own, credited to the
    /// owner's decision and to nothing it did not come from.
    /// </summary>
    [Fact]
    public async Task ATitleNoDatasetHoldsIsWritten()
    {
        var outcome = await _loader.Load();

        var written = _decision.Titles.Where(title => title.Names is not null).ToList();
        written.Select(title => title.Slug).Should().Contain(new[] { "pharaoh-title", "caesar-title", "high-priest" });
        outcome.Written.Should().Be(written.Count);

        var pharaoh = await _db.Entities
            .Include(e => e.Claims)
            .Include(e => e.Names)
            .SingleAsync(e => e.Slug == "pharaoh-title");

        pharaoh.Kind.Should().Be(EntityKind.Title);
        pharaoh.Name.Should().Be("Pharaoh");
        pharaoh.SourceId.Should().Be("essenthos:title:pharaoh-title");
        pharaoh.Claims.Should().ContainSingle().Which.Method.Should().Be(LinkMethod.Manual);
        pharaoh.Names.Should().ContainSingle().Which.HebrewStrongNumber.Should().Be("H6547");

        // A common noun would make every high priest in the Bible a word naming this record.
        var highPriest = await _db.Entities.Include(e => e.Names).SingleAsync(e => e.Slug == "high-priest");
        highPriest.Names.Should().OnlyContain(n => n.HebrewStrongNumber == null && n.GreekStrongNumber == null);
    }

    /// <summary>
    /// A bearer is joined at the verse that names the person and the title together, and only
    /// there: Pharaoh-nechoh at 2KI 23:29, and nine men in all who the text calls Pharaoh.
    /// </summary>
    [Fact]
    public async Task ABearerIsJoinedAtTheVerseThatNamesBoth()
    {
        var outcome = await _loader.Load();

        outcome.Bearers.Should().Be(_decision.Titles.Sum(title => title.Bearers?.Count ?? 0));

        var pharaohs = await _db.TitleBearers
            .Where(b => b.Title!.Slug == "pharaoh-title")
            .Select(b => new { b.Bearer!.Slug, b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse, b.Source })
            .ToListAsync();

        pharaohs.Should().HaveCount(9);
        pharaohs.Single(b => b.Slug == "pharaohneco").Should().BeEquivalentTo(new
        {
            Slug = "pharaohneco",
            CanonicalBook = 12,
            CanonicalChapter = 23,
            CanonicalVerse = 29,
            Source = _decision.Titles.Single(t => t.Slug == "pharaoh-title").Source,
        });

        var nehemiah = await _db.TitleBearers
            .Where(b => b.Bearer!.Slug == "nehemiah")
            .Select(b => b.Title!.Slug)
            .ToListAsync();
        nehemiah.Should().BeEquivalentTo(new[] { "governor", "tirshatha" });
    }

    /// <summary>Every reference the file gives a bearer is a verse of the canonical frame.</summary>
    [Fact]
    public void EveryBearerIsGivenAVerse()
    {
        _decision.Titles.SelectMany(title => title.Bearers ?? [])
            .Should().OnlyContain(bearer => TitleLoader.Verse(bearer.Reference) != null && bearer.Why.Length > 0);
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

    /// <summary>The startup pipeline runs on every boot, and a second boot writes nothing.</summary>
    [Fact]
    public async Task ASecondBootWritesNothing()
    {
        (await _loader.Load()).AlreadyLoaded.Should().BeFalse();
        var claims = await _db.EntityClaims.CountAsync();
        var alternatives = await _db.EntityAlternatives.CountAsync();
        var entities = await _db.Entities.CountAsync();
        var bearers = await _db.TitleBearers.CountAsync();

        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.EntityClaims.CountAsync()).Should().Be(claims);
        (await _db.EntityAlternatives.CountAsync()).Should().Be(alternatives);
        (await _db.Entities.CountAsync()).Should().Be(entities);
        (await _db.TitleBearers.CountAsync()).Should().Be(bearers);
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
        outcome.Missing.Should().Be(
            _decision.Titles.Count(title => title.Names is null)
            + _decision.Titles.Sum(title => title.Bearers?.Count(bearer => !Written.Contains(bearer.Slug)) ?? 0));
        (await _db.TitleBearers.Select(b => b.Bearer!.Slug).ToListAsync())
            .Should().OnlyContain(slug => Written.Contains(slug), "a bearer is never guessed at");
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
