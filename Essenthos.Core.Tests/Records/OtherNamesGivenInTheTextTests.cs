using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The other name a verse gives a person: Pashhur's Magor-missabib (JER 20:3), Naomi's Mara (RUT 1:20).
/// The name is said in the line and its words carry the person; the same words where the text means
/// something else carry nobody.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class OtherNamesGivenInTheTextTests(WitnessDatabase database) : OwnRecordTests(database)
{
    /// <summary>Where מָגוֹר is the common 'terror on every side', not Pashhur's name.</summary>
    private static readonly string[] TerrorOnEverySide = ["JER 6:25", "JER 20:10", "JER 46:5", "JER 49:29"];

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

    [Fact]
    public async Task TheNameIsPashhursAndTheTerrorOnEverySideIsNobodys()
    {
        var bare = TerrorOnEverySide
            .Select(reference => new RuledWord("BHSA", reference, 15, "מָגוֹר"))
            .ToList();
        var placed = Db.Place(bare);
        foreach (var word in placed.Values)
        {
            word.StrongNumber = "H4032";
        }

        await Db.SaveChangesAsync();

        await Load();

        var ruled = Rulings.Where(r => r.Existing == "pashhur-2" && r.Reference == "JER 20:3")
            .Select(r => Words[r.Word]).ToList();
        ruled.Should().HaveCount(3);
        foreach (var word in ruled)
        {
            (await Db.WordEntities.Where(a => a.WordId == word).Select(a => a.Entity!.Slug).ToListAsync())
                .Should().Equal("pashhur-2");
        }

        (await Db.WordEntities.CountAsync(a => placed.Values.Select(w => w.Id).Contains(a.WordId)))
            .Should().Be(0, "'terror on every side' names nobody");
    }

    [Theory]
    [InlineData("pashhur-2", "JER 20:3", "Magor-missabib", "Маґор-Міссавів")]
    [InlineData("naomi", "RUT 1:20", "Mara", "Мара")]
    public void TheOtherNameTheVerseGivesIsSaidInTheLine(string entity, string reference, string english, string ukrainian)
    {
        var byname = OwnNameLoader.Read().Bynames!.Single(b => b.Entity == entity);

        byname.Reference.Should().Be(reference);
        byname.Names["eng"].Should().Equal(english);
        byname.Names["ukr"].Should().Equal(ukrainian);
    }
}
