using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Two texts that number the same passage differently, read side by side.
///
/// This is the defect the frame exists to prevent: the reader used to pair verses by number, so
/// asking for Joel 3 put the English third chapter beside the Hebrew third chapter, which is a
/// different passage. Here the offset is built deliberately and the pairing has to survive it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ParallelPairingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public ParallelPairingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task VersesArePairedByWhereTheySitInTheFrameAndNotByTheirNumber()
    {
        // The English text numbers this passage chapter 3; the Hebrew numbers it chapter 4. Both
        // belong at canonical 3, which is the only thing that makes them comparable.
        var english = Corpus.Add(_db, "kjv", TextKind.Translation, "eng", (3, 1, ["For", "behold"]));
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo", (4, 1, ["כִּי", "הִנֵּה"]));
        _db.SaveChanges();

        Place(english, 3, 1, canonicalChapter: 3, canonicalVerse: 1);
        Place(hebrew, 4, 1, canonicalChapter: 3, canonicalVerse: 1);
        _db.SaveChanges();

        var englishVerses = await Endpoints.Texts.ReadByCanonicalVerse(_db, english.Id, 1, 3, default);
        var hebrewVerses = await Endpoints.Texts.ReadByCanonicalVerse(_db, hebrew.Id, 1, 3, default);

        englishVerses.Should().ContainKey(1);
        hebrewVerses.Should().ContainKey(1);
        Rebuild(englishVerses[1]).Should().Be("For behold");
        Rebuild(hebrewVerses[1]).Should().Be("כִּי הִנֵּה");
    }

    /// <summary>
    /// Reading the chapter the Hebrew calls 3 must not answer with the passage the English calls 3.
    /// Under the old pairing they were the same request.
    /// </summary>
    [Fact]
    public async Task TheHebrewChapterOfTheSameNumberIsADifferentPassage()
    {
        var hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (3, 1, ["וְהָיָה"]),
            (4, 1, ["כִּי"]));
        _db.SaveChanges();

        // Hebrew 3:1 is canonically 2:28 — the tail of the previous chapter — and 4:1 is canonically 3:1.
        Place(hebrew, 3, 1, canonicalChapter: 2, canonicalVerse: 28);
        Place(hebrew, 4, 1, canonicalChapter: 3, canonicalVerse: 1);
        _db.SaveChanges();

        var atCanonicalThree = await Endpoints.Texts.ReadByCanonicalVerse(_db, hebrew.Id, 1, 3, default);
        var atCanonicalTwo = await Endpoints.Texts.ReadByCanonicalVerse(_db, hebrew.Id, 1, 2, default);

        Rebuild(atCanonicalThree[1]).Should().Be("כִּי");
        Rebuild(atCanonicalTwo[28]).Should().Be("וְהָיָה");
    }

    /// <summary>
    /// A text that does not reach this address has no row at all, which is a different fact from a
    /// verse with no words in it.
    /// </summary>
    [Fact]
    public async Task ATextWithNoVerseAtAnAddressAnswersNothingRatherThanEmptiness()
    {
        var greek = Corpus.Add(_db, "nestle1904", TextKind.CriticalEdition, "grc", (1, 1, ["Ἐν"]));
        _db.SaveChanges();
        Place(greek, 1, 1, canonicalChapter: 1, canonicalVerse: 1);
        _db.SaveChanges();

        var elsewhere = await Endpoints.Texts.ReadByCanonicalVerse(_db, greek.Id, 1, 9, default);

        elsewhere.Should().BeEmpty();
    }

    private void Place(Text text, int chapter, int verse, int canonicalChapter, int canonicalVerse)
    {
        var own = _db.VerseAt(text, chapter, verse);
        var existing = _db.VerseReferences.Where(r => r.VerseId == own.Id);
        _db.VerseReferences.RemoveRange(existing);
        _db.VerseReferences.Add(new VerseReference
        {
            VerseId = own.Id,
            CanonicalBook = 1,
            CanonicalChapter = canonicalChapter,
            CanonicalVerse = canonicalVerse,
            IsPrimary = true,
        });
    }

    private static string Rebuild(IEnumerable<TextWordResponse> words) =>
        string.Concat(words.Select(w => w.Text + w.Trailer)).TrimEnd();
}
