using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The model's answer names no verse: its lines are matched to the verses two texts share by their
/// order alone. An answer made while one text lacked the Psalms, read against texts that have them,
/// put Proverbs' pairs on the Psalms and every verse after them on one 2,461 verses later.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class AlignmentWorkspaceTests : IDisposable
{
    private static readonly (int Chapter, int Verse, string[] Words)[] Verses =
    [
        (1, 1, ["In", "the", "beginning"]),
        (1, 2, ["And", "the", "earth"]),
    ];

    private readonly AppDbContext _db;
    private readonly AlignmentPipeline _aligner;
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));

    public AlignmentWorkspaceTests(WitnessDatabase database)
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
    public async Task AnAnswerForTheVersesTheTextsShareIsReused()
    {
        Prepare(answers: ["0-0:0.9:0.9 1-1:0.9:0.9 2-2:0.9:0.9", "0-0:0.9:0.9 1-1:0.9:0.9 2-2:0.9:0.9"]);

        var proposals = await _aligner.Proposals("BSB", "KJV", _workspace, 0.1);

        var at = await _db.Words.ToDictionaryAsync(w => w.Id, w => new { w.Verse!.Number, w.Position });
        proposals.Should().HaveCount(6).And.OnlyContain(pair => at[pair.From].Equals(at[pair.To]));
    }

    [Fact]
    public async Task AnAnswerMissingAVerseIsRefusedRatherThanReadOntoTheVersesAfterIt()
    {
        Prepare(answers: ["0-0:0.9:0.9 1-1:0.9:0.9 2-2:0.9:0.9"]);

        var reading = () => _aligner.Proposals("BSB", "KJV", _workspace, 0.1);

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
    /// A workspace as a finished run leaves it, with the inputs written as the pipeline writes them
    /// now, so the answer is reused and nothing has to be trained.
    /// </summary>
    private void Prepare(string[] answers)
    {
        var lines = Verses.Select(verse => string.Join(' ', verse.Words.Select(word => AlignmentTokens.One(EnglishStemmer.Stem(word)))))
            .ToArray();
        Directory.CreateDirectory(Path.Combine(_workspace, "alignment"));
        File.WriteAllLines(Path.Combine(_workspace, "source.txt"), lines);
        File.WriteAllLines(Path.Combine(_workspace, "target.txt"), lines);
        File.WriteAllLines(Path.Combine(_workspace, "alignment", "pharaoh.txt"), answers);
    }
}
