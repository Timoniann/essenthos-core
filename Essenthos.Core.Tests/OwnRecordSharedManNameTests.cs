using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Words a name two men share had put on the wrong man: Hezekiah's Joah on David's, the Shuppim of 1
/// Chronicles 7 on Huppim, Kulish's firstborn of Josiah on the king.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordSharedManNameTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// The words a name two records share had put on the wrong one: Hezekiah's Joah on David's, the
    /// Shuppim of 1 Chronicles 7 on Huppim, Kulish's firstborn of Josiah on the king, and the Vale of
    /// Siddim on the sea. Each is the record its verse names afterwards and nothing else; the Gomer of
    /// each verse and the words <em>salt sea</em>, which named nobody, are named.
    /// </summary>
    [Theory]
    [InlineData("BHSA", "2CH 29:12", 31, "joah-2", "joah-the-later-gershonite")]
    [InlineData("BHSA", "2CH 29:12", 37, "joah-2", "joah-the-later-gershonite")]
    [InlineData("BHSA", "1CH 7:12", 2, "huppim", "shuppim-the-benjamite")]
    [InlineData("BHSA", "1CH 7:15", 9, "huppim", "shuppim-the-benjamite")]
    [InlineData("GRCBRENT", "1CH 7:12", 2, "huppim", "shuppim-the-benjamite")]
    [InlineData("SWETE", "1CH 7:15", 8, "huppim", "shuppim-the-benjamite")]
    [InlineData("UKR1871", "1CH 3:15", 4, "jehoahaz-2", "johanan-firstborn-of-josiah")]
    public async Task AWordReadAgainNamesTheRecordItsVerseNames(
        string text, string reference, int position, string? wrong, string named) =>
        await ReadAgain(text, reference, position, wrong, named);
}
