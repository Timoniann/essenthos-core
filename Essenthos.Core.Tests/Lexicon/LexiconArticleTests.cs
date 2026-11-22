using System.Text.Json;
using System.Xml.Linq;
using Essenthos.Core.Build;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Lexicons;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// The scholarly lexicons beside Strong's, read into articles under the Strong numbers their sources file them at:
/// Brown-Driver-Briggs through the Open Scriptures lexical index, Abbott-Smith by its own keys, LSJ as STEP Bible keys it.
/// </summary>
public sealed class LexiconArticleTests
{
    private const string Ns = "http://openscriptures.github.com/morphhb/namespace";

    private static XDocument Bdb(string entries) =>
        XDocument.Parse($"<lexicon xmlns=\"{Ns}\"><part id=\"b\" xml:lang=\"heb\"><section id=\"b.cw\">{entries}</section></part></lexicon>",
            LoadOptions.PreserveWhitespace);

    private static XDocument Index(string entries) => XDocument.Parse($"<index xmlns=\"{Ns}\"><part xml:lang=\"heb\">{entries}</part></index>");

    [Theory]
    [InlineData("430", 'H', "H430")]
    [InlineData("H1254a", null, "H1254")]
    [InlineData("H0871", null, "H871")]
    [InlineData("G0001G", null, "G1")]
    [InlineData("G5624", null, "G5624")]
    [InlineData("G6000", null, null)]
    [InlineData("H9001", null, null)]
    [InlineData("b", 'H', null)]
    public void AnExtendedNumberFilesUnderTheNumberItExtends(string written, char? language, string? expected) =>
        LexiconText.Strong(written, language).Should().Be(expected);

    [Fact]
    public void ANumberTheIndexFilesAtTwoArticlesHasBothInBdbsOrder()
    {
        var lexicon = Bdb(
            """
            <entry id="b.cw.aa" type="root"><w>ברא</w> I. <def>shape</def>, <def>create</def>
              <sense><stem>Qal</stem> <asp>Pf</asp>. <w>בָּרָא</w> <ref r="Gen.1.1">Gn 1:1</ref>
                <sense n="1">of heaven and earth, v. <w src="b.cx.aa">ברא</w></sense>
              </sense>
              <page p="135"/><status p="135">done</status></entry>
            <entry id="b.cx.aa" cite="full"><w>ברא</w> II. <pos>vb</pos>. <def>be fat</def> <ref r="1Sam.2.29!a">1 S 2:29</ref><status p="135">base</status></entry>
            """);
        var index = Index(
            """
            <entry id="bxx"><w>בָּרָא</w><xref bdb="b.cx.aa" strong="1254" aug="b"/></entry>
            <entry id="bxy"><w>בָּרָא</w><xref bdb="b.cw.aa" strong="1254" aug="a"/></entry>
            <entry id="byy"><w>בְּרִיא</w><xref bdb="b.cx.aa" strong="1277"/></entry>
            <entry id="bzz"><w>ב</w><xref bdb="b.ab.aa" strong="b"/></entry>
            """);
        var unread = new Dictionary<string, int>();

        var rows = BdbReader.Read(lexicon, index, unread);

        unread.Should().BeEmpty();
        rows.Select(r => (r.StrongNumber, r.Ordinal, r.Entry, r.Status)).Should().Equal(
            ("H1254", 1, "b.cw.aa", "done"), ("H1254", 2, "b.cx.aa", "base"), ("H1277", 1, "b.cx.aa", "base"));
        rows[0].Headword.Should().Be("ברא");
        rows[0].Body.Should().Be(
            "[{\"p\":[{\"t\":\"ברא\",\"he\":1},\" I. \",{\"t\":\"shape\",\"b\":1},\", \",{\"t\":\"create\",\"b\":1}]}," +
            "{\"p\":[{\"t\":\"Qal\",\"b\":1},\" \",{\"t\":\"Pf\",\"i\":1},\". \",{\"t\":\"בָּרָא\",\"he\":1},\" \",{\"t\":\"Gn 1:1\",\"r\":[1,1,1]}],\"d\":1}," +
            "{\"p\":[{\"t\":\"1. \",\"b\":1},\"of heaven and earth, v. \",{\"t\":\"ברא\",\"he\":1}],\"d\":2}]");
        rows[1].Body.Should().Be(
            "[{\"p\":[\"† \",{\"t\":\"ברא\",\"he\":1},\" II. \",{\"t\":\"vb\",\"i\":1},\". \",{\"t\":\"be fat\",\"b\":1},\" \"," +
            "{\"t\":\"1 S 2:29\",\"r\":[9,2,29]}]}]");
        rows.Should().OnlyContain(r => r.Dictionary == "bdb" && r.Source.StartsWith(LexiconWorks.BdbSource));
    }

    [Fact]
    public void AWordNamingAnArticleOfOneNumberLinksThatNumber()
    {
        var lexicon = Bdb(
            """
            <entry id="b.cw.aa"><w>א</w> v. <w src="b.cx.aa">ב</w> and <w src="b.cy.aa">ג</w><status p="1">ref</status></entry>
            <entry id="b.cx.aa"><w>ב</w><status p="1">base</status></entry>
            <entry id="b.cy.aa"><w>ג</w><status p="1">base</status></entry>
            """);
        var index = Index(
            """
            <entry id="a"><xref bdb="b.cw.aa" strong="1"/></entry>
            <entry id="b"><xref bdb="b.cx.aa" strong="2"/></entry>
            <entry id="c"><xref bdb="b.cy.aa" strong="3"/></entry>
            <entry id="d"><xref bdb="b.cy.aa" strong="4"/></entry>
            """);

        var body = BdbReader.Read(lexicon, index).First(r => r.StrongNumber == "H1").Body;

        body.Should().Contain("{\"t\":\"ב\",\"he\":1,\"s\":\"H2\"}").And.Contain("{\"t\":\"ג\",\"he\":1}");
    }

    private const string Tei = "http://www.crosswire.org/2013/TEIOSIS/namespace";

    [Fact]
    public void AbbottSmithLinksTheHebrewTheSeptuagintRendersAndFilesUnderEveryNumberItNames()
    {
        var tei = XDocument.Parse(
            $"""
             <TEI xmlns="{Tei}"><text><body>
             <entry n="ἀγάπη|G26">
               <note type="occurrencesNT">116</note>
               <form>† <orth>ἀγάπη</orth>, <foreign xml:lang="grc">-ης, ἡ</foreign> </form>
               <etym><seg type="septuagint">[in LXX for <foreign xml:lang="heb" n="H160">אַהֲבָה</foreign>;]</seg></etym>
               <sense><gloss>love</gloss>, like <ref><foreign xml:lang="grc">ἀγαπάω</foreign></ref>,
                 <sense n="1.">to one another, <ref osisRef="John.13.35">Jo 13:35</ref>; <ref osisRef="LXX:Ps.21.2">Ps 21:2</ref></sense>
               </sense>
               <re><emph>SYN.</emph>: φιλία</re>
             </entry>
             <entry n="ἀγαπάω|G25"><form><orth>ἀγαπάω</orth></form></entry>
             <entry n="ἅγιος|G40|G39"><form><orth>ἅγιος, -α, -ον <note>Abbott-Smith does not include the article here.</note></orth>,</form> <sense><gloss>holy</gloss></sense></entry>
             <entry n="ἄγγος"><form><orth>ἄγγος</orth></form></entry>
             </body></text></TEI>
             """, LoadOptions.PreserveWhitespace);
        var unread = new Dictionary<string, int>();

        var rows = AbbottSmithReader.Read(tei, unread);

        unread.Should().BeEmpty();
        rows.Select(r => (r.StrongNumber, r.Headword)).Should().Equal(
            ("G25", "ἀγαπάω"), ("G26", "ἀγάπη"), ("G39", "ἅγιος, -α, -ον"), ("G40", "ἅγιος, -α, -ον"));
        rows.Single(r => r.StrongNumber == "G40").Body.Should().Contain("{\"t\":\"Abbott-Smith does not include the article here.\",\"b\":1,\"n\":1}");
        var love = rows.Single(r => r.StrongNumber == "G26").Body;
        love.Should().NotContain("116");
        love.Should().Contain("{\"t\":\"אַהֲבָה\",\"he\":1,\"s\":\"H160\"}");
        love.Should().Contain("{\"t\":\"ἀγαπάω\",\"el\":1,\"s\":\"G25\"}");
        love.Should().Contain("{\"t\":\"Jo 13:35\",\"r\":[43,13,35]}").And.Contain("; Ps 21:2\"");
        love.Should().Contain("{\"p\":[{\"t\":\"1. \",\"b\":1},\"to one another, \"");
        love.Should().EndWith("{\"p\":[{\"t\":\"SYN.\",\"i\":1},\": \",{\"t\":\"φιλία\",\"el\":1}]}]");
    }

    internal const string ThayerFile = """
        # Thayer's Lexicon — G1 to G224

        [← back to contents](README.md)

        ---

        ### ἀγάπη — agápē
        `Strong's G26`

        ἀγάπη, (ης, ἡ, a purely Biblical word (Plutarch, sympos. 7, 6, 3).

        1. affection, good-will, love: Joh 15:13; Rom 13:10; Isa 1:2.

        2. plural, ἀγαπαι, love-feasts: Jud 1:12.

        ---

        ### ἅλας — halas
        `Strong's G217`

        [ἅλα, τό, read by Tdf. in Mat 5:13; see ἅλας.]

        STRONGS NT 217: ἅλαςἅλας, (ατος, τό, salt: Mat 5:13; Mat 5:3-12; Mar 9:50.

        I. properly.

        A. in the New Testament

        1. of food: Luk 14:34.

        a. with the genitive: Col 4:6.

        b. so Heb 5:12 f.

        2. figuratively: Rev 21:1-22:2.

        B. in the Old Testament: Gen 1:1.

        STRONGS NT 217a: ἌλασσαἌλασσα: Act 27:8.

        ---

        ### αἴτιον — aítion
        `Strong's G158`

        - Original: αἴτιον

        - Definition:

        1. cause, fault

        - Part(s) of speech: Noun Neuter

        ---

        ### ἀγρός
        `Strong's G68`

        ἅπτω; 1 aorist participle ἅψας; to fasten: Luk 8:16.

        ---

        ### ἄγε
        `Strong's G34`

        ἄγε, see ἄγω.

        ---
        """;

    [Fact]
    public void ThayerGivesEachArticleItsNumberItsSensesAndItsNewTestamentVerses()
    {
        var left = new ThayerReader.Left();

        var rows = ThayerReader.Read([ThayerFile], left);

        left.Cards.Should().Equal("G158");
        left.Extended.Should().Equal("G217");
        left.Misfiled.Should().Equal("G68");
        rows.Select(r => (r.Dictionary, r.StrongNumber, r.Ordinal, r.Entry, r.Headword)).Should().Equal(
            ("thayer", "G26", 1, "G26", "ἀγάπη"), ("thayer", "G34", 1, "G34", "ἄγε"), ("thayer", "G217", 1, "G217", "ἅλας"));
        rows.Should().OnlyContain(r => r.Source.StartsWith(LexiconWorks.ThayerSource));
        rows[0].Body.Should().Be(
            "[{\"p\":[{\"t\":\"ἀγάπη\",\"el\":1},\", (\",{\"t\":\"ης, ἡ\",\"el\":1},\", a purely Biblical word (Plutarch, sympos. 7, 6, 3).\"]}," +
            "{\"p\":[{\"t\":\"1.\",\"b\":1},\" affection, good-will, love: \",{\"t\":\"Joh 15:13\",\"r\":[43,15,13]},\"; \"," +
            "{\"t\":\"Rom 13:10\",\"r\":[45,13,10]},\"; Isa 1:2.\"],\"d\":1}," +
            "{\"p\":[{\"t\":\"2.\",\"b\":1},\" plural, \",{\"t\":\"ἀγαπαι\",\"el\":1},\", love-feasts: \",{\"t\":\"Jud 1:12\",\"r\":[65,1,12]},\".\"],\"d\":1}]");
    }

    [Fact]
    public void ThayerDropsTheModulesLabelAndDoubledHeadwordAndNestsSensesByTheirKind()
    {
        var body = ThayerReader.Read([ThayerFile]).Single(r => r.StrongNumber == "G217").Body;
        var blocks = JsonDocument.Parse(body).RootElement.EnumerateArray().ToList();

        body.Should().NotContain("STRONGS").And.NotContain("ἅλαςἅλας");
        body.Should().Contain("ἅλα").And.Contain("salt: ").And.NotContain("Ἄλασσα").And.NotContain("Act 27:8");
        body.Should().Contain("{\"t\":\"Mat 5:3-12\",\"r\":[40,5,3,5,12]}").And.Contain("{\"t\":\"Rev 21:1-22:2\",\"r\":[66,21,1,22,2]}");
        body.Should().Contain("{\"t\":\"Heb 5:12\",\"r\":[58,5,12]}").And.Contain("Gen 1:1");
        body.Should().NotContain("[1,1,1]");
        // I. A. 1. a. nest one below the other; 2. comes back to the depth of 1.; B. to the depth of A.
        blocks.Select(b => (b.GetProperty("p")[0].ValueKind == JsonValueKind.Object ? b.GetProperty("p")[0].GetProperty("t").GetString() : null,
                b.TryGetProperty("d", out var depth) ? depth.GetInt32() : 0))
            .Skip(2).Should().Equal(("I.", 1), ("A.", 2), ("1.", 3), ("a.", 4), ("b.", 4), ("2.", 3), ("B.", 2));
    }

    private static string Row(string number, string disambiguated, string greek, string meaning) =>
        string.Join('\t', number, disambiguated + " =", disambiguated, greek, "x", "G:N", "gloss", meaning);

    [Fact]
    public void LsjKeepsEachArticleOnceAndLeavesOutWhatIsNotLsj()
    {
        var lines = new[]
        {
            "TFLSJ header line",
            Row("G0001", "G0001G", "ἄλφα", "<b> ἄλφα</b>, τό, <br /> <i>indeclinable</i>"),
            Row("G0001", "G0001H", "ἆ", "<b> ἔᾱ</b>, see at {ἄγνυμι}"),
            Row("G0026", "G0026G", "ἀγάπη",
                "<b> ἀγάπ-η</b>, ἡ, <br /> <b>love,</b> [<a href=\"javascript:void(0)\" title=\" LXX.Jer.2.2, cf. NT.Rom.5.8, NT.Jude.12. \">LXX+NT</a>]<br /><Level2><b>__II</b></Level2> in <i>plural</i>"),
            Row("G0026", "G0026H", "ἀγάπη",
                "<b> ἀγάπ-η</b>, ἡ, <br /> <b>love,</b> [<a href=\"javascript:void(0)\" title=\" LXX.Jer.2.2, cf. NT.Rom.5.8, NT.Jude.12. \">LXX+NT</a>]<br /><Level2><b>__II</b></Level2> in <i>plural</i>"),
            Row("G0005", "G0005", "ἀββά", "Transliteration of Hebrew אַב 'father' [H0002]. Not in LSJ."),
            Row("G0008", "G0008", "Ἀβιαθάρ", " <b>Ἀβιάθαρ</b>, ὁ.†<BR />  (From Abbott-Smith. LSJ has no entry)"),
            Row("G0011", "G0011", "Ἀβραάμ", "Abraham, father of [H0002]"),
        };
        var unread = new Dictionary<string, int>();

        var rows = LsjReader.Read(lines, unread);

        unread.Should().BeEmpty();
        rows.Select(r => (r.StrongNumber, r.Ordinal, r.Entry, r.Headword)).Should().Equal(
            ("G1", 1, "G0001G", "ἄλφα"), ("G1", 2, "G0001H", "ἆ"), ("G11", 1, "G0011", "Ἀβραάμ"), ("G26", 1, "G0026G", "ἀγάπη"));
        rows[1].Body.Should().Be("[{\"p\":[{\"t\":\"ἔᾱ\",\"b\":1,\"el\":1},\", see at \",{\"t\":\"ἄγνυμι\",\"el\":1}]}]");
        rows[3].Body.Should().Be(
            "[{\"p\":[{\"t\":\"ἀγάπ-η\",\"b\":1,\"el\":1},\", \",{\"t\":\"ἡ\",\"el\":1},\",\"]}," +
            "{\"p\":[{\"t\":\"love,\",\"b\":1},\" [LXX+NT\",{\"t\":\" LXX.Jer.2.2, cf. \",\"x\":1},{\"t\":\"NT.Rom.5.8\",\"x\":1,\"r\":[45,5,8]}," +
            "{\"t\":\", \",\"x\":1},{\"t\":\"NT.Jude.12\",\"x\":1,\"r\":[65,1,12]},{\"t\":\".\",\"x\":1},\"]\"]}," +
            "{\"p\":[{\"t\":\"II\",\"b\":1},\" in \",{\"t\":\"plural\",\"i\":1}],\"d\":1}]");
        rows[2].Body.Should().Contain("{\"t\":\"H2\",\"s\":\"H2\"}");
    }

    [Fact]
    public void GreekAndHebrewRunsKeepTheSpacesBetweenTheirWords()
    {
        var writer = new ArticleWriter();
        writer.Text("so Ar. עֹדֶנּוּ בְאִבּוֹ and λόγος ἀκοῆς, Jb 8:12");

        writer.Json().Should().Be(
            "[{\"p\":[\"so Ar. \",{\"t\":\"עֹדֶנּוּ בְאִבּוֹ\",\"he\":1},\" and \",{\"t\":\"λόγος ἀκοῆς\",\"el\":1},\", Jb 8:12\"]}]");
    }
}

/// <summary>The three lexicons as they lie on disk: what each gives the numbers the Strong page is checked on.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class LexiconArticleCorpusTests
{
    private static string Resources => Path.GetDirectoryName(TestResources.Path("MANIFEST.json"))!;

    [Fact]
    public void EveryLexiconReadsWholeAndFilesTheCheckedNumbers()
    {
        var unread = new Dictionary<string, int>();
        var left = new ThayerReader.Left();
        var bdb = BdbReader.Read(Resources, unread);
        var thayer = ThayerReader.Read(Resources, left);
        var abbott = AbbottSmithReader.Read(Resources, unread);
        var lsj = LsjReader.Read(Resources, unread);

        unread.Should().BeEmpty();
        bdb.Where(r => r.StrongNumber == "H430").Should().ContainSingle().Which.Headword.Should().Be("אֱלֹהִים");
        bdb.Where(r => r.StrongNumber == "H1254").Should().HaveCount(2);
        bdb.Should().Contain(r => r.StrongNumber == "H7965");
        foreach (var number in new[] { "G26", "G3056", "G5485" })
        {
            thayer.Should().Contain(r => r.StrongNumber == number);
            abbott.Should().Contain(r => r.StrongNumber == number);
            lsj.Should().Contain(r => r.StrongNumber == number);
        }

        thayer.Should().HaveCount(5521 - 97 - 10);
        left.Cards.Should().HaveCount(97).And.Contain("G149");
        left.Misfiled.Should().BeEquivalentTo(ThayerReader.Misfiled);
        thayer.Should().OnlyContain(r => !r.Body.Contains("STRONGS NT"));
        foreach (var row in bdb.Concat(thayer).Concat(abbott).Concat(lsj))
        {
            JsonDocument.Parse(row.Body).RootElement.GetArrayLength().Should().BePositive();
        }

        bdb.Concat(thayer).Concat(abbott).Concat(lsj).GroupBy(r => (r.Dictionary, r.StrongNumber, r.Ordinal)).Should().OnlyContain(g => g.Count() == 1);
    }
}

[Collection(WitnessDatabaseCollection.Name)]
public sealed class LexiconEndpointTests : IDisposable
{
    private readonly AppDbContext _db;

    public LexiconEndpointTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.LexiconArticles.ExecuteDelete();
        _db.LexiconArticles.AddRange(
            Article("lsj", "G26", 1, "LSJ on love"),
            Article("thayer", "G26", 1, "Thayer on love"),
            Article("abbott-smith", "G26", 1, "Abbott-Smith on love"),
            Article("bdb", "H1254", 2, "be fat"),
            Article("bdb", "H1254", 1, "shape"));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.LexiconArticles.ExecuteDelete();
        _db.Dispose();
    }

    [Fact]
    public async Task ANumberIsGivenItsArticlesLexiconByLexiconInEachLexiconsOrder()
    {
        var greek = await LexiconEndpoints.Articles(_db, "G26", default);

        greek.Dictionaries.Select(d => d.Id).Should().Equal("thayer", "abbott-smith", "lsj");
        greek.Dictionaries[2].Licence.Should().Be("CC BY-SA 4.0");
        greek.Articles.Select(a => a.Body.GetRawText()).Should().Equal("[{\"p\":[\"Thayer on love\"]}]", "[{\"p\":[\"Abbott-Smith on love\"]}]", "[{\"p\":[\"LSJ on love\"]}]");

        var hebrew = await LexiconEndpoints.Articles(_db, "H1254", default);
        hebrew.Articles.Select(a => (a.Ordinal, a.Status)).Should().Equal((1, "base"), (2, "base"));
        (await LexiconEndpoints.Articles(_db, "H1", default)).Articles.Should().BeEmpty();
    }

    private static LexiconArticle Article(string dictionary, string number, int ordinal, string text) =>
        new()
        {
            Dictionary = dictionary,
            StrongNumber = number,
            Ordinal = ordinal,
            Entry = $"{dictionary}-{ordinal}",
            Headword = "x",
            Status = dictionary == "bdb" ? "base" : null,
            Body = $"[{{\"p\":[\"{text}\"]}}]",
            Source = LexiconWorks.Find(dictionary)!.Source,
        };
}

/// <summary>The articles go from the product the tables producer writes into <c>lexicon_article</c> as they were read.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LexiconArticleTableTests(WitnessDatabase database) : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-lexicons", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    [Fact]
    public async Task TheTableHoldsEveryArticleOfTheProduct()
    {
        Write(BdbReader.Lexicon,
            "<lexicon xmlns=\"http://openscriptures.github.com/morphhb/namespace\"><part><section>" +
            "<entry id=\"a.dl.ad\"><w>אֱלֹהִים</w> <def>gods</def><status p=\"43\">ref</status></entry></section></part></lexicon>");
        Write(BdbReader.Index,
            "<index xmlns=\"http://openscriptures.github.com/morphhb/namespace\"><part><entry id=\"arn\"><xref bdb=\"a.dl.ad\" strong=\"430\"/></entry></part></index>");
        Write($"{ThayerReader.Book}/01-G1-G224.md", LexiconArticleTests.ThayerFile);
        Write(AbbottSmithReader.Lexicon,
            "<TEI xmlns=\"http://www.crosswire.org/2013/TEIOSIS/namespace\"><text><body><entry n=\"ἀγάπη|G26\"><form><orth>ἀγάπη</orth></form> " +
            "<sense><gloss>love</gloss></sense></entry></body></text></TEI>");
        Write(LsjReader.Lexicon, "header\n" + string.Join('\t', "G0026", "G0026 =", "G0026", "ἀγάπη", "agapē", "G:N-F", "love", "<b> ἀγάπη</b>, love") + "\n");

        var store = new Essenthos.Core.Products.ProductStore(_resources);
        TableProducts.Write(store, new HashSet<string> { "lexicon_article" }, force: false).Single().Rows.Should().Be(6);
        await using var db = database.NewContext();
        await db.LexiconArticles.ExecuteDeleteAsync();

        var loaded = await CarriedTables.Load(database.NewConnection().ConnectionString, store, new HashSet<string> { "lexicon_article" }, default);

        loaded.Single().Loaded.Should().Be(6);
        var rows = await db.LexiconArticles.OrderBy(a => a.Id).ToListAsync();
        rows.Select(a => (a.Dictionary, a.StrongNumber, a.Ordinal, a.Headword, a.Status)).Should().Equal(
            ("bdb", "H430", 1, "אֱלֹהִים", "ref"), ("thayer", "G26", 1, "ἀγάπη", null), ("thayer", "G34", 1, "ἄγε", null),
            ("thayer", "G217", 1, "ἅλας", null), ("abbott-smith", "G26", 1, "ἀγάπη", null), ("lsj", "G26", 1, "ἀγάπη", null));
        rows[0].Body.Should().Be("[{\"p\":[{\"t\":\"אֱלֹהִים\",\"he\":1},\" \",{\"t\":\"gods\",\"b\":1}]}]");
        await db.LexiconArticles.ExecuteDeleteAsync();
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_resources, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
