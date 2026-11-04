using Essenthos.Core.Names;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The foreign-names rule's reading of the records (TSK-1448): a base word is a person's own name by its number or its
/// spelling, a translation's word is a spelling of them when it is alike one, and two records are kin when they share a
/// name, a number or a title.
/// </summary>
public sealed class ForeignNameRuleTests : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private ForeignNameRule Rule()
    {
        var records = Path.Combine(_resources, "Essenthos", "records");
        Directory.CreateDirectory(Path.Combine(records, "facts"));
        File.WriteAllText(Path.Combine(records, "records.tsv"),
            "slug\tkind\tname\tsex\tsex_source\nsatan\tperson\tSatan\tmale\t\\N\njesus\tperson\tJesus\tmale\t\\N\njohn\tperson\tJohn\tmale\t\\N\njohn-2\tperson\tJohn\tmale\t\\N\nanointed\ttitle\tAnointed\t\\N\t\\N\n");
        File.WriteAllText(Path.Combine(records, "names.tsv"),
            "slug\tn\tlabel\tkind\thebrew\thebrew_transliterated\tgreek\tgreek_transliterated\thebrew_strong\tgreek_strong\n" +
            "satan\t1\tSatan\tproper name\tשָׂטָן\tsatan\tΣατανᾶς\tSatanas\tH7854\tG4567\n" +
            "jesus\t1\tJesus\tproper name\t\\N\t\\N\tἸησοῦς\tIēsous\t\\N\tG2424\n" +
            "jesus\t2\tChrist\ttitle\t\\N\t\\N\tΧριστός\tChristos\t\\N\tG5547\n");
        File.WriteAllText(Path.Combine(records, "facts", "titles.tsv"), "title\tbearer\tref\tcalled\tby\nanointed\tjesus\t40.1:1\t\\N\ttest\n");
        return new ForeignNameRule(new ProductStore(_resources));
    }

    [Fact]
    public void ABaseWordIsThePersonsOwnNameByItsNumberOrItsSpelling()
    {
        var rule = Rule();

        rule.IsOwnName("satan", "Σατανᾶν", "G4567").Should().BeTrue();
        rule.IsOwnName("satan", "σατανᾶ", null).Should().BeTrue("its letters are alike Σατανᾶς");
        rule.IsOwnName("jesus", "Χριστοῦ", "G5547").Should().BeFalse("a title's number is not the person's own name");
    }

    [Fact]
    public void ATranslationsWordIsASpellingOfThePersonWhenAlikeOneOfTheirSpellingsOrTheBaseWord()
    {
        var rule = Rule();

        rule.IsSpelling("satan", "сатану", "Σατανᾶν").Should().BeFalse("the records hold no Cyrillic form of him here");
        rule.IsSpelling("satan", "Satans", "Σατανᾶν").Should().BeTrue();
        rule.IsSpelling("satan", "Христа", "Σατανᾶν").Should().BeFalse();
    }

    [Fact]
    public void NamesakesAndATitleWithItsBearerAreKin()
    {
        var rule = Rule();

        rule.Kin("john", "john-2").Should().BeTrue("two men called John share the name");
        rule.Kin("anointed", "jesus").Should().BeTrue("a title and its bearer");
        rule.Kin("satan", "jesus").Should().BeFalse();
    }

    [Fact]
    public void SpellingsAreFoldedAndComparedByTrigramsAsTheLoadDoes()
    {
        ForeignNameRule.Fold("Ἰησοῦς").Should().Be("ιησουσ");
        ForeignNameRule.Fold("João").Should().Be("joao");
        ForeignNameRule.Similarity("joao", "joao").Should().Be(1);
        ForeignNameRule.Similarity("jacob", "james").Should().BeLessThan(ForeignNameRule.Spelled);
    }
}
