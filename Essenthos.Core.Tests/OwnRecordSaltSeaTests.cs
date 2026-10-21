using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Vale of Siddim the words had put on the sea, the words <em>salt sea</em> that named nobody, and
/// the Gomer of each verse.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OwnRecordSaltSeaTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>
    /// The words a name two records share had put on the wrong one: Hezekiah's Joah on David's, the
    /// Shuppim of 1 Chronicles 7 on Huppim, Kulish's firstborn of Josiah on the king, and the Vale of
    /// Siddim on the sea. Each is the record its verse names afterwards and nothing else; the Gomer of
    /// each verse and the words <em>salt sea</em>, which named nobody, are named.
    /// </summary>
    [Theory]
    [InlineData("BHSA", "GEN 14:3", 7, "saltsea", "vale-of-siddim")]
    [InlineData("BHSA", "GEN 14:10", 4, "saltsea", "vale-of-siddim")]
    [InlineData("BHSA", "GEN 14:3", 9, null, "saltsea")]
    [InlineData("BHSA", "GEN 14:3", 11, null, "saltsea")]
    [InlineData("BHSA", "JOS 15:5", 4, null, "saltsea")]
    [InlineData("BHSA", "GEN 10:2", 3, null, "gomer")]
    [InlineData("BHSA", "1CH 1:5", 3, null, "gomer")]
    [InlineData("BHSA", "EZK 38:6", 1, null, "gomer-3")]
    public async Task AWordReadAgainNamesTheRecordItsVerseNames(
        string text, string reference, int position, string? wrong, string named) =>
        await ReadAgain(text, reference, position, wrong, named);
}
