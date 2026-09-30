using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The kings, the rulers of the nations and the prophets of their days, against the file the corpus
/// actually ships: every row resting on a verse written as one, every word from the vocabularies the
/// tables allow, and the rows reaching the endpoint as the timeline of the kings reads them. With
/// them the rulings on the kings: a mark for every king the text judges, quoted from verses, the ages
/// a verse gives, and the carryings away and the return by the rulers' years.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ReignLoaderTests : IDisposable
{
    private const string Source = "a test";

    private readonly AppDbContext _db;
    private readonly ReignLoader _loader;
    private readonly ReignDecision _decision = ReignLoader.Records();
    private readonly ReignRulings _rulings = ReignLoader.Rulings();

    public ReignLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Empty();
        _loader = new ReignLoader(_db, NullLogger<ReignLoader>.Instance);

        var slugs = _decision.Rulers.Select(r => r.Slug)
            .Concat(_decision.Statements.SelectMany(s => new[] { s.Person, s.Ruler, s.Through }))
            .Concat(_decision.Prophets.SelectMany(p => p.Fields.Select(f => f.Place).Prepend(p.Slug)))
            .Concat(_rulings.Verdicts.Select(v => v.King))
            .Concat(_rulings.Ages.Select(a => a.Person))
            .Concat(_rulings.Events.SelectMany(e => e.Datings.Select(d => d.Ruler)))
            .OfType<string>()
            .Distinct();
        var entities = slugs.ToDictionary(slug => slug, slug => new Entity
        {
            Kind = EntityKind.Person,
            Slug = slug,
            Name = slug,
            SourceId = slug,
            Source = Source,
        });
        _db.Entities.AddRange(entities.Values);

        foreach (var ruler in _decision.Rulers)
        {
            _db.Periods.AddRange(ruler.Reigns.Select(reign => new Period
            {
                Slug = reign.Period,
                Name = reign.Period,
                Kind = "reign",
                Entity = entities[ruler.Slug],
                Source = Source,
            }));
        }

        _db.Events.AddRange(_rulings.Events.Select(e => e.Event).OfType<string>().Select(slug => new Event
        {
            Slug = slug,
            Name = slug,
            Source = Source,
        }));

        _db.SaveChanges();
    }

    public void Dispose()
    {
        Empty();
        _db.Dispose();
    }

    private void Empty()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM period");
        _db.Database.ExecuteSqlRaw("DELETE FROM event");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    /// <summary>
    /// Every statement cites a verse the loader can read, in words the tables accept, and says what a
    /// year counts from only where it gives one or dates by a death.
    /// </summary>
    [Fact]
    public void EveryStatementRestsOnAVerseInTheTablesOwnWords()
    {
        foreach (var statement in _decision.Statements)
        {
            ReignLoader.Verses(statement.Verse).Should().NotBeNull(statement.Verse);
            ReignRoles.All.Should().Contain(statement.Role);
            ReignStatementKinds.All.Should().Contain(statement.Kind);

            if (statement.Count is { } count)
            {
                ReignCounts.All.Should().Contain(count);
                (statement.Year is not null || count == ReignCounts.Death).Should().BeTrue(statement.Verse);
            }

            if (statement.Role == ReignRoles.Accession)
            {
                statement.Year.Should().NotBeNull("a king's accession is dated by the other king's year");
            }
        }

        _decision.Rulers.Select(r => r.Realm).Should().OnlyContain(realm => RulerRealms.All.Contains(realm));

        foreach (var field in _decision.Prophets.SelectMany(p => p.Fields))
        {
            ReignLoader.Verses(field.Verse).Should().NotBeNull(field.Verse);
            ProphetRealms.All.Should().Contain(field.Realm);
            ProphetFieldKinds.All.Should().Contain(field.Kind);
        }

        foreach (var ruler in _decision.Rulers)
        {
            if (ruler.Reigned is { } reigned)
            {
                ReignLoader.Verses(reigned.Verse).Should().NotBeNull(reigned.Verse);
                reigned.InYears.Should().BePositive(ruler.Slug);
            }

            if (ruler.Throne is { } throne)
            {
                ReignLoader.Verses(throne.Verse).Should().NotBeNull(throne.Verse);
                throne.Names.Keys.Should().Contain(["eng", "ukr", "deu", "spa"], ruler.Slug);
            }
        }
        _decision.Rulers.Select(r => r.Slug).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("ISA 1:1", 23, 1, 1, null)]
    [InlineData("JER 1:1-3", 24, 1, 1, 3)]
    [InlineData("2KI 14:25", 12, 14, 25, null)]
    public void AReferenceIsReadAsTheFileWritesIt(string reference, int book, int chapter, int verse, int? end)
    {
        ReignLoader.Verses(reference).Should().Be((book, chapter, verse, end));
    }

    [Theory]
    [InlineData("ISA 1")]
    [InlineData("JER 1:3-1")]
    [InlineData("Nowhere 1:1")]
    public void AReferenceThatIsNotAVerseIsRefused(string reference)
    {
        ReignLoader.Verses(reference).Should().BeNull();
    }

    /// <summary>
    /// Everything the file names is written once, and a second load finds it there and writes nothing.
    /// </summary>
    [Fact]
    public async Task TheFileIsLoadedOnceAndLeftAloneAfter()
    {
        var first = await _loader.Load();

        first.AlreadyLoaded.Should().BeFalse();
        first.Missing.Should().Be(0);
        first.Rulers.Should().Be(_decision.Rulers.Count);
        first.Statements.Should().Be(_decision.Statements.Count);
        first.Lengths.Should().Be(_decision.Rulers.Count(r => r.Reigned is not null));
        first.Fields.Should().Be(_decision.Prophets.Sum(p => p.Fields.Count));
        first.Verdicts.Should().Be(_rulings.Verdicts.Count);
        first.Ages.Should().Be(_rulings.Ages.Count);
        first.Events.Should().Be(_rulings.Events.Sum(e => e.Datings.Count));

        var second = await _loader.Load();

        second.AlreadyLoaded.Should().BeTrue();
        (await _db.ReignStatements.CountAsync()).Should().Be(_decision.Statements.Count);
        (await _db.RulerVerdictPassages.CountAsync()).Should().Be(
            _rulings.Verdicts.Sum(v => v.Witnesses.Sum(w => w.Verses.Count)));
    }

    /// <summary>
    /// The endpoint sends each ruler with the periods he is drawn by, and names everyone else a
    /// statement mentions — the prophets, and Zerubbabel and Shealtiel, who are not kings.
    /// </summary>
    [Fact]
    public async Task TheTimelineOfTheKingsListsTheRulersAndThePeopleOfTheirDays()
    {
        await _loader.Load();

        var kings = await KingsEndpoints.Kings(_db, null, CancellationToken.None);

        kings.Rulers.Select(r => r.Slug).Should().Equal(_decision.Rulers.Select(r => r.Slug));
        kings.Rulers.Single(r => r.Slug == "david").Reigns.Should().ContainSingle(r => r.Over == RulerRealms.Judah);
        kings.People.Select(p => p.Slug).Should().Contain(["isaiah", "zerubbabel-2", "shealtiel-2"]);
        kings.People.Select(p => p.Slug).Should().NotContain("hezekiah");

        var shealtiel = kings.Statements.Where(s => s.Ruler == "shealtiel-2").ToList();
        shealtiel.Should().NotBeEmpty().And.OnlyContain(s => s.Through == "zerubbabel-2");
        kings.Statements.Should().Contain(s =>
            s.Person == "jonah" && s.Ruler == "jeroboam-2" && s.Verse.BookOrdinal == 12 && s.Verse.Chapter == 14
            && s.Verse.Verse == 25);
    }

    /// <summary>
    /// Every prophet the text sets in a king's days is said to have prophesied somewhere, so the
    /// chart can put him beside his kingdom rather than in a column of his own.
    /// </summary>
    [Fact]
    public void EveryProphetOfTheKingsIsPlacedWhereHeProphesied()
    {
        var prophets = _decision.Statements.Where(s => s.Role == ReignRoles.Prophet).Select(s => s.Person).ToHashSet();
        var placed = _decision.Prophets
            .Where(p => p.Fields.Any(f => f.Kind == ProphetFieldKinds.Prophesied))
            .Select(p => p.Slug)
            .ToHashSet();

        prophets.Should().BeSubsetOf(placed);
        _decision.Prophets.Select(p => p.Slug).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// A ruler comes with the length the text gives his reign and the name he reigned under, and a
    /// prophet with where he spoke and where he came from: Amos at Bethel, from Tekoa in Judah.
    /// </summary>
    [Fact]
    public async Task TheTimelineOfTheKingsSaysHowLongEachReignedAndWhereEachProphetSpoke()
    {
        await _loader.Load();

        var kings = await KingsEndpoints.Kings(_db, "ukr", CancellationToken.None);

        var elah = kings.Rulers.Single(r => r.Slug == "elah-2");
        elah.Reigned.Should().NotBeNull();
        (elah.Reigned!.Years, elah.Reigned.Verse.BookOrdinal, elah.Reigned.Verse.Chapter, elah.Reigned.Verse.Verse)
            .Should().Be(((int?)2, 11, 16, 8));

        var eliakim = kings.Rulers.Single(r => r.Slug == "eliakim-2");
        eliakim.Throne.Should().NotBeNull();
        (eliakim.Throne!.Name, eliakim.Throne.LocalName).Should().Be(("Jehoiakim", "Єгояким"));
        kings.Rulers.Single(r => r.Slug == "ahab").Throne.Should().BeNull();

        var amos = kings.People.Single(p => p.Slug == "amos");
        amos.Fields.Select(f => (f.Kind, f.Realm, f.Place?.Slug)).Should().Equal(
            (ProphetFieldKinds.Prophesied, ProphetRealms.Israel, "bethel"),
            (ProphetFieldKinds.From, ProphetRealms.Judah, "tekoa-2"));
        kings.People.Single(p => p.Slug == "zerubbabel-2").Fields.Should().BeEmpty();
    }

    /// <summary>
    /// Every king of the united kingdom, of Israel and of Judah is either marked or listed as one the
    /// text tells nothing to judge by, and never both; the owner's three stand as he set them.
    /// </summary>
    [Fact]
    public void EveryKingIsMarkedOrSaidToBeUnmarked()
    {
        var kings = _decision.Rulers
            .Where(r => r.Realm is RulerRealms.United or RulerRealms.Israel or RulerRealms.Judah)
            .Select(r => r.Slug)
            .ToList();
        var marked = _rulings.Verdicts.Select(v => v.King).ToList();
        var unmarked = _rulings.Unmarked.Select(u => u.King).ToList();

        marked.Should().OnlyHaveUniqueItems();
        marked.Concat(unmarked).Should().BeEquivalentTo(kings);

        foreach (var left in _rulings.Unmarked)
        {
            left.Reason.Should().NotBeNullOrWhiteSpace(left.King);
            left.Verses.Should().NotBeEmpty(left.King)
                .And.OnlyContain(verse => ReignLoader.Verses(verse) != null, left.King);
        }

        var marks = _rulings.Verdicts.ToDictionary(v => v.King, v => v.Mark);
        (marks["david"], marks["saul"], marks["solomon"])
            .Should().Be((RulerMarks.Right, RulerMarks.Mixed, RulerMarks.Mixed));
    }

    /// <summary>
    /// A mark is quoted from verses the loader can read, in the tables' own words; a reading gives
    /// its reason and a judgment the text states needs none; and a king his histories part on is mixed.
    /// </summary>
    [Fact]
    public void EveryVerdictIsQuotedFromVersesAndAReadingSaysWhy()
    {
        foreach (var verdict in _rulings.Verdicts)
        {
            RulerMarks.All.Should().Contain(verdict.Mark, verdict.King);
            verdict.Witnesses.Should().NotBeEmpty(verdict.King);
            verdict.Witnesses.Select(w => w.Book).Should().OnlyHaveUniqueItems(verdict.King);

            foreach (var witness in verdict.Witnesses)
            {
                VerdictWitnesses.All.Should().Contain(witness.Book, verdict.King);
                RulerMarks.All.Should().Contain(witness.Mark, verdict.King);
                VerdictBases.All.Should().Contain(witness.Basis, verdict.King);
                witness.Verses.Should().NotBeEmpty(verdict.King).And.OnlyHaveUniqueItems(verdict.King);

                foreach (var verse in witness.Verses)
                {
                    ReignLoader.Verses(verse).Should().NotBeNull(verse);
                }

                if (witness.Basis == VerdictBases.Reading)
                {
                    witness.Reason.Should().NotBeNullOrWhiteSpace(verdict.King);
                }
                else
                {
                    witness.Reason.Should().BeNull(verdict.King);
                }
            }

            var said = verdict.Witnesses.Select(w => w.Mark).Distinct().ToList();
            verdict.Mark.Should().Be(said.Count == 1 ? said[0] : RulerMarks.Mixed, verdict.King);
        }
    }

    /// <summary>
    /// An age is a verse's, of somebody the timeline of the kings names; an event is one of the two
    /// kinds, befalls Israel or Judah, and every verse dating it names a ruler.
    /// </summary>
    [Fact]
    public void EveryAgeAndEveryCarryingAwayRestsOnAVerse()
    {
        var rulers = _decision.Rulers.Select(r => r.Slug).ToHashSet();
        var named = rulers.Concat(_decision.Prophets.Select(p => p.Slug)).ToHashSet();

        foreach (var age in _rulings.Ages)
        {
            named.Should().Contain(age.Person);
            StatedAgeKinds.All.Should().Contain(age.Kind, age.Person);
            age.Years.Should().BePositive(age.Person);
            ReignLoader.Verses(age.Verse).Should().NotBeNull(age.Verse);
            ReignLoader.Verses(age.Verse)!.Value.EndVerse.Should().BeNull(age.Verse);
        }

        _rulings.Ages.Select(a => (a.Person, a.Kind, a.Verse)).Should().OnlyHaveUniqueItems();
        _rulings.Events.Select(e => e.Slug).Should().OnlyHaveUniqueItems();

        foreach (var happened in _rulings.Events)
        {
            ReignEventKinds.All.Should().Contain(happened.Kind, happened.Slug);
            happened.Realm.Should().BeOneOf([RulerRealms.Israel, RulerRealms.Judah], happened.Slug);
            happened.Datings.Should().NotBeEmpty(happened.Slug)
                .And.Contain(d => d.Year != null, "one verse at least gives a ruler's year");

            foreach (var dating in happened.Datings)
            {
                rulers.Should().Contain(dating.Ruler, happened.Slug);
                ReignLoader.Verses(dating.Verse).Should().NotBeNull(dating.Verse);
                (dating.Year ?? 1).Should().BePositive(dating.Verse);
            }
        }
    }

    /// <summary>
    /// A king comes with his mark and each history's own: Hezekiah right in both, Manasseh evil in
    /// Kings and mixed in Chronicles, Elah a reading; a king the text does not judge, and a ruler of
    /// the nations, with none.
    /// </summary>
    [Fact]
    public async Task TheTimelineOfTheKingsSaysWhatTheTextSaysOfEachKing()
    {
        await _loader.Load();

        var kings = await KingsEndpoints.Kings(_db, null, CancellationToken.None);

        var hezekiah = kings.Rulers.Single(r => r.Slug == "hezekiah").Verdict;
        hezekiah.Should().NotBeNull();
        (hezekiah!.Mark, hezekiah.Basis).Should().Be((RulerMarks.Right, VerdictBases.Text));
        hezekiah.Sources.Select(s => (s.Source, s.Mark)).Should().Equal(
            (VerdictWitnesses.Kings, RulerMarks.Right),
            (VerdictWitnesses.Chronicles, RulerMarks.Right));
        hezekiah.Sources[0].Passages.Select(p => (p.Verse.BookOrdinal, p.Verse.Chapter, p.Verse.Verse, p.EndVerse))
            .Should().Equal((12, 18, 3, (int?)null), (12, 18, 5, (int?)6));

        var manasseh = kings.Rulers.Single(r => r.Slug == "manasseh-3").Verdict!;
        manasseh.Mark.Should().Be(RulerMarks.Mixed);
        manasseh.Sources.Select(s => (s.Source, s.Mark)).Should().Equal(
            (VerdictWitnesses.Kings, RulerMarks.Evil),
            (VerdictWitnesses.Chronicles, RulerMarks.Mixed));

        var elah = kings.Rulers.Single(r => r.Slug == "elah-2").Verdict!;
        (elah.Mark, elah.Basis).Should().Be((RulerMarks.Evil, VerdictBases.Reading));

        kings.Rulers.Single(r => r.Slug == "tibni").Verdict.Should().BeNull();
        kings.Rulers.Single(r => r.Slug == "shishak").Verdict.Should().BeNull();
    }

    /// <summary>
    /// The ages go with whom the verse gives them to — both of Ahaziah's, and Darius the Mede's as
    /// about — and the events come in the order they happened, each with every verse that dates it.
    /// </summary>
    [Fact]
    public async Task TheTimelineOfTheKingsGivesTheAgesAndTheCarryingsAway()
    {
        await _loader.Load();

        var kings = await KingsEndpoints.Kings(_db, null, CancellationToken.None);

        kings.Rulers.Single(r => r.Slug == "ahaziah-2").Ages
            .Select(a => (a.Kind, a.Years, a.About, a.Verse.BookOrdinal, a.Verse.Chapter, a.Verse.Verse))
            .Should().Equal(
                (StatedAgeKinds.Accession, 22, false, 12, 8, 26),
                (StatedAgeKinds.Accession, 42, false, 14, 22, 2));
        kings.Rulers.Single(r => r.Slug == "darius-2").Ages.Should().ContainSingle(a => a.About && a.Years == 62);
        kings.Rulers.Single(r => r.Slug == "solomon").Ages.Should().BeEmpty();
        kings.People.Single(p => p.Slug == "isaiah").Ages.Should().BeEmpty();

        kings.Events.Select(e => (e.Slug, e.Kind, e.Realm)).Should().Equal(
            ("exile-of-israel", ReignEventKinds.Exile, RulerRealms.Israel),
            ("first-taking-of-jerusalem", ReignEventKinds.Exile, RulerRealms.Judah),
            ("exile-of-jehoiachin", ReignEventKinds.Exile, RulerRealms.Judah),
            ("fall-of-jerusalem", ReignEventKinds.Exile, RulerRealms.Judah),
            ("decree-of-cyrus", ReignEventKinds.Return, RulerRealms.Judah));

        var samaria = kings.Events[0];
        samaria.Event.Should().Be("theassyriancaptivityofisrael");
        samaria.Datings.Select(d => (d.Ruler, d.Year, d.Verse.Chapter, d.Verse.Verse, d.EndVerse)).Should().Equal(
            ("hoshea", (int?)9, 17, 6, (int?)null),
            ("hoshea", (int?)9, 18, 10, (int?)11),
            ("hezekiah", (int?)6, 18, 10, (int?)11));
        kings.Events[1].Datings.Should().Contain(d => d.Ruler == "eliakim-2" && d.Year == null);
    }

    /// <summary>
    /// What is added goes over the wire under the names the client reads, through the serializer the
    /// API answers with.
    /// </summary>
    [Fact]
    public async Task TheVerdictsTheAgesAndTheEventsAreSentAsTheClientReadsThem()
    {
        await _loader.Load();

        var kings = await KingsEndpoints.Kings(_db, null, CancellationToken.None);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(
            kings, AppJsonSerializerContext.Default.GetTypeInfo(typeof(KingsTimelineResponse))!));

        var josiah = wire.RootElement.GetProperty("rulers").EnumerateArray()
            .Single(r => r.GetProperty("slug").GetString() == "josiah");
        var verdict = josiah.GetProperty("verdict");
        verdict.GetProperty("mark").GetString().Should().Be(RulerMarks.Right);
        verdict.GetProperty("basis").GetString().Should().Be(VerdictBases.Text);
        var source = verdict.GetProperty("sources")[0];
        source.GetProperty("source").GetString().Should().Be(VerdictWitnesses.Kings);
        source.GetProperty("passages")[0].GetProperty("verse").GetProperty("slug").GetString().Should().Be("2-kings");
        source.GetProperty("passages")[0].GetProperty("endVerse").ValueKind.Should().Be(JsonValueKind.Null);

        var age = josiah.GetProperty("ages")[0];
        (age.GetProperty("kind").GetString(), age.GetProperty("years").GetInt32(), age.GetProperty("about").GetBoolean())
            .Should().Be((StatedAgeKinds.Accession, 8, false));

        var decree = wire.RootElement.GetProperty("events").EnumerateArray().Last();
        decree.GetProperty("event").GetString().Should().Be("cyrusdecree");
        var dating = decree.GetProperty("datings")[0];
        (dating.GetProperty("ruler").GetString(), dating.GetProperty("year").GetInt32(), dating.GetProperty("endVerse").GetInt32())
            .Should().Be(("cyrus", 1, 3));
    }
}
