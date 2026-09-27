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
/// tables allow, and the rows reaching the endpoint as the timeline of the kings reads them.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ReignLoaderTests : IDisposable
{
    private const string Source = "a test";

    private readonly AppDbContext _db;
    private readonly ReignLoader _loader;
    private readonly ReignDecision _decision = ReignLoader.Records();

    public ReignLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM period");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new ReignLoader(_db, NullLogger<ReignLoader>.Instance);

        var slugs = _decision.Rulers.Select(r => r.Slug)
            .Concat(_decision.Statements.SelectMany(s => new[] { s.Person, s.Ruler, s.Through }))
            .Concat(_decision.Prophets.SelectMany(p => p.Fields.Select(f => f.Place).Prepend(p.Slug)))
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

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM period");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
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

        var second = await _loader.Load();

        second.AlreadyLoaded.Should().BeTrue();
        (await _db.ReignStatements.CountAsync()).Should().Be(_decision.Statements.Count);
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
}
