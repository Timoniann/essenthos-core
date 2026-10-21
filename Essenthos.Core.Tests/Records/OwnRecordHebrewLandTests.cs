using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>Hebrew names read against their verse that name a land or a place.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordHebrewLandTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// Hebrew names no number, lexeme or peoples rule could settle, each read against its verse: a
    /// name that acts as a nation is the people, the land of Ham is the land, the Negeb of Caleb is the
    /// people where no land is held, a namesake is the one the verse identifies, the giant's sons are
    /// the giant's, Ezra's Aramaic Israel is the Israelites, a word whose number is its neighbour's is
    /// still the name it spells, and Judges 10:12's Amalek is no longer the Zidonians.
    /// </summary>
    [Theory]
    [InlineData("PSA 105:23", 10, null, "ham-3")]
    [InlineData("2KI 17:6", 29, null, "media")]
    [InlineData("GEN 24:10", 23, null, "mesopotamia")]
    [InlineData("2SA 8:13", 10, null, "syria")]
    [InlineData("JER 40:6", 9, null, "mizpah-3")]
    [InlineData("PSA 95:8", 5, null, "meribah-2")]
    [InlineData("ISA 2:16", 5, null, "tarshish-5")]
    public async Task AHebrewNameReadAgainstItsVerseNamesThePeopleLandOrManItSays(
        string reference, int position, string? wrong, string named) =>
        await ReadAgain("BHSA", reference, position, wrong, named);
}
