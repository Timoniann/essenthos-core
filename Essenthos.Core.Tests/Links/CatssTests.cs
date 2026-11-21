using Essenthos.Core.Catss;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// CATSS's parallel text as the Oxford Text Archive prints it (item 1111, CC BY-NC-SA 3.0; the lines below are its own,
/// Tov and Kraft's), read into lines and laid on BHSA's and Brenton's words.
/// </summary>
public sealed class CatssTests : IDisposable
{
    private static readonly string[] Genesis =
    [
        "~a\"ParTx\"b\"HbGk\"c\"Gen\"x0",
        "~x",
        "B/R)$YT                       E)N A)RXH=|",
        "BR)                           E)POI/HSEN",
        ")LHYM                         O( QEO\\S",
        ")T H/$MYM                     TO\\N OU)RANO\\N",
        "W/)T H/)RC                    KAI\\ TH\\N GH=N",
        "~y",
        "W/H/)RC                       H( ^ DE\\ GH=",
        "HYTH                          H)=N",
        "THW                           A)O/RATOS",
        "W/BHW                         KAI\\ A)KATASKEU/ASTOS",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-catss-test", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void TheCitationsNumberChaptersAndVersesAsBhsDoes()
    {
        var verses = CatssReader.Read(Genesis);

        verses.Select(v => (v.Book, v.Chapter, v.Verse)).Should().Equal(("Gen", 1, 1), ("Gen", 1, 2));
        verses[0].Lines[0].Hebrew.Should().Equal("B/R)$YT");
        verses[0].Lines[0].Greek.Should().Equal("E)N", "A)RXH=|");
        verses[1].Lines[0].Greek.Should().Equal("H(", "DE\\", "GH=");
        verses[1].Lines[0].Marks.Should().Contain("^");
    }

    [Fact]
    public void ASingleChapterBookAndASetVerseAreCounted()
    {
        string? book = null;
        int chapter = 0, verse = 0;
        CatssReader.Cite("~c\"Obad\"y0", ref book, ref chapter, ref verse);
        CatssReader.Cite("~y", ref book, ref chapter, ref verse);
        (book, chapter, verse).Should().Be(("Obad", 1, 1));
        CatssReader.Cite("~y12", ref book, ref chapter, ref verse);
        verse.Should().Be(12);
        CatssReader.Cite("~x", ref book, ref chapter, ref verse);
        (chapter, verse).Should().Be((2, 1));
    }

    [Fact]
    public void ColumnBMinusesPlusesAndPlaceholdersAreToldApart()
    {
        var lines = CatssReader.Read(
        [
            "~c\"Gen\"x1",
            "^ L/MYN/W                     ^^^",
            ")$R                           OU(=",
            "^^^ ^ =L/MYN/W                KATA\\ GE/NOS {d} KAI\\ KAQ' O(MOIO/THTA",
            "ZR(                           {..^SPORI/MOU}",
            "{...}                         SPORI/MOU",
            "(DN                           ---",
            "--+ =;W/YHY                   KAI\\ E)GE/NETO",
            "*)HL/H **)HL/W                TH\\N SKHNH\\N AU)TOU=",
            ")XR =)XD .rd                  EI(=S",
            "L/W =L) .w) <sp>              MH/ [6]",
        ]).Single().Lines;

        lines[0].Should().Match<CatssLine>(l => l.Kind == CatssHebrew.Words && l.Greek.Count == 0 && l.Marks.Contains("^^^"));
        lines[2].Kind.Should().Be(CatssHebrew.Transposed);
        lines[2].Retroversion.Should().Be("=L/MYN/W");
        lines[2].Greek.Should().Equal("KATA\\", "GE/NOS", "KAI\\", "KAQ'", "O(MOIO/THTA");
        lines[3].References.Should().ContainSingle().Which.Should().Match<CatssReference>(r => r.Kind == '^' && r.Words.Single() == "SPORI/MOU");
        lines[4].Kind.Should().Be(CatssHebrew.Elsewhere);
        lines[5].Minus.Should().BeTrue();
        lines[6].Kind.Should().Be(CatssHebrew.Plus);
        lines[7].Hebrew.Should().Equal(")HL/H");
        lines[8].Retroversion.Should().Be("=)XD .rd");
        CatssLinks.Read(lines[8].Retroversion).Should().BeTrue();
        CatssLinks.Read("=%p+").Should().BeFalse();
        CatssLinks.Read("=@CPH .$c").Should().BeFalse();
        lines[9].Verses.Should().Equal("[6]");
        CatssLinks.Named("[6]", 23).Should().Be((23, 6));
        CatssLinks.Named("[38.16]", 37).Should().Be((38, 16));
    }

    [Fact]
    public void ALineRunningOverIsOneLine()
    {
        var lines = CatssReader.Read(
        [
            "~c\"Gen\"x5",
            "{...$TYM W/$L$YM} W/$B( #".PadRight(CatssReader.GreekColumn) + "O)KTAKO/SIA ^ DU/O",
            "# M)WT =+".PadRight(CatssReader.GreekColumn) + "#",
            "$NH".PadRight(CatssReader.GreekColumn) + "E)/TH",
        ]).Single().Lines;

        lines.Should().HaveCount(2);
        lines[0].Hebrew.Should().Equal("W/$B(", "M)WT");
        lines[0].Greek.Should().Equal("O)KTAKO/SIA", "DU/O");
    }

    [Fact]
    public void HebrewAndGreekAreTranscribedToTheLettersTheWitnessesPrint()
    {
        HebrewBetacode.Letters("W/H/)RC").Should().Be("והארצ");
        HebrewBetacode.Letters("&(R").Should().Be("שער");
        GreekBetacode.Bare("A)RXH=|").Should().Be("αρχη");
        GreekBetacode.Bare("QEO\\S").Should().Be("θεοσ");
        CatssLinks.Folded("εποιησεν").Should().Be(CatssLinks.Folded("εποιησε"));
    }

    [Fact]
    public void ALineIsLaidOnBhsaAndBrentonAndAWordBrentonSpellsOtherwiseTakesItsPlace()
    {
        var store = new ProductStore(_root);
        ProductCorpus.Text(store, "BHSA", "hbo",
        [
            ("1.1:1", ["בְּ", "רֵאשִׁ֖ית", "בָּרָ֣א", "אֱלֹהִ֑ים", "אֵ֥ת", "הַ", "שָּׁמַ֖יִם", "וְ", "אֵ֥ת", "הָ", "אָֽרֶץ"]),
            ("1.1:2", ["וְ", "הָ", "אָ֗רֶץ", "הָיְתָ֥ה", "תֹ֨הוּ֙", "וָ", "בֹ֔הוּ"]),
        ]);
        ProductCorpus.Text(store, "GRCBRENT", "grc",
        [
            ("1.1:1", ["Ἐν", "ἀρχῇ", "ἐποίησεν", "ὁ", "Θεὸς", "τὸν", "οὐρανὸν", "καὶ", "τὴν", "γῆν"]),
            ("1.1:2", ["ἡ", "δὲ", "γῆ", "ἦν", "κενή", "καὶ", "ἀκατασκεύαστος"]),
        ]);
        var texts = new ProductTexts(store);

        var (drafts, tallies) = CatssLinks.Draw(CatssReader.Read(Genesis), texts["BHSA"], texts["GRCBRENT"]);

        string Greek(CatssLinks.Draft d) => string.Join(' ', d.Greek.Select(id => texts["GRCBRENT"].Words[id].Surface));
        string Hebrew(CatssLinks.Draft d) => string.Join(' ', d.Hebrew.Select(id => texts["BHSA"].Words[id].Surface));
        drafts.Should().OnlyContain(d => d.Relation == LinkRelation.Renders);
        drafts.Select(d => (Greek(d), Hebrew(d))).Should().Contain(
        [
            ("Ἐν ἀρχῇ", "בְּ רֵאשִׁ֖ית"),
            ("τὸν οὐρανὸν", "אֵ֥ת הַ שָּׁמַ֖יִם"),
            ("ἡ δὲ γῆ", "וְ הָ אָ֗רֶץ"),
            ("κενή", "תֹ֨הוּ֙"),
            ("καὶ ἀκατασκεύαστος", "וָ בֹ֔הוּ"),
        ]);
        drafts.Single(d => Greek(d) == "κενή").Evidence.Should().Contain("spells otherwise");
        tallies.Single().GreekFilled.Should().Be(1);
    }
}
