using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The published alignments made into stated rows from files: Door43's interlinears and Clear Bible's sets, over two texts
/// whose frames place Philippians 1:16 and 1:17 at each other's addresses, as the Textus Receptus orders them. Both
/// sources number a verse as the translation prints it, so the translation's 1:16 meets the original's 1:16.
/// </summary>
public sealed class StatedAlignmentSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void A_Door43_verse_is_the_translations_own_and_meets_the_witness_at_its_frame_address()
    {
        var store = Store("IRVPAN", "pan", ["love", "knowing"], ["others", "ambition"]);
        Write("Door43/pa_irv/51-PHP.usfm",
            """
            \id PHP
            \c 1
            \v 16 \zaln-s |x-strong="G00260" x-lemma="ἀγάπη" x-occurrence="1" x-occurrences="1" x-content="ἀγάπης"\*\w love|x-occurrence="1" x-occurrences="1"\w*\zaln-e\* \w knowing|x-occurrence="1" x-occurrences="1"\w*
            \v 17 \w others|x-occurrence="1" x-occurrences="1"\w* \zaln-s |x-strong="G20520" x-lemma="ἐριθεία" x-occurrence="1" x-occurrences="1" x-content="ἐριθείας"\*\w ambition|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """);

        var source = new Door43AlignmentSource();
        var rows = source.Rows(new ProductTexts(store), "IRVPAN-NESTLE1904", _ => { }).ToList();

        rows.Select(row => row.Shape).Should().BeEquivalentTo("50.1:16.1|50.1:16.4", "50.1:17.2|50.1:17.4");
        source.Inputs(store, "IRVPAN-NESTLE1904").Should().Equal("Door43/pa_irv/51-PHP.usfm");
    }

    [Fact]
    public void A_translation_aligned_only_to_the_Greek_states_no_Hebrew_pair()
    {
        var source = new Door43AlignmentSource();

        source.States("IRVPAN-NESTLE1904").Should().BeTrue();
        source.States("IRVPAN-BHSA").Should().BeFalse();
        source.States("IRV2019-BHSA").Should().BeTrue();
        source.States("KJV-BHSA").Should().BeFalse();
    }

    [Fact]
    public void The_Berean_set_lays_its_Greek_verse_on_the_witness_words_of_the_Bereans_verse_of_that_number()
    {
        var store = Store("BSB", "eng", ["The", "latter", "love"], ["The", "former", "ambition"]);
        Write("ClearBible/data/sources/BGNT.tsv",
            """
            id	altId	text	strongs	gloss	gloss2	lemma	pos	morph
            n50001016001	οἱ-1	οἱ	G3588
            n50001016002	μὲν-1	μὲν	G3303
            n50001016003	ἐξ-1	ἐξ	G1537
            n50001016004	ἀγάπης-1	ἀγάπης	G0026
            n50001017001	οἱ-1	οἱ	G3588
            n50001017002	δὲ-1	δὲ	G1161
            n50001017003	ἐξ-1	ἐξ	G1537
            n50001017004	ἐριθείας-1	ἐριθείας	G2052
            """);
        Write("ClearBible/data/eng/targets/BSB/nt_BSB.tsv",
            """
            id	source_verse	text	skip_space_after	exclude	id_range_end	source_verse_range_end
            50001016001	50001016	The
            50001016002	50001016	latter
            50001016003	50001016	love
            50001017001	50001017	The
            50001017002	50001017	former
            50001017003	50001017	ambition
            """);
        Write("ClearBible/data/eng/alignments/BSB/BGNT-BSB-manual.json",
            """
            {"records": [
              {"source": ["n50001016004"], "target": ["500010160031"]},
              {"source": ["n50001017004"], "target": ["500010170031"]}
            ]}
            """);

        var rows = new ClearBibleSource().Rows(new ProductTexts(store), "BSB-NESTLE1904", _ => { }).ToList();

        rows.Select(row => row.Shape).Should().BeEquivalentTo("50.1:16.3|50.1:16.4", "50.1:17.3|50.1:17.4");
    }

    /// <summary>A translation and Nestle 1904, each with Philippians 1:16 and 1:17 at each other's frame addresses.</summary>
    private ProductStore Store(string translation, string language, string[] sixteen, string[] seventeen)
    {
        var store = new ProductStore(_root);
        Text(store, translation, language, sixteen, seventeen);
        Text(store, "NESTLE1904", "grc", ["οἱ", "μὲν", "ἐξ", "ἀγάπης"], ["οἱ", "δὲ", "ἐξ", "ἐριθείας"]);
        return store;
    }

    private static void Text(ProductStore store, string text, string language, string[] sixteen, string[] seventeen)
    {
        using (var declared = store.Create(TextField.ProductOf(text), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", text), new TextField("kind", "translation"), new TextField("language", language)]);
        }

        using (var writer = store.Create(TextWord.ProductOf(text), ProductKind.Machine, TextWord.Schema(text), "test", []))
        {
            writer.WriteAll(new[] { ("50.1:16", sixteen), ("50.1:17", seventeen) }.SelectMany(verse => verse.Item2.Select((surface, n) =>
                new TextWord(new WordKey(text, VerseKey.Parse(verse.Item1), n + 1), null, surface, " ", null, null, null, null,
                    WordFolding.Fold(surface, language), null, false, null, null, null))));
        }

        using var frame = store.Create(FrameFile.Product(text), ProductKind.Machine, FrameRow.Schema, "test", []);
        frame.WriteAll(
        [
            new FrameRow(VerseKey.Parse("50.1:16"), null, [VerseKey.Parse("50.1:17")], "moved"),
            new FrameRow(VerseKey.Parse("50.1:17"), null, [VerseKey.Parse("50.1:16")], "moved"),
        ]);
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
