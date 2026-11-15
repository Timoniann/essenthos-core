using Essenthos.Core.Conversion;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A pair's <c>strong</c> rows as the build draws them from the store's files (<see cref="StrongNumberSource"/>): the
/// Reina-Valera's numbers, laid from Gómez's edition onto its words, matched within the verse against BHSA.
///
/// The rules these hold were the database loader's before PRB-1698 and are the producer's now: a row says
/// <c>strong</c> and carries a confidence, a tag's words are one occurrence of its number, a number the witness does not
/// write reaches nothing, a number of the other series is never offered, a verse is offered the witness at every address
/// it stands at, a bare object marker is no target, a word stated absent is left out of its phrase, and a source's
/// statement is never written over.
/// </summary>
public sealed class StrongNumberSourceTests : IDisposable
{
    private const string Pair = EbibleTextSource.ReinaValera + "-BHSA";

    private const string Suffixed = """{"pos": "prep", "suffixPerson": "p3"}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    private readonly ProductStore _store;

    public StrongNumberSourceTests()
    {
        _store = new ProductStore(_root);

        // Gómez's edition: a tag over three words is one rendering; Genesis 1:2 carries a Greek number; 2:1 is a psalm's
        // first verse, whose title the Hebrew numbers as a verse of its own; 3:1 has two words for the object marker.
        var edition = Path.Combine(_root, "ReinaValera1909");
        Directory.CreateDirectory(edition);
        File.WriteAllText(Path.Combine(edition, "02-GENspaRV1909.usfm"),
            "\\id GEN Genesis\n\\c 1\n\\p\n"
            + "\\v 1 \\w EN el principio|strong=\"H7225\"\\w* \\w crió|strong=\"H1254\"\\w* \\w Dios|strong=\"H430\"\\w* "
            + "\\w los cielos|strong=\"H8064\"\\w* \\w y|strong=\"H776\"\\w*.\n"
            + "\\v 2 \\w tierra|strong=\"G1093\"\\w*.\n"
            + "\\c 2\n\\p\n\\v 1 \\w David|strong=\"H1732\"\\w*.\n"
            + "\\c 3\n\\p\n\\v 1 \\w y|strong=\"H853\"\\w* \\w lo|strong=\"H853\"\\w*.\n");

        // The words product holds each tag's first number, as the conversion writes it.
        Text(EbibleTextSource.ReinaValera, "spa", "translation",
            [
                ("1.1:1", [("EN", "H7225", null), ("el", "H7225", null), ("principio", "H7225", null), ("crió", "H1254", null),
                    ("Dios", "H430", null), ("los", "H8064", null), ("cielos", "H8064", null), ("y", "H776", null)]),
                ("1.1:2", [("tierra", "G1093", null)]),
                ("1.2:1", [("David", "H1732", null)]),
                ("1.3:1", [("y", "H853", null), ("lo", "H853", null)]),
            ],
            ("1.2:1", ["1.2:1", "1.2:0"]));
        Text("BHSA", "hbo", "critical-edition",
            [
                ("1.1:1", [("בְּ", "H9003", null), ("רֵאשִׁית", "H7225", null), ("בָּרָא", "H1254", null), ("אֱלֹהִים", "H430", null),
                    ("אֵת", "H853", null), ("הַ", "H9009", null), ("שָּׁמַיִם", "H8064", null)]),
                ("1.1:2", [("וְ", "H9000", null), ("הָ", "H9009", null), ("אָרֶץ", "H776", null)]),
                ("1.2:0", [("דָוִד", "H1732", null)]),
                ("1.2:1", [("שִׁיר", "H7892", null)]),
                ("1.3:1", [("אֵת", "H853", null), ("אֹתוֹ", "H853", Suffixed)]),
            ]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Every_row_says_strong_and_renders_with_a_confidence_credited_to_the_numbering()
    {
        var rows = Rows();

        rows.Should().NotBeEmpty().And.OnlyContain(row => row.Method == MappingMethods.Strong && row.Relation == "renders"
            && row.Confidence != null && row.Source == ReinaValeraStrongLinkLoader.Credit + ", matched within the verse against BHSA");
    }

    [Fact]
    public void A_tag_s_words_are_one_occurrence_and_reach_the_word_carrying_its_number()
    {
        var rows = Rows();

        rows.Single(row => row.To == "1.1:1.2").Should().Match<MappingRow>(row =>
            row.From == "1.1:1.1 1.1:1.2 1.1:1.3" && row.Confidence == StrongNumberMatch.Unambiguous);
        rows.Single(row => row.From == "1.1:1.5").To.Should().Be("1.1:1.4");
        rows.Single(row => row.From == "1.1:1.6 1.1:1.7").To.Should().Be("1.1:1.7");
    }

    [Fact]
    public void A_number_the_witness_does_not_write_in_the_verse_reaches_nothing() =>
        Rows().Should().NotContain(row => row.From.Contains("1.1:1.8"), "the Hebrew verse here stops before the earth");

    [Fact]
    public void A_Greek_number_is_not_offered_to_a_Hebrew_witness() =>
        Rows().Should().NotContain(row => row.From.StartsWith("1.1:2.", StringComparison.Ordinal));

    [Fact]
    public void A_verse_is_offered_the_witness_at_every_address_it_stands_at() =>
        Rows().Single(row => row.From == "1.2:1.1").To.Should().Be("1.2:0.1");

    [Fact]
    public void A_bare_object_marker_is_no_target_and_both_tags_reach_the_pronoun()
    {
        var marker = Rows().Single(row => row.From.StartsWith("1.3:1.", StringComparison.Ordinal));

        marker.From.Should().Be("1.3:1.1 1.3:1.2");
        marker.To.Should().Be("1.3:1.2");
        marker.Confidence.Should().Be(StrongNumberMatch.OneSideContended);
    }

    [Fact]
    public void A_word_stated_absent_is_left_out_of_its_phrase_s_match()
    {
        Method(MappingMethods.Stated, new MappingRow("1.1:1.2", "", "expands", MappingMethods.Stated, null, "a verdict under test", null, null, null));

        Rows().Single(row => row.To == "1.1:1.2").From.Should().Be("1.1:1.1 1.1:1.3");
    }

    [Fact]
    public void A_match_on_a_stated_word_yields_and_one_naming_exactly_the_stated_words_is_a_claim_on_them()
    {
        Method(MappingMethods.Stated,
            new MappingRow("1.1:1.5", "1.1:1.4", "renders", MappingMethods.Stated, null, "Clear Bible Alignments under test", null, null, null),
            new MappingRow("1.1:1.4", "1.1:1.7", "renders", MappingMethods.Stated, null, "Clear Bible Alignments under test", null, null, null));

        var rows = Rows();

        rows.Single(row => row.From == "1.1:1.5").Evidence.Should().Be(TaggedTextLinkLoader.CorroborationNote);
        rows.Single(row => row.From == "1.1:1.4").Status.Should().Be(MappingStatus.YieldsToStated, "a laid phrase's other words are the merge's to settle");
    }

    private List<MappingRow> Rows() =>
        [.. StrongNumberSource.All.Single(source => source.States(Pair)).Rows(new ProductTexts(_store), Pair, _ => { })];

    private void Method(string method, params MappingRow[] rows)
    {
        using var writer = _store.Create(MappingProducts.Of(Pair, method), ProductKind.Machine, MappingRow.Schema, "test", []);
        writer.WriteAll(rows);
    }

    private void Text(string slug, string language, string kind, (string Verse, (string Surface, string? Strong, string? Morphology)[] Words)[] verses,
        params (string Own, string[] Canonical)[] frame)
    {
        using (var declared = _store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", slug), new TextField("kind", kind), new TextField("language", language),
                new TextField("versification", "kjv")]);
        }

        using (var writer = _store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []))
        {
            writer.WriteAll(verses.OrderBy(verse => VerseKey.Parse(verse.Verse)).SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(slug, VerseKey.Parse(verse.Verse), n + 1), word.Surface, " ", null, word.Strong, null, word.Morphology,
                null, null, false, null, null, null))));
        }

        using var frames = _store.Create(FrameFile.Product(slug), ProductKind.Machine, FrameRow.Schema, "test", []);
        frames.WriteAll(frame.Select(row => new FrameRow(VerseKey.Parse(row.Own), null, [.. row.Canonical.Select(key => VerseKey.Parse(key))], "test")));
    }
}
