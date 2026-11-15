using Essenthos.Core.Descriptions;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The families a tree is drawn from (TSK-1666): how ties make families, the document's format, and the rule that a stored
/// card or document is made again only when its composer's version moves.
/// </summary>
public sealed class FamilyDocumentsTests
{
    private static FamilyGraph.Relationship Rel(string from, string relation, string to, (int, int, int)? verse = null) =>
        new(from, relation, to, verse);

    [Fact]
    public void Ties_of_the_four_ranks_make_families_numbered_by_their_first_person()
    {
        var families = FamilyGraph.Of(
            [
                Rel("isaac", "son-of", "abraham", (1, 21, 3)),
                Rel("abraham", "husband-of", "sarah"),
                Rel("abraham", "king-of", "ur"),
                Rel("jesus", "son-of", "yhvh"),
                Rel("moses", "brother-of", "moses"),
                Rel("aaron", "brother-of", "miriam"),
                Rel("isaac", "son-of", "abraham", (1, 22, 2)),
            ],
            ["abraham", "isaac", "sarah", "jesus", "moses", "aaron", "miriam"]);

        families.Ties.Select(t => $"{t.From} {t.Relation} {t.To} {t.Component}").Should().Equal(
            "aaron brother-of miriam 1", "abraham husband-of sarah 2", "isaac son-of abraham 2");
        families.Ties.Single(t => t.From == "isaac").Verse.Should().Be((1, 21, 3), "the first verse stating a tie is its verse");
        families.Members.Select(m => $"{m.Slug} {m.Component}").Should().Equal(
            "aaron 1", "abraham 2", "isaac 2", "jesus 3", "miriam 1", "moses 4", "sarah 2");
    }

    private static RecordCard Card(string slug, string? local = null, string? source = null, string? described = null) =>
        new(0, slug, "person", slug.ToUpperInvariant(), null, source, "male", null, local, described);

    [Fact]
    public void The_line_is_ours_then_the_sources_on_an_English_page_then_what_the_corpus_says()
    {
        Card("a", local: "ours", source: "theirs", described: "said").Line("ukr").Should().Be("ours");
        Card("a", source: "theirs", described: "said").Line("eng").Should().Be("theirs");
        Card("a", source: "theirs", described: "said").Quoted("eng").Should().BeTrue();
        Card("a", source: "theirs", described: "said").Line("ukr").Should().Be("said");
        Card("a", source: "theirs", described: "said").Quoted("ukr").Should().BeFalse();
        Card("a").Line("eng").Should().BeNull();
    }

    /// <summary>
    /// The fixture's document and the composers' versions, pinned together. When this fails because the output changed, raise
    /// <see cref="FamilyDocuments.Version"/> (or <see cref="RecordCards.Version"/>) and pin the new hash with it: a stored
    /// document is made again only when the version moves.
    /// </summary>
    [Fact]
    public void A_change_to_the_document_raises_the_version()
    {
        var (body, hash) = FamilyDocuments.Compose(
            7,
            [
                new FamilyDocuments.Person("isaac", "male", [], 2),
                new FamilyDocuments.Person("abraham", "male", ["king-of"], 3),
                new FamilyDocuments.Person("sarah", "female", [], 1),
            ],
            [
                new FamilyDocuments.Tie("isaac", "son-of", "abraham", 1, 21, 3),
                new FamilyDocuments.Tie("abraham", "husband-of", "sarah", null, null, null),
            ],
            new Dictionary<string, RecordCard>
            {
                ["abraham"] = Card("abraham", source: "father of Isaac (GEN 21:3)"),
                ["isaac"] = Card("isaac", described: "son of Abraham") with { AlsoCalled = "Yitzhak" },
            },
            "eng");

        body.Should().Be(
            """{"format":1,"family":7,"people":[{"slug":"abraham","name":"ABRAHAM","sex":"male","line":"father of Isaac (GEN 21:3)","quoted":true,"roles":["king-of"],"ties":3},{"slug":"isaac","name":"ISAAC","sex":"male","line":"son of Abraham","alsoCalled":"Yitzhak","ties":2},{"slug":"sarah","name":"sarah","sex":"female","ties":1}],"ties":[[0,2,"husband-of"],[1,0,"son-of",1,21,3]]}""");
        (FamilyDocuments.Version, RecordCards.Version, hash).Should().Be((1, 1, Pinned));
    }

    private const string Pinned = "1d9f8c9219a5e552";
}
