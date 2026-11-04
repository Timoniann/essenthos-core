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
    public void The_merge_withdraws_a_machine_link_across_verses_no_verse_link_joins_and_keeps_every_other()
    {
        MappingRow Row(string method, string from, string to, long id, double? confidence = 0.9, string source = Machine) =>
            new(id, from, to, "renders", method, confidence, source, null, null, null);

        var output = MappingMerge.Merge(new MappingMerge.Input(
            "KJV-BHSA", "KJV", "BHSA",
            new[]
            {
                Row(MappingMethods.Aligner, "1.1:1.1", "1.1:1.1", 1),
                Row(MappingMethods.Aligner, "1.1:1.2", "1.1:4.1", 2),
                Row(MappingMethods.Aligner, "1.1:2.1", "1.1:3.1", 3),
                Row(MappingMethods.Strong, "1.1:1.3", "1.1:4.2", 4),
                Row(MappingMethods.Stated, "1.1:4.1", "1.1:2.2", 5, null, "Clear Bible"),
            }.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            [], new Dictionary<(string, VerseKey), string>(), new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(),
            new Dictionary<string, long>(), PairOrdinals.Base + PairOrdinals.Range, Verses: Frames));

        output.Final.Select(r => r.Id).Should().BeEquivalentTo([1L, 3L, 4L, 5L]);
        output.Methods[MappingMethods.Aligner].Select(r => r.Status).Should().Equal(null, "withdrawn-by unjoined-verses", null);
        output.Report.Should().Contain(r => r.Kind == "withdrawn" && r.Method == MappingMethods.Aligner && r.By == CrossingRule.Name && r.Rows == 1);
    }
}
