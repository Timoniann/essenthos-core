using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Whether the two texts were laid against each other correctly at all — which the word-level
/// measures assume and none of them can check.
///
/// Verses are paired by canonical address. Where two texts divide a chapter differently the pairing
/// is wrong, and the corpus has two ways of noticing: the chapter holds a different number of
/// verses, or it holds the same number divided in different places. The second is the dangerous one
/// — Leviticus 11:15 in Brenton is Masoretic 11:16, every link in it names the wrong Hebrew word,
/// and nothing about any single link looks wrong. What is visible is the verse as a whole: its
/// links are uniformly faint, because the model had nothing to work with.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CorpusPairingTests : IDisposable
{
    /// <summary>Below the threshold a verse pair has to clear, and not by an accident of rounding.</summary>
    private const double Faint = 0.2;

    private readonly AppDbContext _db;
    private readonly CorpusCheck _check;
    private readonly Text _hebrew;
    private readonly Text _greek;

    public CorpusPairingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _check = new CorpusCheck(_db, NullLogger<CorpusCheck>.Instance);

        // One chapter, and the Greek divides it into three verses where the Hebrew has two.
        _hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא"]),
            (1, 2, ["אֵת", "הַ", "שָּׁמַיִם"]));
        _greek = Corpus.Add(_db, "lxx-brenton", TextKind.PrintedEdition, "grc",
            (1, 1, ["ἐν", "ἀρχῇ", "ἐποίησεν"]),
            (1, 2, ["ὁ", "θεὸς", "τὸν"]),
            (1, 3, ["οὐρανὸν"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task AChapterTheTwoDivideIntoDifferentNumbersOfVersesIsCounted()
    {
        Aligned(verse: 1, position: 1, confidence: 0.9);

        var pairing = (await _check.Measure()).Pairing.Single();

        pairing.Text.Should().Be("lxx-brenton");
        pairing.Against.Should().Be("bhsa");
        pairing.Chapters.Should().Be(1);
        pairing.Divided.Should().Be(1);
    }

    /// <summary>
    /// The signal that catches a wrong pairing where the counts agree. Every link in the verse is
    /// faint, and no link in it is remarkable on its own.
    /// </summary>
    [Fact]
    public async Task AVerseWhoseEveryLinkIsFaintIsReportedAsSuspect()
    {
        Aligned(verse: 1, position: 1, confidence: Faint);
        Aligned(verse: 1, position: 2, confidence: Faint);
        Aligned(verse: 1, position: 3, confidence: Faint);

        var pairing = (await _check.Measure()).Pairing.Single();

        pairing.Suspect.Should().Be(1);
        pairing.Worst.Should().ContainSingle().Which.Should().EndWith("1:1");
    }

    /// <summary>
    /// A verse the model was confident about is not suspect however few links it has, and a single
    /// faint link is a faint link rather than evidence about the verse it sits in.
    /// </summary>
    [Fact]
    public async Task AVerseTheModelWasSureOfIsNotSuspectAndNeitherIsOneFaintLink()
    {
        Aligned(verse: 1, position: 1, confidence: 0.9);
        Aligned(verse: 1, position: 2, confidence: 0.9);
        Aligned(verse: 1, position: 3, confidence: 0.9);
        Aligned(verse: 2, position: 1, confidence: Faint);

        var pairing = (await _check.Measure()).Pairing.Single();

        pairing.Suspect.Should().Be(0);
        pairing.Verses.Should().Be(2);
    }

    private void Aligned(int verse, int position, double confidence)
    {
        var link = new Link
        {
            FromTextId = _greek.Id,
            ToTextId = _hebrew.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = confidence,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.SaveChanges();

        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id, WordId = _db.WordAt(_greek, 1, verse, position).Id, Side = LinkSide.From,
        });
        _db.LinkWords.Add(new LinkWord
        {
            LinkId = link.Id, WordId = _db.WordAt(_hebrew, 1, verse, position).Id, Side = LinkSide.To,
        });
        _db.SaveChanges();
    }
}
