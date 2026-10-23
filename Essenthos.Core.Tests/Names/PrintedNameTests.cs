using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// A name in a language comes from where the record is mostly named, not from the few verses a
/// generation pass happened to read.
/// </summary>
public sealed class PrintedNameTests
{
    private const int Ohienko = 1;
    private const int OpenTranslation = 2;
    private const int Kulish = 3;
    private const int Reina = 4;
    private const int Luther = 5;
    private const int Elberfelder = 6;

    private static readonly IReadOnlyDictionary<int, string> Languages = new Dictionary<int, string>
    {
        [Ohienko] = "ukr", [OpenTranslation] = "ukr", [Kulish] = "ukr", [Reina] = "spa", [Luther] = "deu", [Elberfelder] = "deu",
    };

    private static Rendering Printed(int text, string form, int occurrences, int entity = 1) =>
        new(entity, text, form, form.ToLowerInvariant(), occurrences, false);

    private static Dictionary<(int, string), IReadOnlyDictionary<string, string>> Held(
        string language, string nominative, string? genitive = null, string? locative = null, int entity = 1)
    {
        var cases = new Dictionary<string, string> { [GrammaticalCases.Nominative] = nominative };
        if (genitive is not null)
        {
            cases[GrammaticalCases.Genitive] = genitive;
        }

        if (locative is not null)
        {
            cases[GrammaticalCases.Locative] = locative;
        }

        return new() { [(entity, language)] = cases };
    }

    private static List<PrintedName> Of(
        Dictionary<(int, string), IReadOnlyDictionary<string, string>> held,
        params Rendering[] renderings) =>
        [.. PrintedNames.Of(renderings, Languages, held, new HashSet<int> { 1 })];

    /// <summary>
    /// The pass read Galilee off Joshua's verses, where the Ohienko Bible writes <em>Ґаліл</em>; its
    /// Gospel and Acts verses say <em>Галілеї</em>, and the nominative is the one the New Ukrainian
    /// Translation prints once.
    /// </summary>
    [Fact]
    public void GalileeIsNamedAsTheGospelsNameIt()
    {
        var renamed = Of(Held("ukr", "Ґаліл", "Ґаліла", "Ґалілі"),
            Printed(Ohienko, "Ґаліл", 2), Printed(Ohienko, "Ґалілі", 2), Printed(Ohienko, "Ґаліла", 1),
            Printed(Ohienko, "Галілеї", 32), Printed(Ohienko, "Галілею", 11), Printed(Ohienko, "Галілейській", 3),
            Printed(OpenTranslation, "Галілеї", 43), Printed(OpenTranslation, "Галілею", 4),
            Printed(OpenTranslation, "Галілея", 1));

        renamed.Should().ContainSingle();
        renamed[0].Cases.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            [GrammaticalCases.Nominative] = "Галілея",
            [GrammaticalCases.Genitive] = "Галілеї",
            [GrammaticalCases.Locative] = "Галілеї",
        });
        renamed[0].Replaced[GrammaticalCases.Nominative].Should().Be("Ґаліл");
    }

    /// <summary>One text naming the record as the pass did, in a third of its words, keeps the name.</summary>
    [Fact]
    public void ANameOneTextPrintsWhereTheRecordIsNamedStands()
    {
        Of(Held("ukr", "Корнилій", "Корнилія"),
                Printed(Ohienko, "Корнилій", 5), Printed(Ohienko, "Корнилія", 3), Printed(Ohienko, "Корнелій", 1),
                Printed(Kulish, "Корнелий", 5), Printed(Kulish, "Корнелия", 3),
                Printed(OpenTranslation, "Корнелій", 7), Printed(OpenTranslation, "Корнелія", 3))
            .Should().BeEmpty();
    }

    /// <summary>
    /// A name no text prints at all came from the pass's own idea of it, not from a few verses, and
    /// this rule does not judge it.
    /// </summary>
    [Fact]
    public void ANameNoTextPrintsIsNotThisRulesToMove()
    {
        Of(Held("deu", "Josef", "Josefs"),
                Printed(Luther, "Joseph", 120), Printed(Luther, "Josephs", 40))
            .Should().BeEmpty();
    }

    /// <summary>
    /// A record with two names is not respelled by this rule, and a longer name the record is also
    /// printed under is another name: Kirjath is not Kirjath-Jearim.
    /// </summary>
    [Fact]
    public void AnotherNameIsNotAnotherSpelling()
    {
        PrintedNames.Of(
                [Printed(Ohienko, "Аврам", 5), Printed(Ohienko, "Авраам", 200), Printed(Ohienko, "Авраама", 120)],
                Languages, Held("ukr", "Аврам", "Аврама"), new HashSet<int>())
            .Should().BeEmpty("Abram and Abraham are two names, and the heading moves by the verses calling him each");

        Of(Held("deu", "Kirjath", "Kirjaths"),
                Printed(Luther, "Kirjath", 1), Printed(Luther, "Kirjath-Jearim", 19), Printed(Elberfelder, "Kirjath-Jearim", 18))
            .Should().BeEmpty();
    }

    /// <summary>
    /// In a declining language a case the texts do not print leaves the name as it was: a heading in
    /// the new spelling beside a genitive in the old would be worse than either.
    /// </summary>
    [Fact]
    public void ACaseTheTextsDoNotPrintKeepsTheName()
    {
        Of(Held("ukr", "Ґаліл", "Ґаліла", "Ґалілі"),
                Printed(Ohienko, "Ґаліл", 2), Printed(Ohienko, "Галілею", 40), Printed(Ohienko, "Галілея", 2))
            .Should().BeEmpty();
    }

    /// <summary>
    /// The Reina-Valera calls Satan <em>Satán</em> in Job and Zechariah and <em>Satanás</em> through the
    /// New Testament, where he is named most. Luther writes <em>Pinehas</em> where the pass wrote
    /// <em>Pinhas</em>, and the genitive no text prints goes, so a German line says <em>von Pinehas</em>.
    /// </summary>
    [Fact]
    public void AnUndecliningLanguageTakesThePrintedSpelling()
    {
        var satan = Of(Held("spa", "Satán"), Printed(Reina, "Satán", 17), Printed(Reina, "Satanás", 38), Printed(Reina, "Beelzebub", 7));
        satan.Single().Cases[GrammaticalCases.Nominative].Should().Be("Satanás");

        var phinehas = Of(Held("deu", "Pinhas", "Pinhas'"),
            Printed(Luther, "Pinehas", 17), Printed(Elberfelder, "Pinehas", 33), Printed(Elberfelder, "Pinhas", 1));
        phinehas.Single().Cases.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            [GrammaticalCases.Nominative] = "Pinehas",
            [GrammaticalCases.Genitive] = null,
        });
    }

    [Theory]
    [InlineData("Ґаліл", "Галілея", true)]
    [InlineData("Pinhas", "Pinehas", true)]
    [InlineData("Satán", "Satanás", true)]
    [InlineData("Medien", "Meder", false)]
    [InlineData("Kirjath", "Kirjath-Jearim", false)]
    [InlineData("Мисаїл", "Мисах", false)]
    public void OneNameIsOneOpeningOrOneLetterApart(string held, string printed, bool same) =>
        PrintedNames.SameName(printed, held).Should().Be(same);
}
