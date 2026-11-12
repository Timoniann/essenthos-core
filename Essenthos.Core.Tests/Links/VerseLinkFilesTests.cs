using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The verse links of a pair drawn from its frames and final (<see cref="VerseLinks"/>), and the merge's rule on machine links
/// across verses no verse link joins (<see cref="CrossingRule"/>), on a pair KJV-BHSA held in memory. The KJV's 1:2 covers the
/// address 1:3 as well as its own; the BHSA prints 1:1 to 1:4 at their own addresses.
/// </summary>
public sealed class VerseLinkFilesTests
{
    private const string Machine = "SIL.Machine ibm4, symmetrised";

    private static readonly PairFrames Frames = new(
        Text("KJV", ("1.1:1", [(1, 1, 1)]), ("1.1:2", [(1, 1, 2), (1, 1, 3)]), ("1.1:4", [(1, 1, 4)])),
        Text("BHSA", ("1.1:1", [(1, 1, 1)]), ("1.1:2", [(1, 1, 2)]), ("1.1:3", [(1, 1, 3)]), ("1.1:4", [(1, 1, 4)])),
        ReverseLinked: false);

    private static TextFrame Text(string slug, params (string Own, (int, int, int)[] Addresses)[] verses) =>
        new(slug, [.. verses.Select(v => VerseKey.Parse(v.Own))],
            verses.ToDictionary(v => VerseKey.Parse(v.Own), v => (IReadOnlyList<(int Book, int Chapter, int Verse)>)v.Addresses));

    private static FinalRow Settled(string method, string from, string to, string relation = "renders", string source = "a source", long id = 1) =>
        new(id, from, to, relation, null, true, method, null, source);

    [Fact]
    public void Verses_at_one_address_are_one_link_and_a_covered_address_joins_what_stands_there()
    {
        var rows = VerseLinks.Derive(Frames, [Settled(MappingMethods.Aligner, "1.1:1.1", "1.1:1.1")]);

        rows.Select(r => (r.From, r.To)).Should().Equal(("1.1:1", "1.1:1"), ("1.1:2", "1.1:2 1.1:3"), ("1.1:4", "1.1:4"));
        rows.Should().OnlyContain(r => r.Relation == "equals" && r.Method == "stated-by-source" && r.Confidence == null);
    }

    [Fact]
    public void A_pair_neither_linked_nor_declared_has_no_verse_links()
    {
        VerseLinks.Derive(Frames, []).Should().BeEmpty();
    }

    [Fact]
    public void A_stated_source_crossing_verses_nothing_joins_makes_a_verse_link_and_a_machine_does_not()
    {
        var rows = VerseLinks.Derive(Frames, [
            Settled(MappingMethods.Stated, "1.1:1.2", "1.1:2.1", source: "Clear Bible", id: 1),
            Settled(MappingMethods.Aligner, "1.1:4.1", "1.1:1.1", source: Machine, id: 2),
        ]);

        rows.Should().ContainSingle(r => r.Relation == "renders").Which.Should().Be(new VerseLinkRow(
            "1.1:1", "1.1:2", "renders", "stated-by-source", null, "Clear Bible", "the verse this source's own word links put against that one"));
        rows.Should().NotContain(r => r.From == "1.1:4" && r.To == "1.1:1");
    }

    [Fact]
    public void A_link_made_by_hand_crossing_verses_nothing_joins_makes_a_verse_link_and_a_stated_one_wins_the_same_pair()
    {
        var rows = VerseLinks.Derive(Frames, [
            Settled(MappingMethods.Ruling, "1.1:1.2", "1.1:2.1", source: "a ruling of this project: Genesis 1", id: 1),
            Settled(MappingMethods.Manual, "1.1:1.3", "1.1:4.3", source: "a reviewer", id: 2),
            Settled(MappingMethods.Manual, "1.1:4.1", "1.1:1.1", source: "a reviewer", id: 3),
            Settled(MappingMethods.Stated, "1.1:4.2", "1.1:1.2", source: "Clear Bible", id: 4),
            Settled(MappingMethods.Ruling, "1.1:2.5", "1.1:3.5", source: "a ruling of this project: joined already", id: 5),
        ]);

        const string byHand = "the verse a word link made by hand puts against that one";
        rows.Where(r => r.Relation == "renders").Should().Equal(
            new VerseLinkRow("1.1:1", "1.1:2", "renders", "manual", null, "a ruling of this project: Genesis 1", byHand),
            new VerseLinkRow("1.1:1", "1.1:4", "renders", "manual", null, "a reviewer", byHand),
            new VerseLinkRow("1.1:4", "1.1:1", "renders", "stated-by-source", null, "Clear Bible",
                "the verse this source's own word links put against that one"));
    }

    [Fact]
    public void The_merge_keeps_an_accepted_link_across_verses_and_its_verse_link_joins_a_machine_link_there()
    {
        MappingRow Row(string method, string from, string to) => new(from, to, "renders", method, 0.9, Machine, null, null, null);

        var input = new MappingMerge.Input(
            "KJV-BHSA", "KJV", "BHSA",
            new[] { Row(MappingMethods.Aligner, "1.1:4.1", "1.1:1.1"), Row(MappingMethods.Aligner, "1.1:4.3", "1.1:1.3") }
                .GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            [new RulingRow(RulingRow.Accept, "1.1:4.2", "1.1:1.2", "renders", "", "the names stand a verse apart")],
            new Dictionary<(string, VerseKey), string>(), new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(),
            1, Verses: Frames);
        var output = MappingMerge.Merge(input);

        output.Final.Select(r => (r.From, r.To, r.Method)).Should().BeEquivalentTo([
            ("1.1:4.1", "1.1:1.1", MappingMethods.Aligner), ("1.1:4.2", "1.1:1.2", MappingMethods.Ruling), ("1.1:4.3", "1.1:1.3", MappingMethods.Aligner),
        ]);
        VerseLinks.Derive(Frames, output.Final).Should().ContainSingle(r => r.Relation == "renders").Which.Should().Be(new VerseLinkRow(
            "1.1:4", "1.1:1", "renders", "manual", null, "a ruling of this project: the names stand a verse apart",
            "the verse a word link made by hand puts against that one"));
    }

    [Fact]
    public void The_merge_withdraws_a_machine_link_across_verses_no_verse_link_joins_and_keeps_every_other()
    {
        MappingRow Row(string method, string from, string to, double? confidence = 0.9, string source = Machine) =>
            new(from, to, "renders", method, confidence, source, null, null, null);

        var output = MappingMerge.Merge(new MappingMerge.Input(
            "KJV-BHSA", "KJV", "BHSA",
            new[]
            {
                Row(MappingMethods.Aligner, "1.1:1.1", "1.1:1.1"),
                Row(MappingMethods.Aligner, "1.1:1.2", "1.1:4.1"),
                Row(MappingMethods.Aligner, "1.1:2.1", "1.1:3.1"),
                Row(MappingMethods.Strong, "1.1:1.3", "1.1:4.2"),
                Row(MappingMethods.Stated, "1.1:4.1", "1.1:2.2", null, "Clear Bible"),
            }.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            [], new Dictionary<(string, VerseKey), string>(), new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(),
            1, Verses: Frames));

        output.Final.Select(r => (r.From, r.To)).Should().BeEquivalentTo(
            [("1.1:1.1", "1.1:1.1"), ("1.1:2.1", "1.1:3.1"), ("1.1:1.3", "1.1:4.2"), ("1.1:4.1", "1.1:2.2")]);
        output.Methods[MappingMethods.Aligner].Select(r => r.Status).Should().Equal(null, "withdrawn-by unjoined-verses", null);
        output.Report.Should().Contain(r => r.Kind == "withdrawn" && r.Method == MappingMethods.Aligner && r.By == CrossingRule.Name && r.Rows == 1);
        output.Report.Should().Contain(r => r.Kind == "needs" && r.Method == CrossingRule.Name && r.By == "align" && r.Rows == 1);
    }
}
