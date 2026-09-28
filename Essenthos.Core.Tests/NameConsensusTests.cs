using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The name of an entity in a text read off the verses that name it: the word they share and other
/// verses do not, whatever case, prefix or spelling it is printed in.
/// </summary>
public class NameConsensusTests
{
    private static readonly ConsensusBar Lenient = new(1, 0.1, 0.05);

    private const int Abraham = 1;
    private const int Saul = 2;
    private const int Paul = 3;
    private const int Mahlon = 4;
    private const int Chilion = 5;
    private const int Joshua = 6;
    private const int Og = 7;

    [Theory]
    [InlineData("وَلِإِبْرَاهِيمَ", "ولابراهيم")]
    [InlineData("Ἀβραὰμ", "αβρααμ")]
    [InlineData("Μωυσῆς", "μωυσησ")]
    [InlineData("Abraham's", "abrahams")]
    [InlineData("Авраамові", "авраамові")]
    [InlineData("아브라함이라", "아브라함이라")]
    [InlineData("दाऊद", "दाऊद")]
    public void AWordIsComparedWithoutTheMarksAScriptWritesAtWill(string surface, string folded) =>
        NameConsensus.Fold(surface).Should().Be(folded);

    [Fact]
    public void AFormIsFiledUnderTheLettersItAddsAFewTo()
    {
        NameConsensus.Cores("авраамові").Should().Contain("авраам").And.NotContain("ав");
        NameConsensus.Cores("ог").Should().Equal("ог");
        NameConsensus.Cores("я").Should().BeEmpty();
    }

    [Fact]
    public void TheWordEveryVerseOfTheEntityPrintsIsItsNameInEveryCase()
    {
        var text = Text(
            (101, "І сказав Бог Авраамові"),
            (102, "І пішов Авраам"),
            (103, "І дав Авраамові сина"),
            (104, "І встав Авраам рано"),
            (105, "І сказав Він Авраама"),
            (201, "І пішов народ"),
            (202, "І сказав цар"),
            (203, "І встав пророк рано"));

        var (findings, claims) = NameConsensus.Read(text, Question((Abraham, [101, 102, 103, 104, 105])));

        var name = findings.Single().Name!;
        name.Core.Should().Be("авраам");
        name.Forms.Should().BeEquivalentTo("авраам", "авраамові", "авраама");
        name.Coverage.Should().Be(1);
        name.Specificity.Should().Be(1);
        Surfaces(text, NameConsensus.Settle(findings, claims, Lenient))
            .Should().BeEquivalentTo("Авраамові", "Авраам", "Авраамові", "Авраам", "Авраама");
    }

    [Fact]
    public void TwoSpellingsOfOneNameShareTheCoreThatJoinsThem()
    {
        var text = Text(
            (101, "and Abram went"),
            (102, "and Abram said"),
            (103, "thy name shall be Abraham"),
            (104, "and Abraham rose"),
            (201, "and the people went"));

        var (findings, _) = NameConsensus.Read(text, Question((Abraham, [101, 102, 103, 104])));

        findings.Single().Name!.Forms.Should().BeEquivalentTo("abram", "abraham");
    }

    [Fact]
    public void ANamesakesVersesDoNotCountAgainstTheName()
    {
        var text = Text(
            (101, "and Saul the king went"),
            (102, "and Saul said"),
            (103, "and Saul slept"),
            (201, "and Saul who is Paul preached"),
            (202, "and Saul went to Tarsus"),
            (203, "and Paul said"));

        var (findings, claims) = NameConsensus.Read(
            text,
            Question([(Saul, [101, 102, 103]), (Paul, [201, 202, 203])], [(Saul, Paul)]));

        var king = findings.Single(finding => finding.Entity == Saul).Name!;
        king.Core.Should().Be("saul");
        king.Specificity.Should().Be(1);
        NameConsensus.Settle(findings, claims, Lenient)
            .Where(word => word.Entity == Saul)
            .Should().HaveCount(3);
    }

    [Fact]
    public void AWordTwoNamesakesBothStandBesideNamesNeither()
    {
        var text = Text(
            (101, "Mary stood by"),
            (102, "Mary the mother wept"),
            (103, "Mary Magdalene and Mary the mother came"),
            (201, "Magdalene saw"),
            (202, "Mary Magdalene ran"));
        var mother = 11;
        var magdalene = 12;

        var (findings, claims) = NameConsensus.Read(
            text,
            Question([(mother, [101, 102, 103]), (magdalene, [103, 201, 202])], [(mother, magdalene)]));

        NameConsensus.Settle(findings, claims, Lenient)
            .Should().NotContain(word => word.Verse == 2 && Surface(text, word) == "Mary");
    }

    [Fact]
    public void TwoNamesThatFitTheVersesEquallyAreNotGuessedBetween()
    {
        var text = Text(
            (101, "and Mahlon and Chilion went"),
            (102, "and Mahlon and Chilion died"),
            (201, "and the people went"));

        var (findings, claims) = NameConsensus.Read(
            text,
            Question((Mahlon, [101, 102]), (Chilion, [101, 102])));

        findings.Should().OnlyContain(finding => finding.Margin == 0);
        NameConsensus.Settle(findings, claims, Lenient).Should().BeEmpty();
    }

    [Fact]
    public void AWordATextPrintsInLowerCaseIsNotANameInAScriptWithCapitals()
    {
        var text = Text(
            (101, "Иисус великий иерей встал"),
            (102, "Иисусу великому иерею сказал"),
            (103, "Иисус иерей облечен"),
            (201, "Иисус Навин пошел"),
            (202, "Иисус сказал народу"),
            (203, "И сказал Иисус"),
            (204, "И встал Иисус"),
            (205, "Иисус пошел"),
            (206, "и пришел Иисус"));

        var (findings, _) = NameConsensus.Read(text, Question((Joshua, [101, 102, 103])));

        findings.Single().Name!.Forms.Should().NotContain(form => form.StartsWith("иере"));
    }

    [Fact]
    public void ATextThatWritesEveryWordInLowerCaseStillHasNames()
    {
        var text = Text(
            (101, "και ειπεν αβρααμ"),
            (102, "και ανεστη αβρααμ"),
            (103, "αβρααμ δε ειπεν"),
            (201, "και ειπεν ο λαος"));

        var (findings, _) = NameConsensus.Read(text, Question((Abraham, [101, 102, 103])));

        findings.Single().Name!.Core.Should().Be("αβρααμ");
    }

    [Fact]
    public void ANameOfTwoLettersGathersItsCaseEndings()
    {
        var text = Text(
            (101, "и вышел Ог царь Васанский"),
            (102, "и убили Ога царя Васанского"),
            (103, "и дали Огу"),
            (104, "и Огом"),
            (105, "и вышел Ог"),
            (201, "и Сигона царя Есевонского"),
            (202, "и пришел Бог"));

        var (findings, _) = NameConsensus.Read(text, Question((Og, [101, 102, 103, 104, 105])));

        findings.Single().Name!.Forms.Should().BeEquivalentTo("ог", "ога", "огу", "огом");
    }

    [Fact]
    public void AStrayWordThatSharesThreeLettersWithTheNameIsLeftOut()
    {
        var verses = Enumerable.Range(0, 120)
            .Select(verse => (101 + verse, verse == 7 ? "and he went to Gabaar" : $"and Aaron spoke {verse}"))
            .Append((901, "and the people went"))
            .ToArray();
        var text = Text(verses);

        var (findings, _) = NameConsensus.Read(text, Question((Abraham, verses.Take(120).Select(verse => verse.Item1).ToArray())));

        findings.Single().Name!.Forms.Should().Equal("aaron");
    }

    [Theory]
    [InlineData("שְׂגוּב", true)]
    [InlineData("מְשֻׁלָּם", false)]
    public void ANameReadOffOneVerseStandsOnlyIfItIsSpeltLikeTheOriginals(string lemma, bool accepted)
    {
        var text = Text(
            (101, "and Segub went up"),
            (201, "and he went up"),
            (202, "and they went up"));
        var question = new ConsensusQuestion(
            new Dictionary<int, IReadOnlySet<int>> { [Joshua] = new HashSet<int> { 101 } },
            new Dictionary<int, IReadOnlySet<int>>(),
            new Dictionary<int, IReadOnlySet<string>> { [Joshua] = new HashSet<string> { lemma } });

        var (findings, _) = NameConsensus.Read(text, question);

        new ConsensusBar(5, 0.1, 0.05).Accepts(findings.Single()).Should().Be(accepted);
    }

    private static List<ConsensusVerse> Text(params (int Address, string Words)[] verses)
    {
        var word = 0L;
        return verses
            .Select(verse => new ConsensusVerse(
                [verse.Address],
                verse.Words.Split(' ').Select(surface => (++word, surface)).ToList()))
            .ToList();
    }

    private static ConsensusQuestion Question(params (int Entity, int[] Addresses)[] entities) =>
        Question(entities, []);

    private static ConsensusQuestion Question((int Entity, int[] Addresses)[] entities, (int One, int Other)[] namesakes) =>
        new(
            entities.ToDictionary(entity => entity.Entity, entity => (IReadOnlySet<int>)entity.Addresses.ToHashSet()),
            namesakes
                .SelectMany(pair => new[] { (pair.One, pair.Other), (pair.Other, pair.One) })
                .GroupBy(pair => pair.Item1)
                .ToDictionary(group => group.Key, group => (IReadOnlySet<int>)group.Select(pair => pair.Item2).ToHashSet()));

    private static string Surface(IReadOnlyList<ConsensusVerse> text, ConsensusWord word) =>
        text[word.Verse].Words.Single(printed => printed.Word == word.Word).Surface;

    private static IEnumerable<string> Surfaces(IReadOnlyList<ConsensusVerse> text, IEnumerable<ConsensusWord> words) =>
        words.Select(word => Surface(text, word));
}
