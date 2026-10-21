using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// Words a ruling reads again, each named afterwards by the record its verse names and by nothing
/// else. Where a case names a wrong record, the word is put on it before the rulings are loaded. Every
/// case is a word of its own, so the class puts all of them on their wrong records in one world and
/// loads the rulings once, and each case reads that load.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordReadAgainTests(WitnessDatabase database) : OwnRecordTests, IAsyncLifetime
{
    /// <summary>
    /// Hebrew names no number, lexeme or peoples rule could settle, each read against its verse: a
    /// name that acts as a nation is the people, the land of Ham is the land, the Negeb of Caleb is the
    /// people where no land is held, a namesake is the one the verse identifies, the giant's sons are
    /// the giant's, Ezra's Aramaic Israel is the Israelites, a word whose number is its neighbour's is
    /// still the name it spells, and Judges 10:12's Amalek is no longer the Zidonians.
    /// </summary>
    public static TheoryData<string, string, int, string?, string> HebrewNames => new()
    {
        // A land or a place.
        { "BHSA", "PSA 105:23", 10, null, "ham-3" },
        { "BHSA", "2KI 17:6", 29, null, "media" },
        { "BHSA", "GEN 24:10", 23, null, "mesopotamia" },
        { "BHSA", "2SA 8:13", 10, null, "syria" },
        { "BHSA", "JER 40:6", 9, null, "mizpah-3" },
        { "BHSA", "PSA 95:8", 5, null, "meribah-2" },
        { "BHSA", "ISA 2:16", 5, null, "tarshish-5" },

        // A man, or God.
        { "BHSA", "JER 1:1", 4, null, "hilkiah-7" },
        { "BHSA", "NEH 13:13", 9, null, "zadok-10" },
        { "BHSA", "1SA 31:2", 16, null, "ishvi-2" },
        { "BHSA", "JER 35:4", 27, null, "shallum-7" },
        { "BHSA", "1CH 6:21", 6, null, "azariah-son-of-zephaniah" },
        { "BHSA", "2SA 21:22", 7, null, "raphah" },
        { "BHSA", "2CH 28:3", 20, null, "yhvh" },

        // A people.
        { "BHSA", "EXO 17:8", 3, null, "amalekites" },
        { "BHSA", "JDG 10:12", 4, "sidonians", "amalekites" },
        { "BHSA", "ISA 21:17", 7, null, "kedar-2" },
        { "BHSA", "EZK 38:5", 4, null, "put-2" },
        { "BHSA", "1CH 4:40", 18, null, "ham" },
        { "BHSA", "1SA 30:14", 14, null, "calebites" },
        { "BHSA", "ISA 13:17", 5, null, "medes" },
        { "BHSA", "EZR 6:16", 4, null, "israelites" },
    };

    /// <summary>
    /// The words a name two records share had put on the wrong one: Hezekiah's Joah on David's, the
    /// Shuppim of 1 Chronicles 7 on Huppim, Kulish's firstborn of Josiah on the king, and the Vale of
    /// Siddim on the sea. Each is the record its verse names afterwards and nothing else; the Gomer of
    /// each verse and the words <em>salt sea</em>, which named nobody, are named.
    /// </summary>
    public static TheoryData<string, string, int, string?, string> SharedNames => new()
    {
        { "BHSA", "2CH 29:12", 31, "joah-2", "joah-the-later-gershonite" },
        { "BHSA", "2CH 29:12", 37, "joah-2", "joah-the-later-gershonite" },
        { "BHSA", "1CH 7:12", 2, "huppim", "shuppim-the-benjamite" },
        { "BHSA", "1CH 7:15", 9, "huppim", "shuppim-the-benjamite" },
        { "GRCBRENT", "1CH 7:12", 2, "huppim", "shuppim-the-benjamite" },
        { "SWETE", "1CH 7:15", 8, "huppim", "shuppim-the-benjamite" },
        { "UKR1871", "1CH 3:15", 4, "jehoahaz-2", "johanan-firstborn-of-josiah" },
        { "BHSA", "GEN 14:3", 7, "saltsea", "vale-of-siddim" },
        { "BHSA", "GEN 14:10", 4, "saltsea", "vale-of-siddim" },
        { "BHSA", "GEN 14:3", 9, null, "saltsea" },
        { "BHSA", "GEN 14:3", 11, null, "saltsea" },
        { "BHSA", "JOS 15:5", 4, null, "saltsea" },
        { "BHSA", "GEN 10:2", 3, null, "gomer" },
        { "BHSA", "1CH 1:5", 3, null, "gomer" },
        { "BHSA", "EZK 38:6", 1, null, "gomer-3" },
    };

    public async Task InitializeAsync() =>
        World = await database.Shared(() => OwnRecordWorld.Loaded(database, HoldWrongly));

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [MemberData(nameof(HebrewNames))]
    public async Task AHebrewNameReadAgainstItsVerseNamesThePeopleLandOrManItSays(
        string text, string reference, int position, string? wrong, string named) =>
        await ReadAgain(text, reference, position, wrong, named);

    [Theory]
    [MemberData(nameof(SharedNames))]
    public async Task AWordReadAgainNamesTheRecordItsVerseNames(
        string text, string reference, int position, string? wrong, string named) =>
        await ReadAgain(text, reference, position, wrong, named);

    private async Task ReadAgain(string text, string reference, int position, string? wrong, string named)
    {
        var word = Word(World, text, reference, position);
        (await Db.WordEntities.Where(a => a.WordId == word).Select(a => a.Entity!.Slug).ToListAsync())
            .Should().Equal([named], wrong is null ? "its verse names it" : $"its verse names it, not {wrong}");
    }

    private static long Word(OwnRecordWorld world, string text, string reference, int position) =>
        world.Words[world.Rulings.Single(r => r.Text == text && r.Reference == reference && r.Position == position).Word];

    /// <summary>Every case's word on the wrong record it names, before the load.</summary>
    private static async Task HoldWrongly(OwnRecordWorld world)
    {
        var cases = HebrewNames.Concat(SharedNames)
            .Select(row => (Word: Word(world, (string)row[0], (string)row[1], (int)row[2]), Wrong: (string?)row[3]))
            .ToList();
        cases.Select(c => c.Word).Should().OnlyHaveUniqueItems("a word held wrongly for one case would be held for another");

        foreach (var (word, wrong) in cases.Where(c => c.Wrong is not null))
        {
            var held = await world.Db.Entities.SingleOrDefaultAsync(e => e.Slug == wrong);
            if (held is null)
            {
                held = new Entity { Kind = EntityKind.Person, Slug = wrong!, Name = wrong!, SourceId = wrong!, Source = "a test" };
                world.Db.Entities.Add(held);
                await world.Db.SaveChangesAsync();
            }

            world.Db.WordEntities.Add(new WordEntity
            {
                WordId = word, EntityId = held.Id, Method = LinkMethod.StrongNumber, Confidence = 0.99,
                Source = "a resolution by number",
            });
        }

        await world.Db.SaveChangesAsync();
    }
}
