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

        var verses = _rulings
            .Select((ruling, position) => (Chapter: 1, Verse: position + 1, Words: new[] { ruling.StrongNumber }))
            .ToArray();

        _hebrew = Corpus.Add(_db, SenseReadingLoader.Witness, TextKind.CriticalEdition, "hbo", verses);
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["Azariah"]));
        _db.SaveChanges();

        // The words the rulings are about, at the ids the rulings name.
        for (var position = 0; position < _rulings.Count; position++)
        {
            var word = _db.WordAt(_hebrew, 1, position + 1, 1);
            word.StrongNumber = _rulings[position].StrongNumber;
            word.Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "pers"}""");
            _db.SaveChanges();
            _db.Database.ExecuteSqlRaw(
                "UPDATE word SET id = {0} WHERE id = {1}", _rulings[position].WordId, word.Id);
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
        named.Select(a => a.WordId).Should().BeEquivalentTo(_rulings.Select(r => r.WordId));
        named.Should().OnlyContain(a => a.Method == LinkMethod.Manual);
        named.Should().OnlyContain(a => a.Confidence == null);
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
            .Should().Be(outcome.Created);
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
            .Should().Be(outcome.Created);
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
            name.HebrewStrongNumber.Should().Be(ruling.StrongNumber);
            name.GreekStrongNumber.Should().BeNull();
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
    /// A word is ruled on once. Two files ruling on one word would both annotate it, and a reader
    /// would meet a word naming whichever of two decisions the loader happened to write first.
    /// </summary>
    [Fact]
    public void NoWordIsRuledOnTwice()
    {
        _rulings.Select(r => r.WordId).Should().OnlyHaveUniqueItems();
    }
}
