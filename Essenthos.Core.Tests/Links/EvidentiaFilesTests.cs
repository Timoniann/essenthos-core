using Essenthos.Core.Conversion;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A pair's evidentia method file made from the ledger and the two texts' products, with no database: each verdict
/// in its own shape, a verdict on one pair of a wider link on that link, an attached word marked with its head, a
/// word that reads otherwise now reported.
/// </summary>
public sealed class EvidentiaFilesTests : IDisposable
{
    private const string Pair = "KJV-BHSA";
    private const string Run = "KJV-BHSA-20261001T120000.000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Verdicts_become_rows_a_wider_link_takes_its_pair_and_an_attached_word_names_its_head()
    {
        var store = Store(withRunNumber: true);

        var outcome = EvidentiaFiles.Write(store, _root, Pair);

        outcome.Conflicts.Should().ContainSingle().Which.Should().Contain("read 'Lord' and reads 'God' now");
        using var reader = store.Open(MappingProducts.Of(Pair, MappingMethods.Evidentia), MappingRow.Schema);
        var rows = reader.ReadAll();
        rows.Should().HaveCount(5);
        rows.Should().ContainEquivalentOf(new MappingRow(null, "1.1:1.4", "1.1:1.4", "renders", MappingMethods.Evidentia, 0.9123,
            "EVIDENTIA run 7 (1.0.0+test), global-review-known-rendering",
            "safe tier: global known-rendering assignment; accepted with its tier by lead, not read one by one", null, null));
        rows.Should().ContainSingle(r => r.Relation == "expands").Which.From.Should().Be("1.1:1.2");
        rows.Should().ContainSingle(r => r.From == "1.1:1.1 1.1:1.2 1.1:1.3").Which.Should().Match<MappingRow>(r =>
            r.To == "1.1:1.1 1.1:1.2" && r.Evidence!.StartsWith("names one word pair of this link; safe tier:"));
        rows.Should().ContainSingle(r => r.From == "1.1:2.2").Which.Roles.Should().Be("1.1:2.2=attached@1.1:2.3");
        rows.Should().NotContain(r => r.From == "1.1:2.1", "a rejected verdict is no row");
    }

    [Fact]
    public void A_merge_folds_the_verdict_on_a_wider_link_into_that_link()
    {
        var store = Store(withRunNumber: true);
        EvidentiaFiles.Write(store, _root, Pair);

        MappingFiles.Merge(store, Pair, force: true);

        using var reader = store.Open(MappingProducts.Of(Pair, MappingProducts.Final), FinalRow.Schema);
        var final = reader.ReadAll();
        final.Where(r => r.From == "1.1:1.1 1.1:1.2 1.1:1.3").Select(r => r.Method).Should()
            .BeEquivalentTo([MappingMethods.Stated, MappingMethods.Evidentia]);
        final.Should().NotContain(r => r.From == "1.1:1.1");
    }

    [Fact]
    public void A_run_that_lists_its_heads_names_them_and_finds_none_by_surface()
    {
        var store = Store(withRunNumber: true);
        File.WriteAllLines(Path.Combine(_root, "Essenthos", "evidentia", Run, "heads.tsv"),
            ["at	word	surface	head_at	head	head_surface", "1:1:2	2	did		1	God"]);

        EvidentiaFiles.Write(store, _root, Pair);

        using var reader = store.Open(MappingProducts.Of(Pair, MappingMethods.Evidentia), MappingRow.Schema);
        reader.ReadAll().Should().ContainSingle(r => r.From == "1.1:2.2").Which.Roles.Should().Be("1.1:2.2=attached@1.1:2.1");
    }

    [Fact]
    public void A_ledger_run_without_its_number_is_refused()
    {
        var store = Store(withRunNumber: false);

        var write = () => EvidentiaFiles.Write(store, _root, Pair);

        write.Should().Throw<InvalidDataException>().WithMessage("*names no run number*");
    }

    private ProductStore Store(bool withRunNumber)
    {
        var store = new ProductStore(_root);
        Words(store, "KJV", ("1.1:1", ["In", "the", "beginning", "God", "created"]), ("1.1:2", ["God", "did", "see"]));
        Words(store, "BHSA", ("1.1:1", ["בְּ", "רֵאשִׁית", "בָּרָא", "אֱלֹהִים"]), ("1.1:2", ["וַיַּרְא", "אֱלֹהִים"]));
        using (var stated = store.Create(MappingProducts.Of(Pair, MappingMethods.Stated), ProductKind.Machine, MappingRow.Schema, "test", []))
        {
            stated.WriteAll([new MappingRow(11, "1.1:1.1 1.1:1.2 1.1:1.3", "1.1:1.1 1.1:1.2", "renders", MappingMethods.Stated, null,
                "Berean Standard Bible translation tables", null, null, null)]);
        }

        var folder = Path.Combine(_root, "Essenthos", "evidentia", Run);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "run.json"),
            "{\n" + (withRunNumber ? "  \"run\": 7,\n" : string.Empty) +
            "  \"from\": \"KJV\",\n  \"to\": \"BHSA\",\n  \"startedAt\": \"2026-10-01T12:00:00.0000000+00:00\",\n" +
            "  \"ruleVersion\": \"1.0.0+test\",\n  \"scope\": {},\n  \"configuration\": {},\n" +
            "  \"reviews\": [ { \"reviewer\": \"lead\", \"reviewedAt\": \"2026-10-01T13:00:00.0000000+00:00\", \"examined\": false, \"note\": null } ]\n}\n");
        string Line(string at, string word, string surface, string target, string targetSurface, string absence, string rule,
            string verdict, string rationale) =>
            string.Join('\t', at, word, surface, string.Empty, target, targetSurface, absence, rule, "safe", "0.9123", verdict, "0",
                string.Empty, string.Empty, string.Empty, rationale);
        File.WriteAllLines(Path.Combine(folder, "01.tsv"),
        [
            "at\tword\tsurface\ttarget_at\ttarget\ttarget_surface\tabsence\trule\ttier\tconfidence\tverdict\treview\tcorrected_at\tcorrected\tcorrected_surface\trationale",
            Line("1:1", "1", "In", "1", "בְּ", "", "global-review-known-rendering", "approved", "global known-rendering assignment"),
            Line("1:1", "2", "the", "", "", "expands", "unwritten-article", "approved", "the original writes no article"),
            Line("1:1", "4", "God", "4", "אֱלֹהִים", "", "global-review-known-rendering", "approved", "global known-rendering assignment"),
            Line("1:1", "4", "Lord", "4", "אֱלֹהִים", "", "names", "approved", "a word that reads otherwise now"),
            Line("1:2", "1", "God", "2", "אֱלֹהִים", "", "names", "rejected", "rejected by its reader"),
            Line("1:2", "2", "did", "1", "וַיַּרְא", "", "attached-word", "approved", "AuxiliaryVerb of 'see'"),
            Line("1:2", "3", "see", "1", "וַיַּרְא", "", "global-review-known-rendering", "approved", "global known-rendering assignment"),
        ]);
        return store;
    }

    private static void Words(ProductStore store, string text, params (string Verse, string[] Words)[] verses)
    {
        var id = text == "KJV" ? 100 : 200;
        using (var writer = store.Create(TextWord.ProductOf(text), ProductKind.Machine, TextWord.Schema(text), "test", []))
        {
            writer.WriteAll(verses.SelectMany(verse => verse.Words.Select((surface, n) => new TextWord(
                new WordKey(text, VerseKey.Parse(verse.Verse), n + 1), ++id, surface, " ", null, null, null, null, null, null, false, null, null, null))));
        }

        using var frame = store.Create(FrameFile.Product(text), ProductKind.Machine, FrameRow.Schema, "test", []);
        frame.WriteAll([]);
    }
}
