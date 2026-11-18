using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Names;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// Whom a translation's verses say its words name, made from files (<see cref="NameConsensusPass"/>): the word every verse
/// the original names a record in prints, written where the names the pair's earlier method files carry say nothing.
/// </summary>
public sealed class NameConsensusFilesTests : IDisposable
{
    private const string Name = """{"pos": "nmpr"}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void The_word_every_verse_naming_a_record_prints_is_written_as_that_record()
    {
        var store = Store();

        var names = Write(store);

        names.Should().HaveCount(Verses).And.OnlyContain(name => name.Record == "abram" && name.Own.Position == 2,
            "Abram is printed in every verse BHSA names him in and in no other, and nothing else is");
    }

    [Fact]
    public void A_word_an_earlier_method_file_carries_a_name_to_is_not_written()
    {
        var store = Store();
        using (var writer = store.Create(MappingProducts.Of("DRA-BHSA", MappingMethods.Stated), ProductKind.Machine, MappingRow.Schema, "test", []))
        {
            writer.WriteAll([new MappingRow("1.12:1.2", "1.12:1.2", "renders", MappingMethods.Stated, null, "a test", null, null, null)]);
        }

        var names = Write(store);

        names.Should().HaveCount(Verses - 1).And.NotContain(name => name.Own.Own == "1.12:1.2",
            "the stated link carries BHSA's Abram to that word already");
    }

    [Fact]
    public void A_text_whose_inputs_did_not_move_is_not_written_again_when_another_languages_text_moved()
    {
        var store = Store();
        Text(store, "UBIO", "ukr", [
            .. Enumerable.Range(1, Verses).Select(verse => ($"1.12:{verse}", new (string, string?)[] { ("і", null), ("Аврам", null), ("пішов", null) })),
        ]);
        using (var writer = store.Create(NameProducts.Bases, ProductKind.Hand, NameBase.Schema, "test", []))
        {
            writer.WriteAll([new NameBase("DRA", "1-39", "BHSA", "a test"), new NameBase("UBIO", "1-39", "BHSA", "a test")]);
        }

        using (var writer = store.Create(MappingProducts.Of("UBIO-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []))
        {
            writer.WriteAll([]);
        }

        store.Save();
        NameConsensusPass.Write(store, force: true, NameConsensusPass.Precision, null, null, 2, _ => { }).Written.Should().BeTrue();
        store.Save();
        var ubio = store.FullPath(store.Find(NameProducts.Consensus("UBIO"))!);
        var written = File.GetLastWriteTimeUtc(ubio);
        var bytes = File.ReadAllBytes(ubio);

        Text(store, "DRA", "eng", [
            .. Enumerable.Range(1, Verses).Select(verse => ($"1.12:{verse}", new (string, string?)[] { ("and", null), ("Abram", null), ("walked", null) })),
        ]);
        store.Save();
        var again = NameConsensusPass.Write(new ProductStore(_root), force: false, NameConsensusPass.Precision, null, null, 2, _ => { });

        again.Written.Should().BeTrue("the Douay's words moved");
        again.Report.Should().Contain("1 files");
        File.GetLastWriteTimeUtc(ubio).Should().Be(written, "nothing the Ukrainian text's file names moved");
        File.ReadAllBytes(ubio).Should().Equal(bytes);
        NameConsensusPass.Write(new ProductStore(_root), force: false, NameConsensusPass.Precision, null, null, 2, _ => { }).Written.Should().BeFalse();
    }

    [Fact]
    public void Every_translation_names_first_places_is_a_text_the_consensus_names()
    {
        var originals = NamesFirstSource.Placed.Select(pair => MappingProducts.Split(pair).From).Distinct().ToList();

        originals.Should().NotContain(["BHSA", "NESTLE1904"], "names-pairs refuses a pair whose translation has no consensus file");
    }

    [Fact]
    public void A_title_settled_on_a_word_gives_way_to_its_bearer_and_two_records_standing_alike_name_nobody()
    {
        NameConsensusPass.Settling Named(int entity, EntityKind kind, string method = "manual") =>
            new(entity, kind, Database.Entities.ClaimStanding.Of(EnumSpelling.ToLinkMethod(method)), 1.0, "a test", string.Empty);
        var bearers = new HashSet<(int, int)> { (1, 2) };

        NameConsensusPass.Settle([Named(1, EntityKind.Title), Named(2, EntityKind.Person)], bearers).Should().Be(2);
        NameConsensusPass.Settle([Named(2, EntityKind.Person), Named(3, EntityKind.Person)], bearers).Should().BeNull();
        NameConsensusPass.Settle([Named(2, EntityKind.Person), Named(3, EntityKind.Person, "lexical")], bearers).Should().Be(2);
        NameConsensusPass.Settle([Named(2, EntityKind.Person), Named(3, EntityKind.People)], bearers).Should().Be(3,
            "a person and a people stand beside each other, and the first is the one whose kind spells first");
        NameConsensusPass.Settle([Named(2, EntityKind.Person) with { Own = false }, Named(3, EntityKind.Person)], bearers).Should().Be(3,
            "a reading of the word comes before the consensus's own");
    }

    private const int Verses = 13;

    /// <summary>Genesis 12:1-13 naming Abram in BHSA and the Douay, and 13 verses of Genesis 13 naming nobody.</summary>
    private ProductStore Store()
    {
        var store = new ProductStore(_root);
        var naming = Enumerable.Range(1, Verses).Select(verse => $"1.12:{verse}").ToList();
        var other = Enumerable.Range(1, Verses).Select(verse => $"1.13:{verse}").ToList();
        Text(store, "DRA", "eng", [
            .. naming.Select(verse => (verse, new (string, string?)[] { ("and", null), ("Abram", null), ("went", null) })),
            .. other.Select(verse => (verse, new (string, string?)[] { ("and", null), ("he", null), ("went", null) })),
        ]);
        Text(store, "BHSA", "hbo", [
            .. naming.Select(verse => (verse, new (string, string?)[] { ("וַיֵּלֶךְ", "הלך"), ("אַבְרָם", "אַבְרָם") })),
            .. other.Select(verse => (verse, new (string, string?)[] { ("וַיֵּלֶךְ", "הלך"), ("הוּא", "הוּא") })),
        ]);
        using (var writer = store.Create(RecordKinds.Product, ProductKind.Machine, RecordKind.Schema, "test", []))
        {
            writer.WriteAll([new RecordKind("abram", "person", null)]);
        }

        using (var writer = store.Create(NameProducts.Settled("BHSA"), ProductKind.Hand, SettledName.Schema("BHSA"), "test", []))
        {
            writer.WriteAll(naming.Select(verse =>
                new SettledName(WordKey.Parse($"{verse}.2", "BHSA"), "abram", NameRoles.Name, "manual", null, "a test", null)));
        }

        using (var writer = store.Create(NameProducts.Rules, ProductKind.Hand, NameRuleRow.Schema, "test", []))
        {
            writer.WriteAll(NameRules.Seed());
        }

        using (var writer = store.Create(NameProducts.Bases, ProductKind.Hand, NameBase.Schema, "test", []))
        {
            writer.WriteAll([new NameBase("DRA", "1-39", "BHSA", "a test")]);
        }

        using (var writer = store.Create(MappingProducts.Of("DRA-BHSA", MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []))
        {
            writer.WriteAll([]);
        }

        store.Save();
        return store;
    }

    private static List<ConsensusName> Write(ProductStore store)
    {
        var outcome = NameConsensusPass.Write(store, force: true, NameConsensusPass.Precision, null, null, 1, _ => { });
        outcome.Written.Should().BeTrue();
        var graph = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, "build", "products.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(graph)!);
        File.Copy(TestResources.Shipped(Path.Combine(ProductStore.EssenthosFolder, "build", "products.tsv")), graph, overwrite: true);
        ProductGraph.Read(store).Check(store.Manifest, _ => []).Should().BeEmpty("every file the consensus reads is declared on its line");
        using var reader = store.Open(NameProducts.Consensus("DRA"), ConsensusName.Schema("DRA"));
        return reader.ReadAll();
    }

    private static void Text(ProductStore store, string slug, string language, (string Verse, (string Surface, string? Lemma)[] Words)[] verses)
    {
        using (var declared = store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", slug), new TextField("kind", language == "hbo" ? "critical-edition" : "translation"),
                new TextField("language", language), new TextField("versification", "kjv")]);
        }

        using (var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []))
        {
            writer.WriteAll(verses.SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(slug, VerseKey.Parse(verse.Verse), n + 1), word.Surface, " ", word.Lemma, null, null,
                word.Lemma is null ? null : Name, null, null, false, null, null, null))));
        }

        using var frame = store.Create(FrameFile.Product(slug), ProductKind.Machine, FrameRow.Schema, "test", []);
        frame.WriteAll([]);
    }
}
