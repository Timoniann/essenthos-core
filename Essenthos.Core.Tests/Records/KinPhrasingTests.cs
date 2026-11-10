using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>What the verses say of a person's children beyond naming them, said in each reader's language.</summary>
public sealed class KinPhrasingTests
{
    [Theory]
    [InlineData("eng", "seven named sons and a daughter")]
    [InlineData("ukr", "семи названих синів та дочки")]
    [InlineData("deu", "von sieben genannten Söhnen und einer Tochter")]
    [InlineData("spa", "siete hijos nombrados y una hija")]
    public void ACountOfTheNamedSaysSo(string language, string said) =>
        DescriptorPhrasings.Counted(language, [(Kin.Son, 7), (Kin.Daughter, 1)], named: true).Should().Be(said);

    [Theory]
    [InlineData("eng", 7, false, "seventh son of ")]
    [InlineData("eng", 1, false, "firstborn son of ")]
    [InlineData("ukr", 7, false, "сьомий син ")]
    [InlineData("ukr", 1, false, "первісток ")]
    [InlineData("ukr", 1, true, "первістка ")]
    [InlineData("ukr", 2, true, "друга дочка ")]
    [InlineData("deu", 3, false, "dritter Sohn ")]
    [InlineData("spa", 3, false, "tercer hijo de ")]
    [InlineData("spa", 1, true, "primogénita de ")]
    public void APlaceAmongTheChildrenIsSaidInEachLanguage(string language, int ordinal, bool daughter, string before)
    {
        var plain = DescriptorPhrasings.For(language)![daughter ? DescriptorRelations.DaughterOf : DescriptorRelations.SonOf];

        DescriptorPhrasings.Ordinal(language, plain, daughter, ordinal)!.Before.Should().Be(before);
        DescriptorPhrasings.Ordinal(language, plain, daughter, 13).Should().BeNull();
    }

    [Theory]
    [InlineData(1, false, ", з них названий ")]
    [InlineData(1, true, ", з них названа ")]
    [InlineData(3, false, ", з них названі ")]
    public void TheNamedAmongACountAgreeWithTheirNumber(int named, bool daughters, string said) =>
        DescriptorPhrasings.AmongThem("ukr", named, daughters).Should().Be(said);

    [Theory]
    [InlineData("eng", " and other sons and daughters", ", and of other sons")]
    [InlineData("ukr", " та інших синів і дочок", ", а також інших синів")]
    [InlineData("deu", " und weiterer Söhne und Töchter", " sowie weiteren Söhnen")]
    [InlineData("spa", " y otros hijos e hijas", ", y de otros hijos")]
    public void MoreChildrenAreSaidAfterNamesAndAfterACount(string language, string afterNames, string afterCount)
    {
        DescriptorPhrasings.Others(language, sons: true, daughters: true, afterCount: false).Should().Be(afterNames);
        DescriptorPhrasings.Others(language, sons: true, daughters: false, afterCount: true).Should().Be(afterCount);
    }
}
