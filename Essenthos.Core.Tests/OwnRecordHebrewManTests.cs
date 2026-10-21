using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Hebrew names read against their verse that name a man, or God.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordHebrewManTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// Hebrew names no number, lexeme or peoples rule could settle, each read against its verse: a
    /// name that acts as a nation is the people, the land of Ham is the land, the Negeb of Caleb is the
    /// people where no land is held, a namesake is the one the verse identifies, the giant's sons are
    /// the giant's, Ezra's Aramaic Israel is the Israelites, a word whose number is its neighbour's is
    /// still the name it spells, and Judges 10:12's Amalek is no longer the Zidonians.
    /// </summary>
    [Theory]
    [InlineData("JER 1:1", 4, null, "hilkiah-7")]
    [InlineData("NEH 13:13", 9, null, "zadok-10")]
    [InlineData("1SA 31:2", 16, null, "ishvi-2")]
    [InlineData("JER 35:4", 27, null, "shallum-7")]
    [InlineData("1CH 6:21", 6, null, "azariah-son-of-zephaniah")]
    [InlineData("2SA 21:22", 7, null, "raphah")]
    [InlineData("2CH 28:3", 20, null, "yhvh")]
    public async Task AHebrewNameReadAgainstItsVerseNamesThePeopleLandOrManItSays(
        string reference, int position, string? wrong, string named) =>
        await ReadAgain("BHSA", reference, position, wrong, named);
}
