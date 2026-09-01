using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Tests for <see cref="StrongXmlParser"/>. Covers Greek and Hebrew XML parsing,
/// ensuring all fields (including cross-references, derivation with inline refs, etc.)
/// are correctly extracted.
/// </summary>
public class StrongXmlParserTests
{
    private readonly StrongXmlParser _parser = new();

    // ────────────────────────────────────────────────────────────────
    //  Greek tests
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseGreek_BasicEntry_ExtractsAllFields()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="00018">
                   <strongs>18</strongs>
                   <greek BETA="A)GAQO/S" unicode="ἀγαθός" translit="agathós"/>
                   <pronunciation strongs="ag-ath-os'"/>
                   <strongs_derivation>a primary word;</strongs_derivation>
                   <strongs_def> "good" (in any sense, often as noun)</strongs_def>
                   <kjv_def>:--benefit, good(-s, things), well.</kjv_def>
                   <see language="GREEK" strongs="2570"/>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);

        entries.Should().HaveCount(1);
        var e = entries[0];
        e.StrongNumber.Should().Be("G18");
        e.Lemma.Should().Be("ἀγαθός");
        e.Transliteration.Should().Be("agathós");
        e.Pronunciation.Should().Be("ag-ath-os'");
        e.Definition.Should().Contain("good");
        e.Derivation.Should().Be("a primary word;");
        e.KjvDefinition.Should().Contain("benefit");
        e.SeeAlso.Should().Be("G2570");
    }

    [Fact]
    public void ParseGreek_DerivationWithStrongsRef_PreservesReference()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="05624">
                   <strongs>5624</strongs>
                   <greek BETA="W)FE/LIMOS" unicode="ὠφέλιμος" translit="ōphélimos"/>
                   <pronunciation strongs="o-fel'-ee-mos"/>
                   <strongs_derivation>from a form of <strongsref language="GREEK" strongs="3786"/>;</strongs_derivation>
                   <strongs_def> helpful or serviceable, i.e. advantageous</strongs_def>
                   <kjv_def>:--profit(-able).</kjv_def>
                   <see language="GREEK" strongs="3786"/>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);

        entries.Should().HaveCount(1);
        var e = entries[0];
        e.StrongNumber.Should().Be("G5624");
        e.Derivation.Should().Be("from a form of G3786;");
        e.SeeAlso.Should().Be("G3786");
    }

    [Fact]
    public void ParseGreek_DerivationWithMultipleStrongsRefs_PreservesAll()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="00500">
                   <strongs>500</strongs>
                   <greek BETA="A)NTI/XRISTOS" unicode="ἀντίχριστος" translit="antíchristos"/>
                   <pronunciation strongs="an-tee'-khris-tos"/>
                   <strongs_derivation>from <strongsref language="GREEK" strongs="473"/> and <strongsref language="GREEK" strongs="5547"/>;</strongs_derivation>
                   <strongs_def> an opponent of the Messiah</strongs_def>
                   <kjv_def>:--antichrist.</kjv_def>
                   <see language="GREEK" strongs="473"/>
                   <see language="GREEK" strongs="5547"/>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);

        var e = entries[0];
        e.Derivation.Should().Be("from G473 and G5547;");
        e.SeeAlso.Should().Be("G473,G5547");
    }

    [Fact]
    public void ParseGreek_DerivationWithGreekInline_PreservesUnicode()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="00100">
                   <strongs>100</strongs>
                   <greek BETA="A(DRO/THS" unicode="ἁδρότης" translit="hadrótēs"/>
                   <pronunciation strongs="had-rot'-ace"/>
                   <strongs_derivation>from <greek BETA="A(DRO/S" unicode="ἁδρός" translit="hadrós"/> (stout);</strongs_derivation>
                   <strongs_def> plumpness, i.e. (figuratively) liberality</strongs_def>
                   <kjv_def>:--abundance.</kjv_def>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);

        var e = entries[0];
        e.Derivation.Should().Be("from ἁδρός (stout);");
        e.SeeAlso.Should().BeNull();
    }

    [Fact]
    public void ParseGreek_SeeWithHebrewRef_PrefixesWithH()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="00002">
                   <strongs>2</strongs>
                   <greek BETA="*AARW/N" unicode="Ἀαρών" translit="Aarṓn"/>
                   <pronunciation strongs="ah-ar-ohn'"/>
                   <strongs_derivation>of Hebrew origin (<strongsref language="HEBREW" strongs="175"/>);</strongs_derivation>
                   <strongs_def> Aaron, the brother of Moses</strongs_def>
                   <kjv_def>:--Aaron.</kjv_def>
                   <see language="HEBREW" strongs="0175"/>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);

        var e = entries[0];
        e.Derivation.Should().Be("of Hebrew origin (H175);");
        e.SeeAlso.Should().Be("H175");
    }

    [Fact]
    public void ParseGreek_StrongNumberParsesLeadingZeros()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="00001">
                   <strongs>1</strongs>
                   <greek BETA="*A" unicode="Α" translit="A"/>
                   <pronunciation strongs="al'-fah"/>
                   <strongs_derivation>of Hebrew origin;</strongs_derivation>
                   <strongs_def> the first letter of the alphabet</strongs_def>
                   <kjv_def>--Alpha.</kjv_def>
                   <see language="GREEK" strongs="427"/>
                   <see language="GREEK" strongs="260"/>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);

        entries[0].StrongNumber.Should().Be("G1");
        entries[0].SeeAlso.Should().Be("G427,G260");
    }

    [Fact]
    public void ParseGreek_MultipleEntries_ParsesAll()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="00001">
                   <strongs>1</strongs>
                   <greek BETA="*A" unicode="Α" translit="A"/>
                   <pronunciation strongs="al'-fah"/>
                   <strongs_def> first</strongs_def>
                  </entry>
                  <entry strongs="00002">
                   <strongs>2</strongs>
                   <greek BETA="*AARW/N" unicode="Ἀαρών" translit="Aarṓn"/>
                   <pronunciation strongs="ah-ar-ohn'"/>
                   <strongs_def> Aaron</strongs_def>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);

        entries.Should().HaveCount(2);
        entries[0].StrongNumber.Should().Be("G1");
        entries[1].StrongNumber.Should().Be("G2");
    }

    // ────────────────────────────────────────────────────────────────
    //  Hebrew tests
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseHebrew_BasicEntry_ExtractsAllFields()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
                  <osisText>
                  <div type="glossary">
                  <div type="entry" n="1">
                    <w gloss="4a" lemma="אָב" morph="n-m" POS="awb" xlit="ʼâb" ID="H1" xml:lang="heb">אב</w>
                    <foreign xml:lang="grc">
                      <w gloss="G:1118" />
                      <w gloss="G:3962" />
                    </foreign>
                    <list>
                      <item>1) father of an individual</item>
                      <item>2) of God as father of his people</item>
                    </list>
                    <note type="exegesis">a primitive word;</note>
                    <note type="explanation"><hi>father</hi>, in a literal and immediate application</note>
                    <note type="translation">chief, (fore-) father(-less), patrimony.</note>
                  </div>
                  </div>
                  </osisText>
                  </osis>
                  """;

        var entries = _parser.ParseHebrew(xml);

        entries.Should().HaveCount(1);
        var e = entries[0];
        e.StrongNumber.Should().Be("H1");
        e.Lemma.Should().Be("אָב");
        e.Transliteration.Should().Be("ʼâb");
        e.Pronunciation.Should().Be("awb");
        e.Morphology.Should().Be("n-m");
        e.SourceLanguage.Should().Be("heb");
        e.TwotReference.Should().Be("4a");
        e.Definition.Should().Contain("father");
        e.Derivation.Should().Be("a primitive word;");
        e.KjvDefinition.Should().Contain("chief");
        e.DetailedDefinition.Should().Contain("father of an individual");
        e.DetailedDefinition.Should().Contain("\n");
        e.SeeAlso.Should().Be("G1118,G3962");
    }

    [Fact]
    public void ParseHebrew_ExegesisWithWordRefs_PreservesStrongReferences()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
                  <osisText>
                  <div type="glossary">
                  <div type="entry" n="3">
                    <w gloss="1a" lemma="אֵב" morph="n-m" POS="abe" xlit="ʼêb" ID="H3" xml:lang="heb">אב</w>
                    <foreign xml:lang="grc">
                      <w gloss="G:1080" />
                    </foreign>
                    <list>
                      <item>1) freshness, green shoots</item>
                    </list>
                    <note type="exegesis">from the same as <w lemma="אָבִיב" POS="aw-beeb'" src="24" xlit="ʼâbîyb"/>;</note>
                    <note type="explanation"><hi>a green plant</hi></note>
                    <note type="translation">greenness, fruit.</note>
                  </div>
                  </div>
                  </osisText>
                  </osis>
                  """;

        var entries = _parser.ParseHebrew(xml);

        var e = entries[0];
        e.StrongNumber.Should().Be("H3");
        e.Derivation.Should().Be("from the same as אָבִיב (H24);");
        e.SeeAlso.Should().Be("G1080");
    }

    [Fact]
    public void ParseHebrew_ExegesisWithMultipleWordRefs_PreservesAll()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
                  <osisText>
                  <div type="glossary">
                  <div type="entry" n="500">
                    <w lemma="אֶלְעָלֵא" morph="n-pr-loc" POS="el-aw-lay'" xlit="ʼElʻâlêʼ" ID="H500" xml:lang="x-pn">אלעלא</w>
                    <list>
                      <item>Elealeh = "God is ascending"</item>
                    </list>
                    <note type="exegesis">from <w lemma="אֵל" POS="ale" src="410" xlit="ʼêl"/> and <w lemma="עָלָה" POS="aw-law'" src="5927" xlit="ʻâlâh"/>; God (is) going up;</note>
                    <note type="explanation"><hi>Elale</hi> or <hi>Elaleh</hi>, a place east of the Jordan</note>
                    <note type="translation">Elealeh.</note>
                  </div>
                  </div>
                  </osisText>
                  </osis>
                  """;

        var entries = _parser.ParseHebrew(xml);

        var e = entries[0];
        e.StrongNumber.Should().Be("H500");
        e.SourceLanguage.Should().Be("x-pn");
        e.Derivation.Should().Contain("אֵל (H410)");
        e.Derivation.Should().Contain("עָלָה (H5927)");
        e.TwotReference.Should().BeNull(); // no gloss on proper name entries
    }

    [Fact]
    public void ParseHebrew_AramaicEntry_SetsSourceLanguage()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
                  <osisText>
                  <div type="glossary">
                  <div type="entry" n="1001">
                    <w gloss="2628" lemma="בִּירָא" morph="n-f" POS="bee-raw'" xlit="bîyrâʼ" ID="H1001" xml:lang="arc">בירא</w>
                    <list>
                      <item>1) castle, citadel, palace</item>
                    </list>
                    <note type="exegesis">(Aramaic) corresponding to <w lemma="בִּירָה" POS="bee-raw'" src="1002" xlit="bîyrâh"/>;</note>
                    <note type="explanation"><hi>a palace</hi></note>
                    <note type="translation">palace.</note>
                  </div>
                  </div>
                  </osisText>
                  </osis>
                  """;

        var entries = _parser.ParseHebrew(xml);

        var e = entries[0];
        e.StrongNumber.Should().Be("H1001");
        e.SourceLanguage.Should().Be("arc");
        e.TwotReference.Should().Be("2628");
        e.Derivation.Should().Contain("בִּירָה (H1002)");
    }

    [Fact]
    public void ParseHebrew_NoForeignBlock_SeeAlsoIsNull()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
                  <osisText>
                  <div type="glossary">
                  <div type="entry" n="5000">
                    <w gloss="1271a" lemma="נָאוֶה" morph="a" POS="naw-veh'" xlit="nâʼveh" ID="H5000" xml:lang="heb">נאוה</w>
                    <list>
                      <item>1) comely, beautiful, seemly</item>
                    </list>
                    <note type="exegesis">from <w lemma="נָאָה" POS="naw-aw'" src="4998" xlit="nâʼâh"/> or <w lemma="נָוֶה" POS="naw-veh'" src="5116" xlit="nâveh"/>;</note>
                    <note type="explanation"><hi>suitable</hi>, or <hi>beautiful</hi></note>
                    <note type="translation">becometh, comely, seemly.</note>
                  </div>
                  </div>
                  </osisText>
                  </osis>
                  """;

        var entries = _parser.ParseHebrew(xml);

        var e = entries[0];
        e.SeeAlso.Should().BeNull();
        e.Derivation.Should().Contain("נָאָה (H4998)");
        e.Derivation.Should().Contain("נָוֶה (H5116)");
    }

    [Fact]
    public void ParseHebrew_NoListItems_DetailedDefinitionIsNull()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
                  <osisText>
                  <div type="glossary">
                  <div type="entry" n="99">
                    <w gloss="10" lemma="אֲגָם" morph="n-m" POS="ag-am'" xlit="ʼăgam" ID="H99" xml:lang="heb">אגם</w>
                    <note type="exegesis">a primitive root;</note>
                    <note type="explanation"><hi>a marsh</hi></note>
                    <note type="translation">pool, standing water.</note>
                  </div>
                  </div>
                  </osisText>
                  </osis>
                  """;

        var entries = _parser.ParseHebrew(xml);

        entries[0].DetailedDefinition.Should().BeNull();
    }

    [Fact]
    public void ParseHebrew_DefinitionWithHiElements_ExtractsText()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
                  <osisText>
                  <div type="glossary">
                  <div type="entry" n="50">
                    <w gloss="5" lemma="אָבָה" morph="v" POS="aw-baw'" xlit="ʼâbâh" ID="H50" xml:lang="heb">אבה</w>
                    <note type="explanation"><hi>suitable</hi>, or <hi>beautiful</hi></note>
                  </div>
                  </div>
                  </osisText>
                  </osis>
                  """;

        var entries = _parser.ParseHebrew(xml);

        entries[0].Definition.Should().Be("suitable, or beautiful");
    }

    [Fact]
    public void ParseHebrew_MultipleEntries_ParsesAll()
    {
        var xml = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <osis xmlns="http://www.bibletechnologies.net/2003/OSIS/namespace">
                  <osisText>
                  <div type="glossary">
                  <div type="entry" n="1">
                    <w lemma="אָב" morph="n-m" POS="awb" xlit="ʼâb" ID="H1" xml:lang="heb">אב</w>
                    <note type="explanation"><hi>father</hi></note>
                  </div>
                  <div type="entry" n="2">
                    <w lemma="אַב" morph="n-m" POS="ab" xlit="ʼab" ID="H2" xml:lang="arc">אב</w>
                    <note type="explanation"><hi>father</hi></note>
                  </div>
                  </div>
                  </osisText>
                  </osis>
                  """;

        var entries = _parser.ParseHebrew(xml);

        entries.Should().HaveCount(2);
        entries[0].StrongNumber.Should().Be("H1");
        entries[0].SourceLanguage.Should().Be("heb");
        entries[1].StrongNumber.Should().Be("H2");
        entries[1].SourceLanguage.Should().Be("arc");
    }

    // ────────────────────────────────────────────────────────────────
    //  Edge cases
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseGreek_EmptyEntries_ReturnsEmptyList()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries></entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);
        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseGreek_EntryWithNoSeeElements_SeeAlsoIsNull()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="00100">
                   <strongs>100</strongs>
                   <greek BETA="A(DRO/THS" unicode="ἁδρότης" translit="hadrótēs"/>
                   <pronunciation strongs="had-rot'-ace"/>
                   <strongs_derivation>from ἁδρός (stout);</strongs_derivation>
                   <strongs_def> plumpness</strongs_def>
                   <kjv_def>:--abundance.</kjv_def>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);
        entries[0].SeeAlso.Should().BeNull();
    }

    [Fact]
    public void ParseGreek_CleanTextCollapsesWhitespace()
    {
        var xml = """
                  <?xml version='1.0' encoding='utf-8' standalone='yes'?>
                  <strongsdictionary>
                  <prologue>test</prologue>
                  <entries>
                  <entry strongs="00018">
                   <strongs>18</strongs>
                   <greek BETA="A)GAQO/S" unicode="ἀγαθός" translit="agathós"/>
                   <pronunciation strongs="ag-ath-os'"/>
                   <strongs_def> "good"  (in any sense,
                   often as noun)</strongs_def>
                  </entry>
                  </entries>
                  </strongsdictionary>
                  """;

        var entries = _parser.ParseGreek(xml);
        // Multiline and extra whitespace should be collapsed
        entries[0].Definition.Should().NotContain("\n");
        entries[0].Definition.Should().NotContain("  ");
    }
}

