using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>Hebrew names read against their verse that name a people.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordHebrewPeopleTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// Hebrew names no number, lexeme or peoples rule could settle, each read against its verse: a
    /// name that acts as a nation is the people, the land of Ham is the land, the Negeb of Caleb is the
    /// people where no land is held, a namesake is the one the verse identifies, the giant's sons are
    /// the giant's, Ezra's Aramaic Israel is the Israelites, a word whose number is its neighbour's is
    /// still the name it spells, and Judges 10:12's Amalek is no longer the Zidonians.
    /// </summary>
    [Theory]
    [InlineData("EXO 17:8", 3, null, "amalekites")]
    [InlineData("JDG 10:12", 4, "sidonians", "amalekites")]
    [InlineData("ISA 21:17", 7, null, "kedar-2")]
    [InlineData("EZK 38:5", 4, null, "put-2")]
    [InlineData("1CH 4:40", 18, null, "ham")]
    [InlineData("1SA 30:14", 14, null, "calebites")]
    [InlineData("ISA 13:17", 5, null, "medes")]
    [InlineData("EZR 6:16", 4, null, "israelites")]
    public async Task AHebrewNameReadAgainstItsVerseNamesThePeopleLandOrManItSays(
        string reference, int position, string? wrong, string named) =>
        await ReadAgain("BHSA", reference, position, wrong, named);
}
