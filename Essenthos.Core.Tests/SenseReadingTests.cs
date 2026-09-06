using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a model's reading is allowed to become, and — the half that matters more — what it is not.
///
/// This is the step where a guess turns into a row a reader is shown, so every case here is one
/// where the wrong behaviour would be invisible afterwards: a reading somebody has already
/// established is false, a reading the run itself contradicts, a band nobody measured, and a
/// reading standing against an answer the lexicon already resolved. Each of those would produce a
/// card that looks exactly like scholarship.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SenseReadingTests : IDisposable
{
    /// <summary>
    /// A word the review files already speak about, so the shipped refusal list is what the test
    /// exercises rather than a fixture standing in for it. Nehemiah 8:7 is one of the seven readings
    /// the adjudication found the model wrong about.
    /// </summary>
    private const long RefusedWord = 6854130;

    private readonly AppDbContext _db;
    private readonly string _readings;
    private readonly Text _hebrew;
    private readonly Text _english;

    public SenseReadingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");

        _readings = Path.Combine(Path.GetTempPath(), $"sense-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_readings, "run-1"));

        _hebrew = Corpus.Add(_db, SenseReadingLoader.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["זכריה"]),
            (1, 2, ["זכריה"]),
            (1, 3, ["זכריה"]),
            (1, 4, ["זכריה"]),
            (1, 5, ["זכריה"]),
            (1, 6, ["משה"]));

        _english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng",
            (1, 1, ["Zechariah"]),
            (1, 6, ["Moses"]));

        _db.SaveChanges();

        Mark(1, "H2148");
        Mark(2, "H2148");
        Mark(3, "H2148");
        Mark(4, "H2148");
        Mark(5, "H2148");
        Mark(6, "H4872");

        Person("zechariah-1", "Zechariah", "H2148");
        Person("zechariah-2", "Zechariah", "H2148");
        var moses = Person("moses", "Moses", "H4872");
        _db.SaveChanges();

        _db.EntityVerses.Add(new EntityVerse
        {
            EntityId = _db.Entities.Single(e => e.Slug == "zechariah-1").Id,
            CanonicalBook = 1,
            CanonicalChapter = 1,
            CanonicalVerse = 1,
            Source = "a test",
        });
        _db.SaveChanges();
        _ = moses;
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
        if (Directory.Exists(_readings))
        {
            Directory.Delete(_readings, recursive: true);
        }
    }

    private void Mark(int verse, string number)
    {
        var word = _db.WordAt(_hebrew, 1, verse, 1);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "pers"}""");
        _db.SaveChanges();
    }

    private Entity Person(string slug, string name, string number)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(entity);
        _db.EntityNames.Add(new EntityName
        {
            Entity = entity, Label = name, HebrewStrongNumber = number, Kind = "name",
        });
        return entity;
    }

    /// <summary>One answer, written the way a run writes them.</summary>
    private void Answer(long wordId, string referent, string confidence, string? reason = null)
    {
        var row = JsonSerializer.Serialize(new
        {
            word_id = wordId,
            strong_number = "H2148",
            referent,
            names = (string?)null,
            confidence,
            reason = reason ?? "the verse decides it",
            prompt_version = "sense-1",
            model = "a-model",
            run = "2026-09-05T21:35:25+00:00",
        });

        File.AppendAllText(
            Path.Combine(_readings, "run-1", SenseReadingFiles.AnswersFileName), row + Environment.NewLine);
    }

    private SenseReadingLoader Loader() =>
        new(
            _db,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [SenseReadingFiles.ConfigurationKey] = _readings,
                })
                .Build(),
            NullLogger<SenseReadingLoader>.Instance);

    private async Task<Dictionary<long, WordEntity>> Load()
    {
        await Loader().Load("unused, the path is configured");
        return await _db.WordEntities.Include(a => a.Entity).ToDictionaryAsync(a => a.WordId, a => a);
    }

    private Word Hebrew(int verse) => _db.WordAt(_hebrew, 1, verse, 1);

    [Fact]
    public async Task AReadingAnnotatesAWordNoNumberCouldSettle()
    {
        Answer(Hebrew(1).Id, "zechariah-2", "high");

        var named = await Load();
        named.Should().ContainKey(Hebrew(1).Id)
            .WhoseValue.Entity!.Slug.Should().Be("zechariah-2");
    }

    /// <summary>
    /// The method is its own, and the row says which model, under which prompt, on which day. A
    /// reading stored under the resolution's method would be a guess wearing the lexicon's name.
    /// </summary>
    [Fact]
    public async Task AReadingSaysWhoReadItAndWhen()
    {
        Answer(Hebrew(1).Id, "zechariah-2", "high", "the son of Berechiah, as the verse says");

        var named = await Load();
        var annotation = named[Hebrew(1).Id];

        annotation.Method.Should().Be(LinkMethod.ModelReading);
        annotation.Source.Should().Contain("a-model").And.Contain("sense-1").And.Contain("2026-09-05");
        annotation.Note.Should().Contain("the son of Berechiah");
        annotation.Confidence.Should().Be(0.99);
    }

    /// <summary>
    /// The confidence is the measured survival of the band, and the two bands are nine times apart.
    /// Flattening them would throw away the one calibration the run actually has.
    /// </summary>
    [Fact]
    public async Task TheBandTheModelChoseReachesTheRow()
    {
        Answer(Hebrew(1).Id, "zechariah-2", "high");
        Answer(Hebrew(2).Id, "zechariah-2", "medium");

        var named = await Load();
        named[Hebrew(1).Id].Confidence.Should().Be(0.99);
        named[Hebrew(2).Id].Confidence.Should().Be(0.93);
    }

    /// <summary>
    /// The model used <c>low</c> on seventeen answers in the whole corpus and one of them names an
    /// entity, so the band has no measured rate. A number invented for it would be exactly the
    /// heuristic-dressed-as-scholarship this whole design refuses — on the rows the model itself was
    /// least sure of.
    /// </summary>
    [Fact]
    public async Task AReadingInABandNobodyMeasuredIsNotLoaded()
    {
        Answer(Hebrew(1).Id, "zechariah-2", "low");

        var named = await Load();
        named.Should().NotContainKey(Hebrew(1).Id);
    }

    /// <summary>
    /// The model saying the encyclopedia holds nobody who fits is the most valuable answer in the
    /// file and the one a tie-break could never give. It is still not an annotation: there is no
    /// record to point at, and inventing one here would be the loader deciding what the encyclopedia
    /// contains.
    /// </summary>
    [Fact]
    public async Task AReadingThatNamesNobodyTheEncyclopediaHoldsWritesNothing()
    {
        Answer(Hebrew(1).Id, SenseReading.Unlisted, "high");
        Answer(Hebrew(2).Id, SenseReading.Unclear, "medium");

        var named = await Load();
        named.Should().BeEmpty();
    }

    /// <summary>
    /// A reading a later pass read again and contradicted is evidence about the method, not data
    /// about the text. Loading it at a lower confidence would be the corpus publishing something it
    /// knows to be false and hedging about it.
    /// </summary>
    [Fact]
    public async Task AReadingASecondPassFoundWrongIsRefused()
    {
        _db.Database.ExecuteSqlRaw("UPDATE word SET id = {0} WHERE id = {1}", RefusedWord, Hebrew(1).Id);
        Answer(RefusedWord, "zechariah-2", "high");

        var named = await Load();
        named.Should().NotContainKey(RefusedWord);
    }

    /// <summary>
    /// The whole refusal list, asserted as counts so that a file quietly emptied by a bad
    /// regeneration fails the build instead of silently loading seventy-four answers somebody has
    /// already shown to be wrong.
    /// </summary>
    [Fact]
    public void TheReviewFilesRefuseSeventyFourReadings()
    {
        var refused = SenseReadingFiles.Refused().Readings;

        refused.Should().HaveCount(74);
        refused.Count(r => r.Verdict == "wrong").Should().Be(61);
        refused.Count(r => r.Verdict == "model-wrong").Should().Be(7);
        refused.Count(r => r.Verdict == "both-wrong").Should().Be(4);
        refused.Count(r => r.Verdict == "contested").Should().Be(2);
        refused.Select(r => r.WordId).Should().OnlyHaveUniqueItems();
        refused.Should().OnlyContain(r => r.Why.Length > 0);
    }

    /// <summary>
    /// A run that answered one word two different ways has not answered it. Taking the first would
    /// make which shard finished first into a fact about the text.
    /// </summary>
    [Fact]
    public async Task AWordTheRunAnsweredTwoWaysIsRefused()
    {
        Answer(Hebrew(1).Id, "zechariah-1", "high");
        Answer(Hebrew(1).Id, "zechariah-2", "high");

        var named = await Load();
        named.Should().NotContainKey(Hebrew(1).Id);
    }

    /// <summary>
    /// The encyclopedia's own list of verses is a second and independent answer, so where it agrees
    /// it is recorded as a second claim — and deliberately does not raise the row's number, because
    /// that list is known to put verses on the wrong man.
    /// </summary>
    [Fact]
    public async Task AVerseTheEncyclopediaAgreesAboutCarriesASecondClaim()
    {
        Answer(Hebrew(1).Id, "zechariah-1", "high");
        Answer(Hebrew(2).Id, "zechariah-1", "high");

        var named = await Load();
        var corroborated = named[Hebrew(1).Id];
        var alone = named[Hebrew(2).Id];

        (await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == corroborated.Id)).Should().Be(2);
        (await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == alone.Id)).Should().Be(1);
        corroborated.Confidence.Should().Be(alone.Confidence);
    }

    [Fact]
    public async Task AReadingTravelsToTheWordThatRendersIt()
    {
        Answer(Hebrew(1).Id, "zechariah-2", "high");
        Link(Hebrew(1), _db.WordAt(_english, 1, 1, 1), LinkMethod.Aligner, 0.5);

        var named = await Load();
        var carried = named[_db.WordAt(_english, 1, 1, 1).Id];

        carried.Entity!.Slug.Should().Be("zechariah-2");
        carried.Confidence.Should().BeApproximately(0.99 * 0.5, 1e-9);
    }

    /// <summary>
    /// The safety property the whole standing order exists for: a reading can add an answer where
    /// the numbers left none, and can never take one away. Moses is resolved by his number and by
    /// the aligner reaching the English word; a reading of the same English word naming somebody
    /// else must leave what the reader sees exactly as it was.
    /// </summary>
    [Fact]
    public async Task AReadingDoesNotDisplaceWhatTheNumbersResolved()
    {
        var english = _db.WordAt(_english, 1, 6, 1);
        Link(Hebrew(6), english, LinkMethod.StatedBySource, null);
        await new EntityAnnotationLoader(_db, NullLogger<EntityAnnotationLoader>.Instance).Load();

        Answer(Hebrew(1).Id, "zechariah-2", "high");
        Link(Hebrew(1), english, LinkMethod.Aligner, 0.5);
        await Loader().Load("unused, the path is configured");

        var shown = await Annotations.Of(_db, english.Id, default);
        shown.Should().NotBeNull();
        shown!.Slug.Should().Be("moses");
        shown.Method.Should().Be("strong-number");
    }

    /// <summary>
    /// And the rule the readings must not break: two methods of the same standing naming two
    /// different people is the corpus saying it does not know, and it still shows nothing.
    /// </summary>
    [Fact]
    public async Task TwoMethodsOfEqualStandingDisagreeingStillShowNothing()
    {
        var word = Hebrew(1);
        await Load();

        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = _db.Entities.Single(e => e.Slug == "zechariah-1").Id,
            Method = LinkMethod.ModelReading,
            Confidence = 0.99,
            Source = "a second reading",
        });
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = _db.Entities.Single(e => e.Slug == "zechariah-2").Id,
            Method = LinkMethod.ModelReading,
            Confidence = 0.99,
            Source = "a third reading",
        });
        await _db.SaveChangesAsync();

        (await Annotations.Of(_db, word.Id, default)).Should().BeNull();
    }

    [Fact]
    public async Task RunningAgainWritesNothingFurther()
    {
        Answer(Hebrew(1).Id, "zechariah-2", "high");
        await Load();
        var first = await _db.WordEntities.CountAsync();

        var again = await Loader().Load("unused, the path is configured");

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(first);
    }

    [Fact]
    public async Task AMissingReadingsDirectoryIsNotAFailure()
    {
        Directory.Delete(_readings, recursive: true);

        var outcome = await Loader().Load("unused, the path is configured");

        outcome.NoReadings.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(0);
    }

    private void Link(Word from, Word to, LinkMethod method, double? confidence)
    {
        var link = new Link
        {
            FromTextId = from.TextId,
            ToTextId = to.TextId,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = from, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = to, Side = LinkSide.To });
        _db.SaveChanges();
    }
}
