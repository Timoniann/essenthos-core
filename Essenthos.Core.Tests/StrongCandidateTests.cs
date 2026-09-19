using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Strong number the corpus worked out for a word, as the reader receives it.
///
/// The Septuagint prints no Strong numbers, so a word of Brenton reaches a dictionary entry only by
/// its lemma — a proposal the corpus made, not a number the edition states. The reader needs it to
/// say what the word means, and needs it kept apart from a stated number so it can say how it knows.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongCandidateTests : IDisposable
{
    private const string LemmaSource = "the lemma GLAUx gives the word, matched against Strong's Greek entries";
    private const double LemmaConfidence = 0.9;

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _septuagint;

    public StrongCandidateTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _septuagint = Corpus.Add(_db, "LXX-BRENTON", TextKind.PrintedEdition, "grc",
            (1, 1, ["Ἐν", "ἀρχῇ", "ἐποίησεν", "ὁ", "θεὸς"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task AWordWithOneProposedNumberCarriesItWithItsProvenance()
    {
        Propose(2, "G746", LinkMethod.Lexical, LemmaConfidence);

        var word = await Read(2);

        word.StrongNo.Should().BeNull("the edition states no number, and the field says what a source states");
        word.StrongCandidate.Should().Be(
            new StrongCandidateResponse("G746", "lexical", LemmaConfidence));
    }

    [Fact]
    public async Task AWordWhoseLemmaTwoEntriesClaimGetsNone()
    {
        Propose(3, "G4160", LinkMethod.Lexical, LemmaConfidence);
        Propose(3, "G4161", LinkMethod.Lexical, LemmaConfidence);

        (await Read(3)).StrongCandidate.Should().BeNull("choosing between the two would be a guess");
    }

    [Fact]
    public async Task TwoMethodsProposingOneNumberAreOneAnswerDescribedByTheSurer()
    {
        Propose(5, "G2316", LinkMethod.Lexical, 0.6);
        Propose(5, "G2316", LinkMethod.Aligner, LemmaConfidence);

        (await Read(5)).StrongCandidate!.Should().Be(
            new StrongCandidateResponse("G2316", "aligner", LemmaConfidence));
    }

    [Fact]
    public async Task AWordWhoseSourceStatesANumberIsNotOfferedAProposal()
    {
        var word = _db.WordAt(_septuagint, 1, 1, 4);
        word.StrongNumber = "G3588";
        _db.SaveChanges();
        Propose(4, "G3739", LinkMethod.Lexical, LemmaConfidence);

        var read = await Read(4);

        read.StrongNo.Should().Be("G3588");
        read.StrongCandidate.Should().BeNull();
    }

    [Fact]
    public async Task AWordNothingWasProposedForCarriesNothing()
    {
        (await Read(1)).StrongCandidate.Should().BeNull();
    }

    private async Task<TextWordResponse> Read(int position)
    {
        var chapter = await Texts.ReadChapter(_db, _septuagint.Id, 1, 1, default);
        return chapter.Single().Words[position - 1];
    }

    private void Propose(int position, string number, LinkMethod method, double confidence)
    {
        _db.WordStrongs.Add(new WordStrong
        {
            WordId = _db.WordAt(_septuagint, 1, 1, position).Id,
            Number = number,
            Method = method,
            Confidence = confidence,
            Source = LemmaSource,
        });
        _db.SaveChanges();
    }
}
