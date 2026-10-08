using System.Text.Json;
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
/// The owner's ruling of 2026-10-08: in <em>the sons of Israel</em> the people is on <em>sons</em> and
/// Jacob on <em>Israel</em>, and a tribe's name standing alone is whichever its sentence was read as.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EponymReadingTests : IDisposable
{
    private const string Son = "H1121";
    private const string Israel = "H3478";
    private const string Reuben = "H7205";
    private const string Joseph = "H3130";
    private const string Ephraim = "H669";

    private readonly AppDbContext _db;
    private readonly Text _hebrew;
    private readonly Text _english;
    private readonly Dictionary<string, Entity> _records = [];

    public EponymReadingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (46, 9, ["ובני", "ראובן"]),
            (48, 1, ["את", "אפרים"]));
        _db.AddBook(_hebrew, 2, "Exodus", (1, 7, ["ובני", "ישראל", "פרו"]));
        _db.AddBook(_hebrew, 4, "Numbers", (1, 10, ["לבני", "יוסף"]), (1, 20, ["בני", "ראובן"]));
        _db.AddBook(_hebrew, 11, "1 Kings", (12, 16, ["לאהליך", "ישראל"]), (12, 19, ["ויפשעו", "ישראל"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (48, 1, ["Ephraim"]));
        _db.AddBook(_english, 2, "Exodus", (1, 7, ["the", "children", "of", "Israel", "were", "fruitful"]));
        _db.AddBook(_english, 11, "1 Kings", (12, 16, ["O", "Israel"]));
        var bsb = Corpus.Add(_db, "BSB", TextKind.Translation, "eng", (1, 1, ["x"]));
        _db.AddBook(bsb, 2, "Exodus", (1, 7, ["but", "the", "Israelites", "were", "fruitful"]));
        _db.SaveChanges();

        Hebrew(1, 46, 9, 1, Son, "subs", construct: true);
        Hebrew(1, 46, 9, 2, Reuben, "nmpr");
        Hebrew(1, 48, 1, 2, Ephraim, "nmpr");
        Hebrew(2, 1, 7, 1, Son, "subs", construct: true);
        Hebrew(2, 1, 7, 2, Israel, "nmpr");
        Hebrew(4, 1, 10, 1, Son, "subs", construct: true);
        Hebrew(4, 1, 10, 2, Joseph, "nmpr");
        Hebrew(4, 1, 20, 1, Son, "subs", construct: true);
        Hebrew(4, 1, 20, 2, Reuben, "nmpr");
        Hebrew(11, 12, 16, 2, Israel, "nmpr");
        Hebrew(11, 12, 19, 2, Israel, "nmpr");
        _db.SaveChanges();

        Link(English(2, 1, 7, 2), At(_hebrew, 2, 1, 7, 1));
        Link(English(2, 1, 7, 4), At(_hebrew, 2, 1, 7, 2));
        Link(English(11, 12, 16, 2), At(_hebrew, 11, 12, 16, 2));
        Link(English(1, 48, 1, 1), At(_hebrew, 1, 48, 1, 2));
        Link(At(bsb, 2, 1, 7, 3), At(_hebrew, 2, 1, 7, 1));
        Link(At(bsb, 2, 1, 7, 3), At(_hebrew, 2, 1, 7, 2));

        var jacob = Record("jacob", EntityKind.Person, Israel, null);
        Record("israelites", EntityKind.People, Israel, jacob);
        var reuben = Record("reuben", EntityKind.Person, Reuben, null);
        Record("reubenites", EntityKind.People, Reuben, reuben);
        var ephraim = Record("ephraim", EntityKind.Person, Ephraim, null);
        Record("ephraimites", EntityKind.People, Ephraim, ephraim);
        var joseph = Record("joseph", EntityKind.Person, Joseph, null);
        Record("joseph-2", EntityKind.Person, Joseph, null);
        Record("josephites", EntityKind.People, null, joseph);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM link");
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry");
    }

    private Word At(Text text, int book, int chapter, int verse, int position) =>
        _db.Words.Single(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == book
                              && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse && w.Position == position);

    private Word English(int book, int chapter, int verse, int position) => At(_english, book, chapter, verse, position);

    private void Hebrew(int book, int chapter, int verse, int position, string number, string pos, bool construct = false)
    {
        var word = At(_hebrew, book, chapter, verse, position);
        word.StrongNumber = number;
        word.Morphology = JsonDocument.Parse(construct
            ? $$"""{"pos": "{{pos}}", "state": "c", "number": "pl"}"""
            : $$"""{"pos": "{{pos}}", "nameType": "pers,gens,topo"}""");
    }

    private void Link(Word translation, Word original)
    {
        var link = new Link
        {
            FromTextId = translation.TextId, ToTextId = original.TextId, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = translation, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = original, Side = LinkSide.To });
        _db.SaveChanges();
    }

    private Entity Record(string slug, EntityKind kind, string? number, Entity? origin)
    {
        var entity = new Entity
        {
            Kind = kind, Slug = slug, Name = slug, SourceId = slug, Source = "a test", OriginEntityId = origin?.Id,
            Names = [new EntityName { Label = slug.Split('-')[0], HebrewStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        _db.SaveChanges();
        _records[slug] = entity;
        return entity;
    }

    private void Annotate(Word word, string slug, LinkMethod method, string source, double? confidence = 0.8, string? note = null)
    {
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = _records[slug].Id, Method = method, Confidence = confidence, Source = source,
            Note = note,
        });
        _db.SaveChanges();
    }

    private async Task<List<string>> Shown(Word word) =>
        [.. (await Annotations.AllOf(_db, word.Id, CancellationToken.None)).Select(entity => entity.Slug).Order()];

    private EponymReadingLoader Loader() => new(_db, NullLogger<EponymReadingLoader>.Instance);

    private static EponymReadings Read(params EponymReading[] readings) => new("a model", "2026-10-08", readings);

    private static EponymReading Reading(string reference, int position, string surface, string? names, string instead) =>
        new(EntityCandidates.Witness, reference, position, surface, names, instead, 0.95, "the sentence says so");

    /// <summary>
    /// <em>The children of Israel were fruitful</em>: Jacob on the name, the Israelites on
    /// <em>children</em>, and the Berean's one word for the two takes the people.
    /// </summary>
    [Fact]
    public async Task TheSonsOfIsraelAreThePeopleOnSonsAndJacobOnIsrael()
    {
        await Loader().Load(Read());

        (await Shown(At(_hebrew, 2, 1, 7, 2))).Should().Equal("jacob");
        (await Shown(At(_hebrew, 2, 1, 7, 1))).Should().Equal("israelites");
        (await Shown(English(2, 1, 7, 2))).Should().Equal(["israelites"], "children renders sons");
        (await Shown(English(2, 1, 7, 4))).Should().Equal("jacob");
        (await Shown(At(_db.Texts.Single(t => t.Slug == "BSB"), 2, 1, 7, 3))).Should()
            .Equal(["israelites"], "one word renders both, and it is the people");
    }

    /// <summary>
    /// Genesis 46:9, <em>the sons of Reuben: Hanoch…</em>, are his own sons: <em>sons</em> names
    /// nobody there, and in Numbers 1:20 the tribe.
    /// </summary>
    [Fact]
    public async Task HisOwnSonsNamedInThePassageAreNotThePeople()
    {
        var outcome = await Loader().Load(Read());

        (await Shown(At(_hebrew, 1, 46, 9, 2))).Should().Equal("reuben");
        (await Shown(At(_hebrew, 1, 46, 9, 1))).Should().BeEmpty();
        (await Shown(At(_hebrew, 4, 1, 20, 1))).Should().Equal("reubenites");
        (await Shown(At(_hebrew, 4, 1, 20, 2))).Should().Equal("reuben");
        outcome.Names.Should().Be(4);
        outcome.Heads.Should().Be(3);
    }

    /// <summary>The tribe of Joseph has no word of its own; <em>the sons of Joseph</em> are it.</summary>
    [Fact]
    public async Task TheSonsOfJosephAreTheTribeOfJoseph()
    {
        await Loader().Load(Read());

        (await Shown(At(_hebrew, 4, 1, 10, 1))).Should().Equal("josephites");
        (await Shown(At(_hebrew, 4, 1, 10, 2))).Should().Equal("joseph");
    }

    /// <summary>
    /// What the interim rule and a reading of the verse wrote as the people on the name after
    /// <em>sons</em> is taken back, with what it carried to the King James; a person's ruling is not.
    /// </summary>
    [Fact]
    public async Task TheOtherAnswerIsTakenBackWithWhatItCarriedButARulingStands()
    {
        var name = At(_hebrew, 2, 1, 7, 2);
        Annotate(name, "israelites", LinkMethod.RuleBased, EponymNameLoader.FormerSource, 0.9);
        Annotate(English(2, 1, 7, 4), "israelites", LinkMethod.RuleBased, EponymNameLoader.FormerSource, 0.9,
            $"through BHSA word {name.Id}, linked by stated-by-source");
        Annotate(At(_hebrew, 4, 1, 20, 2), "reubenites", LinkMethod.Manual, "a person's ruling", null);

        var outcome = await Loader().Load(Read());

        (await Shown(name)).Should().Equal("jacob");
        (await Shown(English(2, 1, 7, 4))).Should().Equal("jacob");
        (await Shown(At(_hebrew, 4, 1, 20, 2))).Should().Equal(["reubenites"], "a ruling is not this pass's to change");
        outcome.Withdrawn.Should().Be(1);
    }

    /// <summary>
    /// A name standing alone is what its reading says, above the interim answer, and carried; one the
    /// reading could not settle keeps the interim answer.
    /// </summary>
    [Fact]
    public async Task ANameStandingAloneIsWhatItsSentenceWasReadAs()
    {
        var tents = At(_hebrew, 11, 12, 16, 2);
        var rebelled = At(_hebrew, 11, 12, 19, 2);
        var ephraim = At(_hebrew, 1, 48, 1, 2);
        Annotate(tents, "israelites", LinkMethod.RuleBased, EponymNameLoader.Source, 0.9);
        Annotate(rebelled, "israelites", LinkMethod.RuleBased, EponymNameLoader.Source, 0.9);
        Annotate(ephraim, "ephraimites", LinkMethod.ModelReading, "a reading before the tribes were records", 0.8);
        Annotate(English(1, 48, 1, 1), "ephraimites", LinkMethod.ModelReading, "a reading before the tribes were records", 0.8,
            $"through BHSA word {ephraim.Id}, linked by stated-by-source");
        _db.WordEntityClaims.Add(new WordEntityClaim
        {
            WordEntityId = _db.WordEntities.Single(a => a.WordId == ephraim.Id).Id, Method = LinkMethod.StatedBySource,
            Source = "a dataset's list of the verses each record is named in",
        });
        _db.SaveChanges();

        var outcome = await Loader().Load(Read(
            Reading("1KI 12:16", 2, "ישראל", "israelites", "jacob"),
            Reading("GEN 48:1", 2, "אפרים", "ephraim", "ephraimites"),
            Reading("1KI 12:19", 2, "ישראל", null, "jacob")));

        (await Shown(tents)).Should().Equal("israelites");
        (await Shown(English(11, 12, 16, 2))).Should().Equal("israelites");
        (await Shown(ephraim)).Should().Equal("ephraim");
        (await Shown(English(1, 48, 1, 1))).Should().Equal("ephraim");
        (await Shown(rebelled)).Should().Equal(["israelites"], "unclear keeps the interim answer");
        outcome.ReadAsTheMan.Should().Be(1);
        outcome.ReadAsThePeople.Should().Be(1);
        outcome.Unclear.Should().Be(1);
        (await _db.WordEntities.SingleAsync(a => a.WordId == tents.Id)).Method.Should().Be(LinkMethod.RuleBased,
            "a reading agreeing with the row there lands as a claim on it");
        (await _db.WordEntityClaims.AnyAsync(c => c.WordEntity!.WordId == tents.Id && c.Source == EponymReadingLoader.ReadingSource))
            .Should().BeTrue();
    }

    /// <summary>
    /// Where a reading of the word names the people, the interim answer or the verses' consensus of the
    /// man beside it is taken back, though this pass does not answer the word itself.
    /// </summary>
    [Fact]
    public async Task TheInterimManBesideAReadingOfThePeopleGoes()
    {
        var ephraim = At(_hebrew, 1, 48, 1, 2);
        Annotate(ephraim, "ephraim", LinkMethod.RuleBased, EponymNameLoader.Source, 0.7);
        Annotate(English(1, 48, 1, 1), "ephraim", LinkMethod.RuleBased, EponymNameLoader.Source, 0.7,
            $"through BHSA word {ephraim.Id}, linked by stated-by-source");
        Annotate(ephraim, "ephraimites", LinkMethod.ModelReading, "two models read the verse", 0.9);
        var tents = At(_hebrew, 11, 12, 16, 2);
        Annotate(tents, "israelites", LinkMethod.ModelReading, "two models read the verse", 0.9);
        Annotate(tents, "jacob", LinkMethod.RuleBased, NameConsensusPass.Source, 0.9);

        await Loader().Load(Read());

        (await Shown(ephraim)).Should().Equal("ephraimites");
        (await Shown(English(1, 48, 1, 1))).Should().BeEmpty();
        (await Shown(tents)).Should().Equal(["israelites"], "the verses' consensus gives way to a reading as well");
    }

    /// <summary>
    /// After the word for a tribe the owner's ruling gives the man; a reading of the people beside him,
    /// and what it carried, is taken back.
    /// </summary>
    [Fact]
    public async Task TheOwnersConstructRulingsStandAlone()
    {
        var ephraim = At(_hebrew, 1, 48, 1, 2);
        Annotate(ephraim, "ephraim", LinkMethod.RuleBased, TribeNameLoader.Source, 0.9);
        _db.WordEntityClaims.Add(new WordEntityClaim
        {
            WordEntityId = _db.WordEntities.Single(a => a.WordId == ephraim.Id).Id, Method = LinkMethod.RuleBased,
            Confidence = 0.9, Source = TribeNameLoader.Source,
        });
        Annotate(ephraim, "ephraimites", LinkMethod.ModelReading, "two models read the verse", 0.9);
        Annotate(English(1, 48, 1, 1), "ephraimites", LinkMethod.ModelReading, "two models read the verse", 0.9,
            $"through BHSA word {ephraim.Id}, linked by stated-by-source");

        var outcome = await Loader().Load(Read());

        (await Shown(ephraim)).Should().Equal("ephraim");
        (await _db.WordEntities.AnyAsync(a => a.WordId == English(1, 48, 1, 1).Id && a.Entity!.Slug == "ephraimites"))
            .Should().BeFalse();
        outcome.Constructs.Should().Be(1);
    }

    /// <summary>A reading whose word is not where it says is refused, and a second load writes nothing.</summary>
    [Fact]
    public async Task AMovedWordIsRefusedAndASecondLoadWritesNothing()
    {
        var moved = () => Loader().Load(Read(Reading("1KI 12:16", 2, "יהודה", "israelites", "jacob")));
        await moved.Should().ThrowAsync<InvalidDataException>();

        (await Loader().Load(Read())).AlreadyLoaded.Should().BeFalse();
        var rows = await _db.WordEntities.CountAsync();
        (await Loader().Load(Read())).AlreadyLoaded.Should().BeTrue();
        (await _db.WordEntities.CountAsync()).Should().Be(rows);
    }

    /// <summary>A reading added to the file after a load is applied by the next one, and only then is it done.</summary>
    [Fact]
    public async Task AReadingAddedLaterIsAppliedByTheNextLoad()
    {
        var ephraim = At(_hebrew, 1, 48, 1, 2);
        Annotate(ephraim, "ephraimites", LinkMethod.RuleBased, EponymNameLoader.Source, 0.7);
        await Loader().Load(Read());

        var later = Read(Reading("GEN 48:1", 2, "אפרים", "ephraim", "ephraimites"));
        (await Loader().Load(later)).ReadAsTheMan.Should().Be(1);
        (await Shown(ephraim)).Should().Equal("ephraim");
        (await Loader().Load(later)).AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>
    /// Hebrews 11:21, <em>Jacob blessed each of the sons of Joseph</em>: his own two sons, so υἱῶν names
    /// nobody, as בְּנֵי does in Genesis 46; in 11:22, <em>the departing of the sons of Israel</em>, the people.
    /// </summary>
    [Fact]
    public async Task HisOwnSonsInTheGreekAreNotThePeopleEither()
    {
        var (blessed, departing) = Hebrews();

        await Loader().Load(Read());

        (await Shown(blessed[0])).Should().BeEmpty();
        (await Shown(blessed[1])).Should().Equal("joseph");
        (await Shown(departing[0])).Should().Equal("israelites");
        (await Shown(departing[1])).Should().Equal("jacob");
    }

    /// <summary>
    /// What the rule wrote on an earlier load and no longer gives — the people on <em>sons</em> of Hebrews
    /// 11:21, and the same carried into a translation — is taken back, and the load after writes nothing.
    /// </summary>
    [Fact]
    public async Task WhatTheRuleNoLongerGivesIsTakenBackWithWhatItCarried()
    {
        var (blessed, _) = Hebrews();
        await Loader().Load(Read());
        var kjv = _english;
        _db.AddBook(kjv, 58, "Hebrews", (11, 21, ["the", "sons", "of", "Joseph"]));
        await _db.SaveChangesAsync();
        var sons = At(kjv, 58, 11, 21, 2);
        Annotate(blessed[0], "josephites", LinkMethod.RuleBased, EponymReadingLoader.Source, 0.9,
            "G5207, sons of G2501, so the people of that name");
        Annotate(sons, "josephites", LinkMethod.RuleBased, EponymReadingLoader.Source, 0.9,
            $"through NESTLE1904 word {blessed[0].Id}, linked by stated-by-source");

        var outcome = await Loader().Load(Read());

        outcome.AlreadyLoaded.Should().BeFalse();
        outcome.Outdated.Should().Be(2);
        (await Shown(blessed[0])).Should().BeEmpty();
        (await Shown(sons)).Should().BeEmpty();
        (await Loader().Load(Read())).AlreadyLoaded.Should().BeTrue();
    }

    /// <summary>Hebrews 11:21 and 11:22 in the Nestle text: υἱῶν Ἰωσὴφ and υἱῶν Ἰσραὴλ.</summary>
    private (Word[] Blessed, Word[] Departing) Hebrews()
    {
        var nestle = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc", (1, 1, ["x"]));
        _db.AddBook(nestle, 58, "Hebrews", (11, 21, ["υἱῶν", "Ἰωσὴφ"]), (11, 22, ["υἱῶν", "Ἰσραὴλ"]));
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = "G2501", Lemma = "Ἰωσήφ", Derivation = "of Hebrew origin (H3130)" });
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = "G2474", Lemma = "Ἰσραήλ", Derivation = "of Hebrew origin (H3478)" });
        _db.SaveChanges();

        Word[] Verse(int verse, string name)
        {
            var words = new[] { At(nestle, 58, 11, verse, 1), At(nestle, 58, 11, verse, 2) };
            words[0].StrongNumber = "G5207";
            words[0].Morphology = JsonDocument.Parse("""{"form": "N-GPM"}""");
            words[1].StrongNumber = name;
            return words;
        }

        var blessed = Verse(21, "G2501");
        var departing = Verse(22, "G2474");
        _db.SaveChanges();
        return (blessed, departing);
    }

    /// <summary>The readings shipped with the loader name only the two candidates of each word.</summary>
    [Fact]
    public void TheShippedReadingsNameTheManOrThePeople()
    {
        var file = EponymReadingLoader.Embedded();
        file.Readings.Should().OnlyContain(r => r.Names == null || r.Names != r.Instead);
        file.Readings.Select(r => r.Word).Should().OnlyHaveUniqueItems();
        file.Readings.Should().OnlyContain(r => r.Confidence >= 0 && r.Confidence <= 1);
    }
}
