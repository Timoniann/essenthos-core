using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Rulings on names of the New Testament and the texts linked to them: the Synodal's Christ and
/// Jehoiakim, Philip in the Received Text, the tribes of Revelation and the God of Jacob.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordNewTestamentTests(WitnessDatabase database) : OwnRecordTests(database)
{
    [Fact]
    public async Task SynodalAddedChristNamesTheTitleAndJesusLocallyWithoutChangingSatan()
    {
        var address = new RuledWord("RUSV", "1CO 5:5", 15, "Христа");
        var christ = Words.TryGetValue(address, out var id) ? Db.Words.Single(w => w.Id == id)
            : Db.Place([address])[address];
        var russianSatan = Db.Words.Single(w => w.VerseId == christ.VerseId && w.Position == 2);
        russianSatan.Surface = "сатане";
        var greekAddress = new RuledWord("NESTLE1904", "1CO 5:5", 5, "Σατανᾷ");
        var greek = Db.Place([greekAddress])[greekAddress];
        foreach (var slug in new[] { "satan", "jesus", "anointed" })
            if (!Db.Entities.Any(e => e.Slug == slug))
                Db.Entities.Add(new Entity { Slug = slug, SourceId = slug, Name = slug,
                    Kind = slug == "anointed" ? EntityKind.Title : EntityKind.Person, Source = "a test" });
        await Db.SaveChangesAsync();
        var satan = await Db.Entities.SingleAsync(e => e.Slug == "satan");
        Db.TitleBearers.Add(new TitleBearer {
            TitleEntityId = (await Db.Entities.SingleAsync(e => e.Slug == "anointed")).Id,
            BearerEntityId = (await Db.Entities.SingleAsync(e => e.Slug == "jesus")).Id,
            CanonicalBook = 43, CanonicalChapter = 1, CanonicalVerse = 41,
            Source = "a fixture of the held Christ-Jesus title/bearer relation" });
        foreach (var word in new[] { russianSatan, greek })
            Db.WordEntities.Add(new() { Word = word, Entity = satan, Method = LinkMethod.StrongNumber,
                Confidence = 0.99, Source = "independent Satan reading" });
        var link = new Link { FromTextId = christ.TextId, ToTextId = greek.TextId,
            Method = LinkMethod.Aligner, Relation = LinkRelation.Renders, Confidence = 0.29,
            Provenance = new() { Source = "a fixture's faint alignment" } };
        link.Words.Add(new() { Word = christ, Side = LinkSide.From });
        link.Words.Add(new() { Word = greek, Side = LinkSide.To });
        await Db.SaveChangesAsync();
        var satanIds = await Db.WordEntities.Where(a => a.EntityId == satan.Id).Select(a => a.Id).ToArrayAsync();

        await Load();
        var named = await Db.WordEntities.Where(a => a.WordId == christ.Id).Include(a => a.Entity).AsNoTracking().ToListAsync();
        named.Select(a => a.Entity!.Slug).Should().BeEquivalentTo("anointed", "jesus");
        (await Essenthos.Core.Corpus.Annotations.AllOf(Db, [christ.Id], CancellationToken.None))[christ.Id]
            .Select(e => e.Slug).Should().BeEquivalentTo("anointed", "jesus");
        named.Should().OnlyContain(a => a.Method == LinkMethod.Manual && a.Confidence == null);
        var ids = named.Select(a => a.Id).Order().ToArray();
        var carrier = new AnnotationCarrier(Db,
            new CrossedNameLoader(Db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(Db, NullLogger<ForeignNames>.Instance), new EqualTwinNames(Db, NullLogger<EqualTwinNames>.Instance), new PronounReferents(Db, NullLogger<PronounReferents>.Instance), NullLogger<AnnotationCarrier>.Instance);
        await carrier.Carry();
        (await Db.WordEntities.Where(a => a.WordId == greek.Id).Select(a => a.EntityId).ToArrayAsync())
            .Should().Equal(satan.Id);
        (await Db.WordEntities.Where(a => a.EntityId == satan.Id && (a.WordId == russianSatan.Id || a.WordId == greek.Id)).Select(a => a.Id).ToArrayAsync())
            .Should().BeEquivalentTo(satanIds);
        var references = new OwnReferenceLoader(Db, NullLogger<OwnReferenceLoader>.Instance);
        await references.Load();
        await Db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        foreach (var slug in new[] { "jesus", "anointed" })
            (await Db.EntityVerses.AnyAsync(v => v.Entity!.Slug == slug && v.CanonicalBook == 46
                && v.CanonicalChapter == 5 && v.CanonicalVerse == 5 && v.Names)).Should().BeTrue();
        await Load();
        (await Db.WordEntities.Where(a => a.WordId == christ.Id && a.EntityId != satan.Id)
            .OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync()).Should().Equal(ids);
        await Db.WordEntities.Where(a => a.WordId == christ.Id && a.EntityId != satan.Id).ExecuteDeleteAsync();
        await Load();
        (await Db.WordEntities.Where(a => a.WordId == christ.Id && a.EntityId != satan.Id)
            .Include(a => a.Entity).Select(a => a.Entity!.Slug).ToArrayAsync()).Should().BeEquivalentTo("anointed", "jesus");
        (await Db.WordEntities.Where(a => a.WordId == greek.Id).Select(a => a.EntityId).ToArrayAsync())
            .Should().Equal(satan.Id);
    }

    [Fact]
    public async Task SynodalJehoiakimNamesTheKingWithoutAddingHimToGreekJeconiah()
    {
        var addresses = new[] {
            new RuledWord("RUSV", "MAT 1:11", 3, "Иоакима"),
            new RuledWord("RUSV", "MAT 1:11", 4, "Иоаким"),
        };
        var missing = Db.Place(addresses.Where(a => !Words.ContainsKey(a)));
        var source = addresses.Select(a => Words.TryGetValue(a, out var id)
            ? Db.Words.Single(w => w.Id == id) : missing[a]).ToArray();
        var others = Db.Place([
            new RuledWord("NESTLE1904", "MAT 1:11", 5, "Ἰεχονίαν"),
            new RuledWord("RUSV", "MAT 1:13", 6, "Елиакима"),
        ]);
        var greek = others.Single(p => p.Key.Text == "NESTLE1904").Value;
        var namesake = others.Single(p => p.Key.Text == "RUSV").Value;
        foreach (var slug in new[] { "eliakim-2", "jehoiachin", "eliakim-4" })
            if (!Db.Entities.Any(e => e.Slug == slug))
                Db.Entities.Add(new Entity { Slug = slug, SourceId = slug, Name = slug,
                    Kind = EntityKind.Person, Source = "a test" });
        await Db.SaveChangesAsync();
        var king = await Db.Entities.SingleAsync(e => e.Slug == "eliakim-2");
        var jeconiah = await Db.Entities.SingleAsync(e => e.Slug == "jehoiachin");
        var otherEliakim = await Db.Entities.SingleAsync(e => e.Slug == "eliakim-4");
        foreach (var pair in new[] { (greek, jeconiah), (namesake, otherEliakim) })
            Db.WordEntities.Add(new WordEntity { Word = pair.Item1, Entity = pair.Item2,
                Method = LinkMethod.Manual, Source = "a test's independent name reading" });
        var link = new Link { FromTextId = source[0].TextId, ToTextId = greek.TextId,
            Method = LinkMethod.Aligner, Relation = LinkRelation.Renders, Confidence = 0.29,
            Provenance = new() { Source = "a fixture's faint alignment" } };
        Db.LinkWords.Add(new LinkWord { Link = link, Word = source[0], Side = LinkSide.From });
        Db.LinkWords.Add(new LinkWord { Link = link, Word = greek, Side = LinkSide.To });
        await Db.SaveChangesAsync();

        await Load();
        var ids = source.Select(w => w.Id).ToArray();
        var named = await Db.WordEntities.Where(a => ids.Contains(a.WordId)).ToListAsync();
        named.Should().HaveCount(2).And.OnlyContain(a => a.EntityId == king.Id);
        named.Should().OnlyContain(a => a.Method == LinkMethod.Manual && a.Confidence == null);
        (await Db.WordEntities.AnyAsync(a => a.WordId == greek.Id && a.EntityId == king.Id))
            .Should().BeFalse();
        (await Db.WordEntities.SingleAsync(a => a.WordId == namesake.Id)).EntityId.Should().Be(otherEliakim.Id);
        var carrier = new AnnotationCarrier(Db,
            new CrossedNameLoader(Db, NullLogger<CrossedNameLoader>.Instance),
            new ForeignNames(Db, NullLogger<ForeignNames>.Instance),
            new EqualTwinNames(Db, NullLogger<EqualTwinNames>.Instance), new PronounReferents(Db, NullLogger<PronounReferents>.Instance), NullLogger<AnnotationCarrier>.Instance);
        await carrier.Carry();
        (await Db.WordEntities.AnyAsync(a => a.WordId == greek.Id && a.EntityId == king.Id))
            .Should().BeFalse();
        var references = new OwnReferenceLoader(Db, NullLogger<OwnReferenceLoader>.Instance);
        await references.Load();
        await Db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        (await Db.EntityVerses.AnyAsync(v => v.EntityId == king.Id && v.CanonicalBook == 40
            && v.CanonicalChapter == 1 && v.CanonicalVerse == 11 && v.Names)).Should().BeTrue();
        var annotationIds = named.Select(a => a.Id).Order().ToArray();
        await Load();
        (await Db.WordEntities.Where(a => ids.Contains(a.WordId) && a.EntityId == king.Id).OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync())
            .Should().Equal(annotationIds);
        await Db.WordEntities.Where(a => a.WordId == source[0].Id && a.EntityId == king.Id).ExecuteDeleteAsync();
        await Load();
        (await Db.WordEntities.AnyAsync(a => a.WordId == source[0].Id && a.EntityId == king.Id))
            .Should().BeTrue();
        (await Db.WordEntities.AnyAsync(a => a.WordId == greek.Id && a.EntityId == king.Id))
            .Should().BeFalse();
    }

    [Fact]
    public async Task PhilipInTheReceivedTextNamesHerodiasHusbandAndKeepsTheTetrarchSeparate()
    {
        var sourceAddress = new RuledWord("TR1894", "LUK 3:19", 13, "φιλιππου");
        var source = Words.TryGetValue(sourceAddress, out var sourceId)
            ? await Db.Words.SingleAsync(w => w.Id == sourceId)
            : Db.Place([sourceAddress])[sourceAddress];
        var targets = Db.Place([
            new RuledWord("TR1550", "LUK 3:19", 13, "φιλιππου"),
            new RuledWord("KJV", "LUK 3:19", 13, "Philip's"),
            new RuledWord("TR1550", "LUK 3:1", 19, "φιλιππου"),
        ]);
        foreach (var word in targets.Values.Append(source))
        {
            word.StrongNumber = "G5376";
            if (word.Text!.Slug.StartsWith("TR", StringComparison.Ordinal))
            {
                word.Text.Kind = TextKind.CriticalEdition;
                word.Text.Language = "grc";
            }
        }
        if (!await Db.Entities.AnyAsync(e => e.Slug == "philip-2"))
            Db.Entities.Add(new Entity { Slug = "philip-2", SourceId = "philip-2", Name = "Philip", Kind = EntityKind.Person, Source = "a test" });
        var tetrarch = new Entity { Slug = "philip-4", SourceId = "philip-4", Name = "Philip", Kind = EntityKind.Person, Source = "a test" };
        Db.Entities.Add(tetrarch);
        var tetrarchWord = targets.Single(p => p.Key.Reference == "LUK 3:1").Value;
        Db.WordEntities.Add(new WordEntity { Word = tetrarchWord, Entity = tetrarch, Method = LinkMethod.Manual, Source = "a test's tetrarch ruling" });
        foreach (var target in targets.Where(p => p.Key.Reference == "LUK 3:19").Select(p => p.Value))
        {
            var link = new Link { FromTextId = source.TextId, ToTextId = target.TextId,
                Relation = LinkRelation.Renders, Method = LinkMethod.StrongNumber, Confidence = 1,
                Provenance = new() { Source = "a fixture's stated number match" } };
            Db.LinkWords.Add(new LinkWord { Link = link, Word = source, Side = LinkSide.From });
            Db.LinkWords.Add(new LinkWord { Link = link, Word = target, Side = LinkSide.To });
        }
        await Db.SaveChangesAsync();
        await Load();
        var references = new OwnReferenceLoader(Db, NullLogger<OwnReferenceLoader>.Instance);
        await references.Load();
        await Db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        var wordIds = targets.Where(p => p.Key.Reference == "LUK 3:19").Select(p => p.Value.Id).Append(source.Id).ToArray();
        var named = await Db.WordEntities.Include(a => a.Entity).Where(a => wordIds.Contains(a.WordId)).ToListAsync();
        named.Should().HaveCount(3).And.OnlyContain(a => a.Entity!.Slug == "philip-2");
        named.Should().OnlyContain(a => a.Source.Contains("Textus Receptus"));
        named.Single(a => a.WordId == source.Id).Note.Should().Contain("Herodias");
        named.Where(a => a.WordId != source.Id).Should().OnlyContain(a => a.Note!.StartsWith("through "));
        (await Db.EntityVerses.AnyAsync(v => v.Entity!.Slug == "philip-2" && v.CanonicalBook == 42
            && v.CanonicalChapter == 3 && v.CanonicalVerse == 19 && v.Names)).Should().BeTrue();
        (await Db.WordEntities.SingleAsync(a => a.WordId == tetrarchWord.Id)).EntityId.Should().Be(tetrarch.Id);
        var annotationIds = named.Select(a => a.Id).Order().ToArray();
        await Load();
        var again = await references.Load();
        again.Written.Should().Be(0);
        again.Withdrawn.Should().Be(0);
        (await Db.WordEntities.Where(a => wordIds.Contains(a.WordId)).OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync())
            .Should().Equal(annotationIds);
    }

    [Fact]
    public async Task RevelationTribalNamesIdentifyTheAncestorsAndKeepNamesakesSeparate()
    {
        var addresses = new[] {
            (new RuledWord("NESTLE1904", "REV 7:4", 16, "Ἰσραήλ"), "jacob"),
            (new RuledWord("NESTLE1904", "REV 7:5", 3, "Ἰούδα"), "judah"),
            (new RuledWord("NESTLE1904", "REV 7:6", 8, "Νεφθαλεὶμ"), "naphtali"),
            (new RuledWord("NESTLE1904", "REV 7:6", 13, "Μανασσῆ"), "manasseh"),
            (new RuledWord("BSB", "REV 7:4", 18, "Israel"), "jacob"),
            (new RuledWord("BSB", "REV 7:5", 5, "Judah"), "judah"),
            (new RuledWord("BSB", "REV 7:6", 12, "Naphtali"), "naphtali"),
            (new RuledWord("BSB", "REV 7:6", 19, "Manasseh"), "manasseh"),
        };
        var added = Db.Place(addresses.Select(a => a.Item1).Where(a => !Words.ContainsKey(a)));
        var focus = addresses.ToDictionary(a => a.Item1, a => Words.TryGetValue(a.Item1, out var id)
            ? Db.Words.Single(w => w.Id == id) : added[a.Item1]);
        foreach (var slug in addresses.Select(a => a.Item2).Concat(new[] { "manasseh-3", "judah-6", "israelites" }).Distinct())
            if (!Db.Entities.Any(e => e.Slug == slug))
                Db.Entities.Add(new Entity { Slug = slug, SourceId = slug, Name = slug,
                    Kind = slug == "judah-6" ? EntityKind.Place : slug == "israelites" ? EntityKind.People : EntityKind.Person,
                    Source = "a test" });
        await Db.SaveChangesAsync();
        var entities = await Db.Entities.ToDictionaryAsync(e => e.Slug);
        foreach (var (address, target) in addresses.Where(a => a.Item2 != "naphtali"))
            Db.WordEntities.Add(new WordEntity { Word = focus[address], Entity = entities[target == "jacob"
                ? "israelites" : target == "judah" ? "judah-6" : "manasseh-3"],
                Method = LinkMethod.ModelReading, Confidence = 0.99, Source = "a fixture of the old readings" });
        var separate = Db.Place([new RuledWord("BSB", "MAT 1:10", 1, "Manasseh")]).Values.Single();
        Db.WordEntities.Add(new WordEntity { Word = separate, Entity = entities["manasseh-3"],
            Method = LinkMethod.Manual, Source = "a test of the king in the genealogy" });
        await Db.SaveChangesAsync();

        await Load();
        foreach (var (address, target) in addresses)
            (await Db.WordEntities.Where(a => a.WordId == focus[address].Id).Select(a => a.Entity!.Slug).ToArrayAsync())
                .Should().Equal(target);
        (await Db.WordEntities.SingleAsync(a => a.WordId == separate.Id)).EntityId.Should().Be(entities["manasseh-3"].Id);
        var references = new OwnReferenceLoader(Db, NullLogger<OwnReferenceLoader>.Instance);
        await references.Load();
        await Db.Database.ExecuteSqlRawAsync(DatasetLoader.NamingVerses);
        (await Db.EntityVerses.AnyAsync(v => v.EntityId == entities["naphtali"].Id && v.CanonicalBook == 66
            && v.CanonicalChapter == 7 && v.CanonicalVerse == 6 && v.Names)).Should().BeTrue();
        var ids = focus.Values.Select(w => w.Id).ToArray();
        var before = await Db.WordEntities.Where(a => ids.Contains(a.WordId)).OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync();
        await Load();
        (await Db.WordEntities.Where(a => ids.Contains(a.WordId)).OrderBy(a => a.Id).Select(a => a.Id).ToArrayAsync())
            .Should().Equal(before);
        await Db.WordEntities.Where(a => a.WordId == focus[addresses[2].Item1].Id).ExecuteDeleteAsync();
        await Load();
        (await Db.WordEntities.SingleAsync(a => a.WordId == focus[addresses[2].Item1].Id)).EntityId
            .Should().Be(entities["naphtali"].Id);
    }

    /// <summary>
    /// The Greek Jacob of 'the God of Abraham, Isaac and Jacob' is the patriarch, which the number cannot
    /// say because the father of Joseph bears it too; the Jacob of Romans 11:26 is the people. A rendering
    /// linked to the Greek word is given the patriarch through the link, and the father of Joseph keeps
    /// his own verse.
    /// </summary>
    [Fact]
    public async Task TheGodOfJacobNamesThePatriarchAndRomansJacobThePeople()
    {
        var file = SenseReadingFiles.AllRulings().Single(f => f.Rulings.Any(r => r.Reference == "ACT 7:32" && r.Existing == "jacob"));
        var english = Db.Place([new RuledWord("KJV", "ACT 7:32", 21, "Jacob")]).Single().Value;
        var fatherOfJoseph = Words[new RuledWord("NESTLE1904", "MAT 1:15", 15, "Ἰακώβ")];
        var greekId = Words[file.Rulings.Single(r => r.Reference == "ACT 7:32").Word];
        var greek = await Db.Words.SingleAsync(w => w.Id == greekId);
        var link = new Link { FromTextId = english.TextId, ToTextId = greek.TextId,
            Relation = LinkRelation.Renders, Method = LinkMethod.StrongNumber, Confidence = 1,
            Provenance = new() { Source = "a fixture's stated number match" } };
        Db.LinkWords.Add(new LinkWord { Link = link, Word = english, Side = LinkSide.From });
        Db.LinkWords.Add(new LinkWord { Link = link, Word = greek, Side = LinkSide.To });
        await Db.SaveChangesAsync();

        await Load();

        file.Rulings.Should().HaveCount(7);
        foreach (var ruling in file.Rulings)
        {
            (await Db.WordEntities.Where(a => a.WordId == Words[ruling.Word]).Select(a => a.Entity!.Slug).ToListAsync())
                .Should().Equal([ruling.Reference == "ROM 11:26" ? "israelites" : "jacob"], "{0}", ruling.Word);
        }

        (await Db.WordEntities.Where(a => a.WordId == english.Id).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jacob");
        (await Db.WordEntities.Where(a => a.WordId == fatherOfJoseph).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jacob-2");
    }
}
