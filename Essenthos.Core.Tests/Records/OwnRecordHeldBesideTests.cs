using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// A record that held two men keeps the one its line and picture were made for, and the other is
/// written as a record of his own from the word of his verse, taking it from the record that held
/// him: Pashhur the priest of 1 Chronicles 9:12 leaves Zedekiah's envoy, the Merarite Amaziah
/// leaves the priest of Bethel, and Levi the publican is a man of his own beside Matthew. Each case
/// is a word of its own, so all of them are read as the record that held them in one world and the
/// rulings loaded once.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordHeldBesideTests(WitnessDatabase database) : OwnRecordTests, IAsyncLifetime
{
    public static TheoryData<string, string, string> Cases => new()
    {
        { "pashhur-the-priest", "pashhur", "1CH 9:12" },
        { "malchijah-father-of-pashhur", "malchijah-2", "1CH 9:12" },
        { "amaziah-the-merarite", "amaziah-3", "1CH 6:30" },
        { "elhanan-son-of-dodo", "elhanan", "2SA 23:24" },
        { "conaniah-chief-of-the-levites", "conaniah", "2CH 35:9" },
        { "benaiah-the-trumpeting-priest", "benaiah-4", "1CH 15:24" },
    };

    public async Task InitializeAsync() =>
        World = await database.Shared(() => OwnRecordWorld.Loaded(database, ReadAsTheRecordThatHeldThem));

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AManARecordHeldBesideAnotherIsWrittenFromHisOwnVerse(string written, string held, string reference)
    {
        var ruling = Rulings.Single(r => r.Create?.Slug == written);
        ruling.Reference.Should().Be(reference);
        ruling.Corrects.Should().Be(held);

        var record = await Db.Entities.SingleAsync(e => e.Slug == written);
        record.Source.Should().StartWith("Essenthos");
        record.Distinguisher.Should().NotBeNullOrEmpty();
        (await Db.WordEntities.Where(a => a.WordId == Words[ruling.Word]).Select(a => a.EntityId).ToListAsync())
            .Should().Equal(record.Id);
    }

    private static async Task ReadAsTheRecordThatHeldThem(OwnRecordWorld world)
    {
        var cases = Cases.Select(row => (Ruling: world.Rulings.Single(r => r.Create?.Slug == (string)row[0]), Held: (string)row[1]))
            .ToList();
        cases.Select(c => c.Ruling.Word).Should().OnlyHaveUniqueItems();

        foreach (var (ruling, held) in cases)
        {
            var record = world.Db.Entities.Local.FirstOrDefault(e => e.Slug == held)
                         ?? await world.Db.Entities.SingleOrDefaultAsync(e => e.Slug == held)
                         ?? world.Db.Entities.Add(new Entity
                         {
                             Kind = EntityKind.Person, Slug = held, Name = held, SourceId = held, Source = "a test",
                         }).Entity;
            await world.Db.SaveChangesAsync();
            world.Db.WordEntities.Add(new WordEntity
            {
                WordId = world.Words[ruling.Word], EntityId = record.Id, Method = LinkMethod.ModelReading,
                Confidence = 0.8, Source = "a reading of the verse",
            });
        }

        await world.Db.SaveChangesAsync();
    }
}
