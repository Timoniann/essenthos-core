using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// One pair of texts can be aligned from different words: a recipe line before and after a text is
/// reloaded, the live corpus's words and a cold build's. Each set of inputs keeps its own answer, in a
/// folder of the workspace named by their digest, and training one never clears another.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class AlignmentKeyedAnswerTests : IDisposable
{
    private static readonly (int Chapter, int Verse, string[] Words)[] Verses =
    [
        (1, 1, ["In", "the", "beginning"]),
        (1, 2, ["And", "the", "earth"]),
    ];

    private const string Straight = "0-0:0.9:0.9 1-1:0.9:0.9 2-2:0.9:0.9";
    private const string Crossed = "0-2:0.9:0.9 1-1:0.9:0.9 2-0:0.9:0.9";

    private readonly AppDbContext _db;
    private readonly AlignmentPipeline _aligner;
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));

    public AlignmentKeyedAnswerTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _aligner = new AlignmentPipeline(_db, NullLogger<AlignmentPipeline>.Instance);

        Corpus.Add(_db, "BSB", TextKind.Translation, "eng", Verses);
        Corpus.Add(_db, "KJV", TextKind.Translation, "eng", Verses);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task TheAnswerForTheseWordsIsFoundBesideOneMadeFromOtherWords()
    {
        Store(_workspace, Lines(Verses), Lines(Rendered("earth", "land")), Crossed);
        Store(Keyed(Lines(Verses), Lines(Verses)), Lines(Verses), Lines(Verses), Straight);

        var proposals = await _aligner.Proposals("BSB", "KJV", _workspace, 0.1);

        await OnTheirOwnPositions(proposals);
        File.ReadAllLines(Path.Combine(_workspace, "target.txt")).Should().Equal(Lines(Rendered("earth", "land")));
        File.ReadAllText(Path.Combine(_workspace, "alignment", "pharaoh.txt")).Should().StartWith(Crossed);
    }

    [Fact]
    public async Task AnAnswerStoredBeforeAnswersWereKeyedIsReusedWhileItsWordsAreThese()
    {
        Store(_workspace, Lines(Verses), Lines(Verses), Straight);

        var proposals = await _aligner.Proposals("BSB", "KJV", _workspace, 0.1);

        await OnTheirOwnPositions(proposals);
        Directory.GetDirectories(_workspace).Should().OnlyContain(folder => !Path.GetFileName(folder).StartsWith("inputs-"));
    }

    [Fact]
    public async Task TwoSetsOfWordsForOnePairKeepAnAnswerEach()
    {
        Store(Keyed(Lines(Verses), Lines(Verses)), Lines(Verses), Lines(Verses), Straight);
        var renamed = Rendered("earth", "land");
        Store(Keyed(Lines(Verses), Lines(renamed)), Lines(Verses), Lines(renamed), Crossed);

        var before = await _aligner.Proposals("BSB", "KJV", _workspace, 0.1);
        await _db.Words.Where(w => w.Text!.Slug == "KJV" && w.Surface == "earth")
            .ExecuteUpdateAsync(set => set.SetProperty(w => w.Surface, "land"));
        var after = await _aligner.Proposals("BSB", "KJV", _workspace, 0.1);

        await OnTheirOwnPositions(before);
        var at = await Positions();
        after.Should().HaveCount(6).And.Contain(pair => at[pair.From].Position != at[pair.To].Position);
        Directory.GetDirectories(_workspace, "inputs-*").Should().HaveCount(2);
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

    private async Task<Dictionary<long, (int Number, int Position)>> Positions() =>
        await _db.Words.ToDictionaryAsync(w => w.Id, w => (w.Verse!.Number, w.Position));

    private async Task OnTheirOwnPositions(IReadOnlyList<(long From, long To, double Confidence)> proposals)
    {
        var at = await Positions();
        proposals.Should().HaveCount(6).And.OnlyContain(pair => at[pair.From].Equals(at[pair.To]));
    }
}
