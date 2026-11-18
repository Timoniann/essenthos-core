using Essenthos.Core.ClearBible;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The shape of Clear Bible's Young's Literal Translation set, whose target file marks punctuation in an
/// <c>isPunc</c> column rather than <c>exclude</c>.
/// </summary>
public sealed class ClearBibleYoungTokenTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"essenthos-ylt-{Guid.NewGuid():N}");

    public ClearBibleYoungTokenTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ATokenTheFileMarksAsPunctuationIsLeftOut()
    {
        var path = Path.Combine(_folder, "target.tsv");
        File.WriteAllLines(path,
        [
            "id	altId	text	transType	isPunc	isPrimary",
            "01001001001	In-1	In	k	False	True",
            "01001001002	,-1	,		True	False",
        ]);

        ClearBibleAlignment.Tokens(path).Select(token => token.Excluded).Should().Equal(false, true);
    }
}

/// <summary>
/// The Old Testament set's records are numbered by the morpheme edition, though the set calls its source
/// <c>WLC</c>: read by the release's <c>WLC.tsv</c>, which writes גֵ֣יא~בֶנ־הִנֹּ֔ם as one word, every
/// identifier after such a word named a later one.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class ClearBibleYoungNumberingTests
{
    private static readonly string Folder = TestResources.ClearBibleFolder;

    private static readonly ClearBibleSet Set = ClearBibleSet.YoungOldTestament("YLT", "BHSA");

    [Fact]
    public void EverySourceWordTheRecordsNameIsARowOfTheSetsTokenFile()
    {
        var rows = ClearBibleAlignment.Tokens(Path.Combine(Folder, Set.Source)).Select(token => token.Id).ToHashSet();

        ClearBibleAlignment.Records(Path.Combine(Folder, Set.Alignment))
            .SelectMany(record => record.Source)
            .Where(id => !rows.Contains(id))
            .Should().BeEmpty();
    }

    /// <summary>
    /// Jeremiah 7:31: <em>the valley of the son of Hinnom</em> names three source words and <em>To burn</em>
    /// the two after them, which are the ל and the verb, not בְּנֵי and its suffix.
    /// </summary>
    [Fact]
    public void JeremiahSevenThirtyOnesToBurnIsTheInfinitive()
    {
        var tokens = ClearBibleAlignment.Tokens(Path.Combine(Folder, Set.Source)).ToDictionary(token => token.Id);
        var toBurn = ClearBibleAlignment.Records(Path.Combine(Folder, Set.Alignment))
            .Where(record => record.Target.Count == 1 && record.Target[0] is "24007031023" or "24007031024")
            .SelectMany(record => record.Source)
            .Select(id => ClearBibleLinks.Comparable(tokens[id].Text, "hbo"));

        toBurn.Should().Equal("ל", "שרף");
    }
}
