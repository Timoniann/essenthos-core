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
/// The split reaching the words, over the two Jerichos as the corpus actually holds them.
///
/// What the design promises a reader is the difference between the three standings, and every test
/// here is one of those differences: an occurrence two independent accounts agree about carries a
/// method saying an inference reached it and a second claim naming the model; one only the gazetteer
/// places carries the same method and no model claim; one only a reading settles carries
/// <c>model-reading</c>, which is the method that may never displace a resolution. Beside those, the
/// two refusals — an occurrence nothing settled writes nothing, and a word something else already
/// named is left exactly as it was, because a word naming two places is worse than a word naming
/// none.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SiteSplitLoadTests : IDisposable
{
    private const string Gazetteer =
        "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    private const string Number = "H3405";

    private readonly AppDbContext _db;
    private readonly string _folder;
    private readonly Text _witness;

    public SiteSplitLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");

        _folder = Path.Combine(Path.GetTempPath(), $"sites-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);

        Place("jericho", "Jericho", "Tell es Sultan");
        Place("jericho-2", "Jericho", "Tell el Alayiq");
        _db.SaveChanges();

        _witness = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["יריחו"]), (1, 2, ["יריחו"]), (1, 3, ["יריחו"]), (1, 4, ["יריחו"]));
        _db.SaveChanges();

        for (var verse = 1; verse <= 4; verse++)
        {
            var word = _db.WordAt(_witness, 1, verse, 1);
            word.StrongNumber = Number;
            word.Morphology = JsonDocument.Parse("""{"pos": "subs", "nameType": "topo"}""");
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task A_register_naming_its_word_by_row_id_is_refused_with_the_converter_named()
    {
        File.WriteAllText(Path.Combine(_folder, "register-0000.jsonl"),
            JsonSerializer.Serialize(new { number = Number, name = "Jericho", wordId = _db.WordAt(_witness, 1, 1, 1).Id, why = "a test" }) + "\n");

        var load = () => Load();

        await load.Should().ThrowAsync<InvalidDataException>().WithMessage("*address-word-ids.py*");
    }

    [Fact]
    public async Task An_occurrence_whose_word_reads_otherwise_now_is_not_annotated()
    {
        Register(Occurrence(1, "jericho", SiteRegisterFiles.ByBoth));
        var word = _db.WordAt(_witness, 1, 1, 1);
        word.Surface = "another reading";
        _db.SaveChanges();

        await Load();

        (await _db.WordEntities.AnyAsync(a => a.WordId == word.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task An_occurrence_both_accounts_agree_about_is_written_as_an_inference_naming_the_model()
    {
        Register(Occurrence(1, "jericho", SiteRegisterFiles.ByBoth));

        var outcome = await Load();

        outcome.Agreed.Should().Be(1);
        outcome.Written.Should().Be(1);

        var annotation = await Annotation(1);
        annotation.Should().NotBeNull();
        annotation!.Entity.Slug.Should().Be("jericho");
        annotation.Method.Should().Be(LinkMethod.Lexical,
            "a verse list is a statement about the verse, and reaching the word from it is an inference");
        annotation.Confidence.Should().NotBeNull("an inference may never be stored without one");
        annotation.Claims.Should().Contain(claim => claim.Method == LinkMethod.ModelReading,
            "a reading is half of what established it and must arrive with the model on it");
    }

    [Fact]
    public async Task An_occurrence_only_the_gazetteer_places_carries_no_model_claim()
    {
        Register(Occurrence(1, "jericho", SiteRegisterFiles.ByTheGazetteer));

        var outcome = await Load();

        outcome.Stated.Should().Be(1);

        var annotation = await Annotation(1);
        annotation!.Method.Should().Be(LinkMethod.Lexical);
        annotation.Claims.Should().NotContain(claim => claim.Method == LinkMethod.ModelReading,
            "the reading declined, and crediting it with the answer would be a claim of the wrong kind");
    }

    [Fact]
    public async Task An_occurrence_only_a_reading_settles_is_written_as_a_reading()
    {
        Register(Occurrence(1, "jericho-2", SiteRegisterFiles.ByTheReading));

        var outcome = await Load();

        outcome.Read.Should().Be(1);

        var annotation = await Annotation(1);
        annotation!.Entity.Slug.Should().Be("jericho-2");
        annotation.Method.Should().Be(LinkMethod.ModelReading,
            "nothing but a reading of the sentence stands behind it");
        annotation.Source.Should().StartWith("a reading of the verse by claude-sonnet-5");
        annotation.Source.Should().Contain("sites-1").And.Contain("2026-09-10");
    }

    [Fact]
    public async Task An_occurrence_nothing_settled_writes_nothing_and_is_counted()
    {
        Register(Unsettled(1), Occurrence(2, "jericho", SiteRegisterFiles.ByBoth));

        var outcome = await Load();

        outcome.Occurrences.Should().Be(2);
        outcome.Unsettled.Should().Be(1);
        outcome.Written.Should().Be(1);
        (await Annotation(1)).Should().BeNull(
            "a page that names one of its two verses is worth more than one that guesses at both");
    }

    /// <summary>
    /// <c>word_entity</c> is unique on the word and the entity rather than on the word, so a second
    /// referent lands beside the first instead of failing. A reader would then meet a word that names
    /// two towns four kilometres apart, which is the exact failure this whole pass exists to end.
    /// </summary>
    [Fact]
    public async Task A_word_something_else_already_names_is_left_alone()
    {
        var other = await _db.Entities.FirstAsync(e => e.Slug == "jericho-2");
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(_witness, 1, 1, 1).Id,
            Entity = other,
            Method = LinkMethod.StrongNumber,
            Confidence = 0.9,
            Source = "an earlier pass",
        });
        await _db.SaveChangesAsync();

        Register(Occurrence(1, "jericho", SiteRegisterFiles.ByBoth));

        var outcome = await Load();

        outcome.Spoken.Should().Be(1);
        outcome.Written.Should().Be(0);

        var word = _db.WordAt(_witness, 1, 1, 1);
        var annotations = await _db.WordEntities.Where(a => a.WordId == word.Id).ToListAsync();
        annotations.Should().ContainSingle().Which.Source.Should().Be("an earlier pass");
    }

    /// <summary>
    /// The same word, the same answer. Here the earlier row is the one the register reaches, so the
    /// register has nothing to add but its own claim — and a claim on an answer somebody else already
    /// wrote is corroboration, which belongs on the row rather than beside it.
    /// </summary>
    [Fact]
    public async Task A_word_already_named_as_this_register_names_it_gains_the_claim_and_not_a_second_row()
    {
        var same = await _db.Entities.FirstAsync(e => e.Slug == "jericho");
        _db.WordEntities.Add(new WordEntity
        {
            WordId = _db.WordAt(_witness, 1, 1, 1).Id,
            Entity = same,
            Method = LinkMethod.StrongNumber,
            Confidence = 0.9,
            Source = "an earlier pass",
        });
        await _db.SaveChangesAsync();

        Register(Occurrence(1, "jericho", SiteRegisterFiles.ByBoth));

        await Load();

        var annotation = await Annotation(1);
        annotation!.Source.Should().Be("an earlier pass", "another method agreeing is not a second answer");
        annotation.Claims.Should().Contain(claim => claim.Source.StartsWith(SiteSplitLoader.SourcePrefix));
    }

    [Fact]
    public async Task An_answer_naming_a_record_the_encyclopedia_no_longer_holds_writes_nothing()
    {
        Register(Occurrence(1, "jericho-9", SiteRegisterFiles.ByBoth));

        var outcome = await Load();

        outcome.Unresolvable.Should().Be(1);
        outcome.Written.Should().Be(0);
    }

    [Fact]
    public async Task Running_it_twice_writes_the_split_once()
    {
        Register(Occurrence(1, "jericho", SiteRegisterFiles.ByBoth));

        await Load();
        var again = await Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_checkout_without_the_register_loads_nothing_and_says_so()
    {
        var outcome = await Load(Path.Combine(_folder, "not-here"));

        outcome.AlreadyLoaded.Should().BeFalse();
        outcome.Occurrences.Should().Be(0);
        (await _db.WordEntities.CountAsync()).Should().Be(0);
    }

    private Entity Place(string slug, string name, string site)
    {
        var place = new Entity
        {
            Kind = EntityKind.Place,
            Slug = slug,
            Name = name,
            SourceId = $"openbible:{slug}",
            Source = Gazetteer,
            OpenBibleId = slug,
            ModernEquivalent = site,
        };
        _db.Entities.Add(place);
        return place;
    }

    private async Task<WordEntity?> Annotation(int verse)
    {
        var word = _db.WordAt(_witness, 1, verse, 1);
        return await _db.WordEntities
            .Include(a => a.Entity)
            .Include(a => a.Claims)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.WordId == word.Id);
    }

    private object Occurrence(int verse, string referent, string standing) =>
        Line(verse, referent, standing, $"settled by the {standing} account");

    private object Unsettled(int verse) =>
        Line(verse, null, null, "neither the gazetteer nor a reading of the verse names one of them");

    private object Line(int verse, string? referent, string? standing, string why) =>
        new
        {
            number = Number,
            name = "Jericho",
            address = _db.Address(_db.WordAt(_witness, 1, verse, 1).Id),
            witness = EntityCandidates.Witness,
            reference = $"GEN 1:{verse}",
            spelling = "יריחו",
            referent,
            standing,
            why,
            candidates = new[] { "jericho", "jericho-2" },
            gazetteer = referent is null ? Array.Empty<string>() : new[] { referent },
            otherDataset = Array.Empty<string>(),
            annotated = Array.Empty<string>(),
            reading = new
            {
                referent,
                confidence = "high",
                reason = "the verse names the Jordan beside it",
                model = "claude-sonnet-5",
                effort = "medium",
                promptVersion = "sites-1",
                askedAt = "2026-09-10T12:00:00+00:00",
                note = (string?)null,
            },
            check = (object?)null,
        };

    private void Register(params object[] lines) =>
        File.WriteAllLines(
            Path.Combine(_folder, "register-0000.jsonl"),
            lines.Select(line => JsonSerializer.Serialize(line)));

    private async Task<SiteSplitOutcome> Load(string? folder = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SiteRegisterFiles.ConfigurationKey] = folder ?? _folder,
            })
            .Build();

        var loader = new SiteSplitLoader(
            _db, configuration, NullLogger<SiteSplitLoader>.Instance);
        return await loader.Load(_folder);
    }
}
