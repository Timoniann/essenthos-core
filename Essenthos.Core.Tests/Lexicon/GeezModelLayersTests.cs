using System.Text.Json;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Build;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// The model's two layers over the Ge'ez taken from a finished run into the store: every checked line lands on the word
/// its key names today, printed as the run saw it, or nothing is written; the table the build loads gives each word its
/// id and the Greek words theirs; Dillmann's senses go in under entries the lexicon loads.
/// </summary>
public sealed class GeezModelLayersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-geez-layers", Guid.NewGuid().ToString("N"));

    private string Resources => Path.Combine(_root, "Resources");

    private string Run => Path.Combine(_root, "run");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void EveryCheckedLineLandsOnTheWordItsKeyNamesAndTheTableGivesItsIdAndItsGreeksIds()
    {
        var store = Store("ቃልየ", "ገብረ");
        WriteRun(("ቃልየ", "ገብረ"), Line(Geez(1), "ቃል", greek: [Greek(1)]), Line(Geez(2), "ገብረ", greek: []));

        var outcome = GeezModelLayers.ImportGlosses(store, Run);

        outcome.Failed.Should().BeFalse(outcome.ToString());
        outcome.Written.Should().Be(2);
        using (var reader = store.Open(GeezModelLayers.Glosses(1), GeezGlossRow.Schema))
        {
            var rows = reader.ReadAll();
            rows.Select(r => r.Word).Should().Equal(Key("GEEZ81", 1), Key("GEEZ81", 2));
            rows[0].Greek.Should().Equal(Key("GRCBRENT", 1));
            rows[0].Made.Should().Be(new DateOnly(2026, 11, 16));
        }

        TableProducts.Write(store, new HashSet<string> { "geez_word_gloss" }, force: false).Single().Rows.Should().Be(2);
        using var table = store.Open(TableProducts.Product("geez_word_gloss"));
        var first = table.Rows().First();
        first[0].Should().Be(WordIds.Of(Key("GEEZ81", 1)).ToString());
        first[7].Should().Be($"{{{WordIds.Of(Key("GRCBRENT", 1))}}}");
    }

    [Fact]
    public void ALineWhoseWordTheTextNoLongerPrintsAsTheRunSawItStopsTheImportAndNothingIsWritten()
    {
        var store = Store("ቃልየ", "ገብሩ");
        WriteRun(("ቃልየ", "ገብረ"), Line(Geez(1), "ቃል", greek: []), Line(Geez(2), "ገብረ", greek: []));

        var outcome = GeezModelLayers.ImportGlosses(store, Run);

        outcome.Failed.Should().BeTrue();
        outcome.Unplaced.Should().ContainSingle().Which.Should().Contain("1.1:1.2").And.Contain("ገብሩ");
        store.Exists(GeezModelLayers.Glosses(1)).Should().BeFalse();
    }

    [Fact]
    public void TheTableRefusesGlossesOnWordsTheTextNoLongerHolds()
    {
        var store = Store("ቃልየ", "ገብረ");
        WriteRun(("ቃልየ", "ገብረ"), Line(Geez(1), "ቃል", greek: []), Line(Geez(2), "ገብረ", greek: []));
        GeezModelLayers.ImportGlosses(store, Run).Failed.Should().BeFalse();
        WriteWords(store, "GEEZ81", "ቃልየ");

        var write = () => TableProducts.Write(store, new HashSet<string> { "geez_word_gloss" }, force: false);

        write.Should().Throw<InvalidDataException>().WithMessage("*1.1:1.2*");
    }

    [Fact]
    public void DillmannsSensesGoInUnderEntriesTheLexiconLoadsAndASenseRenderedEmptyIsLeftOut()
    {
        var store = Store("ቃልየ");
        Directory.CreateDirectory(Run);
        WriteFacts("claude-sonnet-5-5", "geez-dillmann-test");
        File.WriteAllLines(Path.Combine(Run, "clean.jsonl"),
        [
            Json(new
            {
                custom_id = "dl-0001", entry = "Lword", headword = "ቃል", model = "claude-sonnet-5-5", run = "geez-dillmann-test", prompt_version = "geez-v1",
                senses = new object[]
                {
                    new { n = "1", en = "voice", uk = "голос", de = "Stimme", es = "voz" },
                    new { n = "2", en = "", uk = "", de = "", es = "" },
                    new { n = "2a", en = "word", uk = "слово", de = "Wort", es = "palabra" },
                },
            }),
        ]);
        var lexicon = new List<DillmannHeadword> { new("Lword", "ቃል", ["ቃል"], ["vox"], []) };

        var outcome = GeezModelLayers.ImportRenderings(store, Run, lexicon);

        outcome.Failed.Should().BeFalse(outcome.ToString());
        using var reader = store.Open(GeezModelLayers.Renderings, DillmannRenderingRow.Schema);
        reader.ReadAll().Select(r => (r.Ordinal, r.Sense, r.Uk)).Should().Equal((1, "1", "голос"), (3, "2a", "слово"));

        GeezModelLayers.ImportRenderings(store, Run, [new("Lother", "ቃል", ["ቃል"], ["vox"], [])]).Failed.Should().BeTrue();
    }

    private static WordKey Key(string text, int position) => new(text, new VerseKey(1, 1, 1), position);

    private static long Geez(int position) => WordIds.Of(Key("GEEZ81", position));

    private static long Greek(int position) => WordIds.Of(Key("GRCBRENT", position));

    private ProductStore Store(params string[] geez)
    {
        var store = new ProductStore(Resources);
        WriteWords(store, "GEEZ81", geez);
        WriteWords(store, "GRCBRENT", "λόγος", "ἐποίησεν");
        return store;
    }

    private static void WriteWords(ProductStore store, string text, params string[] surfaces)
    {
        using var writer = store.Create(TextWord.ProductOf(text), ProductKind.Machine, TextWord.Schema(text), "test", []);
        writer.WriteAll(surfaces.Select((surface, n) => new TextWord(Key(text, n + 1), surface, " ", null, null, null, null, null, null, false, null, null, null)));
    }

    private static object Line(long word, string lemma, long[] greek) => new
    {
        custom_id = "gl-01-001-01", word_id = word.ToString(), book = "Gen", chapter = 1, lemma, lemma_source = "dillmann",
        sense_en = "a sense", sense_uk = "значення", en = "a line", uk = "рядок",
        greek_word_ids = greek.Select(id => id.ToString()).ToArray(), greek_text = "GRCBRENT", morph = "", conf = "high",
        warnings = Array.Empty<string>(), model = "claude-opus-5-5", run = "geez-gloss-test", prompt_version = "geez-v1-short",
    };

    private void WriteRun((string First, string Second) asked, params object[] lines)
    {
        Directory.CreateDirectory(Path.Combine(Run, "inputs"));
        WriteFacts("claude-opus-5-5", "geez-gloss-test");
        File.WriteAllLines(Path.Combine(Run, "clean.jsonl"), lines.Select(Json));
        File.WriteAllText(Path.Combine(Run, "rejects.jsonl"), string.Empty);
        File.WriteAllLines(Path.Combine(Run, "inputs", "gloss-chapters.jsonl"),
        [
            Json(new
            {
                book = "Gen", ordinal = 1, chapter = 1, greek_text = "GRCBRENT",
                geez = new[]
                {
                    new { id = Geez(1).ToString(), c = 1, v = 1, label = "", p = 1, t = asked.First },
                    new { id = Geez(2).ToString(), c = 1, v = 1, label = "", p = 2, t = asked.Second },
                },
                greek = new[]
                {
                    new { id = Greek(1).ToString(), c = 1, v = 1, label = "", p = 1, t = "λόγος" },
                    new { id = Greek(2).ToString(), c = 1, v = 1, label = "", p = 2, t = "ἐποίησεν" },
                },
            }),
        ]);
    }

    private void WriteFacts(string model, string run)
    {
        File.WriteAllText(Path.Combine(Run, "manifest.json"),
            Json(new { at = "2026-11-16T14:08:31+00:00", run, model, prompt_version = "geez-v1" }));
        File.WriteAllLines(Path.Combine(Run, "submissions.jsonl"), [Json(new { at = "2026-11-16T17:13:49+00:00" })]);
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);
}
