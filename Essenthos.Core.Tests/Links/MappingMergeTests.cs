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

    private const long Ordinal = 1;

    /// <summary>The number each case gives the links it proposes, by their words, to say which of them stand.</summary>
    private readonly Dictionary<string, long> _links = new(StringComparer.Ordinal);

    private MappingRow Row(string method, string from, string to, string relation = "renders", double? confidence = null,
        string source = "source", long? link = null, string? evidence = null)
    {
        if (link is { } number)
        {
            _links[from + "|" + to] = number;
        }

        return new(from, to, relation, method, confidence, source, evidence, null, null);
    }

    private long Link(FinalRow row) => _links.GetValueOrDefault(row.From + "|" + row.To);

    private static MappingMerge.Output Merge(
        IEnumerable<MappingRow> rows,
        IReadOnlyList<RulingRow>? rulings = null,
        Dictionary<(string, VerseKey), string>? hashes = null,
        Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>? manifests = null,
        string pair = "KJV-BHSA",
        long ordinal = Ordinal) =>
        MappingMerge.Merge(new MappingMerge.Input(
            pair, pair[..pair.IndexOf('-')], pair[(pair.IndexOf('-') + 1)..],
            rows.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            rulings ?? [],
            hashes ?? [],
            manifests ?? new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(),
            ordinal));

    private static string? Status(MappingMerge.Output output, string method, int index = 0) => output.Methods[method][index].Status;

    [Fact]
    public void Methods_on_the_same_words_are_one_link_whose_top_claim_is_settled()
    {
        var output = Merge([
            Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.1", confidence: 0.98, source: Machine, link: 7),
            Row(MappingMethods.Strong, "1.1:1.1", "1.1:1.1", confidence: 0.7, link: 7),
        ]);

        output.Final.Should().HaveCount(2).And.OnlyContain(r => Link(r) == 7 && r.Id == output.Final[0].Id);
        output.Final.Single(r => r.Settled).Method.Should().Be(MappingMethods.Composed, "a composition outranks a Strong pairing under 0.9");
        output.Final[0].Settled.Should().BeTrue("a link begins with its settled claim");
    }

    [Fact]
    public void A_stated_link_withdraws_a_composition_competing_for_its_word()
    {
        var output = Merge([
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: Berean, link: 1),
            Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.2", confidence: 0.9, source: Machine, link: 2),
        ]);

        Link(output.Final.Should().ContainSingle().Which).Should().Be(1);
        Status(output, MappingMethods.Composed).Should().Be("withdrawn-by stated");
        output.Report.Should().Contain(r => r.Kind == "withdrawn" && r.Method == MappingMethods.Composed && r.By == MappingMethods.Stated && r.Rows == 1);
    }

    [Fact]
    public void Links_of_equal_rank_on_one_word_stand_side_by_side()
    {
        var output = Merge([
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: Berean, link: 1),
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.3", source: Berean, link: 2),
        ]);

        output.Final.Select(Link).Should().Equal(1, 2);
        output.Methods[MappingMethods.Stated].Should().OnlyContain(r => r.Status == null);
    }

    [Fact]
    public void A_stated_absence_wins_its_word_over_any_correspondence()
    {
        var output = Merge([
            Row(MappingMethods.Strong, "1.1:1.4", "1.1:1.2", confidence: 0.95, link: 1),
            Row(MappingMethods.Stated, "1.1:1.4", string.Empty, relation: "expands", source: "Zefania KJV+ italics", link: 2),
            Row(MappingMethods.Stated, string.Empty, "1.1:1.5", relation: "omits", source: Berean, link: 3),
            Row(MappingMethods.Aligner, "1.1:1.6", "1.1:1.5", confidence: 0.6, source: "SIL.Machine ibm4", link: 4),
        ]);

        output.Final.Select(Link).Should().BeEquivalentTo([2L, 3L]);
        Status(output, MappingMethods.Strong).Should().Be("withdrawn-by stated");
        Status(output, MappingMethods.Aligner).Should().Be("withdrawn-by stated");
    }

    [Fact]
    public void An_evidentia_absence_withdraws_only_the_aligner_and_gives_way_to_every_other_method()
    {
        const string Run = "EVIDENTIA run 14";
        const string Safe = "decision 5, safe tier: supplied-article-spa";
        var output = Merge([
            Row(MappingMethods.Evidentia, string.Empty, "1.1:1.1", relation: "omits", source: Run, link: 1, evidence: Safe),
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: "WLCM-RV09-manual, made by hand", link: 2),
            Row(MappingMethods.Evidentia, "1.1:1.2", string.Empty, relation: "expands", source: Run, link: 3, evidence: Safe),
            Row(MappingMethods.Strong, "1.1:1.2", "1.1:1.2", confidence: 0.5, link: 4),
            Row(MappingMethods.Evidentia, string.Empty, "1.1:1.3", relation: "omits", source: Run, link: 5, evidence: Safe),
            Row(MappingMethods.Composed, "1.1:1.3", "1.1:1.3", confidence: 0.9, source: Machine, link: 6),
            Row(MappingMethods.Evidentia, string.Empty, "1.1:1.4", relation: "omits", source: Run, link: 7, evidence: Safe),
            Row(MappingMethods.Aligner, "1.1:1.4", "1.1:1.4", confidence: 0.6, source: "SIL.Machine ibm4", link: 8),
            Row(MappingMethods.Evidentia, "1.1:1.5", string.Empty, relation: "expands", source: Run, link: 9, evidence: Safe),
            Row(MappingMethods.Aligner, "1.1:1.5", "1.1:1.5", confidence: 0.9, source: "SIL.Machine ibm4", link: 10),
            Row(MappingMethods.Lexical, "1.1:1.5", "1.1:1.5", confidence: 0.6, link: 10),
        ]);

        output.Final.Where(r => r.Settled).Select(Link).Should().BeEquivalentTo([2L, 4L, 6L, 7L, 10L]);
        Status(output, MappingMethods.Evidentia, 0).Should().Be("withdrawn-by stated", "a hand alignment does not lose to a rule's no counterpart");
        Status(output, MappingMethods.Evidentia, 1).Should().Be("withdrawn-by strong", "even a weak Strong pairing outlasts it");
        Status(output, MappingMethods.Evidentia, 2).Should().Be("withdrawn-by composed");
        Status(output, MappingMethods.Evidentia, 3).Should().BeNull();
        Status(output, MappingMethods.Aligner, 0).Should().Be("withdrawn-by evidentia", "the aligner's guess alone gives way to the verdict");
        Status(output, MappingMethods.Evidentia, 4).Should().Be("withdrawn-by aligner", "a link another method also claims is not the aligner's alone");
    }

    [Fact]
    public void An_approved_verdict_outranks_a_strong_pairing_under_nine_tenths_and_the_aligner_outranks_form_matching()
    {
        var output = Merge([
            Row(MappingMethods.Strong, "1.1:1.1", "1.1:1.1", confidence: 0.7, link: 1),
            Row(MappingMethods.Evidentia, "1.1:1.1", "1.1:1.1", confidence: 0.55, source: "EVIDENTIA run 8", link: 1, evidence: "decision 9, safe tier: rule"),
            Row(MappingMethods.Lexical, "1.1:1.2", "1.1:1.2", confidence: 0.95, link: 2),
            Row(MappingMethods.Aligner, "1.1:1.2", "1.1:1.2", confidence: 0.3, source: "SIL.Machine ibm4", link: 2),
        ]);

        output.Final.Where(r => r.Settled).Select(r => r.Method).Should().Equal(MappingMethods.Evidentia, MappingMethods.Aligner);
    }

    /// <summary>
    /// The owner's order (NOT-0218): a person, then a stated source, then a text's own Strong numbers at nine tenths or more,
    /// then EVIDENTIA; numbers laid from another edition keep their place below a composition.
    /// </summary>
    [Fact]
    public void A_text_s_own_strong_pairing_at_nine_tenths_outranks_an_approved_verdict_and_gives_way_to_a_stated_source()
    {
        var verdict = Row(MappingMethods.Evidentia, "1.1:1.1", "1.1:1.3", confidence: 0.63, source: "EVIDENTIA run 18", link: 2,
            evidence: "decision 9, attached tier: attached-word");
        var strong = Row(MappingMethods.Strong, "1.1:1.1", "1.1:1.2", confidence: 0.9, link: 1);

        Link(Merge([strong, verdict], pair: "LUTH1912-BHSA").Final.Should().ContainSingle().Which).Should().Be(1);
        Link(Merge([strong, verdict], pair: "LSG1910-BHSA").Final.Should().ContainSingle().Which).Should().Be(2);
        Link(Merge([strong with { Confidence = 0.7 }, verdict], pair: "LUTH1912-BHSA").Final.Should().ContainSingle().Which).Should().Be(2);
        Link(Merge([strong, Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.4", link: 3)], pair: "LUTH1912-BHSA")
            .Final.Should().ContainSingle().Which).Should().Be(3);

        var agreed = Merge([strong, verdict with { To = "1.1:1.2" }], pair: "LUTH1912-BHSA").Final;
        agreed.Should().HaveCount(2);
        agreed.Single(r => r.Settled).Method.Should().Be(MappingMethods.Strong);
        MappingMethods.Rank(MappingMethods.Strong, 0.9, null).Should()
            .BeGreaterThan(MappingMethods.Rank(MappingMethods.Evidentia, 0.9, "decision 1, safe tier: rule"))
            .And.BeLessThan(MappingMethods.Rank(MappingMethods.Stated, null, null));
    }

    [Fact]
    public void A_refusal_by_source_suppresses_only_that_source_on_the_shape()
    {
        var output = Merge(
            [
                Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.1", confidence: 0.98, source: Machine, link: 1),
                Row(MappingMethods.Strong, "1.1:1.1", "1.1:1.1", confidence: 0.5, link: 1),
            ],
            [new RulingRow(RulingRow.Refuse, "1.1:1.1", "1.1:1.1", "renders", "SIL.Machine", "a reading")]);

        output.Final.Should().ContainSingle().Which.Method.Should().Be(MappingMethods.Strong);
        Status(output, MappingMethods.Composed).Should().Be("refused-by ruling");
    }

    [Fact]
    public void A_ruling_on_one_word_takes_it_out_of_that_source_rows()
    {
        var output = Merge(
            [Row(MappingMethods.Stated, "1.1:1.1 1.1:1.2", "1.1:1.1", source: Berean, link: 1)],
            [new RulingRow(RulingRow.Refuse, "1.1:1.2", string.Empty, string.Empty, Berean, "the second word renders the next one")]);

        output.Final.Should().ContainSingle().Which.From.Should().Be("1.1:1.1");
        Status(output, MappingMethods.Stated).Should().Be("narrowed-by ruling");
    }

    [Fact]
    public void An_accepted_shape_wins_and_is_written_where_no_method_proposes_it()
    {
        var output = Merge(
            [
                Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: Berean, link: 1),
                Row(MappingMethods.Aligner, "1.1:1.1", "1.1:1.2", confidence: 0.4, source: "SIL.Machine ibm4", link: 2),
            ],
            [
                new RulingRow(RulingRow.Accept, "1.1:1.1", "1.1:1.2", "renders", string.Empty, "read in the verse"),
                new RulingRow(RulingRow.Accept, "1.1:1.9", "1.1:1.9", "renders", string.Empty, "read in the verse"),
            ]);

        output.Final.Where(r => r.Settled).Select(r => (r.From, r.To, r.Method)).Should().Equal(
            ("1.1:1.1", "1.1:1.2", MappingMethods.Aligner),
            ("1.1:1.9", "1.1:1.9", MappingMethods.Ruling));
        Status(output, MappingMethods.Stated).Should().Be("withdrawn-by aligner");
        output.Final.Last().Id.Should().Be(LinkIds.Of(Ordinal, "KJV-BHSA", "1.1:1.9", "1.1:1.9"), "a link's id comes from its words, whoever proposed it");
    }

    [Fact]
    public void A_changed_verse_drops_the_model_rows_citing_it_and_lists_the_pair()
    {
        var verse = VerseKey.Parse("1.1:1");
        var output = Merge(
            [
                Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.1", confidence: 0.98, source: Machine, link: 1),
                Row(MappingMethods.Stated, "1.1:1.2", "1.1:1.2", source: Berean, link: 2),
                Row(MappingMethods.Composed, "1.1:2.1", "1.1:2.1", confidence: 0.98, source: Machine, link: 3),
            ],
            hashes: new() { [("KJV", verse)] = "aaaa", [("KJV", VerseKey.Parse("1.1:2"))] = "bbbb" },
            manifests: new()
            {
                ["KJV"] = new Dictionary<VerseKey, string> { [verse] = "cccc", [VerseKey.Parse("1.1:2")] = "bbbb" },
                ["BHSA"] = null,
            });

        Link(output.Final.Should().ContainSingle().Which).Should().Be(3);
        output.Methods[MappingMethods.Composed].Select(r => r.Status).Should().Equal(MappingStatus.Stale, null);
        Status(output, MappingMethods.Stated).Should().Be(MappingStatus.Stale);
        output.Report.Should().Contain(r => r.Kind == "needs" && r.By == "compose");
        output.Report.Should().Contain(r => r.Kind == "unchecked" && r.Note.StartsWith("BHSA"));
    }

    [Fact]
    public void A_changed_verse_keeps_the_rows_a_producer_made_again_and_lists_every_other_method_that_lost_rows()
    {
        var verse = VerseKey.Parse("1.1:1");
        const string Clear = "Clear Bible Alignments";
        var rows = new[]
        {
            Row(MappingMethods.Stated, "1.1:1.2", "1.1:1.2", source: Berean),
            Row(MappingMethods.Stated, "1.1:1.3", "1.1:1.3", source: Clear),
            Row(MappingMethods.Lexical, "1.1:1.4", "1.1:1.4", confidence: 0.4, source: "letters"),
            Row(MappingMethods.Strong, "1.1:1.5", "1.1:1.5", confidence: 0.9, source: "numbers"),
        };
        var output = MappingMerge.Merge(new MappingMerge.Input(
            "KJV-BHSA", "KJV", "BHSA",
            rows.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            [],
            new Dictionary<(string, VerseKey), string> { [("KJV", verse)] = "aaaa" },
            new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>
            {
                ["KJV"] = new Dictionary<VerseKey, string> { [verse] = "cccc" },
                ["BHSA"] = null,
            },
            1,
            Remade: new Dictionary<string, IReadOnlySet<string>> { [MappingMethods.Stated] = new HashSet<string> { Clear } },
            Producers: new Dictionary<string, string> { [MappingMethods.Stated] = "forge stated-pairs", [MappingMethods.Strong] = "forge strong-pairs" }));

        output.Methods[MappingMethods.Stated].Select(r => r.Status).Should().Equal(null, MappingStatus.Stale);
        Status(output, MappingMethods.Lexical).Should().Be(MappingStatus.Stale);
        Status(output, MappingMethods.Strong).Should().Be(MappingStatus.Stale);
        output.Final.Should().ContainSingle().Which.Source.Should().Be(Berean);
        output.Report.Should().Contain(r => r.Kind == MappingMerge.NoProducer && r.Method == MappingMethods.Lexical && r.By == "letters" && r.Rows == 1);
        output.Report.Should().Contain(r => r.Kind == "needs" && r.Method == MappingMethods.Strong && r.By == "forge strong-pairs");
        output.Report.Should().Contain(r => r.Kind == MappingMerge.NoProducer && r.Method == MappingMethods.Stated && r.By == Clear && r.Rows == 1);
    }

    [Fact]
    public void A_verse_whose_accents_alone_changed_keeps_its_rows_and_one_whose_words_changed_does_not()
    {
        var (accented, joined) = (VerseKey.Parse("1.12:6"), VerseKey.Parse("1.38:24"));
        var rows = new[]
        {
            Row(MappingMethods.Composed, "1.12:6.10", "1.12:6.12", confidence: 0.98, source: Machine, link: 1),
            Row(MappingMethods.Composed, "1.38:24.11", "1.38:24.11", confidence: 0.98, source: Machine, link: 2),
        };
        var then = new Dictionary<VerseKey, string[]> { [accented] = ["до", "дубрави", "Море"], [joined] = ["λε", "γοντες", "Θαμάρ"] };
        var now = new Dictionary<VerseKey, string[]> { [accented] = ["до", "дубрави", "Море́"], [joined] = ["λέγοντες", "Θαμάρ"] };
        var output = MappingMerge.Merge(new MappingMerge.Input(
            "KJV-BHSA", "KJV", "BHSA",
            rows.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            [],
            new Dictionary<(string, VerseKey), string> { [("KJV", accented)] = "aaaa", [("KJV", joined)] = "bbbb" },
            new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>
            {
                ["KJV"] = new Dictionary<VerseKey, string> { [accented] = "cccc", [joined] = "dddd" },
                ["BHSA"] = null,
            },
            Ordinal,
            then.ToDictionary(v => ("KJV", v.Key), v => VerseFold.Of(v.Value, v.Key == joined ? "grc" : "ukr")),
            (text, verse) => VerseFold.Of(now[verse], verse == joined ? "grc" : "ukr")));

        Link(output.Final.Should().ContainSingle().Which).Should().Be(1);
        output.Methods[MappingMethods.Composed].Select(r => r.Status).Should().Equal(null, MappingStatus.Stale);
        output.Report.Should().Contain(r => r.Kind == "read-same" && r.Rows == 1);
    }

    [Fact]
    public void A_link_s_id_is_derived_from_its_pair_and_words_and_the_output_is_deterministic()
    {
        MappingRow[] rows =
        [
            Row(MappingMethods.Composed, "1.1:1.2", "1.1:1.2", confidence: 0.9, source: Machine),
            Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.1", confidence: 0.9, source: Machine),
        ];
        var first = Merge(rows);
        var second = Merge(rows.Reverse());
        var other = Merge(rows, pair: "ASV-BHSA", ordinal: 2);

        first.Final.Select(r => r.From).Should().Equal("1.1:1.1", "1.1:1.2");
        second.Final.Should().Equal(first.Final);
        first.Final.Select(r => r.Id).Should().Equal(
            LinkIds.Of(Ordinal, "KJV-BHSA", "1.1:1.1", "1.1:1.1"), LinkIds.Of(Ordinal, "KJV-BHSA", "1.1:1.2", "1.1:1.2"));
        first.Final.Should().OnlyContain(r => LinkIds.OrdinalOf(r.Id) == Ordinal && r.Id > 0);
        other.Final.Should().OnlyContain(r => LinkIds.OrdinalOf(r.Id) == 2, "each pair numbers its links under its own ordinal, so ids never meet across pairs");
    }

    [Fact]
    public void A_shape_whose_id_is_taken_is_hashed_again_until_one_is_free()
    {
        var ids = new LinkIds.Allocator(Ordinal, "KJV-BHSA");

        var first = ids.Next("1.1:1.1", "1.1:1.1");
        var again = ids.Next("1.1:1.1", "1.1:1.1");

        first.Should().Be(LinkIds.Of(Ordinal, "KJV-BHSA", "1.1:1.1", "1.1:1.1"));
        again.Should().Be(LinkIds.Of(Ordinal, "KJV-BHSA", "1.1:1.1", "1.1:1.1", attempt: 1)).And.NotBe(first);
        LinkIds.OrdinalOf(again).Should().Be(Ordinal);
        ids.Collisions.Should().Be(1);
        FluentActions.Invoking(() => LinkIds.Of(LinkIds.MaxOrdinal + 1, "KJV-BHSA", "1.1:1.1", "1.1:1.1"))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Between_stated_sources_one_aligned_by_hand_settles_before_one_carried_from_another_edition()
    {
        const string hand = "Clear Bible Alignments WLCM-AVD-manual, BiblioNexus, github.com/Clear-Bible/Alignments, CC BY 4.0, made by hand";
        const string carried = "BSOJ's alignment of the Van Dyck to unfoldingWord's Hebrew Bible and Greek New Testament, made in translationCore";
        const string transfer = "Clear Bible Alignments SBLGNT-JFA11-transfer, transferred from their Spanish Reina-Valera 1909 alignment, not made by hand";
        var output = Merge([
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: carried, link: 1),
            Row(MappingMethods.Stated, "1.1:1.1", "1.1:1.1", source: hand, link: 1),
            Row(MappingMethods.Stated, "1.1:1.2", "1.1:1.2", source: transfer, link: 2),
            Row(MappingMethods.Stated, "1.1:1.2", "1.1:1.2", source: Berean, link: 2),
        ]);

        output.Final.Where(r => r.Settled).Select(r => r.Source).Should().Equal(hand, Berean);
        MappingMethods.Making(transfer).Should().Be(MappingMethods.StatedMaking.Transferred, "'not made by hand' is not 'made by hand'");
    }

    [Fact]
    public void A_newer_evidentia_run_supersedes_an_older_one_in_the_books_and_tiers_it_covers()
    {
        MappingRow Verdict(int run, string from, string to, string tier, long id) =>
            Row(MappingMethods.Evidentia, from, to, confidence: 0.8, source: $"EVIDENTIA run {run} (1.0.0)", link: id, evidence: $"decision {id}, {tier} tier: rule");

        var output = Merge([
            Verdict(6, "1.1:1.1", "1.1:1.1", "safe", 1),
            Verdict(10, "1.1:1.1", "1.1:1.1", "safe", 1),
            Verdict(6, "1.1:1.2", "1.1:1.3", "safe", 2),
            Verdict(10, "1.1:1.3", "1.1:1.4", "safe", 3),
            Verdict(6, "2.1:1.1", "2.1:1.1", "safe", 4),
            Verdict(12, "1.1:1.5", "1.1:1.5", "attached", 5),
        ]);

        output.Final.Select(r => (Link(r), r.Source[..r.Source.IndexOf(" (")])).Should().Equal(
            (1L, "EVIDENTIA run 10"), (3L, "EVIDENTIA run 10"), (5L, "EVIDENTIA run 12"), (4L, "EVIDENTIA run 6"));
        output.Methods[MappingMethods.Evidentia].Select(r => r.Status).Should().Equal(
            "superseded-by evidentia run 10", null, "superseded-by evidentia run 10", null, null, null);
        output.Report.Should().Contain(r => r.Kind == "superseded" && r.By == "evidentia run 10" && r.Rows == 2);
    }

    [Fact]
    public void A_lower_link_inside_a_standing_link_on_both_sides_stands_as_a_refinement()
    {
        var output = Merge([
            Row(MappingMethods.Strong, "1.1:1.1 1.1:1.2 1.1:1.3", "1.1:1.2 1.1:1.3", confidence: 0.95, link: 1),
            Row(MappingMethods.Composed, "1.1:1.2", "1.1:1.3", confidence: 0.9, source: Machine, link: 2),
            Row(MappingMethods.Composed, "1.1:1.3", "1.1:1.4", confidence: 0.9, source: Machine, link: 3),
        ]);

        output.Final.Select(Link).Should().Equal(1, 2);
        Status(output, MappingMethods.Composed, 1).Should().Be("withdrawn-by strong", "its TO word lies outside the winner");
        output.Report.Should().Contain(r => r.Kind == "refinements" && r.Rows == 1);
    }

    [Fact]
    public void A_word_the_competition_leaves_bare_keeps_its_best_lower_link_when_it_is_sure_enough()
    {
        var output = Merge(
        [
            Row(MappingMethods.Evidentia, "1.1:1.2", "1.1:1.2", confidence: 0.5, source: "EVIDENTIA run 8 (1.0.0)", link: 1, evidence: "decision 1, safe tier: rule"),
            Row(MappingMethods.Strong, "1.1:1.1 1.1:1.2", "1.1:1.1", confidence: 0.7, link: 2),
            Row(MappingMethods.Composed, "1.1:1.3 1.1:1.2", "1.1:1.5", confidence: 0.65, source: Machine, link: 3),
            Row(MappingMethods.Evidentia, "1.1:1.4", "1.1:1.6", confidence: 0.5, source: "EVIDENTIA run 8 (1.0.0)", link: 4, evidence: "decision 4, safe tier: rule"),
            Row(MappingMethods.Composed, "1.1:1.4 1.1:1.6", "1.1:1.7", confidence: 0.6, source: Machine, link: 5),
            Row(MappingMethods.Stated, "1.1:1.7 1.1:1.8", "1.1:1.8", source: Berean, link: 7),
        ],
        [new RulingRow(RulingRow.Accept, "1.1:1.8", "1.1:1.8", "renders", string.Empty, "read in the verse")]);

        output.Final.Where(r => r.Settled).Select(Link).Should().HaveCount(5).And.Contain([1L, 2L, 4L, 7L],
            "1.1:1.1 keeps the 0.7 Strong link and 1.1:1.7 its stated one; 1.1:1.3 and 1.1:1.6 have only compositions under 0.7");
        Status(output, MappingMethods.Composed).Should().Be("withdrawn-by evidentia");
        output.Report.Should().Contain(r => r.Kind == "orphans" && r.Rows == 2);
        output.Report.Should().Contain(r => r.Kind == "bare-words" && r.Rows == 4).And.Contain(r => r.Kind == "bare-words-left" && r.Rows == 2);
        MappingMethods.OrphanFloor.Should().Be(0.7);
    }

    [Fact]
    public void Strong_numbers_laid_from_another_edition_rank_below_a_composition_and_a_text_s_own_do_not()
    {
        MappingRow[] rows =
        [
            Row(MappingMethods.Strong, "1.1:1.1", "1.1:1.1", confidence: 0.9, link: 1),
            Row(MappingMethods.Composed, "1.1:1.1", "1.1:1.2", confidence: 0.98, source: Machine, link: 2),
        ];

        Link(Merge(rows, pair: "LSG1910-BHSA").Final.Should().ContainSingle().Which).Should().Be(2);
        Link(Merge(rows, pair: "KJV-BHSA").Final.Should().ContainSingle().Which).Should().Be(1);
        string[] laid = ["RUSV", "LSG1910", "JND2024", "SCH1951", "RLT2018", "CUV", "CUVS", "RV1909"];
        laid.Should().OnlyContain(slug => Essenthos.Core.Conversion.SideStrongNumbers.Layered(slug));
        ((string[])["KJV", "LUTH1912", "UBIO", "BHSA"]).Should().NotContain(slug => Essenthos.Core.Conversion.SideStrongNumbers.Layered(slug));
    }
}
