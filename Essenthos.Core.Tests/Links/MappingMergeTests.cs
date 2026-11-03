using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>The merge rule of DOC-0224 §6.4, one case each, on a pair KJV-BHSA held in memory.</summary>
public sealed class MappingMergeTests
{
    private const string Berean = "Berean Standard Bible translation tables";
    private const string Machine = "SIL.Machine, aligned as written and through BSB";

    private static MappingRow Row(string method, string from, string to, string relation = "renders", double? confidence = null,
        string source = "source", long? id = null, string? evidence = null) =>
        new(id, from, to, relation, method, confidence, source, evidence, null, null);

    private static MappingMerge.Output Merge(
        IEnumerable<MappingRow> rows,
        IReadOnlyList<RulingRow>? rulings = null,
        Dictionary<(string, VerseKey), string>? hashes = null,
        Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>? manifests = null,
        Dictionary<string, long>? previous = null) =>
        MappingMerge.Merge(new MappingMerge.Input(
            "KJV-BHSA", "KJV", "BHSA",
            rows.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            rulings ?? [],
            hashes ?? [],
            manifests ?? new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(),
            previous ?? [],
            PairOrdinals.Base + PairOrdinals.Range));

    private static string? Status(MappingMerge.Output output, string method, int index = 0) => output.Methods[method][index].Status;

    [Fact]
    public void Methods_on_the_same_words_are_one_link_whose_top_claim_is_settled()
    {
        var output = Merge([
            Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.1", confidence: 0.98, source: Machine, id: 7),
            Row(MappingMethods.Strong, "1.1:1.1", "1.1:1.1", confidence: 0.7, id: 7),
        ]);

        output.Final.Should().HaveCount(2).And.OnlyContain(r => r.Id == 7);
        output.Final.Single(r => r.Settled).Method.Should().Be(MappingMethods.Composed, "a composition outranks a Strong pairing under 0.9");
        output.Final[0].Settled.Should().BeTrue("a link begins with its settled claim");
    }

    [Fact]
    public void A_stated_link_withdraws_a_composition_competing_for_its_word()
    {
        var output = Merge([
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: Berean, id: 1),
            Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.2", confidence: 0.9, source: Machine, id: 2),
        ]);

        output.Final.Should().ContainSingle().Which.Id.Should().Be(1);
        Status(output, MappingMethods.Composed).Should().Be("withdrawn-by stated");
        output.Report.Should().Contain(r => r.Kind == "withdrawn" && r.Method == MappingMethods.Composed && r.By == MappingMethods.Stated && r.Rows == 1);
    }

    [Fact]
    public void Links_of_equal_rank_on_one_word_stand_side_by_side()
    {
        var output = Merge([
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: Berean, id: 1),
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.3", source: Berean, id: 2),
        ]);

        output.Final.Select(r => r.Id).Should().Equal(1, 2);
        output.Methods[MappingMethods.Stated].Should().OnlyContain(r => r.Status == null);
    }

    [Fact]
    public void A_stated_absence_wins_its_word_over_any_correspondence()
    {
        var output = Merge([
            Row(MappingMethods.Strong, "1.1:1.4", "1.1:1.2", confidence: 0.95, id: 1),
            Row(MappingMethods.Stated, "1.1:1.4", string.Empty, relation: "expands", source: "Zefania KJV+ italics", id: 2),
            Row(MappingMethods.Stated, string.Empty, "1.1:1.5", relation: "omits", source: Berean, id: 3),
            Row(MappingMethods.Aligner, "1.1:1.6", "1.1:1.5", confidence: 0.6, source: "SIL.Machine ibm4", id: 4),
        ]);

        output.Final.Select(r => r.Id).Should().BeEquivalentTo([2L, 3L]);
        Status(output, MappingMethods.Strong).Should().Be("withdrawn-by stated");
        Status(output, MappingMethods.Aligner).Should().Be("withdrawn-by stated");
    }

    [Fact]
    public void An_approved_verdict_outranks_a_strong_pairing_and_the_aligner_outranks_form_matching()
    {
        var output = Merge([
            Row(MappingMethods.Strong, "1.1:1.1", "1.1:1.1", confidence: 0.9, id: 1),
            Row(MappingMethods.Evidentia, "1.1:1.1", "1.1:1.1", confidence: 0.55, source: "EVIDENTIA run 8", id: 1, evidence: "decision 9, safe tier: rule"),
            Row(MappingMethods.Lexical, "1.1:1.2", "1.1:1.2", confidence: 0.95, id: 2),
            Row(MappingMethods.Aligner, "1.1:1.2", "1.1:1.2", confidence: 0.3, source: "SIL.Machine ibm4", id: 2),
        ]);

        output.Final.Where(r => r.Settled).Select(r => r.Method).Should().Equal(MappingMethods.Evidentia, MappingMethods.Aligner);
    }

    [Fact]
    public void A_refusal_by_source_suppresses_only_that_source_on_the_shape()
    {
        var output = Merge(
            [
                Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.1", confidence: 0.98, source: Machine, id: 1),
                Row(MappingMethods.Strong, "1.1:1.1", "1.1:1.1", confidence: 0.5, id: 1),
            ],
            [new RulingRow(RulingRow.Refuse, "1.1:1.1", "1.1:1.1", "renders", "SIL.Machine", "a reading")]);

        output.Final.Should().ContainSingle().Which.Method.Should().Be(MappingMethods.Strong);
        Status(output, MappingMethods.Composed).Should().Be("refused-by ruling");
    }

    [Fact]
    public void A_ruling_on_one_word_takes_it_out_of_that_source_rows()
    {
        var output = Merge(
            [Row(MappingMethods.Stated, "1.1:1.1 1.1:1.2", "1.1:1.1", source: Berean, id: 1)],
            [new RulingRow(RulingRow.Refuse, "1.1:1.2", string.Empty, string.Empty, Berean, "the second word renders the next one")]);

        output.Final.Should().ContainSingle().Which.From.Should().Be("1.1:1.1");
        output.Final[0].Id.Should().Be(1);
        Status(output, MappingMethods.Stated).Should().Be("narrowed-by ruling");
    }

    [Fact]
    public void An_accepted_shape_wins_and_is_written_where_no_method_proposes_it()
    {
        var output = Merge(
            [
                Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: Berean, id: 1),
                Row(MappingMethods.Aligner, "1.1:1.1", "1.1:1.2", confidence: 0.4, source: "SIL.Machine ibm4", id: 2),
            ],
            [
                new RulingRow(RulingRow.Accept, "1.1:1.1", "1.1:1.2", "renders", string.Empty, "read in the verse"),
                new RulingRow(RulingRow.Accept, "1.1:1.9", "1.1:1.9", "renders", string.Empty, "read in the verse"),
            ]);

        output.Final.Where(r => r.Settled).Select(r => (r.From, r.To, r.Method)).Should().Equal(
            ("1.1:1.1", "1.1:1.2", MappingMethods.Aligner),
            ("1.1:1.9", "1.1:1.9", MappingMethods.Ruling));
        Status(output, MappingMethods.Stated).Should().Be("withdrawn-by aligner");
        output.Final.Last().Id.Should().Be(PairOrdinals.Base + PairOrdinals.Range, "a link no export numbered takes the first id of its pair's range");
    }

    [Fact]
    public void A_changed_verse_drops_the_model_rows_citing_it_and_lists_the_pair()
    {
        var verse = VerseKey.Parse("1.1:1");
        var output = Merge(
            [
                Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.1", confidence: 0.98, source: Machine, id: 1),
                Row(MappingMethods.Stated, "1.1:1.2", "1.1:1.2", source: Berean, id: 2),
                Row(MappingMethods.Composed, "1.1:2.1", "1.1:2.1", confidence: 0.98, source: Machine, id: 3),
            ],
            hashes: new() { [("KJV", verse)] = "aaaa", [("KJV", VerseKey.Parse("1.1:2"))] = "bbbb" },
            manifests: new()
            {
                ["KJV"] = new Dictionary<VerseKey, string> { [verse] = "cccc", [VerseKey.Parse("1.1:2")] = "bbbb" },
                ["BHSA"] = null,
            });

        output.Final.Should().ContainSingle().Which.Id.Should().Be(3);
        output.Methods[MappingMethods.Composed].Select(r => r.Status).Should().Equal(MappingStatus.Stale, null);
        Status(output, MappingMethods.Stated).Should().Be(MappingStatus.Stale);
        output.Report.Should().Contain(r => r.Kind == "needs" && r.By == "compose");
        output.Report.Should().Contain(r => r.Kind == "unchecked" && r.Note.StartsWith("BHSA"));
    }

    [Fact]
    public void A_link_keeps_the_id_an_earlier_merge_gave_it_and_the_output_is_deterministic()
    {
        MappingRow[] rows =
        [
            Row(MappingMethods.Composed, "1.1:1.2", "1.1:1.2", confidence: 0.9, source: Machine),
            Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.1", confidence: 0.9, source: Machine),
        ];
        var first = Merge(rows);
        var previous = first.Final.ToDictionary(r => r.From + "|" + r.To, r => r.Id);
        var second = Merge(rows.Reverse(), previous: previous);

        first.Final.Select(r => r.From).Should().Equal("1.1:1.1", "1.1:1.2");
        second.Final.Should().Equal(first.Final);
    }
}
