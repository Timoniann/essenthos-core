using Essenthos.Core.Berean;
using Essenthos.Core.ClearBible;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// How the Berean's two statements about its New Testament reach Nestle 1904's words: its own translation tables,
/// whose Greek is the Berean Greek Bible, and Clear Bible's alignment, whose records were numbered over an earlier
/// revision of the English than the token file shipped beside them. The verses are the files' own.
/// </summary>
public class BereanNestleJoinTests
{
    /// <summary>
    /// Matthew 20:9: the table reads <em>Καὶ ἐλθόντες</em> where Nestle reads <em>ἐλθόντες δὲ</em>, the same count. Laid
    /// by place, «came» stood on δὲ; laid word against word it stands on ἐλθόντες, and the Berean's καὶ and Nestle's δὲ
    /// meet nothing.
    /// </summary>
    [Fact]
    public void TheTableIsLaidOnNestleWordAgainstWordAndNotByPlace()
    {
        var rows = new[]
        {
            Row(18183, "Καὶ", "G2532", " - "), Row(18184, "ἐλθόντες", "G2064", " came [and] "),
            Row(18185, "οἱ", "G3588", " [The workers who were hired] "), Row(18186, "περὶ", "G4012", " about "),
            Row(18187, "τὴν", "G3588", " the "), Row(18188, "ἑνδεκάτην", "G1734", " eleventh "),
            Row(18189, "ὥραν", "G5610", " hour "), Row(18190, "ἔλαβον", "G2983", " received "),
            Row(18191, "ἀνὰ", "G303", " each "), Row(18192, "δηνάριον", "G1220", " a denarius "),
        };
        var nestle = Words(
            ("ἐλθόντες", "G2064"), ("δὲ", "G1161"), ("οἱ", "G3588"), ("περὶ", "G4012"), ("τὴν", "G3588"),
            ("ἑνδεκάτην", "G1734"), ("ὥραν", "G5610"), ("ἔλαβον", "G2983"), ("ἀνὰ", "G303"), ("δηνάριον", "G1220"));

        BereanLinks.Counterparts(rows, nestle).Should().Equal(-1, 0, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    /// <summary>
    /// Luke 18:30: «will fail to receive» renders the Berean's ὃς οὐχὶ μὴ ἀπολάβῃ together, and Nestle reads λάβῃ. Laid on
    /// ὃς οὐχὶ μὴ alone it would say they mean «receive»; the rendering is not written, and the rest of the verse is.
    /// </summary>
    [Fact]
    public void ARenderingOverAWordNestleDoesNotPrintIsNotWritten()
    {
        var rows = new[]
        {
            Row(69914, "ὃς", "G3739", " vvv ", 1), Row(69915, "οὐχὶ", "G3780", " vvv ", 2), Row(69916, "μὴ", "G3361", " vvv ", 3),
            Row(69917, "ἀπολάβῃ", "G618", " will fail to receive ", 4), Row(69918, "πολλαπλασίονα", "G4179", " many times more ", 5),
        };
        var nestle = Words(("ὃς", "G3739"), ("οὐχὶ", "G3780"), ("μὴ", "G3361"), ("λάβῃ", "G2983"), ("πολλαπλασίονα", "G4179"));
        var counterparts = BereanLinks.Counterparts(rows, nestle)!;
        counterparts.Should().Equal(0, 1, 2, -1, 4);

        var english = "will fail to receive many times more".Split(' ').Select((word, at) => new BereanLinks.Word(100 + at, 1, word, null)).ToList();
        List<List<long>> witness = [.. counterparts.Select(at => at < 0 ? [] : new List<long> { nestle[at].Id })];
        var drafts = new List<BereanLinks.Draft>();
        var silences = new BereanLinks.Silences();
        BereanLinks.Pair(rows, english, witness, drafts, silences, unprinted: new HashSet<int> { 3 }).Should().BeTrue();

        drafts.Should().ContainSingle().Which.To.Should().Equal(nestle[4].Id);
        silences.Unprinted.Should().Be(1);
    }

    /// <summary>
    /// Matthew 26:51: the token file makes the apostrophe of <em>Jesus’</em> a token of its own, and the records count
    /// <em>Jesus’</em> as one word, so μετὰ («companions») names the apostrophe and every number after it the word before
    /// the one meant. The verse is refused.
    /// </summary>
    [Fact]
    public void AVerseWhoseRecordsNameAMarkIsMisnumbered()
    {
        var tokens = Tokens("40026051", "At", "this", ",", "one", "of", "Jesus", "’", "companions", "drew", "his", "sword");
        var records = new[]
        {
            Record(["n40026051004"], ["400260510051"]),
            Record(["n40026051005"], ["400260510071"]),
            Record(["n40026051006"], ["400260510061"]),
            Record(["n40026051007", "n40026051008", "n40026051009", "n40026051010"], ["400260510081"]),
        };

        ClearBibleLinks.Misnumbered(records, tokens).Should().Equal(ClearBibleAlignment.Verse(40, 26, 51));
    }

    /// <summary>
    /// Mark 8:10: the records were numbered over a revision without «immediately», and no number falls on a mark, so only
    /// what the words are shows it: πλοῖον («boat») names «the», μετὰ («with») «boat». Numbered as the token file numbers
    /// the verse, the same records agree with their glosses and the verse stands.
    /// </summary>
    [Fact]
    public void AVerseWhoseRecordsMissTheirGlossesIsMisnumbered()
    {
        var tokens = Tokens("41008010", "He", "immediately", "got", "into", "the", "boat", "with", "His", "disciples");
        var glosses = new[]
        {
            Source("n41008010003", "ἐμβὰς", "He got"), Source("n41008010004", "εἰς", "into"), Source("n41008010005", "τὸ", "the"),
            Source("n41008010006", "πλοῖον", "boat"), Source("n41008010007", "μετὰ", "with"), Source("n41008010009", "μαθητῶν", "disciples"),
            Source("n41008010010", "αὐτοῦ", "His"),
        };
        ClearBibleRecord[] stated =
        [
            Record(["n41008010003"], ["410080100011", "410080100021"]), Record(["n41008010004"], ["410080100031"]),
            Record(["n41008010005"], ["410080100041"]), Record(["n41008010006"], ["410080100051"]),
            Record(["n41008010007"], ["410080100061"]), Record(["n41008010009"], ["410080100081"]),
            Record(["n41008010010"], ["410080100071"]),
        ];
        ClearBibleRecord[] renumbered =
        [
            Record(["n41008010003"], ["410080100011", "410080100031"]), Record(["n41008010004"], ["410080100041"]),
            Record(["n41008010005"], ["410080100051"]), Record(["n41008010006"], ["410080100061"]),
            Record(["n41008010007"], ["410080100071"]), Record(["n41008010009"], ["410080100091"]),
            Record(["n41008010010"], ["410080100081"]),
        ];

        ClearBibleLinks.Misnumbered(stated, tokens).Should().BeEmpty();
        ClearBibleLinks.Unglossed(stated, tokens, glosses).Should().Equal(ClearBibleAlignment.Verse(41, 8, 10));
        ClearBibleLinks.Unglossed(renumbered, tokens, glosses).Should().BeEmpty();
    }

    /// <summary>The Berean's set is the one read as numbered over a revision of its token file.</summary>
    [Fact]
    public void TheBereanSetIsReadAsRevised() =>
        ClearBibleSet.Berean("BSB", "NESTLE1904").Numbering.Should().Be(ClearBibleNumbering.Revised);

    private static BereanRow Row(double order, string greek, string strong, string rendering, int english = 0) =>
        new(order, english == 0 ? (int)order : english, 1, "Greek", greek, greek, strong, "Bible", rendering);

    private static List<BereanLinks.Word> Words(params (string Surface, string Strong)[] words) =>
        [.. words.Select((word, at) => new BereanLinks.Word(at + 1, 1, word.Surface, word.Strong))];

    private static List<ClearBibleToken> Tokens(string verse, params string[] texts) =>
        [.. texts.Select((text, at) => new ClearBibleToken($"{verse}{at + 1:000}", text, !char.IsLetter(text[0]), null))];

    private static ClearBibleToken Source(string id, string text, string gloss) => new(id, text, false, null, Gloss: gloss);

    private static ClearBibleRecord Record(string[] source, string[] target) => new(source, target);
}
