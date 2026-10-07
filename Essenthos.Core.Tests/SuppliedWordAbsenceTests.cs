using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The words an edition marks as its translators' own are stated absent from the originals it is
/// linked to, as the King James' italics are, and the aligner's renderings of them are taken back.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SuppliedWordAbsenceTests : IDisposable
{
    private const string Aligner = "SIL.Machine, aligned as written and as stems";

    private readonly AppDbContext db;
    private readonly Dictionary<string, Text> texts;

    public SuppliedWordAbsenceTests(WitnessDatabase database)
    {
        db = database.NewContext();
        db.Database.ExecuteSqlRaw("DELETE FROM text; DELETE FROM entity");
        texts = new Dictionary<string, Text>
        {
            ["ASV"] = Text("ASV", TextKind.Translation, "eng"),
            ["RUSV"] = Text("RUSV", TextKind.Translation, "rus"),
            ["BRENTON"] = Text("BRENTON", TextKind.Translation, "eng"),
            ["KJV"] = Text("KJV", TextKind.Translation, "eng"),
            ["BHSA"] = Text("BHSA", TextKind.CriticalEdition, "hbo"),
            ["NESTLE1904"] = Text("NESTLE1904", TextKind.CriticalEdition, "grc"),
            ["TR1894"] = Text("TR1894", TextKind.PrintedEdition, "grc"),
            ["GRCBRENT"] = Text("GRCBRENT", TextKind.PrintedEdition, "grc"),
        };
        db.Texts.AddRange(texts.Values);
        db.SaveChanges();
    }

    public void Dispose()
    {
        db.Database.ExecuteSqlRaw("DELETE FROM text; DELETE FROM entity");
        db.Dispose();
    }

    [Fact]
    public async Task AMarkedRunIsAbsentFromTheOriginalAndTheAlignersRenderingOfItIsWithdrawn()
    {
        // Ruth 1:1 "there was a famine": was and a are the revisers' italics.
        var w = Place(
            ("ASV", "RUT 1:1", 1, "there"), ("ASV", "RUT 1:1", 2, "was"), ("ASV", "RUT 1:1", 3, "a"), ("ASV", "RUT 1:1", 4, "famine"),
            ("BHSA", "RUT 1:1", 1, "וַיְהִ֥י"), ("BHSA", "RUT 1:1", 2, "רָעָ֖ב"));
        Mark("ASV", w["was"], w["a"]);
        var lost = Link(w["was"], w["וַיְהִ֥י"], LinkMethod.Aligner, Aligner);
        var famine = Link(w["famine"], w["רָעָ֖ב"], LinkMethod.Aligner, Aligner);

        (await Outcome("ASV")).Should().Be(new SuppliedWordOutcome("ASV", 2, 1, 1, 0));

        var links = await db.Links.Include(l => l.Words).Include(l => l.Provenance).ToListAsync();
        links.Select(l => l.Id).Should().NotContain(lost).And.Contain(famine);
        var absence = links.Single(l => l.Relation == LinkRelation.Expands);
        absence.Method.Should().Be(LinkMethod.StatedBySource);
        absence.ToTextId.Should().Be(texts["BHSA"].Id);
        absence.Words.Should().OnlyContain(word => word.Side == LinkSide.From);
        absence.Words.Select(word => word.WordId).Should().BeEquivalentTo([w["was"].Id, w["a"].Id]);
        absence.Provenance!.Source.Should().Contain("American Standard Version");
    }

    [Fact]
    public async Task ASecondRunWritesAndWithdrawsNothing()
    {
        var w = Place(("ASV", "RUT 1:1", 1, "was"), ("ASV", "RUT 1:1", 2, "famine"),
            ("BHSA", "RUT 1:1", 1, "וַיְהִ֥י"), ("BHSA", "RUT 1:1", 2, "רָעָ֖ב"));
        Mark("ASV", w["was"]);
        Link(w["was"], w["וַיְהִ֥י"], LinkMethod.Aligner, Aligner);
        Link(w["famine"], w["רָעָ֖ב"], LinkMethod.Aligner, Aligner);
        await SuppliedWordAbsences.State(db);
        var before = await Fingerprint();

        (await Outcome("ASV")).Should().Be(new SuppliedWordOutcome("ASV", 1, 0, 0, 0));
        (await Fingerprint()).Should().Equal(before);
    }

    /// <summary>
    /// A word another method renders stays rendered, and is not shown absent from the same witness as
    /// well: the reader would draw it supplied and rendered at once.
    /// </summary>
    [Fact]
    public async Task AnotherMethodsRenderingAndAnAlignerLinkThatAlsoNamesAnUnmarkedWordStand()
    {
        var w = Place(("ASV", "RUT 1:1", 1, "there"), ("ASV", "RUT 1:1", 2, "was"), ("ASV", "RUT 1:1", 3, "a"),
            ("ASV", "RUT 1:1", 4, "of"), ("BHSA", "RUT 1:1", 1, "וַיְהִ֥י"), ("BHSA", "RUT 1:1", 2, "רָעָ֖ב"));
        Mark("ASV", w["was"], w["a"], w["of"]);
        var numbered = Link(w["was"], w["וַיְהִ֥י"], LinkMethod.StrongNumber, "the edition's stated numbering");
        var phrase = Link([w["there"], w["a"]], [w["רָעָ֖ב"]], LinkMethod.Aligner, Aligner);

        (await Outcome("ASV")).Should().Be(new SuppliedWordOutcome("ASV", 1, 1, 0, 2));
        (await db.Links.Select(l => l.Id).ToListAsync()).Should().Contain([numbered, phrase]);
        (await db.Links.Include(l => l.Words).SingleAsync(l => l.Relation == LinkRelation.Expands))
            .Words.Select(word => word.WordId).Should().Equal(w["of"].Id);
    }

    /// <summary>
    /// An absence this pass wrote that a source's rendering has since come to contradict is taken back,
    /// so a later load never leaves a word supplied and rendered at once.
    /// </summary>
    [Fact]
    public async Task AnAbsenceASourcesRenderingNowContradictsIsTakenBack()
    {
        var w = Place(("ASV", "RUT 1:1", 1, "was"), ("ASV", "RUT 1:1", 2, "a"), ("ASV", "RUT 1:1", 3, "famine"),
            ("BHSA", "RUT 1:1", 1, "וַיְהִ֥י"), ("BHSA", "RUT 1:1", 2, "רָעָ֖ב"));
        Mark("ASV", w["was"], w["a"]);
        Link(w["famine"], w["רָעָ֖ב"], LinkMethod.Aligner, Aligner);
        await SuppliedWordAbsences.State(db);
        Link(w["was"], w["וַיְהִ֥י"], LinkMethod.StatedBySource, "a hand alignment");

        (await Outcome("ASV")).Should().Be(new SuppliedWordOutcome("ASV", 1, 1, 1, 1));
        (await db.Links.Include(l => l.Words).SingleAsync(l => l.Relation == LinkRelation.Expands))
            .Words.Select(word => word.WordId).Should().Equal(w["a"].Id);
    }

    [Fact]
    public async Task AnAlignerLinkAnotherMethodAlsoClaimsStands()
    {
        var w = Place(("ASV", "RUT 1:1", 1, "was"), ("BHSA", "RUT 1:1", 1, "וַיְהִ֥י"));
        Mark("ASV", w["was"]);
        var claimed = Link(w["was"], w["וַיְהִ֥י"], LinkMethod.Aligner, Aligner);
        db.LinkClaims.Add(new LinkClaim
        {
            LinkId = claimed, Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a hand alignment" },
        });
        db.SaveChanges();

        (await Outcome("ASV")).Withdrawn.Should().Be(0);
        (await db.Links.AnyAsync(l => l.Id == claimed)).Should().BeTrue();
    }

    [Fact]
    public async Task TheSynodalsBracketsAreAbsentFromTheHebrewAndEveryGreekWitnessButNotFromTheSeptuagint()
    {
        var w = Place(
            ("RUSV", "JDG 16:13", 1, "прибьешь"), ("BHSA", "JDG 16:13", 1, "וַתֹּ֣אמֶר"), ("GRCBRENT", "JDG 16:13", 1, "ἐγκρούσῃς"),
            ("RUSV", "JHN 1:1", 1, "было"), ("NESTLE1904", "JHN 1:1", 1, "ἦν"), ("TR1894", "JHN 1:1", 1, "ην"),
            ("RUSV", "JHN 1:1", 2, "Слово"), ("NESTLE1904", "JHN 1:1", 2, "Λόγος"));
        Mark("RUSV", w["прибьешь"]);
        Mark("RUSV", w["было"]);
        Link(w["прибьешь"], w["וַתֹּ֣אמֶר"], LinkMethod.Aligner, Aligner);
        var septuagint = Link(w["прибьешь"], w["ἐγκρούσῃς"], LinkMethod.Aligner, Aligner);
        Link(w["было"], w["ην"], LinkMethod.StrongNumber, "a numbering");
        Link(w["Слово"], w["Λόγος"], LinkMethod.StrongNumber, "a numbering");

        (await Outcome("RUSV")).Should().Be(new SuppliedWordOutcome("RUSV", 2, 2, 1, 1));

        var absences = await db.Links.Where(l => l.Relation == LinkRelation.Expands)
            .Select(l => l.ToText!.Slug).ToListAsync();
        absences.Should().BeEquivalentTo(["BHSA", "NESTLE1904"], "TR1894's numbers render было, so it is not absent from TR1894");
        (await db.Links.AnyAsync(l => l.Id == septuagint)).Should().BeTrue();
    }

    [Fact]
    public async Task BrentonsItalicsAreAbsentFromHisGreekAndNothingIsStatedWhereTheGreekHasNoVerse()
    {
        var w = Place(
            ("BRENTON", "GEN 1:2", 1, "was"), ("GRCBRENT", "GEN 1:2", 1, "ἦν"), ("BHSA", "GEN 1:2", 1, "הָיְתָ֥ה"),
            ("BRENTON", "1SA 17:12", 1, "And"), ("GRCBRENT", "1SA 17:11", 1, "καὶ"));
        Mark("BRENTON", w["was"]);
        Mark("BRENTON", w["And"]);
        Link(w["was"], w["הָיְתָ֥ה"], LinkMethod.Aligner, Aligner);
        Link(w["And"], w["καὶ"], LinkMethod.StrongNumber, "a numbering");

        (await Outcome("BRENTON")).Should().Be(new SuppliedWordOutcome("BRENTON", 1, 1, 0, 0));
        (await db.Links.Where(l => l.Relation == LinkRelation.Expands).Select(l => l.ToText!.Slug).ToListAsync())
            .Should().Equal("GRCBRENT");
    }

    [Fact]
    public async Task TheKingJamesIsLeftToItsOwnLoader()
    {
        var w = Place(("KJV", "RUT 1:1", 1, "was"), ("BHSA", "RUT 1:1", 1, "וַיְהִ֥י"));
        Mark("KJV", w["was"]);
        Link(w["was"], w["וַיְהִ֥י"], LinkMethod.Aligner, Aligner);

        (await SuppliedWordAbsences.State(db)).Should().NotContain(outcome => outcome.Text == "KJV")
            .And.OnlyContain(outcome => outcome.Words == 0);
        (await db.Links.CountAsync(l => l.Relation == LinkRelation.Expands)).Should().Be(0);
    }

    private async Task<SuppliedWordOutcome> Outcome(string text) =>
        (await SuppliedWordAbsences.State(db)).Single(outcome => outcome.Text == text);

    private static Text Text(string slug, TextKind kind, string language) =>
        new() { Slug = slug, Name = slug, Kind = kind, Language = language };

    private Dictionary<string, Word> Place(params (string Text, string Reference, int Position, string Surface)[] words)
    {
        var placed = db.Place(words.Select(w => new RuledWord(w.Text, w.Reference, w.Position, w.Surface)), texts);
        return placed.ToDictionary(pair => pair.Key.Surface, pair => pair.Value);
    }

    private void Mark(string text, params Word[] words)
    {
        var position = db.WordGroups.Count(g => g.TextId == texts[text].Id) + 1;
        var group = new WordGroup { TextId = texts[text].Id, Kind = WordGroupKind.Supplied, Position = position };
        foreach (var word in words)
        {
            group.Words.Add(new WordGroupWord { WordId = word.Id });
        }

        db.WordGroups.Add(group);
        db.SaveChanges();
    }

    private long Link(Word from, Word to, LinkMethod method, string source) => Link([from], [to], method, source);

    private long Link(Word[] from, Word[] to, LinkMethod method, string source)
    {
        var link = new Link
        {
            FromTextId = from[0].TextId, ToTextId = to[0].TextId, Relation = LinkRelation.Renders,
            Method = method, Confidence = method == LinkMethod.StatedBySource ? null : 0.9, Provenance = new() { Source = source },
            Fingerprint = LinkShape.Of([.. from.Select(w => w.Id)], [.. to.Select(w => w.Id)]),
        };
        foreach (var word in from) link.Words.Add(new() { Word = word, Side = LinkSide.From });
        foreach (var word in to) link.Words.Add(new() { Word = word, Side = LinkSide.To });
        link.Claims.Add(new() { Method = method, Confidence = link.Confidence, Provenance = link.Provenance });
        db.Links.Add(link);
        db.SaveChanges();
        return link.Id;
    }

    private async Task<List<string>> Fingerprint() =>
        await db.Database.SqlQueryRaw<string>(
                "SELECT l.id || ':' || l.relation || ':' || string_agg(lw.word_id || lw.side, ',' ORDER BY lw.word_id) AS \"Value\" " +
                "FROM link l JOIN link_word lw ON lw.link_id = l.id GROUP BY l.id, l.relation ORDER BY l.id")
            .ToListAsync();
}
