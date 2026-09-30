using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The records this corpus writes for itself, exercised against the rulings it actually ships
/// rather than against a fixture standing in for them.
///
/// The words are given the ids the ruling file names, so what is under test is the file a reader
/// would be shown from — a ruling whose word id drifted, or whose referent slug was renamed away,
/// fails here rather than quietly annotating nothing on a live database.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<OwnRecordRuling> _rulings;
    private readonly Text _hebrew;
    private readonly Text _english;

    public OwnRecordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _rulings = [.. SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)];

        var ruled = _rulings.DistinctBy(ruling => ruling.WordId).ToList();
        var verses = ruled
            .Select((ruling, position) => (Chapter: 1, Verse: position + 1, Words: new[] { ruling.StrongNumber ?? "name" }))
            .ToArray();

        _hebrew = Corpus.Add(_db, SenseReadingLoader.Witness, TextKind.CriticalEdition, "hbo", verses);
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["Azariah"]));
        _db.SaveChanges();

        // The words the rulings are about, at the ids the rulings name.
        for (var position = 0; position < ruled.Count; position++)
        {
            var word = _db.WordAt(_hebrew, 1, position + 1, 1);
            word.StrongNumber = ruled[position].StrongNumber;
            word.Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "pers"}""");
            _db.SaveChanges();
            _db.Database.ExecuteSqlRaw(
                "UPDATE word SET id = {0} WHERE id = {1}", ruled[position].WordId, word.Id);
        }

        // The records the rulings point at or name as alternatives.
        foreach (var slug in Named())
        {
            _db.Entities.Add(new Entity
            {
                Kind = EntityKind.Person,
                Slug = slug,
                Name = slug,
                SourceId = slug,
                Source = "a test",
            });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    /// <summary>Every record a ruling refers to and does not create.</summary>
    private IEnumerable<string> Named() =>
        _rulings
            .SelectMany(r => new[] { r.Existing }
                .Concat(r.Alternatives?.Select(a => a.Slug) ?? []))
            .Where(slug => slug is not null)
            .Select(slug => slug!)
            .Where(slug => _rulings.All(r => r.Create?.Slug != slug))
            .Distinct();

    private OwnRecordLoader Loader(bool bulk = false) =>
        new(
            _db,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [OwnRecordLoader.BulkConfigurationKey] = bulk ? "true" : "false",
                })
                .Build(),
            NullLogger<OwnRecordLoader>.Instance);

    private Task<OwnRecordOutcome> Load() =>
        Loader().Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}"));

    [Fact]
    public async Task EveryRulingAnnotatesItsWord()
    {
        await Load();

        var named = await _db.WordEntities.Include(a => a.Entity).ToListAsync();
        named.Select(a => a.WordId).Should().BeEquivalentTo(_rulings.Select(r => r.WordId).Distinct());
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
        await Load();

        var woman = await _db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == "daughter");
        woman.Name.Should().Be("Woman with an issue of blood");
        woman.Distinguisher.Should().Contain("MAT 9:20-22");
        woman.Notes.Should().Contain("MAT 9:22; MRK 5:34; LUK 8:48").And.Contain("does not give her name");
        woman.Claims.Should().ContainSingle(claim => claim.Method == LinkMethod.Manual)
            .Which.Source.Should().Contain("2026-09-30");

        var words = SenseReadingFiles.AddressedRulings().Rulings.Select(r => r.WordId);
        (await _db.WordEntities.Where(a => a.EntityId == woman.Id).Select(a => a.WordId).ToListAsync())
            .Should().BeEquivalentTo(words);
    }

    /// <summary>
    /// The woman's record was listed by a dataset and is headed, described and read by us, so the
    /// page credits the ruling and not the dataset, on a database that loaded the ruling before the
    /// credit moved as well as on a cold one. The dataset's listing stays as a claim.
    /// </summary>
    [Fact]
    public async Task ARecordTheRulingReHeadsIsCreditedToTheRulingAndACreditIsNotMovedTwice()
    {
        await Load();
        await Load();

        var woman = await _db.Entities.Include(e => e.Claims).SingleAsync(e => e.Slug == "daughter");
        woman.Source.Should().StartWith("Essenthos").And.Contain("2026-09-30");
        woman.Claims.Should().ContainSingle(claim => claim.Method == LinkMethod.StatedBySource)
            .Which.Source.Should().Be("a test");

        // A database that loaded the file before the credit moved: the dataset's credit is back and
        // the file is already recorded, so nothing else of the file runs again.
        woman.Source = "BibleData by a test";
        await _db.SaveChangesAsync();
        await Load();
        (await _db.Entities.SingleAsync(e => e.Slug == "daughter")).Source.Should().StartWith("Essenthos");
    }

    /// <summary>
    /// A record we wrote says it is ours, in the source a reader is shown and in a claim carrying
    /// the method and whoever decided. A dataset's row carries neither, so the two can never be
    /// confused.
    /// </summary>
    [Fact]
    public async Task ARecordWeWroteSaysItIsOursAndWhatEstablishedIt()
    {
        await Load();

        foreach (var ruling in _rulings.Where(r => r.Create is not null))
        {
            var written = await _db.Entities
                .Include(e => e.Claims)
                .Include(e => e.Verses)
                .SingleAsync(e => e.Slug == ruling.Create!.Slug);

            written.Source.Should().StartWith("Essenthos");
            written.SourceId.Should().StartWith("essenthos:");
            written.Claims.Should().ContainSingle()
                .Which.Should().Match<EntityClaim>(c =>
                    c.Method == LinkMethod.Manual && c.Confidence == null && c.Note == ruling.Why);
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
        await Load();

        var ruling = _rulings.First(r => r.Create is not null);
        var word = await _db.Words.SingleAsync(w => w.Id == ruling.WordId);
        var reference = await _db.VerseReferences.SingleAsync(r => r.VerseId == word.VerseId && r.IsPrimary);
        var rests = await _db.EntityVerses.SingleAsync(v => v.Entity!.Slug == ruling.Create!.Slug);

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
        await Load();

        foreach (var ruling in _rulings.Where(r => r.Alternatives is { Count: > 0 }))
        {
            var slug = ruling.Create?.Slug ?? ruling.Existing!;
            var alternatives = await _db.EntityAlternatives
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
        var outcome = await Load();

        outcome.Created.Should().Be(_rulings.Count(r => r.Create is not null));
        (await _db.Entities.CountAsync(e => e.Source.StartsWith("Essenthos")))
            .Should().Be(outcome.Created + _rulings.Count(r => r.Existing is not null && r.Says?.Name is not null));
    }

    /// <summary>
    /// A person decided who is named, so the seed carries no confidence — and a word reached across
    /// a link that is itself a guess does carry one, because there the reach is what is uncertain
    /// and not the decision.
    /// </summary>
    [Fact]
    public async Task ADecisionCarriedAcrossAnUncertainLinkPicksUpTheLinksConfidence()
    {
        var ruling = _rulings[0];
        var hebrew = await _db.Words.SingleAsync(w => w.Id == ruling.WordId);
        var english = _db.WordAt(_english, 1, 1, 1);

        var link = new Link
        {
            FromTextId = hebrew.TextId,
            ToTextId = english.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.5,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = hebrew, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = english, Side = LinkSide.To });
        await _db.SaveChangesAsync();

        await Load();

        var carried = await _db.WordEntities.SingleAsync(a => a.WordId == english.Id);
        carried.Method.Should().Be(LinkMethod.Manual);
        carried.Confidence.Should().Be(0.5);
    }

    /// <summary>
    /// The bulk pass is built and off. With no readings on disk there is nothing to count either
    /// way, and what matters is that the switch decides rather than the presence of the files.
    /// </summary>
    [Fact]
    public async Task TheBulkPassWritesNothingWhileTheSwitchIsOff()
    {
        var outcome = await Load();

        outcome.Withheld.Should().Be(0);
        (await _db.Entities.CountAsync(e => e.Source.StartsWith("Essenthos")))
            .Should().Be(outcome.Created + _rulings.Count(r => r.Existing is not null && r.Says?.Name is not null));
    }

    [Fact]
    public async Task RunningAgainWritesNothingFurther()
    {
        await Load();
        var entities = await _db.Entities.CountAsync();
        var annotations = await _db.WordEntities.CountAsync();
        var names = await _db.EntityNames.CountAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        again.Labelled.Should().Be(0);
        (await _db.Entities.CountAsync()).Should().Be(entities);
        (await _db.WordEntities.CountAsync()).Should().Be(annotations);
        (await _db.EntityNames.CountAsync()).Should().Be(names);
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
        var outcome = await Load();

        outcome.Labelled.Should().Be(_rulings.Count(r => r.Create is not null));

        foreach (var ruling in _rulings.Where(r => r.Create is not null))
        {
            var written = await _db.Entities
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
    /// And a record written before this pass named anything is named on the next boot. The guard
    /// that skips the rulings is what would otherwise keep it unnamed for ever — the records this
    /// is about are already there, so a step that named only what it created on this boot would
    /// never reach one of them.
    /// </summary>
    [Fact]
    public async Task ARecordWrittenBeforeThisPassNamedAnythingIsNamedOnTheNextBoot()
    {
        await Load();
        _db.EntityNames.RemoveRange(await _db.EntityNames.ToListAsync());
        await _db.SaveChangesAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        again.Labelled.Should().Be(_rulings.Count(r => r.Create is not null));
        (await _db.EntityNames.CountAsync()).Should().Be(again.Labelled);
    }

    /// <summary>
    /// A rulings file arriving on a corpus that already holds the others is applied on the next
    /// boot, and only it: the files are decided at different times, so whether one is recorded says
    /// nothing about another.
    /// </summary>
    [Fact]
    public async Task ARulingsFileTheCorpusDoesNotYetHoldIsAppliedAloneOnTheNextBoot()
    {
        await Load();
        var report = SenseReadingFiles.ReportRulings();
        await _db.WordEntities.Where(a => a.Source == report.Source).ExecuteDeleteAsync();
        var entities = await _db.Entities.CountAsync();

        var again = await Load();

        again.AlreadyLoaded.Should().BeFalse();
        again.Created.Should().Be(0);
        (await _db.Entities.CountAsync()).Should().Be(entities);
        (await _db.WordEntities.Where(a => a.Source == report.Source).Select(a => a.WordId).ToListAsync())
            .Should().BeEquivalentTo(report.Rulings.Select(r => r.WordId));
    }

    /// <summary>
    /// 1 Chronicles 1:9, where a reading had put the trading place on the son of Cush. The ruling is
    /// the answer, so the reading at the word and the copy the links carried from it are taken back
    /// rather than left beside it.
    /// </summary>
    [Fact]
    public async Task ARulingTakesBackTheReadingItOverrulesAndItsCarriedCopy()
    {
        var ruling = SenseReadingFiles.GenealogyRulings().Rulings[0];
        var place = new Entity
        {
            Kind = EntityKind.Place, Slug = "raamah-2", Name = "Raamah", SourceId = "raamah-2", Source = "a test",
        };
        _db.Entities.Add(place);
        await _db.SaveChangesAsync();

        const string reading = "a reading of the verse by a-model, prompt sense-1, run to 2026-09-06";
        var rendering = _db.WordAt(_english, 1, 1, 1);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = ruling.WordId, EntityId = place.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.99, Source = reading, Note = "read as the place",
        });
        _db.WordEntities.Add(new WordEntity
        {
            WordId = rendering.Id, EntityId = place.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.97, Source = reading,
            Note = $"through BHSA word {ruling.WordId}, linked by aligner",
        });
        await _db.SaveChangesAsync();

        await Load();

        (await _db.WordEntities.AnyAsync(a => a.EntityId == place.Id)).Should().BeFalse();
        (await _db.WordEntities.Include(a => a.Entity).SingleAsync(a => a.WordId == ruling.WordId))
            .Entity!.Slug.Should().Be(ruling.Existing);
    }

    /// <summary>
    /// A word is ruled on once, unless a later ruling corrects the earlier one and says which answer
    /// it takes back. Two files ruling on one word otherwise both annotate it, and a reader would
    /// meet a word naming whichever of two decisions the loader happened to write first.
    /// </summary>
    [Fact]
    public void NoWordIsRuledOnTwiceUnlessTheLaterRulingCorrectsTheEarlier()
    {
        foreach (var word in _rulings.GroupBy(r => r.WordId).Where(g => g.Count() > 1))
        {
            var (earlier, later) = (word.First(), word.Last());
            word.Count().Should().Be(2, $"word {word.Key} is ruled on once and corrected once at most");
            later.Corrects.Should().Be(earlier.Existing, $"the ruling on {later.Reference} corrects the earlier one");
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
        file.Rulings.Should().OnlyContain(r => r.Existing != null && r.Create == null && r.WordId > 0
                                               && r.StrongNumber != null && r.StrongNumber.Length > 1 && r.Why.Length > 0);
        file.Source.Should().Contain("claude-sonnet").And.Contain("claude-opus").And.Contain("2026-09-30");
        file.DecidedBy.Should().Contain("No person read them");

        await Load();

        var slugs = await _db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id);
        var named = await _db.WordEntities.Where(a => a.Source == file.Source).ToListAsync();
        named.Select(a => (a.WordId, a.EntityId)).Should().BeEquivalentTo(
            file.Rulings.Select(r => (r.WordId, slugs[r.Existing!])));
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
        file.Rulings.Should().OnlyContain(r => r.Existing != null && r.Create == null && r.WordId > 0 && r.Why.Length > 0);
        file.Source.Should().Contain("claude-opus-5-5").And.Contain("2026-09-30").And.Contain("owner's instruction");
        file.DecidedBy.Should().Contain("No person read them");

        await Load();

        var slugs = await _db.Entities.ToDictionaryAsync(e => e.Slug, e => e.Id);
        var named = await _db.WordEntities.Where(a => a.Source == file.Source).ToListAsync();
        named.Select(a => (a.WordId, a.EntityId)).Should().BeEquivalentTo(
            file.Rulings.Select(r => (r.WordId, slugs[r.Existing!])));
        foreach (var ruling in file.Rulings.Where(r => r.Says?.Name is not null))
        {
            var record = await _db.Entities.AsNoTracking().SingleAsync(e => e.Slug == ruling.Existing);
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
        await Load();

        foreach (var correction in _rulings.Where(r => r.Corrects is not null))
        {
            var named = await _db.WordEntities.Include(a => a.Entity)
                .Where(a => a.WordId == correction.WordId)
                .Select(a => a.Entity!.Slug)
                .ToListAsync();
            named.Should().Equal(correction.Existing);
        }
    }
}
