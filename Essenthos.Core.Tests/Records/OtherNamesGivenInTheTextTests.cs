using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The other name a verse gives a person: Pashhur's Magor-missabib (JER 20:3), Naomi's Mara (RUT 1:20).
/// The name is said in the line and its words carry the person; the same words where the text means
/// something else carry nobody.
/// </summary>
public sealed class OtherNamesGivenInTheTextTests
{
    private static readonly IReadOnlyList<OwnRecordRuling> Rulings =
        [.. SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)];

    [Fact]
    public void MagorMissabibIsRuledAtJeremiah20_3AndAtNoOtherVerse()
    {
        var name = Rulings.Where(r => r.StrongNumber is "H4032" or "H4036").ToList();

        name.Should().NotBeEmpty().And.OnlyContain(r => r.Existing == "pashhur-2" && r.Reference == "JER 20:3");
        name.Select(r => r.StrongNumber).Should().BeEquivalentTo("H4032", "H4036");
        Rulings.Where(r => r.Existing == "pashhur-2" && r.Reference == "JER 20:3")
            .Select(r => (r.Text, r.Position)).Should()
            .BeEquivalentTo([("BHSA", 24), ("BHSA", 26), ("SWETE", 22)]);
    }
}
