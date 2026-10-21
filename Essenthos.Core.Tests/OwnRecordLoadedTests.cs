using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What one load of the shipped rulings writes, read by tests that change nothing. The class's
/// database is its own and its tests run one after another, so the rulings are placed and loaded once
/// and every test reads that load; a test that changes the world before it loads belongs in one of the
/// other own-record classes, which make the world afresh for every test.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordLoadedTests(WitnessDatabase database) : OwnRecordTests, IAsyncLifetime
{
    private OwnRecordOutcome FirstLoad => World.FirstLoad!;

    public async Task InitializeAsync() => World = await database.Shared(() => OwnRecordWorld.Loaded(database));

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EveryRulingAnnotatesItsWord()
    {
        var named = await Db.WordEntities.Include(a => a.Entity).ToListAsync();
        named.Select(a => a.WordId).Distinct().Should().BeEquivalentTo(Rulings.Select(r => Words[r.Word]).Distinct());
        named.Should().OnlyContain(a => a.Method == LinkMethod.Manual);
        named.Should().OnlyContain(a => a.Confidence == null);
    }

    /// <summary>
    /// Daughter is the word Jesus says to the woman with the issue of blood, and no name of hers: the
    /// ruling that gives her the three words heads her record by what the text says of her, says who
    /// decided, and leaves the slug, which her picture is filed under, as it was.
    /// </summary>
    [Fact]
    public async Task ADaughterAddressedByHerWordsIsTheWomanWithTheIssueOfBloodAndKeepsHerSlug()
    {
        var woman = await Db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == "daughter");
        woman.Name.Should().Be("Woman with an issue of blood");
        woman.Distinguisher.Should().Contain("MAT 9:20-22");
        woman.Notes.Should().Contain("MAT 9:22; MRK 5:34; LUK 8:48").And.Contain("does not give her name");
        woman.Claims.Should().ContainSingle(claim => claim.Method == LinkMethod.Manual)
            .Which.Source.Should().Contain("2026-09-30");

        var words = SenseReadingFiles.AddressedRulings().Rulings.Select(r => Words[r.Word]);
        (await Db.WordEntities.Where(a => a.EntityId == woman.Id).Select(a => a.WordId).ToListAsync())
            .Should().BeEquivalentTo(words);
    }

    /// <summary>
    /// A record we wrote says it is ours, in the source a reader is shown and in a claim carrying
    /// the method and whoever decided. A dataset's row carries neither, so the two can never be
    /// confused. A later ruling that amends what the record says adds its own decision beside it.
    /// </summary>
    [Fact]
    public async Task ARecordWeWroteSaysItIsOursAndWhatEstablishedIt()
    {
        foreach (var ruling in Rulings.Where(r => r.Create is not null))
        {
            var written = await Db.Entities
                .Include(e => e.Claims)
                .Include(e => e.Verses)
                .SingleAsync(e => e.Slug == ruling.Create!.Slug);

            written.Source.Should().StartWith("Essenthos");
            written.SourceId.Should().StartWith("essenthos:");
            written.Claims.Should().ContainSingle(c => c.Note == ruling.Why)
                .And.OnlyContain(c => c.Method == LinkMethod.Manual && c.Confidence == null,
                    "a later ruling that amends the record says so in a decision of its own");
            written.Verses.Should().ContainSingle()
                .Which.Source.Should().StartWith("Essenthos");
        }
    }

    /// <summary>
    /// The verse a record rests on is read from the word rather than transcribed beside it, so the
    /// two cannot come to disagree.
    /// </summary>
    [Fact]
    public async Task TheVerseARecordRestsOnIsTheVerseOfTheWord()
    {
        var ruling = Rulings.First(r => r.Create is not null);
        var word = await Db.Words.SingleAsync(w => w.Id == Words[ruling.Word]);
        var reference = await Db.VerseReferences.SingleAsync(r => r.VerseId == word.VerseId && r.IsPrimary);
        var rests = await Db.EntityVerses.SingleAsync(v => v.Entity!.Slug == ruling.Create!.Slug);

        rests.CanonicalBook.Should().Be(reference.CanonicalBook);
        rests.CanonicalChapter.Should().Be(reference.CanonicalChapter);
        rests.CanonicalVerse.Should().Be(reference.CanonicalVerse);
    }

    /// <summary>
    /// The shape the owner asked for on Judges 4:11: the word names Hobab, and the record says out
    /// loud that he may be Reuel. A silence and a guess are both worse answers than a named doubt.
    ///
    /// An alternative names a record where the encyclopedia holds one and describes it where it does
    /// not — Jerioth may be Azubah, who has a page, and may equally be no name at all, which has
    /// none. What has to be true of every one of them is that a reader is told what it is.
    /// </summary>
    [Fact]
    public async Task AnUnsettledRecordNamesWhoElseItMightBe()
    {
        foreach (var ruling in Rulings.Where(r => r.Alternatives is { Count: > 0 }))
        {
            var slug = ruling.Create?.Slug ?? ruling.Existing!;
            var alternatives = await Db.EntityAlternatives
                .Include(a => a.Alternative)
                .Where(a => a.Entity!.Slug == slug)
                .ToListAsync();

            alternatives.Should().HaveCount(ruling.Alternatives!.Count);
            alternatives.Should().OnlyContain(a => a.Alternative != null || a.Describes != null);
            alternatives.Should().OnlyContain(a => a.Reason.Length > 0);
            alternatives.Should().OnlyContain(a => a.Source.StartsWith("Essenthos"));
        }
    }

    /// <summary>
    /// A ruling that names a record the encyclopedia already holds does not write a second one. The
    /// duplicate records this corpus already has to live with are exactly what that would produce.
    /// </summary>
    [Fact]
    public async Task ARulingOnAnExistingRecordCreatesNothing()
    {
        var outcome = FirstLoad;

        outcome.Created.Should().Be(Rulings.Count(r => r.Create is not null));
        (await Db.Entities.CountAsync(e => e.Source.StartsWith("Essenthos")))
            .Should().Be(outcome.Created + Rulings.Count(r => r.Existing is not null && r.Says?.Name is not null));
    }

    /// <summary>
    /// The bulk pass is built and off. With no readings on disk there is nothing to count either
    /// way, and what matters is that the switch decides rather than the presence of the files.
    /// </summary>
    [Fact]
    public async Task TheBulkPassWritesNothingWhileTheSwitchIsOff()
    {
        var outcome = FirstLoad;

        outcome.Withheld.Should().Be(0);
        (await Db.Entities.CountAsync(e => e.Source.StartsWith("Essenthos")))
            .Should().Be(outcome.Created + Rulings.Count(r => r.Existing is not null && r.Says?.Name is not null));
    }

    [Fact]
    public async Task RunningAgainWritesNothingFurther()
    {
        var entities = await Db.Entities.CountAsync();
        var annotations = await Db.WordEntities.CountAsync();
        var names = await Db.EntityNames.CountAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        again.Labelled.Should().Be(0);
        (await Db.Entities.CountAsync()).Should().Be(entities);
        (await Db.WordEntities.CountAsync()).Should().Be(annotations);
        (await Db.EntityNames.CountAsync()).Should().Be(names);
    }

    /// <summary>
    /// A record with no <c>entity_name</c> row is reachable by its slug and by nothing else: it
    /// stands in no namesake group, the pass that declines a name into Ukrainian and Russian reads
    /// that table and cannot see it, and no resolution by Strong number arrives at it however
    /// plainly the word carries the number. So the record is named, with the number read off the
    /// word the ruling rests on rather than off the ruling beside it.
    /// </summary>
    [Fact]
    public async Task ARecordWeWroteCarriesTheNameItsNumberReachesItBy()
    {
        var outcome = FirstLoad;

        outcome.Labelled.Should().Be(Rulings.Count(r => r.Create is not null));

        foreach (var ruling in Rulings.Where(r => r.Create is not null))
        {
            var written = await Db.Entities
                .Include(e => e.Names)
                .SingleAsync(e => e.Slug == ruling.Create!.Slug);

            var name = written.Names.Should().ContainSingle().Which;
            name.Label.Should().Be(ruling.Create!.Name);
            name.Kind.Should().Be("proper name");
            var greek = ruling.StrongNumber.StartsWith('G');
            name.HebrewStrongNumber.Should().Be(greek ? null : ruling.StrongNumber);
            name.GreekStrongNumber.Should().Be(greek ? ruling.StrongNumber : null);
        }
    }

    /// <summary>
    /// A word ruled on three times ends on the last answer: the second ruling took back the first,
    /// the third the second, and a warm load gives back none of the answers taken back.
    /// </summary>
    [Fact]
    public async Task AWordCorrectedTwiceEndsOnTheLastRuling()
    {
        var chain = Rulings.GroupBy(r => Words[r.Word]).Where(g => g.Count() > 2).ToList();
        chain.Should().NotBeEmpty("NEH 12:21's Jedaiah is ruled by the review, corrected in 2026-09 and corrected again");

        await Load();

        foreach (var word in chain)
        {
            var named = await Db.WordEntities.Where(a => a.WordId == word.Key).Select(a => a.Entity!.Slug).ToListAsync();
            named.Should().Equal([word.Last().Existing], $"word {word.Key} names the last ruling's man only");
        }
    }

    /// <summary>
    /// A name a dataset lists a verse for, which two readings of the verse gave to the same record:
    /// the word is that record's, under a source that names both models and the day, and says in
    /// who decided that no person read it.
    /// </summary>
    [Fact]
    public async Task ANameTwoReadingsGaveToOneRecordIsThatRecords()
    {
        var file = SenseReadingFiles.NamesakeSecondRulings();
        file.Rulings.Should().NotBeEmpty();
        file.Rulings.Should().OnlyContain(r => r.Existing != null && r.Create == null && r.Position > 0 && r.Surface.Length > 0
                                               && r.StrongNumber != null && r.StrongNumber.Length > 1 && r.Why.Length > 0);
        file.Source.Should().Contain("claude-sonnet").And.Contain("claude-opus").And.Contain("2026-09-30");
        file.DecidedBy.Should().Contain("No person read them");

        var slugs = await Db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id);
        var named = await Db.WordEntities.Where(a => a.Source == file.Source).ToListAsync();
        named.Select(a => (a.WordId, a.EntityId)).Should().BeEquivalentTo(
            file.Rulings.Select(r => (Words[r.Word], slugs[r.Existing!])));
    }

    /// <summary>
    /// The words the records a dataset supplied print and no word of ours named: each given to the
    /// record its reading names, under a source that says who read it and on whose instruction,
    /// and the record the text spells otherwise headed by the name it prints and credited to the ruling.
    /// </summary>
    [Fact]
    public async Task ARecordTheDatasetSuppliedIsGivenTheWordItsVersePrints()
    {
        var file = SenseReadingFiles.DatasetRecordRulings();
        file.Rulings.Should().NotBeEmpty();
        file.Rulings.Should().OnlyContain(r => r.Existing != null && r.Create == null && r.Position > 0 && r.Surface.Length > 0 && r.Why.Length > 0);
        file.Source.Should().Contain("claude-opus-5-5").And.Contain("2026-09-30").And.Contain("owner's instruction");
        file.DecidedBy.Should().Contain("No person read them");

        var slugs = await Db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id);
        var named = await Db.WordEntities.Where(a => a.Source == file.Source).ToListAsync();
        named.Select(a => (a.WordId, a.EntityId)).Should().BeEquivalentTo(
            file.Rulings.Select(r => (Words[r.Word], slugs[r.Existing!])));
        foreach (var ruling in file.Rulings.Where(r => r.Says?.Name is not null))
        {
            var record = await Db.Entities.AsNoTracking().SingleAsync(e => e.Slug == ruling.Existing);
            record.Name.Should().Be(ruling.Says!.Name);
            record.Source.Should().Be(file.Source);
        }
    }

    /// <summary>
    /// A correction takes back the answer it names, and the word names the record the correction
    /// gives it — on a corpus the earlier ruling was already written into as on a cold one.
    /// </summary>
    [Fact]
    public async Task ACorrectionReplacesTheRulingItCorrects()
    {
        foreach (var correction in Rulings.Where(r => r.Corrects is not null))
        {
            var last = Rulings.Last(r => Words[r.Word] == Words[correction.Word]);
            var named = await Db.WordEntities.Include(a => a.Entity)
                .Where(a => a.WordId == Words[correction.Word])
                .Select(a => a.Entity!.Slug)
                .ToListAsync();
            named.Should().Equal(last.Create?.Slug ?? last.Existing);
        }
    }

    /// <summary>
    /// The owner's review notes reach the records they are about: Saul's family says both genealogies
    /// and which is the more natural, Abiah is no longer Machir's daughter in her line, and the
    /// Manasseh of Judges 18:30 says he is not Joseph's son.
    /// </summary>
    [Fact]
    public async Task WhatTheOwnersReviewNotesAskTheRecordsToSayTheySay()
    {
        var records = await Db.Entities
            .Where(e => new[] { "saul", "kish", "ner", "abner", "abijah-3", "manasseh-2", "orpah", "nebat" }.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug);
        foreach (var family in new[] { "saul", "kish", "ner", "abner" })
        {
            records[family].Notes.Should().Contain("1 Chronicles 8:33").And.Contain("1 Samuel 14:50-51");
        }

        records["abijah-3"].Distinguisher.Should().NotContain("Machir");
        records["abijah-3"].Notes.Should().Contain("the text does not say");
        records["manasseh-2"].Notes.Should().Contain("not Joseph's son");
        records["orpah"].Distinguisher.Should().NotContain("elimination");
        records["nebat"].Distinguisher.Should().NotContain("Ephr");
    }

    /// <summary>
    /// Two captains of one name in one story, whose fathers the verses name differently: each is a
    /// record of his own, the word of Jeremiah 42:1 leaves the Maachathite's son, and both records say
    /// that whether they are one man is open.
    /// </summary>
    [Fact]
    public async Task TheSonOfHoshaiahIsAManOfHisOwnAndBothCaptainsSayTheQuestionIsOpen()
    {
        var file = SenseReadingFiles.AllRulings().Single(f => f.Rulings.Any(r => r.Existing == "jaazaniah-5"));
        var son = file.Rulings.Single(r => r.Existing == "jaazaniah-5");
        son.Reference.Should().Be("JER 42:1");
        son.Corrects.Should().Be("jaazaniah");
        (await Db.WordEntities.Where(a => a.WordId == Words[son.Word]).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jaazaniah-5");

        var captains = await Db.Entities.Where(e => e.Slug == "jaazaniah" || e.Slug == "jaazaniah-5").ToListAsync();
        captains.Should().HaveCount(2).And.OnlyContain(e => e.Notes!.Contains("the text does not settle"));
        captains.Single(e => e.Slug == "jaazaniah").Distinguisher.Should().Contain("Maachathite").And.NotContain("Hoshaiah");
        captains.Single(e => e.Slug == "jaazaniah-5").Distinguisher.Should().Contain("son of Hoshaiah");
    }

    /// <summary>The records the names read again create, each of the kind its verse names.</summary>
    [Fact]
    public async Task TheRecordsTheNamesReadAgainCreateAreAManAManAndAValley()
    {
        var created = await Db.Entities
            .Where(e => e.Slug == "joah-the-later-gershonite" || e.Slug == "shuppim-the-benjamite" || e.Slug == "vale-of-siddim")
            .ToDictionaryAsync(e => e.Slug, e => e.Kind);
        created.Should().BeEquivalentTo(new Dictionary<string, EntityKind>
        {
            ["joah-the-later-gershonite"] = EntityKind.Person,
            ["shuppim-the-benjamite"] = EntityKind.Person,
            ["vale-of-siddim"] = EntityKind.Place,
        });
    }
}
