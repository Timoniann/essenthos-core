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
}
