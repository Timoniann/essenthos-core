using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the family tree reads off the shipped files, re-read against the verses: a list of brothers
/// is not a chain of fathers, a name the verse does not print is not a son, a queen mother is a
/// grandmother, a king's other name is said under his own, and nobody in the tree is shown in English
/// to a Russian or Ukrainian reader for want of a name form.
/// </summary>
public sealed partial class GenealogyReadingTests
{
    private static readonly Lazy<IReadOnlyList<DescriptorRecord>> Records = new(() =>
        DescriptorFiles.Read(TestResources.Shipped(Path.Combine("Essenthos", "descriptors"))).Records);

    private static IReadOnlyList<DescriptorClaimRecord> Claims(string entity) =>
        Records.Value.Single(record => record.Entity == entity).Claims ?? [];

    /// <summary>
    /// Exodus 6:24 names Assir, Elkanah and Abiasaph as Korah's three sons, and 1 Chronicles 6:22-23
    /// lists them and then Ebiasaph's son Assir: Elkanah is not Assir's son, nor Abiasaph Elkanah's.
    /// </summary>
    [Fact]
    public void KorahsThreeSonsAreBrothersAndNotAChain()
    {
        Claims("elkanah").Should().NotContain(c => c.Relation == "son-of" && c.Target == "assir");
        Claims("abiasaph").Should().NotContain(c => c.Relation == "son-of" && c.Target == "elkanah");
        Claims("korah-3").Where(c => c.Relation == "father-of").Select(c => c.Target)
            .Should().BeEquivalentTo(["assir", "elkanah", "abiasaph"]);
        Claims("assir-2").Should().Contain(c => c.Relation == "son-of" && c.Target == "abiasaph");
    }

    /// <summary>1 Chronicles 7:12's "Shuppim ... the children of Ir" names neither Muppim nor Iri.</summary>
    [Fact]
    public void IriIsNotMuppimsFather()
    {
        Claims("iri").Should().NotContain(c => c.Target == "muppim");
        Claims("muppim").Should().NotContain(c => c.Target == "iri");
    }

    /// <summary>Maachah daughter of Abishalom is Abijam's mother and Asa's grandmother (1KI 15:2, 15:8, 15:10).</summary>
    [Fact]
    public void MaachahIsAsasGrandmother()
    {
        Claims("maacah-3").Should().ContainSingle(c => c.Target == "asa")
            .Which.Should().Match<DescriptorClaimRecord>(c => c.Relation == "grandmother-of" && c.DecidedBy != null);
        Claims("asa").Should().ContainSingle(c => c.Target == "maacah-3")
            .Which.Should().Match<DescriptorClaimRecord>(c => c.Relation == "grandson-of" && c.DecidedBy != null);
        Claims("maacah-3").Should().Contain(c => c.Relation == "mother-of" && c.Target == "abijam");
    }

    /// <summary>Joseph's Jacob and Heli, and Zerubbabel's Shealtiel, are fathers the text gives, each decided.</summary>
    [Fact]
    public void TheTwoFathersTheTextGivesAreBothDecided()
    {
        Claims("jacob-2").Single(c => c.Target == "joseph-6").DecidedBy.Should().NotBeNull();
        Claims("eli-2").Single(c => c.Target == "joseph-6").DecidedBy.Should().NotBeNull();
        Claims("zerubbabel-2").Where(c => c.Relation == "son-of").Should().OnlyContain(c => c.DecidedBy != null);
    }

    /// <summary>
    /// Achsah's Caleb and Chelubai, Ner's Abiel and Jeiel, Meshullam's Hodaviah and Joed: two fathers the
    /// verses give and nothing settles, each statement decided with the reason both stand.
    /// </summary>
    [Theory]
    [InlineData("achsah", "caleb", "chelubai")]
    [InlineData("ner", "abiel", "jeiel-2")]
    [InlineData("meshuallam", "hodaviah-4", "joed")]
    public void TwoFathersNothingSettlesStandWithTheirReason(string child, string one, string other)
    {
        var fathers = Claims(child).Where(c => c.Relation is "son-of" or "daughter-of").ToList();

        fathers.Select(c => c.Target).Should().BeEquivalentTo([one, other]);
        fathers.Should().OnlyContain(c => c.DecidedBy != null && c.Reason != null);
        fathers.Single(c => c.Target == one).Reason.Should().Contain("Both statements stand");
    }

    /// <summary>
    /// 2 Chronicles 11:18 gives Rehoboam one wife, Mahalath, whose mother is Abihail daughter of Eliab:
    /// the Hebrew has no "and" before Abihail, and "she bare him" in 11:19 is one woman. Abihail is not
    /// a second wife, or the tree marries her to her daughter's husband.
    /// </summary>
    [Fact]
    public void AbihailIsMahalathsMotherAndNotRehoboamsWife()
    {
        Claims("abihail-4").Should().NotContain(c => c.Target == "rehoboam");
        Claims("rehoboam").Should().NotContain(c => c.Target == "abihail-4");
        Claims("mahalath-2").Should().ContainSingle(c => c.Target == "abihail-4")
            .Which.Should().Match<DescriptorClaimRecord>(c => c.Relation == "daughter-of" && c.DecidedBy != null);
        Claims("mahalath-2").Should().Contain(c => c.Relation == "wife-of" && c.Target == "rehoboam");
    }

    /// <summary>King Jehoahaz is the Shallum of 1 Chronicles 3:15 and Jeremiah 22:11, and his line says so.</summary>
    [Fact]
    public void JehoahazsLineSaysHeIsAlsoCalledShallum()
    {
        var line = DistinguisherLoader.ReadOwnLines().Records.Single(r => r.Slug == "jehoahaz-2");

        line.English.Should().Contain("Shallum").And.Contain("JER 22:11");
        line.Ukr.Should().Contain("Шаллум");
        line.Deu.Should().Contain("Sallum");
        line.Spa.Should().Contain("Sallum");
    }

    /// <summary>
    /// Every person the batch of 2026-10-12 names has a nominative and a genitive in each language it
    /// gives, in its own alphabet, so the tree has a Russian and a Ukrainian name to show.
    /// </summary>
    [Fact]
    public void TheNamesGivenForTheTreeAreWholeAndInTheirOwnAlphabet()
    {
        var records = NameFormFiles.Read(TestResources.Shipped(Path.Combine("Essenthos", "name-forms"))).Records
            .Where(r => r.AskedAt == "2026-10-12")
            .ToList();

        records.Select(r => r.Entity).Should().Contain(["adah-wife-of-esau", "oholibamah-wife-of-esau", "admin"])
            .And.OnlyHaveUniqueItems();
        foreach (var record in records)
        {
            record.Names.Should().NotBeNullOrEmpty(record.Entity);
            foreach (var (language, forms) in record.Names!)
            {
                language.Should().BeOneOf("ukr", "rus");
                forms.Keys.Should().BeEquivalentTo(["nominative", "genitive"], record.Entity);
                forms.Values.Should().OnlyContain(form => Cyrillic().IsMatch(form), record.Entity);
            }
        }
    }

    [GeneratedRegex(@"^[\p{IsCyrillic}' \-]+$")]
    private static partial Regex Cyrillic();
}
