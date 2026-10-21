using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A second man under a name the corpus held one record for is a record of his own, and the word of
/// his verse leaves the record that held him.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordSecondBearerTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// Jeshua the son of Kadmiel of NEH 12:24 is a man of his own: the Levite who sealed the covenant is
    /// the son of Azaniah (NEH 10:9), and a record holding both made one man the son of two fathers.
    /// </summary>
    [Fact]
    public async Task JeshuaTheSonOfKadmielIsAManOfHisOwn()
    {
        var ruling = SenseReadingFiles.AllRulings().SelectMany(f => f.Rulings)
            .Single(r => r.Create?.Slug == "jeshua-son-of-kadmiel");
        var held = await Db.Entities.SingleOrDefaultAsync(e => e.Slug == "jeshua-7");
        if (held is null)
        {
            held = new Entity { Kind = EntityKind.Person, Slug = "jeshua-7", Name = "Jeshua", SourceId = "jeshua-7", Source = "a test" };
            Db.Entities.Add(held);
            await Db.SaveChangesAsync();
        }

        Db.WordEntities.Add(new WordEntity
        {
            WordId = Words[ruling.Word], EntityId = held.Id, Method = LinkMethod.StrongNumber,
            Confidence = 0.9, Source = "a resolution by number",
        });
        await Db.SaveChangesAsync();

        await Load();

        (await Db.WordEntities.Where(a => a.WordId == Words[ruling.Word]).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal("jeshua-son-of-kadmiel");
        var record = await Db.Entities.SingleAsync(e => e.Slug == "jeshua-son-of-kadmiel");
        record.Notes.Should().Contain("the text does not say");
        record.Distinguisher.Should().Contain("son of Kadmiel");
    }

    /// <summary>
    /// The second men Strong numbers under a name the corpus held one record for: Jabin of Judges 4, the
    /// chiefs who sealed Nehemiah's covenant, Ittai son of Ribai and the rest are records of their own,
    /// and every word of the file names its own man and not the record it was read as before.
    /// </summary>
    [Fact]
    public async Task SecondBearersAreMenOfTheirOwnAndTheirWordsLeaveTheRecordTheyWereReadAs()
    {
        var file = SenseReadingFiles.AllRulings().Single(f => f.Rulings.Any(r => r.Create?.Slug == "jabin-king-of-canaan"));
        foreach (var ruling in file.Rulings.Where(r => r.Corrects is not null))
        {
            var old = Db.Entities.Local.FirstOrDefault(e => e.Slug == ruling.Corrects)
                      ?? Db.Entities.SingleOrDefault(e => e.Slug == ruling.Corrects);
            if (old is null)
            {
                old = new Entity { Kind = EntityKind.Person, Slug = ruling.Corrects!, Name = ruling.Corrects!, SourceId = ruling.Corrects!, Source = "a test" };
                Db.Entities.Add(old);
                await Db.SaveChangesAsync();
            }

            Db.WordEntities.Add(new WordEntity
            {
                WordId = Words[ruling.Word], EntityId = old.Id, Method = LinkMethod.StrongNumber,
                Confidence = 0.9, Source = "a resolution by number",
            });
        }

        await Db.SaveChangesAsync();
        await Load();

        file.Rulings.Should().HaveCount(22);
        foreach (var ruling in file.Rulings)
        {
            var named = ruling.Create?.Slug ?? ruling.Existing;
            (await Db.WordEntities.Where(a => a.WordId == Words[ruling.Word]).Select(a => a.Entity!.Slug).ToListAsync())
                .Should().Equal([named], "{0} names {1}", ruling.Word, named);
        }

        foreach (var created in file.Rulings.Where(r => r.Create is not null))
        {
            var record = await Db.Entities.SingleAsync(e => e.Slug == created.Create!.Slug);
            record.Kind.Should().Be(EntityKind.Person);
            record.Notes.Should().MatchRegex("the text does not say|^Another man|^Not the king");
        }
    }

    /// <summary>
    /// A record that held two men keeps the one its line and picture were made for, and the other is
    /// written as a record of his own from the word of his verse, taking it from the record that held
    /// him: Pashhur the priest of 1 Chronicles 9:12 leaves Zedekiah's envoy, the Merarite Amaziah
    /// leaves the priest of Bethel, and Levi the publican is a man of his own beside Matthew.
    /// </summary>
    [Theory]
    [InlineData("pashhur-the-priest", "pashhur", "1CH 9:12")]
    [InlineData("malchijah-father-of-pashhur", "malchijah-2", "1CH 9:12")]
    [InlineData("amaziah-the-merarite", "amaziah-3", "1CH 6:30")]
    [InlineData("elhanan-son-of-dodo", "elhanan", "2SA 23:24")]
    [InlineData("conaniah-chief-of-the-levites", "conaniah", "2CH 35:9")]
    [InlineData("benaiah-the-trumpeting-priest", "benaiah-4", "1CH 15:24")]
    public async Task AManARecordHeldBesideAnotherIsWrittenFromHisOwnVerse(string written, string held, string reference)
    {
        var ruling = Rulings.Single(r => r.Create?.Slug == written);
        ruling.Reference.Should().Be(reference);
        ruling.Corrects.Should().Be(held);
        var heldRecord = Db.Entities.SingleOrDefault(e => e.Slug == held)
                         ?? Db.Entities.Add(new Entity
                         {
                             Kind = EntityKind.Person, Slug = held, Name = held, SourceId = held, Source = "a test",
                         }).Entity;
        await Db.SaveChangesAsync();
        var heldId = heldRecord.Id;
        Db.WordEntities.Add(new WordEntity
        {
            WordId = Words[ruling.Word], EntityId = heldId, Method = LinkMethod.ModelReading, Confidence = 0.8,
            Source = "a reading of the verse",
        });
        await Db.SaveChangesAsync();

        await Load();

        var record = await Db.Entities.SingleAsync(e => e.Slug == written);
        record.Source.Should().StartWith("Essenthos");
        record.Distinguisher.Should().NotBeNullOrEmpty();
        (await Db.WordEntities.Where(a => a.WordId == Words[ruling.Word]).Select(a => a.EntityId).ToListAsync())
            .Should().Equal(record.Id);
    }
}
