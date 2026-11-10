using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The model's answer names no verse: its lines are matched to the verses two texts share by their
/// order alone. An answer made while one text lacked the Psalms, read against texts that have them,
/// put Proverbs' pairs on the Psalms and every verse after them on one 2,461 verses later.
/// </summary>
public sealed class AlignmentWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;
    private readonly string _workspace;

    public AlignmentWorkspaceTests()
    {
        _store = new ProductStore(_root);
        _workspace = Path.Combine(_root, "Alignments", "BSB-KJV");
        var verses = ProductCorpus.Genesis(["In", "the", "beginning"], ["And", "the", "earth"]);
        ProductCorpus.Text(_store, "BSB", "eng", verses);
        ProductCorpus.Text(_store, "KJV", "eng", verses);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task AnAnswerForTheVersesTheTextsShareIsReused()
    {
        Prepare(answers: ["0-0:0.9:0.9 1-1:0.9:0.9 2-2:0.9:0.9", "0-0:0.9:0.9 1-1:0.9:0.9 2-2:0.9:0.9"]);

        var proposals = await ProductCorpus.Aligner(_store).Proposals("BSB", "KJV", "BSB-KJV", 0.1);

        proposals.Should().HaveCount(6).And.OnlyContain(pair => pair.From == pair.To, "both texts print the same words");
    }

    [Fact]
    public async Task AnAnswerMissingAVerseIsRefusedRatherThanReadOntoTheVersesAfterIt()
    {
        Prepare(answers: ["0-0:0.9:0.9 1-1:0.9:0.9 2-2:0.9:0.9"]);

        var reading = () => ProductCorpus.Aligner(_store).Proposals("BSB", "KJV", "BSB-KJV", 0.1);

        await reading.Should().ThrowAsync<InvalidOperationException>().WithMessage("*one line for each of the 2 verses*");
    }

    [Fact]
    public void InputsWrittenForOtherVersesAreNotTheInputsOfThisAnswer()
    {
        Directory.CreateDirectory(_workspace);
        var path = Path.Combine(_workspace, "source.txt");
        File.WriteAllLines(path, ["and the earth"]);

        AlignmentPipeline.Unchanged([(path, ["and the earth"])]).Should().BeTrue();
        AlignmentPipeline.Unchanged([(path, ["in the begin", "and the earth"])]).Should().BeFalse();
        AlignmentPipeline.Unchanged([(path, ["and the heaven"])]).Should().BeFalse();
        AlignmentPipeline.Unchanged([(Path.Combine(_workspace, "target.txt"), ["and the earth"])]).Should().BeFalse();
    }

    /// <summary>
    /// A workspace as a finished run leaves it, with the inputs written as the aligner writes them now, so the answer is
    /// reused and nothing has to be trained.
    /// </summary>
    private void Prepare(string[] answers)
    {
        var lines = ProductCorpus.Stems("In the beginning", "And the earth");
        ProductCorpus.Answer(_workspace, lines, lines, answers);
    }
}
