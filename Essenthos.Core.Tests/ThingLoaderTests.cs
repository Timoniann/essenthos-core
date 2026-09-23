using Essenthos.Core.Corpus;
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
/// The objects and the appointed times, written from the record files, and the rules that decide
/// which occurrences of a word are the thing.
///
/// <para>
/// Asked of Postgres because what is under test is the statement that finds a number's occurrences
/// and the one that carries them, and the case that matters most is one word meaning three things:
/// the ark of the covenant, Joseph's coffin and a money chest are all אָרוֹן.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ThingLoaderTests : IDisposable
{
    private const string Ark = "H727";

    private const string Altar = "H4196";

    private const string Incense = "H7004";

    private const string Boaz = "H1162";

    private readonly AppDbContext _db;
    private readonly ThingLoader _loader;
    private readonly Text _hebrew;
    private readonly Text _english;

    public ThingLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new ThingLoader(_db, NullLogger<ThingLoader>.Instance);

        // Everything stands in Genesis in a test corpus, so the rules below name GEN.
        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (25, 10, ["ועשו", "ארון"]),
            (25, 14, ["ארון", "ארון"]),
            (30, 1, ["מזבח", "ה", "קטרת"]),
            (30, 2, ["מזבח"]),
            (50, 26, ["ארון"]),
            (7, 21, ["בעז"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng",
            (25, 10, ["an", "ark"]),
            (7, 21, ["Boaz"]));
        _db.SaveChanges();

        Number(25, 10, 2, Ark);
        Number(25, 14, 1, Ark);
        Number(25, 14, 2, Ark);
        Number(30, 1, 1, Altar);
        Number(30, 1, 3, Incense);
        Number(30, 2, 1, Altar);
        Number(50, 26, 1, Ark);
        Number(7, 21, 1, Boaz);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private void Number(int chapter, int verse, int position, string number) =>
        _db.WordAt(_hebrew, chapter, verse, position).StrongNumber = number;

    private Word Hebrew(int chapter, int verse, int position) => _db.WordAt(_hebrew, chapter, verse, position);

    private async Task<Dictionary<long, string>> Named() =>
        await _db.WordEntities.AsNoTracking()
            .Where(a => a.Source == ThingLoader.Source)
            .Select(a => new { a.WordId, a.Entity!.Slug })
            .ToDictionaryAsync(a => a.WordId, a => a.Slug);

    private static ThingRecord Record(
        string slug,
        string kind = "object",
        IReadOnlyList<OccurrenceRule>? rules = null,
        IReadOnlyList<ThingRelation>? related = null,
        IReadOnlyList<ThingPassage>? passages = null,
        IReadOnlyList<ThingTime>? times = null) =>
        new(slug, kind, kind == "object" ? "furnishing" : "feast", slug.Replace('-', ' '),
            new Dictionary<string, string> { ["ukr"] = "назва " + slug },
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["ukr"] = new Dictionary<string, string> { ["genitive"] = "назви " + slug },
            },
            "a thing the test made", "Notes written from the text.",
            [new ThingWord("ark", "אָרוֹן", "aron", Ark, null, null, null, "chest")],
            passages, times, related, rules, "Read verse by verse.");

    private static readonly OccurrenceRule TheArk = new(Ark, null, ["GEN 50:26"], null);

    /// <summary>
    /// The ark of the covenant is every אָרוֹן but the coffin Joseph was put in, and the one word that
    /// is the coffin stays unnamed.
    /// </summary>
    [Fact]
    public async Task OneWordIsNotOneThing()
    {
        var outcome = await _loader.Load([Record("ark-of-the-covenant", rules: [TheArk])], CancellationToken.None);

        var named = await Named();
        named[Hebrew(25, 10, 2).Id].Should().Be("ark-of-the-covenant");
        named[Hebrew(25, 14, 1).Id].Should().Be("ark-of-the-covenant");
        named[Hebrew(25, 14, 2).Id].Should().Be("ark-of-the-covenant");
        named.Should().NotContainKey(Hebrew(50, 26, 1).Id, "Genesis 50:26 is Joseph's coffin");
        outcome.Words.Should().Be(3);
    }

    /// <summary>A rule naming one occurrence of a number in a verse takes that one and not its neighbour.</summary>
    [Fact]
    public async Task AnOccurrenceCanBeNamedByItsPlaceInTheVerse()
    {
        await _loader.Load(
            [Record("ark-of-the-covenant", rules: [new OccurrenceRule(Ark, ["GEN 25:14#2"], null, null)])],
            CancellationToken.None);

        var named = await Named();
        named.Should().ContainKey(Hebrew(25, 14, 2).Id);
        named.Should().NotContainKey(Hebrew(25, 14, 1).Id);
    }

    /// <summary><em>The altar of the incense</em> is the altar of incense; <em>the altar</em> alone is not.</summary>
    [Fact]
    public async Task ASecondWordBesideItDecidesWhichAltar()
    {
        await _loader.Load(
            [Record("altar-of-incense", rules: [new OccurrenceRule(Altar, ["GEN"], null, Incense)])],
            CancellationToken.None);

        var named = await Named();
        named.Should().ContainKey(Hebrew(30, 1, 1).Id);
        named.Should().NotContainKey(Hebrew(30, 2, 1).Id);
    }

    private async Task ResolvedOntoTheMan()
    {
        var man = new Entity { Kind = EntityKind.Person, Slug = "boaz", Name = "Boaz", SourceId = "person:Boaz_1", Source = "a test" };
        _db.Entities.Add(man);
        _db.WordEntities.Add(new WordEntity
        {
            Word = Hebrew(7, 21, 1), Entity = man, Method = LinkMethod.StrongNumber, Confidence = 0.9,
            Source = "a resolution by number",
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// A model read these words, and says so: the annotation is a reading, with the number the corpus
    /// measured for one, and not a person's ruling.
    /// </summary>
    [Fact]
    public async Task AnUnreviewedRuleIsAModelsReading()
    {
        await _loader.Load([Record("ark-of-the-covenant", rules: [TheArk])], CancellationToken.None);

        var shown = await Annotations.Of(_db, Hebrew(25, 10, 2).Id, CancellationToken.None);
        shown!.Slug.Should().Be("ark-of-the-covenant");
        shown.Method.Should().Be("model-reading");
        shown.Confidence.Should().Be(ThingLoader.ByReading);
    }

    /// <summary>
    /// The pillar Boaz of 1 Kings 7:21 had been resolved by its number onto Ruth's husband. A model's
    /// reading cannot take a name away from a resolution, so until the owner has reviewed the rule the
    /// reader is still shown the man, and the pillar's reading stands on the word beside it.
    /// </summary>
    [Fact]
    public async Task AReadingDoesNotOverruleTheNumbersResolution()
    {
        await ResolvedOntoTheMan();

        await _loader.Load(
            [Record("boaz-pillar", rules: [new OccurrenceRule(Boaz, ["GEN 7:21"], null, null)])],
            CancellationToken.None);

        (await Annotations.Of(_db, Hebrew(7, 21, 1).Id, CancellationToken.None))!.Slug.Should().Be("boaz");
        (await _db.WordEntities.CountAsync(a => a.WordId == Hebrew(7, 21, 1).Id)).Should().Be(2);
    }

    /// <summary>Once the owner has reviewed the rule it is his ruling, and a ruling outranks the number.</summary>
    [Fact]
    public async Task AReviewedRuleOutranksTheNumbersResolution()
    {
        await ResolvedOntoTheMan();

        var outcome = await _loader.Load(
            [Record("boaz-pillar", rules: [new OccurrenceRule(Boaz, ["GEN 7:21"], null, null, "the project owner, 2026-09-24")])],
            CancellationToken.None);

        var shown = await Annotations.Of(_db, Hebrew(7, 21, 1).Id, CancellationToken.None);
        shown!.Slug.Should().Be("boaz-pillar");
        shown.Method.Should().Be("manual");
        shown.Confidence.Should().BeNull();
        shown.Source.Should().Be(ThingLoader.ReviewedSource);
        outcome.AlreadyLoaded.Should().BeFalse();
    }

    /// <summary>A review added to the file later turns the reading into a ruling on the next boot.</summary>
    [Fact]
    public async Task AReviewLandsOnTheNextBoot()
    {
        await _loader.Load([Record("ark-of-the-covenant", rules: [TheArk])], CancellationToken.None);

        var reviewed = TheArk with { Reviewed = "the project owner, 2026-09-24" };
        var record = Record("ark-of-the-covenant", rules: [reviewed]) with { Reviewed = "the project owner, 2026-09-24" };
        (await _loader.Load([record], CancellationToken.None)).AlreadyLoaded.Should().BeFalse();

        (await Annotations.Of(_db, Hebrew(25, 10, 2).Id, CancellationToken.None))!.Method.Should().Be("manual");
        (await _db.WordEntities.CountAsync(a => a.Source == ThingLoader.Source)).Should().Be(0);
        var claim = await _db.EntityClaims.SingleAsync(c => c.Entity!.Slug == "ark-of-the-covenant");
        claim.Method.Should().Be(LinkMethod.Manual);
        claim.Confidence.Should().BeNull();
    }

    /// <summary>
    /// A number somebody else is named by stays off these records' names, so the words of it in
    /// Ruth are still one man's to resolve.
    /// </summary>
    [Fact]
    public async Task ANumberAPersonIsNamedByStaysHis()
    {
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Person, Slug = "boaz", Name = "Boaz", SourceId = "person:Boaz_1", Source = "a test",
            Names = [new EntityName { Label = "Boaz", HebrewStrongNumber = Boaz, Kind = "proper name" }],
        });
        await _db.SaveChangesAsync();
        var pillar = Record("boaz-pillar") with
        {
            Called = [new ThingWord("Boaz", "בֹּעַז", "boaz", Boaz, null, null, null, "in him is strength")],
        };

        await _loader.Load([pillar], CancellationToken.None);

        var name = await _db.EntityNames.SingleAsync(n => n.Entity!.Slug == "boaz-pillar");
        name.Hebrew.Should().Be("בֹּעַז");
        name.HebrewStrongNumber.Should().BeNull();
        (await _db.EntityNames.CountAsync(n => n.HebrewStrongNumber == Boaz)).Should().Be(1);
    }

    /// <summary>The translations reach the record through the links, as every other annotation does.</summary>
    [Fact]
    public async Task TheThingTravelsToTheTranslationTheLinksReach()
    {
        var rendering = _db.WordAt(_english, 25, 10, 2);
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Hebrew(25, 10, 2), Side = LinkSide.To });
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load([Record("ark-of-the-covenant", rules: [TheArk])], CancellationToken.None);

        (await Named())[rendering.Id].Should().Be("ark-of-the-covenant");
        outcome.ByText.Should().Contain(("KJV", 1));
    }

    /// <summary>A word two records' rules both claim is given to neither: the file says two things about it.</summary>
    [Fact]
    public async Task AWordTwoRecordsClaimIsGivenToNeither()
    {
        var outcome = await _loader.Load(
            [
                Record("ark-of-the-covenant", rules: [TheArk]),
                Record("josephs-coffin", rules: [new OccurrenceRule(Ark, ["GEN 50:26", "GEN 25:10"], null, null)]),
            ],
            CancellationToken.None);

        var named = await Named();
        named.Should().NotContainKey(Hebrew(25, 10, 2).Id);
        named[Hebrew(50, 26, 1).Id].Should().Be("josephs-coffin");
        outcome.Refused.Should().Be(1);
    }

    /// <summary>
    /// Everything a page draws is written: the kind and its subtype, the names in a reader's language
    /// and their cases, the passages in order, the calendar with its verse, the relation with its verse,
    /// and the claim saying who decided it.
    /// </summary>
    [Fact]
    public async Task TheRecordCarriesWhatThePageDraws()
    {
        await _loader.Load(
            [
                Record("tabernacle"),
                Record("ark-of-the-covenant",
                    related: [new ThingRelation(ThingRelations.StandsIn, "tabernacle", "GEN 40:21", null)],
                    passages: [new ThingPassage("GEN 25:10-22", PassageRoles.Key, null), new ThingPassage("GEN 37", PassageRoles.Key, null)]),
                Record("passover", "observance",
                    passages: [new ThingPassage("GEN 23:5-8", PassageRoles.Command, null)],
                    times: [new ThingTime(ObservanceCycles.Yearly, 1, 14, null, "GEN 23:5", "at even")]),
            ],
            CancellationToken.None);

        var ark = await _db.Entities
            .Include(e => e.Names).Include(e => e.Passages).Include(e => e.Claims)
            .SingleAsync(e => e.Slug == "ark-of-the-covenant");
        ark.Kind.Should().Be(EntityKind.Object);
        ark.Subtype.Should().Be("furnishing");
        ark.Names.Should().ContainSingle().Which.HebrewStrongNumber.Should().Be(Ark);
        ark.Passages.OrderBy(p => p.Ordinal).Select(p => (p.CanonicalChapter, p.CanonicalVerse, p.EndChapter, p.EndVerse))
            .Should().Equal((25, 10, 25, 22), (37, null, 37, null));
        var established = ark.Claims.Should().ContainSingle().Which;
        established.Method.Should().Be(LinkMethod.ModelReading, "a model read these verses and no person has yet");
        established.Confidence.Should().Be(ThingLoader.ByReading);

        var forms = await _db.EntityNameForms.Where(f => f.EntityId == ark.Id)
            .ToDictionaryAsync(f => f.GrammaticalCase, f => f.Form);
        forms.Should().Equal(new Dictionary<string, string>
        {
            [GrammaticalCases.Nominative] = "назва ark-of-the-covenant",
            [GrammaticalCases.Genitive] = "назви ark-of-the-covenant",
        });

        var relation = await _db.EntityRelationships.SingleAsync(r => r.FromEntityId == ark.Id);
        relation.Type.Should().Be("stands-in");
        relation.Method.Should().Be(LinkMethod.ModelReading);
        relation.Confidence.Should().Be(ThingLoader.ByReading);
        relation.CanonicalChapter.Should().Be(40);

        var passover = await _db.Entities.Include(e => e.Times).SingleAsync(e => e.Slug == "passover");
        passover.Kind.Should().Be(EntityKind.Observance);
        var time = passover.Times.Should().ContainSingle().Which;
        (time.Month, time.Day, time.CanonicalChapter, time.CanonicalVerse).Should().Be((1, 14, 23, 5));
    }

    /// <summary>
    /// Leviticus 16 never says <em>the day of atonement</em> and is the whole of how it is kept, so the
    /// verses of a commanding passage are the observance's references, under a source that says they
    /// came from the passage and not from a word; a passage the file no longer lists takes them back.
    /// </summary>
    [Fact]
    public async Task ACommandingPassageGivesItsVersesAsReferences()
    {
        var atonement = Record("day-of-atonement", "observance",
            passages: [new ThingPassage("GEN 25", PassageRoles.Command, null), new ThingPassage("GEN 30:1", PassageRoles.Key, null)]);

        await _loader.Load([atonement], CancellationToken.None);

        var cited = await _db.EntityVerses
            .Where(v => v.Source == ThingLoader.PassageSource)
            .Select(v => new { v.CanonicalChapter, v.CanonicalVerse })
            .ToListAsync();
        cited.Select(v => (v.CanonicalChapter, v.CanonicalVerse)).Should().BeEquivalentTo(
            [(25, 10), (25, 14)], "a key passage is to be read about it, and only a commanding one is cited");

        (await _loader.Load([atonement], CancellationToken.None)).AlreadyLoaded.Should().BeTrue();

        await _loader.Load(
            [atonement with { Passages = [new ThingPassage("GEN 25:14", PassageRoles.Command, null)] }],
            CancellationToken.None);
        (await _db.EntityVerses.CountAsync(v => v.Source == ThingLoader.PassageSource)).Should().Be(1);
    }

    /// <summary>
    /// A record's names are its names: the phrase whole, with the number of each word of it, and never
    /// one word of the phrase as a name of its own.
    /// </summary>
    [Fact]
    public async Task APhraseIsOneNameCarryingEachOfItsNumbers()
    {
        var atonement = Record("day-of-atonement", "observance") with
        {
            Called =
            [
                new ThingWord("Day of Atonement", "יוֹם הַכִּפֻּרִים", "yom hakkippurim", "H3117,H3725", null, null, null, null),
                new ThingWord("Yom Kippur", null, null, null, null, null, null, null),
            ],
        };

        await _loader.Load([atonement], CancellationToken.None);

        var names = await _db.EntityNames.Where(n => n.Entity!.Slug == "day-of-atonement")
            .Select(n => new { n.Label, n.HebrewStrongNumber }).ToListAsync();
        names.Select(n => n.Label).Should().Equal("Day of Atonement", "Yom Kippur");
        names[0].HebrewStrongNumber.Should().Be("H3117,H3725");
    }

    /// <summary>The startup pipeline runs on every boot, and a second boot writes nothing.</summary>
    [Fact]
    public async Task ASecondBootWritesNothing()
    {
        IReadOnlyList<ThingRecord> records =
        [
            Record("tabernacle"),
            Record("ark-of-the-covenant", rules: [TheArk],
                related: [new ThingRelation(ThingRelations.StandsIn, "tabernacle", "GEN 40:21", null)],
                passages: [new ThingPassage("GEN 25:10-22", PassageRoles.Key, null)]),
        ];
        (await _loader.Load(records, CancellationToken.None)).AlreadyLoaded.Should().BeFalse();
        var rows = await _db.WordEntities.Select(a => a.Id).OrderBy(id => id).ToListAsync();

        var again = await _loader.Load(records, CancellationToken.None);

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.Select(a => a.Id).OrderBy(id => id).ToListAsync()).Should().Equal(rows);
    }

    /// <summary>A rule changed in the file is what the words say on the next boot.</summary>
    [Fact]
    public async Task AChangedRuleIsWrittenAgain()
    {
        await _loader.Load([Record("ark-of-the-covenant", rules: [TheArk])], CancellationToken.None);

        var outcome = await _loader.Load(
            [Record("ark-of-the-covenant", rules: [new OccurrenceRule(Ark, ["GEN 25:10"], null, null)])],
            CancellationToken.None);

        outcome.AlreadyLoaded.Should().BeFalse();
        (await Named()).Keys.Should().Equal(Hebrew(25, 10, 2).Id);
    }

    /// <summary>A slug another record holds is not taken over: <c>ark</c> is the eponym of the Arkites.</summary>
    [Fact]
    public async Task ASlugAnotherRecordHoldsIsNotTakenOver()
    {
        _db.Entities.Add(new Entity { Kind = EntityKind.Person, Slug = "ark", Name = "Ark", SourceId = "person:Ark_1", Source = "a test" });
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load([Record("ark", rules: [TheArk])], CancellationToken.None);

        outcome.Missing.Should().Be(1);
        (await _db.Entities.SingleAsync(e => e.Slug == "ark")).Kind.Should().Be(EntityKind.Person);
        (await Named()).Should().BeEmpty();
    }
}

/// <summary>
/// The record files as they ship: every one of them has to be readable before the corpus ever sees
/// it, because a loader that throws at the forty-first record of a boot has failed every step after it.
/// </summary>
public sealed class ThingFileTests
{
    private static readonly IReadOnlySet<string> Subtypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "furnishing", "vessel", "structure", "vestment", "monument", "weapon", "image", "implement",
        "feast", "fast", "sabbath", "new-moon", "sacred-year", "appointed-time", "rite", "book",
    };

    private readonly IReadOnlyList<ThingRecord> _records = ThingFiles.Read();

    [Fact]
    public void EveryRecordIsWellFormed()
    {
        _records.Select(r => r.Slug).Should().OnlyHaveUniqueItems();
        foreach (var record in _records)
        {
            record.Called.Should().NotBeNullOrEmpty($"{record.Slug} has to be findable by its names");
            foreach (var name in record.Called!)
            {
                name.Label.Should().NotBeNullOrWhiteSpace();
                foreach (var number in $"{name.HebrewStrongNumber},{name.GreekStrongNumber}".Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    number.Should().MatchRegex("^[HG][0-9]+$", $"{record.Slug}: {name.Label}");
                }
            }

            record.Kind.Should().BeOneOf("object", "observance");
            Subtypes.Should().Contain(record.Subtype, $"{record.Slug} has to be sorted by a subtype the pages know");
            record.Names.Should().ContainKey("ukr", $"{record.Slug} needs its Ukrainian name");
            record.Distinguisher.Should().NotBeNullOrWhiteSpace();
            foreach (var passage in record.Passages ?? [])
            {
                ScriptureSpan.TryParse(passage.Reference).Should().NotBeNull($"{record.Slug}: {passage.Reference}");
                PassageRoles.All.Should().Contain(passage.Role);
            }

            foreach (var time in record.Times ?? [])
            {
                ScriptureSpan.TryParse(time.Reference).Should().NotBeNull($"{record.Slug}: {time.Reference}");
                ObservanceCycles.All.Should().Contain(time.Cycle);
            }

            foreach (var relation in record.Related ?? [])
            {
                ThingRelations.All.Should().Contain(relation.Type, $"{record.Slug} relates as {relation.Type}");
                ScriptureSpan.TryParse(relation.Reference)!.IsVerse.Should().BeTrue($"{record.Slug}: {relation.Reference}");
            }

            foreach (var rule in record.Occurrences ?? [])
            {
                rule.Strong.Should().MatchRegex("^[HG][0-9]+$");
                foreach (var span in (rule.Only ?? []).Concat(rule.Except ?? []))
                {
                    ScriptureSpan.TryParse(span).Should().NotBeNull($"{record.Slug}: {span}");
                }
            }
        }
    }

    /// <summary>The names readers search the appointed times by, in the Hebrew they are known by.</summary>
    [Theory]
    [InlineData("day-of-atonement", "Yom Kippur")]
    [InlineData("day-of-trumpets", "Yom Teruah")]
    [InlineData("feast-of-booths", "Sukkot")]
    [InlineData("feast-of-weeks", "Shavuot")]
    [InlineData("passover", "Pesach")]
    [InlineData("new-moon", "Rosh Chodesh")]
    [InlineData("sabbath", "Shabbat")]
    [InlineData("feast-of-dedication", "Hanukkah")]
    [InlineData("menorah", "Menorah")]
    public void AnAppointedTimeAnswersToItsHebrewName(string slug, string name) =>
        _records.Single(r => r.Slug == slug).Called!.Select(n => n.Label).Should().Contain(name);

    /// <summary>The slugs another agent's models are keyed on, which the files must keep.</summary>
    [Fact]
    public void TheSlugsOthersDependOnAreThere() =>
        _records.Select(r => r.Slug).Should()
            .Contain(["ark-of-the-covenant", "menorah", "noahs-ark", "tabernacle"]);
}

/// <summary>How a record file writes a stretch of Scripture, and what each form holds.</summary>
public sealed class ScriptureSpanTests
{
    [Theory]
    [InlineData("LEV", 3, 23, 1, true)]
    [InlineData("LEV", 4, 1, 1, false)]
    [InlineData("GEN 6-9", 1, 9, 29, true)]
    [InlineData("GEN 6-9", 1, 10, 1, false)]
    [InlineData("EXO 25:10-22", 2, 25, 22, true)]
    [InlineData("EXO 25:10-22", 2, 25, 23, false)]
    [InlineData("EXO 25:10-26:37", 2, 26, 1, true)]
    [InlineData("EXO 25:10-26:37", 2, 25, 9, false)]
    [InlineData("LEV 23", 3, 23, 44, true)]
    [InlineData("2KI 12:10", 12, 12, 10, true)]
    [InlineData("1SA 4", 9, 4, 11, true)]
    [InlineData("JHN 2:20", 43, 2, 20, true)]
    [InlineData("PHP 2:8", 50, 2, 8, true)]
    [InlineData("SNG 1:1", 22, 1, 1, true)]
    [InlineData("EZK 40", 26, 40, 5, true)]
    public void ASpanHoldsTheVersesItNames(string written, int book, int chapter, int verse, bool holds) =>
        ScriptureSpan.Parse(written).Holds(book, chapter, verse, 1).Should().Be(holds);

    [Fact]
    public void AnOccurrenceIsNamedByItsPlaceInTheVerse()
    {
        var second = ScriptureSpan.Parse("NUM 8:2#2");
        second.Holds(4, 8, 2, 2).Should().BeTrue();
        second.Holds(4, 8, 2, 1).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ 1:1")]
    [InlineData("EXO 25:22-10")]
    [InlineData("EXO 25#2")]
    [InlineData("EXO 0:1")]
    [InlineData("EXO 25:x")]
    public void WhatIsNotASpanIsRefused(string written) => ScriptureSpan.TryParse(written).Should().BeNull();

    [Fact]
    public void ARuleTakesWhatItsSpansAdmit()
    {
        var rule = new OccurrenceRule("H4501", ["EXO 25-40", "LEV"], ["EXO 37:17#2"], null);
        rule.Admits(2, 25, 31, 1).Should().BeTrue();
        rule.Admits(2, 37, 17, 2).Should().BeFalse();
        rule.Admits(2, 37, 17, 1).Should().BeTrue();
        rule.Admits(11, 7, 49, 1).Should().BeFalse();
    }
}
