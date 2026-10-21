using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// Rulings on the names of Genesis: the man who is Adam, Esau's wives, and the nations the table of
/// nations says a father begat.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordGenesisTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// הָאָדָם of Genesis 2-4 is Adam where the narrative means him, and the translation it reaches
    /// prints his name; אָדָם as mankind (GEN 1:26-27; 5:1-2), a man yet to be (2:5) and the man the
    /// woman is named from (2:23) stay nobody's. The last Adam of 1 Corinthians 15:45 is Christ, and the
    /// name's own resolution there gives way to the ruling; the first Adam of the verse is not touched.
    /// </summary>
    [Fact]
    public async Task TheManIsAdamWhereTheNarrativeMeansHimAndTheLastAdamIsChrist()
    {
        var file = SenseReadingFiles.AllRulings().Single(f => f.Rulings.Any(r => r.Reference == "1CO 15:45"));
        file.Rulings.Where(r => r.Text == "BHSA").Should().OnlyContain(r => r.Existing == "adam")
            .And.HaveCount(23);
        file.Rulings.Select(r => r.Reference).Should()
            .NotContain(["GEN 1:26", "GEN 1:27", "GEN 2:5", "GEN 2:24", "GEN 5:1", "GEN 5:2"]);
        file.Rulings.Should().NotContain(r => r.Reference == "GEN 2:23" && r.Position != 4);
        file.Rulings.Where(r => r.Reference == "1TI 2:14").Should().HaveCount(6).And.OnlyContain(r => r.Existing == "eve");
        file.Rulings.Where(r => r.Reference == "1CO 15:45").Should().HaveCount(6)
            .And.OnlyContain(r => r.Existing == "jesus" && r.Position == 14);

        var english = Db.WordAt(English, 1, 1, 1);
        Tie(Words[Ruled("adam", "GEN 2:7").Word], english);
        var adam = await Db.Entities.SingleAsync(e => e.Slug == "adam");
        var lastAdam = Words[file.Rulings.First(r => r.Reference == "1CO 15:45" && r.Text == "NESTLE1904").Word];
        Db.WordEntities.Add(new WordEntity
        {
            WordId = lastAdam, EntityId = adam.Id, Method = LinkMethod.StrongNumber, Confidence = 0.99,
            Source = "the lexicon's capitalised lemma, as a test",
        });
        await Db.SaveChangesAsync();

        await Load();

        (await Db.WordEntities.Where(a => a.WordId == english.Id).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("adam");
        (await Db.WordEntities.Where(a => a.WordId == lastAdam).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jesus");
        var woman = Words[file.Rulings.First(r => r.Reference == "1TI 2:14" && r.Text == "TR1894").Word];
        (await Db.WordEntities.Where(a => a.WordId == woman).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("eve");
    }

    [Fact]
    public async Task UkrainianAdamNamesAdamAndPreservesHisWifesReading()
    {
        var adamAddress = new RuledWord("UBIO", "GEN 3:8", 17, "Адам");
        var wifeAddress = new RuledWord("UBIO", "GEN 3:8", 20, "жінка");
        var addresses = new[] { adamAddress, wifeAddress };
        var added = Words.ContainsKey(adamAddress)
            ? new Dictionary<RuledWord, Word>() : Db.Place(addresses);
        if (Words.TryGetValue(adamAddress, out var adamWordId))
        {
            var adamWord = await Db.Words.SingleAsync(w => w.Id == adamWordId);
            added[wifeAddress] = Db.Words.Add(new Word { TextId = adamWord.TextId, VerseId = adamWord.VerseId,
                Position = wifeAddress.Position, Surface = wifeAddress.Surface, Trailer = " " }).Entity;
            await Db.SaveChangesAsync();
        }
        var focus = addresses.ToDictionary(a => a, a => Words.TryGetValue(a, out var id)
            ? Db.Words.Single(w => w.Id == id) : added[a]);
        foreach (var slug in new[] { "adam", "eve" })
            if (!Db.Entities.Any(e => e.Slug == slug))
                Db.Entities.Add(new Entity { Slug = slug, SourceId = slug, Name = slug,
                    Kind = EntityKind.Person, Source = "a test" });
        await Db.SaveChangesAsync();
        var adam = await Db.Entities.SingleAsync(e => e.Slug == "adam");
        var eve = await Db.Entities.SingleAsync(e => e.Slug == "eve");
        var wrong = new WordEntity { Word = focus[adamAddress], Entity = eve,
            Method = LinkMethod.RuleBased, Confidence = 0.988028802880288, Source = NameConsensusPass.Source,
            Note = "the word its verses share in this text: адам" };
        var wife = new WordEntity { Word = focus[wifeAddress], Entity = eve,
            Method = LinkMethod.ModelReading, Confidence = 0.91, Source = "a reading of his wife",
            Claims = [new WordEntityClaim { Method = LinkMethod.ModelReading, Confidence = 0.91,
                Source = "a reading of his wife" }] };
        Db.WordEntities.AddRange(wrong, wife);
        await Db.SaveChangesAsync();
        var wifeId = wife.Id;
        var wifeClaimId = wife.Claims.Single().Id;

        await Load();
        var wordId = focus[adamAddress].Id;
        (await Db.WordEntities.Where(a => a.WordId == wordId).Select(a => a.EntityId).ToArrayAsync())
            .Should().Equal(adam.Id);
        (await Db.WordEntities.AsNoTracking().SingleAsync(a => a.Id == wifeId)).EntityId.Should().Be(eve.Id);
        (await Db.WordEntityClaims.AsNoTracking().SingleAsync(c => c.Id == wifeClaimId)).WordEntityId.Should().Be(wifeId);
        var fixedId = (await Db.WordEntities.SingleAsync(a => a.WordId == wordId)).Id;
        await Load();
        (await Db.WordEntities.SingleAsync(a => a.WordId == wordId)).Id.Should().Be(fixedId);
        await Db.WordEntities.Where(a => a.WordId == wordId).ExecuteDeleteAsync();
        await Load();
        (await Db.WordEntities.AsNoTracking().SingleAsync(a => a.WordId == wordId)).EntityId.Should().Be(adam.Id);
    }

    /// <summary>
    /// Esau's wives of Genesis 36:2-3 are each a record of her own, the words of the second list leave the
    /// records of the first, and every record of the six names the other reading; Beeri is not Anah. The
    /// owner's later decision makes the two pairs with one father one woman each, Adah and Bashemath
    /// daughter of Elon, Mahalath and Bashemath daughter of Ishmael, which the fold then joins; Judith
    /// and Aholibamah stay open, with the identification some commentators make.
    /// </summary>
    [Fact]
    public async Task EsausWivesOfTheSecondListAreWomenOfTheirOwnWhoNameTheOtherReading()
    {
        var file = SenseReadingFiles.UnsettledThirdRulings();
        foreach (var held in new[] { "basemath", "judith", "mahalath" })
        {
            var record = Db.Entities.Single(e => e.Slug == held);
            foreach (var ruling in file.Rulings.Where(r => r.Corrects == held))
            {
                Db.WordEntities.Add(new WordEntity
                {
                    WordId = Words[ruling.Word], EntityId = record.Id, Method = LinkMethod.ModelReading,
                    Confidence = 0.8, Source = "a reading of the verse",
                });
            }
        }

        await Db.SaveChangesAsync();
        await Load();

        var wives = new Dictionary<string, string>
        {
            ["adah-wife-of-esau"] = "basemath", ["oholibamah-wife-of-esau"] = "judith", ["basemath-daughter-of-ishmael"] = "mahalath",
        };
        foreach (var (wife, first) in wives)
        {
            var record = await Db.Entities.SingleAsync(e => e.Slug == wife);
            record.Notes.Should().Contain(wife switch
            {
                "adah-wife-of-esau" => "Adah is the Bashemath daughter of Elon of 26:34 under another name",
                "oholibamah-wife-of-esau" => "Some commentators hold that Aholibamah and Judith are one woman",
                _ => "the text does not say",
            });
            (await Db.EntityAlternatives.Where(a => a.EntityId == record.Id).Select(a => a.Alternative!.Slug).ToListAsync())
                .Should().Equal(first);
            var words = file.Rulings.Where(r => (r.Create?.Slug ?? r.Existing) == wife).Select(r => Words[r.Word]).ToList();
            words.Should().HaveCountGreaterThan(3);
            (await Db.WordEntities.Where(a => words.Contains(a.WordId)).Select(a => a.Entity!.Slug).Distinct().ToListAsync())
                .Should().Equal(wife);
            (await Db.Entities.SingleAsync(e => e.Slug == first)).Notes.Should().Contain(first switch
            {
                "mahalath" => "Mahalath is the Bashemath of 36:3 under another name",
                "judith" => "Some commentators hold that Judith and Aholibamah are one woman",
                _ => "the text does not say",
            });
        }

        var beeri = await Db.Entities.SingleAsync(e => e.Slug == "beeri");
        beeri.Distinguisher.Should().NotContain("Anah");
        beeri.Notes.Should().Contain("does not say");
    }

    /// <summary>
    /// Where the text says Canaan begat the Jebusite, the word is a son of Canaan named as the verse
    /// names him, in Genesis and in Chronicles alike; the people of the same name loses that word and
    /// keeps every other word that names it. Lud and Naphtuh, men a dataset made of the plurals, are
    /// the sons of their verse and are headed as the verse writes them.
    /// </summary>
    [Fact]
    public async Task TheNationsAFatherBegatAreHisSonsAndThePeopleKeepItsOtherWords()
    {
        var people = new Entity
        {
            Kind = EntityKind.People, Slug = "jebusites", Name = "Jebusites", SourceId = "jebusites", Source = "a test",
        };
        Db.Entities.Add(people);
        var elsewhere = Db.Place([new RuledWord("BHSA", "JOS 15:63", 3, "הַיְבוּסִי")]).Values.Single();
        await Db.SaveChangesAsync();
        var begotten = Words[Ruled("jebusite-son-of-canaan", "GEN 10:16").Word];
        foreach (var word in new[] { begotten, elsewhere.Id })
        {
            Db.WordEntities.Add(new WordEntity
            {
                WordId = word, EntityId = people.Id, Method = LinkMethod.Lexical, Confidence = 0.9,
                Source = "the gentilic Strong's Dictionary derives, resolving to exactly one people",
            });
        }

        await Db.SaveChangesAsync();

        await Load();

        (await Db.WordEntities.Where(a => a.WordId == begotten).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jebusite-son-of-canaan");
        (await Db.WordEntities.Where(a => a.WordId == Words[Ruled("jebusite-son-of-canaan", "1CH 1:14").Word])
            .Select(a => a.Entity!.Slug).ToListAsync()).Should().Equal("jebusite-son-of-canaan");
        (await Db.WordEntities.Where(a => a.WordId == elsewhere.Id).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jebusites");

        var son = await Db.Entities.SingleAsync(e => e.Slug == "jebusite-son-of-canaan");
        son.Kind.Should().Be(EntityKind.Person);
        son.Name.Should().Be("Jebusite");
        son.Distinguisher.Should().Be("son of Canaan (GEN 10:16)");
        (await Db.Entities.SingleAsync(e => e.Slug == "lud")).Name.Should().Be("Ludim");
        (await Db.Entities.SingleAsync(e => e.Slug == "naphtuh")).Name.Should().Be("Naphtuhim");
    }
}
