using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// One pair of texts can be aligned from different words: a recipe line before and after a text is
/// reloaded, the live corpus's words and a cold build's. Each set of inputs keeps its own answer, in a
/// folder of the workspace named by their digest, and training one never clears another.
/// </summary>
public sealed class AlignmentKeyedAnswerTests : IDisposable
{
    private static readonly (int Chapter, int Verse, string[] Words)[] Verses =
    [
        (1, 1, ["In", "the", "beginning"]),
        (1, 2, ["And", "the", "earth"]),
    ];

    private const string Straight = "0-0:0.9:0.9 1-1:0.9:0.9 2-2:0.9:0.9";
    private const string Crossed = "0-2:0.9:0.9 1-1:0.9:0.9 2-0:0.9:0.9";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;
    private readonly AlignmentPipeline _aligner = new(null, NullLogger<AlignmentPipeline>.Instance);
    private readonly string _workspace;

    public AlignmentKeyedAnswerTests()
    {
        _store = new ProductStore(_root);
        _workspace = Path.Combine(_root, "Alignments", "BSB-KJV");
        Write("BSB", Verses);
        Write("KJV", Verses);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task TheAnswerForTheseWordsIsFoundBesideOneMadeFromOtherWords()
    {
        Store(_workspace, Lines(Verses), Lines(Rendered("earth", "land")), Crossed);
        Store(Keyed(Lines(Verses), Lines(Verses)), Lines(Verses), Lines(Verses), Straight);

        var proposals = await Proposals();

        OnTheirOwnPositions(proposals);
        File.ReadAllLines(Path.Combine(_workspace, "target.txt")).Should().Equal(Lines(Rendered("earth", "land")));
        File.ReadAllText(Path.Combine(_workspace, "alignment", "pharaoh.txt")).Should().StartWith(Crossed);
    }

    [Fact]
    public async Task AnAnswerStoredBeforeAnswersWereKeyedIsReusedWhileItsWordsAreThese()
    {
        Store(_workspace, Lines(Verses), Lines(Verses), Straight);

        var proposals = await Proposals();

        OnTheirOwnPositions(proposals);
        Directory.GetDirectories(_workspace).Should().OnlyContain(folder => !Path.GetFileName(folder).StartsWith("inputs-"));
    }

    [Fact]
    public async Task TwoSetsOfWordsForOnePairKeepAnAnswerEach()
    {
        Store(Keyed(Lines(Verses), Lines(Verses)), Lines(Verses), Lines(Verses), Straight);
        var renamed = Rendered("earth", "land");
        Store(Keyed(Lines(Verses), Lines(renamed)), Lines(Verses), Lines(renamed), Crossed);

        var before = await Proposals();
        Write("KJV", renamed);
        var after = await Proposals();

        OnTheirOwnPositions(before);
        after.Should().HaveCount(6).And.Contain(pair => pair.From % 3 != pair.To % 3);
        Directory.GetDirectories(_workspace, "inputs-*").Should().HaveCount(2);
    }

    [Fact]
    public async Task ABuildFindingNoAnswerForTheWordsAsTheyStandTrainsNothingAndSaysWhichPairWaits()
    {
        Store(_workspace, Lines(Verses), Lines(Rendered("earth", "land")), Crossed);

        var reading = () => Proposals();

        await reading.Should().ThrowAsync<StoredAlignmentMissingException>().WithMessage("*BSB-KJV*other words*");
        Directory.GetDirectories(_workspace, "inputs-*").Should().BeEmpty();
    }

    [Fact]
    public async Task TrainingIsStoredUnderItsInputsAndLeavesTheEarlierAnswerAlone()
    {
        Store(_workspace, Lines(Verses), Lines(Rendered("earth", "land")), Crossed);
        string[] source = ["in the begin", "and the earth"];
        var inputs = new List<(string Name, string[] Lines)> { ("source.txt", source), ("target.txt", source) };

        var folder = await _aligner.Answer(_workspace, "ibm4", inputs, AlignmentPipeline.AlignmentFile, training =>
        {
            File.ReadAllLines(Path.Combine(training, "source.txt")).Should().Equal(source);
            Directory.CreateDirectory(Path.Combine(training, "alignment"));
            File.WriteAllLines(Path.Combine(training, AlignmentPipeline.AlignmentFile), [Straight, Straight]);
            return Task.CompletedTask;
        });

        folder.Should().Be(Path.Combine(_workspace, AlignmentPipeline.Key("ibm4", inputs)));
        File.ReadAllLines(Path.Combine(folder, AlignmentPipeline.AlignmentFile)).Should().Equal(Straight, Straight);
        File.ReadAllText(Path.Combine(_workspace, "alignment", "pharaoh.txt")).Should().StartWith(Crossed);
        Directory.GetDirectories(_workspace, "*.training-*").Should().BeEmpty();

        var again = await _aligner.Answer(_workspace, "ibm4", inputs, AlignmentPipeline.AlignmentFile,
            _ => throw new InvalidOperationException("trained twice"));
        again.Should().Be(folder);
    }

    [Fact]
    public async Task TrainingThatStopsOrWritesNothingLeavesNoAnswer()
    {
        var inputs = new List<(string Name, string[] Lines)> { ("source.txt", ["in"]), ("target.txt", ["in"]) };

        var stopped = () => _aligner.Answer(_workspace, "ibm4", inputs, AlignmentPipeline.AlignmentFile,
            _ => throw new OperationCanceledException());
        await stopped.Should().ThrowAsync<OperationCanceledException>();

        var empty = () => _aligner.Answer(_workspace, "ibm4", inputs, AlignmentPipeline.AlignmentFile, training =>
        {
            Directory.CreateDirectory(Path.Combine(training, "alignment"));
            File.WriteAllText(Path.Combine(training, AlignmentPipeline.AlignmentFile), string.Empty);
            return Task.CompletedTask;
        });
        await empty.Should().ThrowAsync<InvalidOperationException>().WithMessage("*without writing*");

        Directory.GetDirectories(_workspace).Should().BeEmpty();
    }

    [Fact]
    public async Task APoolsStampBesideAnEmptyModelIsNeitherStoredNorReused()
    {
        var inputs = new List<(string Name, string[] Lines)> { ("source.txt", ["in"]), ("target.txt", ["in"]) };
        string? Lacks(string folder) => AlignmentPipeline.ModelLacks(Path.Combine(folder, "model", "GEEZ81"));

        var empty = () => _aligner.Answer(_workspace, "ibm4", inputs, "trained.txt", training =>
        {
            Directory.CreateDirectory(Path.Combine(training, "model"));
            File.WriteAllText(Path.Combine(training, "trained.txt"), "stamp");
            return Task.CompletedTask;
        }, Lacks);
        await empty.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no configuration GEEZ81_swm.yml*");
        Directory.GetDirectories(_workspace).Should().BeEmpty();

        var keyed = Path.Combine(_workspace, AlignmentPipeline.Key("ibm4", inputs));
        Directory.CreateDirectory(Path.Combine(keyed, "model"));
        File.WriteAllLines(Path.Combine(keyed, "source.txt"), ["in"]);
        File.WriteAllLines(Path.Combine(keyed, "target.txt"), ["in"]);
        File.WriteAllText(Path.Combine(keyed, "trained.txt"), "stamp");

        var trained = false;
        var folder = await _aligner.Answer(_workspace, "ibm4", inputs, "trained.txt", training =>
        {
            Model(Path.Combine(training, "model", "GEEZ81"));
            File.WriteAllText(Path.Combine(training, "trained.txt"), "stamp");
            trained = true;
            return Task.CompletedTask;
        }, Lacks);

        trained.Should().BeTrue("a stamp beside an empty model is not a model to reuse");
        Lacks(folder).Should().BeNull();
    }

    [Fact]
    public void APoolTrainsOnEachOfItsTextsBesideTheSourceAsThatTextsOwnPairReadsIt()
    {
        static Dictionary<(int, int, int), List<AlignmentPipeline.Word>> Read(params ((int, int, int) At, string[] Words)[] verses) =>
            verses.ToDictionary(verse => verse.At, verse => verse.Words.Select((word, i) => new AlignmentPipeline.Word(i + 1, word, null)).ToList());
        var lettered = Read(((1, 1, 1), ["ወ", "ሰ", "ለ"]), ((40, 1, 1), ["መ"]));
        var unlettered = Read(((1, 1, 1), ["ወ"]), ((40, 1, 1), ["መ"]));
        var swete = Read(((1, 1, 1), ["και"]));
        var nestle = Read(((40, 1, 1), ["βιβλος"]));

        var inputs = AlignmentPipeline.PoolInputs("GEEZ81", ["SWETE", "NESTLE1904"],
            text => text == "SWETE" ? new(lettered, swete) : new(unlettered, nestle));

        inputs.Select(input => input.Name).Should().Equal("source.txt", "target.txt");
        inputs[0].Lines.Should().Equal(["ወ ሰ ለ", "መ"], "the Old Testament verse is read as the pair numbered alike reads it, lettered words and all");
        inputs[1].Lines.Should().Equal("και", "βιβλος");
    }

    [Fact]
    public void BelowTheAgreedConfidenceAPairStandsOnlyWhereBothDirectionsOfTheModelNameIt()
    {
        var source = new Dictionary<(int, int, int), List<AlignmentPipeline.Word>>
        {
            [(40, 1, 1)] = [new(1, "ወ", null), new(2, "ሰ", null), new(3, "ለ", null), new(4, "መ", null)],
        };
        var target = new Dictionary<(int, int, int), List<AlignmentPipeline.Word>>
        {
            [(40, 1, 1)] = [new(11, "και", null), new(12, "εν", null), new(13, "λογ", null), new(14, "θε", null)],
        };
        Directory.CreateDirectory(_workspace);
        var och = Path.Combine(_workspace, "och.txt");
        var both = Path.Combine(_workspace, "both.txt");
        File.WriteAllLines(och, ["0-0:0.60:0.5 1-1:0.60:0.5 1-2:0.50:0.5 2-2:0.85:0.5 3-3:0.30:0.5"]);
        File.WriteAllLines(both, ["0-0:0.60:0.5 1-2:0.50:0.5 3-3:0.30:0.5"]);

        var agreement = new AlignmentPipeline.Agreement(AlignmentPipeline.PairsOf(both, 1), 0.8);
        var (drafts, _, _, below) = AlignmentPipeline.Read(och, [(40, 1, 1)], source, target, 0.4, Selection.BestPerSource, agreement: agreement);

        drafts.Select(draft => (draft.SourceWordId, draft.TargetWordId)).Should().Equal(
            [(1L, 11L), (3L, 13L)],
            "a pair under 0.80 stands where both directions name it, one from 0.80 stands either way, and a word whose best pair one direction alone names is not handed its second best");
        below.Should().Be(2, "the pair under the threshold and the one only one direction names are both left out");
        FluentActions.Invoking(() => AlignmentPipeline.PairsOf(both, 2)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task APairJoinedByIntersectionReadsItsOwnAnswerInItsOwnWorkspaceAndWritesOnlyTheBooksNamed()
    {
        var workspace = Path.Combine(_root, "Alignments", "BSB-KJV-both");
        var keyed = Path.Combine(workspace, AlignmentPipeline.Key("ibm4",
            [("source.txt", Lines(Verses)), ("target.txt", Lines(Verses)), ("symmetrisation.txt", [AlignmentPipeline.BothDirections])]));
        Store(keyed, Lines(Verses), Lines(Verses), Straight);
        File.WriteAllLines(Path.Combine(keyed, "symmetrisation.txt"), [AlignmentPipeline.BothDirections]);
        var files = new AlignerFiles(new ProductTexts(_store), new AlignmentPipeline(null, NullLogger<AlignmentPipeline>.Instance) { Trains = false });
        var pair = new AlignedPair("BSB", "KJV", Symmetrisation: AlignmentPipeline.BothDirections, Workspace: "BSB-KJV-both", Note: "measured");

        var (rows, _) = await files.Align(pair with { Books = new HashSet<int> { 1 } });
        var (none, _) = await files.Align(pair with { Books = new HashSet<int> { 2 } });

        rows.Should().HaveCount(6).And.OnlyContain(row => row.Source.Contains("symmetrised intersection") && row.Source.EndsWith("; measured"));
        none.Should().BeEmpty("the model reads every verse, and only the target's books named are written");
        Directory.Exists(_workspace).Should().BeFalse("the pair's own workspace is another run's");
    }

    [Fact]
    public void ALexemeIsTheWordsDictionaryFormWhereItHasOne()
    {
        AlignmentPipeline.Lexeme(new AlignmentPipeline.WordForms("בָּרָ֣א", "ברא", "ברא", "H1254", "hbo", 3)).Should().Be("ברא");
        AlignmentPipeline.Lexeme(new AlignmentPipeline.WordForms("Ἐν", "ἐν", null, null, "grc", 1)).Should().Be("ἐν");
        AlignmentPipeline.Lexeme(new AlignmentPipeline.WordForms("λόγοις", null, null, null, "grc", 2))
            .Should().Be(AlignmentPipeline.Comparable(new AlignmentPipeline.WordForms("λόγοις", null, null, null, "grc", 2)));
    }

    [Fact]
    public async Task AWitnessWithoutALemmaIsReadByItsLettersAndABookLeftUnreadIsNeitherTrainedOnNorWritten()
    {
        ProductCorpus.Text(_store, "ONKELOS", "arc", [("1.1:1", ["בְּקַדְמִין", "בְּרָא", "יְיָ"]), ("14.1:1", ["וּמְלַךְ", "שְׁלֹמֹה", "בַּר"])]);
        ProductCorpus.Text(_store, "HEB", "arc", [("1.1:1", ["בראשית", "ברא", "אלהים"]), ("14.1:1", ["וימלך", "שלמה", "בן"])]);
        string[] source = ["בקדמין ברא יי"];
        string[] target = ["בראשית ברא אלהים"];
        var workspace = Path.Combine(_root, "Alignments", "ONKELOS-HEB");
        ProductCorpus.Answer(Path.Combine(workspace, AlignmentPipeline.Key("ibm4", [("source.txt", source), ("target.txt", target)])), source, target,
            Straight);
        var pair = new AlignedPair("ONKELOS", "HEB", Minimum: 0.5, Consonants: true, Unread: new HashSet<int> { 14 });

        var (rows, _) = await ProductCorpus.Aligner(_store).Align(pair);
        var whole = () => ProductCorpus.Aligner(_store).Align(pair with { Unread = null });

        rows.Select(row => (row.From, row.To)).Should().Equal(("1.1:1.1", "1.1:1.1"), ("1.1:1.2", "1.1:1.2"), ("1.1:1.3", "1.1:1.3"));
        rows.Should().OnlyContain(row => row.Source.Contains("the source by its consonants and the target by lexeme"));
        await whole.Should().ThrowAsync<StoredAlignmentMissingException>("read whole, the lines hold 2 Chronicles and no answer was made from them");
        AlignmentPipeline.Consonants(new AlignmentPipeline.WordForms("ܡܰܠܟܳܐ̈", null, null, null, "syc", 1)).Should().Be("ܡܠܟܐ");
    }

    [Fact]
    public void AModelCountsOnlyWithEachDirectionsConfigurationAndATableThatHoldsSomething()
    {
        var prefix = Path.Combine(_workspace, "model", "GEEZ81");
        AlignmentPipeline.ModelLacks(prefix).Should().Contain("no model folder");

        Model(prefix);
        AlignmentPipeline.ModelLacks(prefix).Should().BeNull();

        File.WriteAllText(prefix + "_invswm.ibm_lexnd", string.Empty);
        AlignmentPipeline.ModelLacks(prefix).Should().Contain("no table of it");

        Model(prefix);
        File.WriteAllText(prefix + "_swm.yml", string.Empty);
        AlignmentPipeline.ModelLacks(prefix).Should().Contain("no configuration GEEZ81_swm.yml");
    }

    [Fact]
    public void TheAlignersReportedErrorsAndTooLongModelPathsAreRefused()
    {
        AlignmentPipeline.ReportsAnError("Error while printing source vocabulary.\r\nError while printing source vocabulary.\r\n")
            .Should().BeTrue();
        AlignmentPipeline.ReportsAnError(string.Empty).Should().BeFalse();

        var fits = "C:\\" + new string('a', AlignmentPipeline.LongestAlignerPath - AlignmentPipeline.LongestModelSuffix - 3);
        var check = () => AlignmentPipeline.RefuseTooLong(fits);
        check.Should().NotThrow();
        if (OperatingSystem.IsWindows())
        {
            var tooLong = () => AlignmentPipeline.RefuseTooLong(fits + "a");
            tooLong.Should().Throw<InvalidOperationException>().WithMessage("*opens none longer than*shorter path*");
        }
    }

    private static void Model(string prefix)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        foreach (var direction in new[] { "_swm", "_invswm" })
        {
            File.WriteAllText(prefix + direction + ".yml", "model: ibm4");
            File.WriteAllText(prefix + direction + ".ibm_lexnd", "table");
            File.WriteAllText(prefix + direction + ".src_classes", string.Empty);
        }
    }

    [Fact]
    public void TheDigestNamesTheModelAndEveryLine()
    {
        var key = AlignmentPipeline.Key("ibm4", [("source.txt", ["a b"]), ("target.txt", ["c"])]);

        key.Should().StartWith("inputs-").And.HaveLength("inputs-".Length + 16);
        AlignmentPipeline.Key("ibm4", [("source.txt", ["a b"]), ("target.txt", ["c"])]).Should().Be(key);
        AlignmentPipeline.Key("ibm1", [("source.txt", ["a b"]), ("target.txt", ["c"])]).Should().NotBe(key);
        AlignmentPipeline.Key("ibm4", [("source.txt", ["a", "b"]), ("target.txt", ["c"])]).Should().NotBe(key);
        AlignmentPipeline.Key("ibm4", [("source.txt", ["a b"]), ("target.txt", ["d"])]).Should().NotBe(key);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("1", null, true)]
    [InlineData("true", "0", true)]
    [InlineData("1", "1", false)]
    [InlineData("true", "true", false)]
    [InlineData(null, "1", false)]
    public void ARunThatMayRunNoModelMayStillBeLetTrainTheAligner(string? forbidden, string? aligner, bool refused) =>
        ModelRuns.Refuses(forbidden, aligner).Should().Be(refused);

    private static (int Chapter, int Verse, string[] Words)[] Rendered(string word, string instead) =>
        [.. Verses.Select(verse => (verse.Chapter, verse.Verse, verse.Words.Select(w => w == word ? instead : w).ToArray()))];

    private static string[] Lines((int Chapter, int Verse, string[] Words)[] verses) =>
        [.. verses.Select(verse => string.Join(' ', verse.Words.Select(word => AlignmentTokens.One(EnglishStemmer.Stem(word)))))];

    private string Keyed(string[] source, string[] target) =>
        Path.Combine(_workspace, AlignmentPipeline.Key("ibm4", [("source.txt", source), ("target.txt", target)]));

    private static void Store(string folder, string[] source, string[] target, string answer)
    {
        Directory.CreateDirectory(Path.Combine(folder, "alignment"));
        File.WriteAllLines(Path.Combine(folder, "source.txt"), source);
        File.WriteAllLines(Path.Combine(folder, "target.txt"), target);
        File.WriteAllLines(Path.Combine(folder, "alignment", "pharaoh.txt"), [answer, answer]);
    }

    private Task<IReadOnlyList<(long From, long To, double Confidence)>> Proposals() =>
        new AlignerFiles(new ProductTexts(_store), new AlignmentPipeline(null, NullLogger<AlignmentPipeline>.Instance) { Trains = false })
            .Proposals("BSB", "KJV", "BSB-KJV", 0.1);

    private void Write(string slug, (int Chapter, int Verse, string[] Words)[] verses) =>
        ProductCorpus.Text(_store, slug, "eng", [.. verses.Select(verse => ($"1.{verse.Chapter}:{verse.Verse}", verse.Words))]);

    /// <summary>Both texts print three words a verse, so a word's place in its text names its verse and position alike.</summary>
    private static void OnTheirOwnPositions(IReadOnlyList<(long From, long To, double Confidence)> proposals) =>
        proposals.Should().HaveCount(6).And.OnlyContain(pair => pair.From == pair.To);
}
