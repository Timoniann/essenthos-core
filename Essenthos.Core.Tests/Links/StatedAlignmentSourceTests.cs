using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Door43;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
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
        source.Inputs(store, "IRVPAN-NESTLE1904").Should().Equal("Door43/pa_irv/51-PHP.usfm", Door43BookAlignment.Declared);
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
    public void A_file_written_from_an_earlier_release_is_made_again_and_keeps_only_the_rows_of_other_sources()
    {
        ShippedStrongNumbers.CopyInto(_root);
        var store = new ProductStore(_root);
        var now = InterlinearLinkLoader.UnfoldingWordSource;
        var before = now.Replace(", release 92,", ", release 90,", StringComparison.Ordinal);
        const string other = "Clear Bible alignment, release 3";
        var product = MappingProducts.Of("ULT-NESTLE1904", MappingMethods.Stated);
        using (var writer = store.Create(product, ProductKind.Machine, MappingRow.Schema, "test", []))
        {
            writer.WriteAll([Stated("50.1:16.1", "50.1:16.4", before), Stated("50.1:16.2", "50.1:16.3", before), Stated("50.1:17.1", "50.1:17.2", other)]);
        }

        var source = new Door43AlignmentSource();
        var made = Stated("50.1:16.1", "50.1:16.4", now);
        var written = MethodFiles.Write(store, "ULT-NESTLE1904", MappingMethods.Stated, [made], "test", [], source.Makes);

        using var reader = store.Open(product, MappingRow.Schema);
        reader.ReadAll().Should().BeEquivalentTo([made, Stated("50.1:17.1", "50.1:17.2", other)]);
        reader.Header!.Notes.Where(note => note.Key == MethodFiles.KeptNote).Select(note => note.Value).Should().Equal(other);
        written.Carried.Should().Be(1);
        written.Kept.Should().Be(1, "the shape release 90 stated is stated again");
        written.Gone.Should().Be(1);
        source.Makes(InterlinearLinkLoader.Interlinear(Door43TextSource.Simplified).Source.Replace(", release 92,", ", release 91,", StringComparison.Ordinal))
            .Should().BeTrue();
        source.Makes(InterlinearLinkLoader.Interlinear(Bible4uTextSource.Ohienko).Source).Should().BeTrue();
        source.Makes(now.Replace("Literal Text", "Lexical Text", StringComparison.Ordinal)).Should().BeFalse();
        Datasets.Of(now).Should().Be("unfoldingword-ult").And.Be(Datasets.Of(before));
        Datasets.Of(InterlinearLinkLoader.Interlinear(Door43TextSource.Simplified).Source).Should().Be("unfoldingword-ust");
    }

    private static MappingRow Stated(string from, string to, string source) =>
        new(from, to, "renders", MappingMethods.Stated, null, source, null, null, null);

    private static ProductStore Shipped => new(TestResources.Shipped("."));

    private static Door43BookAlignment Philippians => Door43BookAlignment.All(Shipped).Single(book => book.File == "51-PHP.usfm");

    private const string Release = """
        \id PHP
        \c 1
        \v 16 \zaln-s |x-strong="G00260" x-lemma="ἀγάπη" x-occurrence="1" x-occurrences="1" x-content="ἀγάπης"\*\w любов|x-occurrence="1" x-occurrences="1"\w*\zaln-e\* \w знаючи|x-occurrence="1" x-occurrences="1"\w*
        \v 17 \w інші|x-occurrence="1" x-occurrences="1"\w* \zaln-s |x-strong="G35880" x-lemma="ὁ" x-occurrence="1" x-occurrences="1" x-content="οἱ"\*\w суперечкою|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
        """;

    [Fact]
    public void A_book_aligned_in_a_persons_own_project_is_read_beside_the_release_and_credited_to_the_project()
    {
        var store = Store("UBIO", "ukr", ["Ім'я", "знаючи"], ["інші", "суперечкою"]);
        Write("Door43/uk_ubio_books/51-PHP.usfm",
            """
            \id PHP
            \c 1
            \v 16 \zaln-s |x-strong="G00260" x-lemma="ἀγάπη" x-occurrence="1" x-occurrences="1" x-content="ἀγάπης"\*\w Імƅя|x-occurrence="1" x-occurrences="1"\w*\zaln-e\* \w знаючи|x-occurrence="1" x-occurrences="1"\w*
            """);

        var source = new Door43AlignmentSource();
        var rows = source.Rows(new ProductTexts(store), "UBIO-NESTLE1904", _ => { }).ToList();

        rows.Select(row => (row.Shape, row.Source)).Should().Equal(("50.1:16.1|50.1:16.4", Philippians.Source));
        source.Inputs(store, "UBIO-NESTLE1904").Should().Equal("Door43/uk_ubio_books/51-PHP.usfm", Door43BookAlignment.Declared);
        Door43BookAlignment.Spelling("Імƅя").Should().Be("Ім’я", "the project prints the apostrophe as U+0185");
    }

    [Fact]
    public void Where_the_release_aligns_the_book_too_a_verse_it_states_alike_stays_its_own_and_one_stated_otherwise_is_the_projects()
    {
        var store = Store("UBIO", "ukr", ["любов", "знаючи"], ["інші", "суперечкою"]);
        Write("Door43/uk_ubio/51-PHP.usfm", Release);
        Write("Door43/uk_ubio_books/51-PHP.usfm",
            """
            \id PHP
            \c 1
            \v 16 \zaln-s |x-strong="G00260" x-lemma="ἀγάπη" x-occurrence="1" x-occurrences="1" x-content="ἀγάπης"\*\w любов|x-occurrence="1" x-occurrences="1"\w*\zaln-e\* \w знаючи|x-occurrence="1" x-occurrences="1"\w*
            \v 17 \w інші|x-occurrence="1" x-occurrences="1"\w* \zaln-s |x-strong="G20520" x-lemma="ἐριθεία" x-occurrence="1" x-occurrences="1" x-content="ἐριθείας"\*\w суперечкою|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """);

        var rows = new Door43AlignmentSource().Rows(new ProductTexts(store), "UBIO-NESTLE1904", _ => { }).ToList();

        var release = InterlinearLinkLoader.Interlinear(Bible4uTextSource.Ohienko).Source;
        rows.Select(row => (row.Shape, row.Source)).Should().BeEquivalentTo(
            [("50.1:16.1|50.1:16.4", release), ("50.1:17.2|50.1:17.4", Philippians.Source)],
            "the release's 1:17 put the word on οἱ, and no verse is linked from both");
    }

    [Fact]
    public void A_project_verse_whose_words_are_not_the_corpus_words_is_left_out_and_the_releases_stands()
    {
        var store = Store("UBIO", "ukr", ["любов", "знаючи"], ["інші", "суперечкою"]);
        Write("Door43/uk_ubio/51-PHP.usfm", Release);
        Write("Door43/uk_ubio_books/51-PHP.usfm",
            """
            \id PHP
            \c 1
            \v 17 \w а|x-occurrence="1" x-occurrences="1"\w* \w інші|x-occurrence="1" x-occurrences="1"\w* \zaln-s |x-strong="G20520" x-lemma="ἐριθεία" x-occurrence="1" x-occurrences="1" x-content="ἐριθείας"\*\w суперечкою|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """);
        var log = new List<string>();

        var rows = new Door43AlignmentSource().Rows(new ProductTexts(store), "UBIO-NESTLE1904", log.Add).ToList();

        rows.Select(row => row.Shape).Should().BeEquivalentTo("50.1:16.1|50.1:16.4", "50.1:17.2|50.1:17.1");
        rows.Should().OnlyContain(row => row.Source == InterlinearLinkLoader.Interlinear(Bible4uTextSource.Ohienko).Source);
        log.Should().Contain(line => line.Contains("1 of its 1 verses left out, their words not ours (1:17)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_project_taken_again_at_a_later_commit_is_the_same_producers_and_each_set_of_terms_has_its_credit()
    {
        var source = new Door43AlignmentSource();
        var romans = Door43BookAlignment.All(Shipped).Single(book => book.File == "46-ROM.usfm");

        source.Makes(Shipped, Philippians.Source.Replace(Philippians.Commit[..8], "0123abcd", StringComparison.Ordinal)).Should().BeTrue();
        source.Makes(Shipped, Philippians.Source.Replace("Philippians by", "Colossians by", StringComparison.Ordinal)).Should().BeFalse();
        source.Makes(Philippians.Source).Should().BeFalse("a project is the store's to declare");
        Datasets.Of(Philippians.Source).Should().Be("door43-ubio-by-sa");
        Datasets.Of(romans.Source).Should().Be("door43-ubio-cc0");
        Door43BookAlignment.All(Shipped).Should().OnlyContain(book => book.Slug == Bible4uTextSource.Ohienko && book.Commit.Length == 40);
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
        ShippedStrongNumbers.CopyInto(_root);
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
                new TextWord(new WordKey(text, VerseKey.Parse(verse.Item1), n + 1), surface, " ", null, null, null, null,
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
