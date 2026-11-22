using System.Text.Json;
using Essenthos.Core.Conversion;
using Essenthos.Core.Macula;
using Essenthos.Core.Products;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// MACULA Hebrew's morphs placed on BHSA's words by their letters, and what a word is given from them: a parsing, and the
/// words its pronoun, or its pronoun suffix, refers back to.
/// </summary>
public sealed class MaculaHebrewTests(ITestOutputHelper output)
{
    private const string Header =
        "xml:id\tref\tclass\ttext\ttransliteration\tlang\tlemma\tpos\tperson\tgender\tnumber\ttype\tsdbh\tparticipantref\n";

    /// <summary>
    /// Genesis 2:2 cut down: God (אֱלֹהִים) ... his work (מְלַאכְתּוֹ, the suffix referring to God) ... and he (וְ + הוּא, the
    /// pronoun referring to the suffix, so to God).
    /// </summary>
    private static readonly string Genesis = Header
        + Row("o010020020011", "GEN 2:2!1", "noun", "אֱלֹהִ֜ים", "noun", "", "", "", "common", "")
        + Row("o010020020021", "GEN 2:2!2", "noun", "מְלַאכְתּ", "noun", "", "feminine", "singular", "common", "")
        + Row("o010020020022", "GEN 2:2!2", "pron", "וֹ", "suffix", "third", "masculine", "singular", "pronominal", "010020020011")
        + Row("o010020020031", "GEN 2:2!3", "cj", "וְ", "conjunction", "", "", "", "", "")
        + Row("o010020020032", "GEN 2:2!3", "pron", "ה֖וּא", "pronoun", "third", "masculine", "singular", "personal", "010020020022");

    private static string Row(string id, string reference, string kind, string text, string pos, string person, string gender, string number,
        string type, string refers) =>
        $"{id}\t{reference}\t{kind}\t{text}\tx\tH\t{text}\t{pos}\t{person}\t{gender}\t{number}\t{type}\tSDBH\t{refers}\n";

    private static readonly MaculaHebrewJoin.Word[] Bhsa =
    [
        new(11, 1, 2, 2, 1, "אֱלֹהִ֜ים"),
        new(12, 1, 2, 2, 2, "מְלַאכְתּ֖וֹ"),
        new(13, 1, 2, 2, 3, "וְ"),
        new(14, 1, 2, 2, 4, "ה֖וּא"),
    ];

    private static List<MaculaMorph> Read(string table) => MaculaHebrewReader.Read(table.Split('\n'));

    [Fact]
    public void ReadsAMorphsAddressFeaturesAndReferences()
    {
        var morphs = Read(Genesis);

        morphs.Should().HaveCount(5);
        morphs[2].Should().Match<MaculaMorph>(m => m.Book == 1 && m.Chapter == 2 && m.Verse == 2 && m.Word == 2 && m.IsSuffix);
        morphs[2].Agreement.Should().Be("3ms");
        morphs[2].Referents.Should().Equal("o010020020011");
        morphs[4].IsPronoun.Should().BeTrue();
        morphs[0].Referents.Should().BeEmpty();
    }

    [Fact]
    public void AMorphIsTheBhsaWordWhoseLettersHoldItsLetters()
    {
        var (placed, differing) = MaculaHebrewJoin.Place(Read(Genesis), Bhsa);

        placed.Should().Equal(11, 12, 12, 13, 14);
        differing.Should().BeEmpty();
    }

    [Fact]
    public void AVerseWhoseLettersDifferIsAlignedLetterByLetter()
    {
        // The ketiv writes his tent with ה, the qere with ו: the suffix matches no letter and goes with its host.
        var table = Header
            + Row("o010090210011", "GEN 9:21!1", "noun", "אָהֳל", "noun", "", "masculine", "singular", "common", "")
            + Row("o010090210012", "GEN 9:21!1", "pron", "וֹ", "suffix", "third", "masculine", "singular", "pronominal", "")
            + Row("o010090210021", "GEN 9:21!2", "noun", "נֹחַ", "noun", "", "masculine", "singular", "proper", "")
            + Row("o010090210031", "GEN 9:21!3", "noun", "הַמִּשְׁפָּחוֹת", "noun", "", "feminine", "plural", "common", "")
            + Row("o010090210041", "GEN 9:21!4", "verb", "וַיִּשְׁתַּחֲווּ", "verb", "", "", "", "", "");
        var (placed, differing) = MaculaHebrewJoin.Place(Read(table),
            [new(1, 1, 9, 21, 1, "אָהֳלֹ֑ה"), new(2, 1, 9, 21, 2, "נֹחַ"), new(3, 1, 9, 21, 3, "הַמִּשְׁפָּחוֹת"), new(4, 1, 9, 21, 4, "וַיִּשְׁתַּחֲווּ")]);

        differing.Should().ContainSingle();
        placed.Should().Equal(1, 1, 2, 3, 4);
    }

    [Fact]
    public void AMorphWhoseLettersFallInTwoWordsIsRefused()
    {
        var table = Header + Row("o010010010011", "GEN 1:1!1", "noun", "לָזֶה", "noun", "", "", "", "common", "");
        var (placed, _) = MaculaHebrewJoin.Place(Read(table), [new(1, 1, 1, 1, 1, "לָ"), new(2, 1, 1, 1, 2, "זֶה")]);

        placed.Should().Equal(-1);
    }

    [Fact]
    public void ASuffixRefersOnItsHostWhichSaysSoAndAChainIsFollowedInMaculasMorphs()
    {
        var (parsings, referents) = MaculaHebrewAnnotation.Of(Read(Genesis), Bhsa);

        referents.Should().Equal((12L, 11L), (14L, 11L));
        var host = JsonDocument.Parse(parsings.Single(p => p.Word == 12).Morphology).RootElement;
        host.GetProperty("class").GetString().Should().Be("noun");
        host.GetProperty("suffix").GetString().Should().Be("3ms");
        host.GetProperty("refers").GetString().Should().Be("suffix");
        var pronoun = JsonDocument.Parse(parsings.Single(p => p.Word == 14).Morphology).RootElement;
        pronoun.TryGetProperty("refers", out _).Should().BeFalse("הוּא is a pronoun as a whole");
        pronoun.GetProperty("pos").GetString().Should().Be("pronoun");
    }

    [Fact]
    public void AReferenceEndingAtAPronounReferringToNothingGivesNoRow()
    {
        var table = Genesis.Replace("\tpronominal\tSDBH\t010020020011\n", "\tpronominal\tSDBH\t\n");
        MaculaHebrewAnnotation.Of(Read(table), Bhsa).Referents.Should().BeEmpty();
    }

    [Fact]
    public void AReferenceToNoMorphOfTheTableGivesNoRow()
    {
        var table = Genesis.Replace("\t010020020011\n", "\t010020029999\n");
        MaculaHebrewAnnotation.Of(Read(table), Bhsa).Referents.Should().BeEmpty("which morph was meant is not guessed");
    }

    /// <summary>
    /// Ruth 1:5 cut down: Naomi (נָעֳמִי) ... the woman (הָאִשָּׁה, which MACULA refers on to Naomi) ... she (הִיא, referring to
    /// the woman) ... she returned (תָּשָׁב, its subject the woman) ... a man (אִישׁ, referring nowhere) ... he (הוּא).
    /// </summary>
    private static readonly string Ruth =
        "xml:id\tref\tclass\ttext\ttransliteration\tlang\tlemma\tpos\tperson\tgender\tnumber\ttype\tsdbh\tsubjref\tparticipantref\n"
        + "o080010050011\tRUT 1:5!1\tnoun\tנָעֳמִי\tx\tH\tנָעֳמִי\tnoun\t\tfeminine\tsingular\tproper\tSDBH\t\t\n"
        + "o080010050021\tRUT 1:5!2\tnoun\tאִשָּׁה\tx\tH\tאִשָּׁה\tnoun\t\tfeminine\tsingular\tcommon\tSDBH\t\t080010050011\n"
        + "o080010050031\tRUT 1:5!3\tpron\tהִיא\tx\tH\tהִיא\tpronoun\tthird\tfeminine\tsingular\tpersonal\tSDBH\t\t080010050021\n"
        + "o080010050041\tRUT 1:5!4\tverb\tתָּשָׁב\tx\tH\tשׁוּב\tverb\tthird\tfeminine\tsingular\twayyiqtol\tSDBH\t080010050021\t\n"
        + "o080010050051\tRUT 1:5!5\tnoun\tאִישׁ\tx\tH\tאִישׁ\tnoun\t\tmasculine\tsingular\tcommon\tSDBH\t\t\n"
        + "o080010050061\tRUT 1:5!6\tpron\tהוּא\tx\tH\tהוּא\tpronoun\tthird\tmasculine\tsingular\tpersonal\tSDBH\t\t080010050051\n";

    private static readonly MaculaHebrewJoin.Word[] RuthWords =
    [
        new(51, 8, 1, 5, 1, "נָעֳמִי"),
        new(52, 8, 1, 5, 2, "אִשָּׁה"),
        new(53, 8, 1, 5, 3, "הִיא"),
        new(54, 8, 1, 5, 4, "תָּשָׁב"),
        new(55, 8, 1, 5, 5, "אִישׁ"),
        new(56, 8, 1, 5, 6, "הוּא"),
    ];

    [Fact]
    public void AVerbRefersByItsSubjectAndAChainEndsAtACommonNoun()
    {
        var (parsings, referents) = MaculaHebrewAnnotation.Of(Read(Ruth), RuthWords);

        referents.Should().Equal([(53L, 52L), (54L, 52L), (56L, 55L)],
            "she and the verb's subject are the woman; a common noun ends the chain, whomever MACULA refers it on to");
        var verb = JsonDocument.Parse(parsings.Single(p => p.Word == 54).Morphology).RootElement;
        verb.GetProperty("refers").GetString().Should().Be("subject");
        verb.GetProperty("subject").GetString().Should().Be("3fs");
        var noun = JsonDocument.Parse(parsings.Single(p => p.Word == 52).Morphology).RootElement;
        noun.TryGetProperty("refers", out _).Should().BeFalse("a noun's own reference is followed, never shown on it");
    }

    /// <summary>The whole file on BHSA's words, as the build places it (PRB-1727's measurement).</summary>
    [Fact]
    [Trait("Category", "Corpus")]
    public void TheWholeTableIsPlacedOnBhsa()
    {
        var store = new ProductStore(TestResources.Folder("."));
        var ids = WordIdTable.For(store);
        using var reader = store.Open(TextWord.ProductOf("BHSA"), TextWord.Schema("BHSA"));
        var words = reader.ReadAll()
            .Select(w => new MaculaHebrewJoin.Word(ids.Of(w.Key), w.Key.Verse.Book, w.Key.Verse.Chapter, w.Key.Verse.Verse, w.Key.Position, w.Surface))
            .ToList();
        var morphs = MaculaHebrewReader.ReadFile(TestResources.Folder(MaculaHebrewReader.Folder));
        var (placed, differing) = MaculaHebrewJoin.Place(morphs, words);
        var (parsings, referents) = MaculaHebrewAnnotation.Of(morphs, words);

        var referring = morphs.Where(m => m.Referents.Length > 0).ToList();
        output.WriteLine($"morphs {morphs.Count}, placed {placed.Count(p => p >= 0)}, verses aligned past a difference {differing.Count}");
        output.WriteLine($"parsings {parsings.Count}, referring words {referents.Select(r => r.Word).Distinct().Count()}, rows {referents.Count}");
        output.WriteLine($"referring morphs {referring.Count}, of which pronouns {referring.Count(m => m.IsPronoun)}, suffixes {referring.Count(m => m.IsSuffix)}");
        output.WriteLine($"words referring by a suffix {parsings.Count(p => p.Morphology.Contains("\"refers\":\"suffix\""))}");
        var lettered = Enumerable.Range(0, morphs.Count).Where(i => MaculaHebrewJoin.Letters([morphs[i].Text]).Letters.Length > 0).ToList();
        output.WriteLine($"morphs with letters {lettered.Count}, placed {lettered.Count(i => placed[i] >= 0)}");
        var pronouns = Enumerable.Range(0, morphs.Count).Where(i => morphs[i].IsPronoun && morphs[i].Referents.Length > 0).ToList();
        output.WriteLine($"referring pronouns {pronouns.Count}, placed {pronouns.Count(i => placed[i] >= 0)}");

        lettered.Count(i => placed[i] >= 0).Should().BeGreaterThan(lettered.Count * 99 / 100);
        referents.Select(r => r.Word).Distinct().Count().Should().BeGreaterThan(45_000);
    }
}
