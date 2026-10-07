using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Glaux;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The words eBible's Brenton Greek prints twice or lost, read as Brenton's English, Swete and GLAUx
/// read them.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class BrentonEditTests(Brenton brenton) : IClassFixture<Brenton>
{
    private const int Exodus = 2;

    private const int Job = 18;

    private const int FirstEsdras = 68;

    private string Read(int book, int chapter, int verse) =>
        BrentonDivisions.Printed(brenton.Book(book).Chapters.Single(c => c.Number == chapter).Verses
            .Single(v => v.Number == verse && v.Label.Length == 0).Words);

    [Fact]
    public void TheRepeatedClauseStandsOnceAtTheEndOfExodus33Nine()
    {
        Read(Exodus, 33, 9).Should().EndWith("καὶ ἐλάλει Μωσῇ·");
        Read(Exodus, 33, 10).Should().StartWith("Καὶ ἑώρα πᾶς ὁ λαὸς");
    }

    [Fact]
    public void Job3FourteenBeginsWithTheKings() =>
        Read(Job, 3, 14).Should().StartWith("μετὰ βασιλέων βουλευτῶν γῆς");

    [Fact]
    public void FirstEsdras8KeepsTheClauseBetweenTheTwoEndingsAlike()
    {
        Read(FirstEsdras, 8, 17).Should().EndWith("εἰς τὴν χρείαν τοῦ ἱεροῦ τοῦ Θεοῦ σου τοῦ ἐν Ἱερουσαλὴμ,");
        Read(FirstEsdras, 8, 18).Should().Be(
            "Καὶ τὰ λοιπὰ ὅσα ἂν ὑποπίπτῃ σοι εἰς τὴν χρείαν τοῦ ἱεροῦ τοῦ Θεοῦ σου, δώσεις ἐκ τοῦ βασιλικοῦ γαζοφυλακίου.");
    }

    [Fact]
    public void AnEditThatNoLongerFindsItsWordsStopsTheRead()
    {
        var verse = new VerseDraft(10, [new WordDraft("Καὶ", " "), new WordDraft("ἑώρα", " ")]);
        var act = () => BrentonEdits.Apply(Exodus, [new ChapterDraft(33, [verse])]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*33:10*");
    }
}

/// <summary>The edits made in place on a corpus that loaded Brenton's Greek before them.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class BrentonEditLoadTests : IDisposable
{
    private static readonly int[] Books = [2, 18, 68];

    private readonly AppDbContext _db;
    private readonly BrentonEditLoader _loader;

    public BrentonEditLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new BrentonEditLoader(_db, NullLogger<BrentonEditLoader>.Instance);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    /// <summary>The chapters the edits stand in, as the file prints them.</summary>
    private async Task<Text> Loaded()
    {
        var file = SeptuagintTextSource.Read(TestResources.SeptuagintFolder, edited: false);
        var chapters = BrentonEdits.All.Select(e => (e.Book, e.Chapter)).ToHashSet();
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(new TextSource(file.Definition,
        [
            .. file.Books.Where(b => Books.Contains(b.CanonicalOrdinal))
                .Select(b => b with { Chapters = [.. b.Chapters.Where(c => chapters.Contains((b.CanonicalOrdinal, c.Number)))] }),
        ]));
        return await _db.Texts.SingleAsync(t => t.Slug == SeptuagintTextSource.Slug);
    }

    private Word At(Text text, int book, int chapter, int verse, int position) =>
        _db.Words.Single(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == book
                              && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse && w.Verse.Label == ""
                              && w.Position == position);

    private string Stored(Text text, int book, int chapter, int verse) =>
        string.Concat(_db.Words.AsNoTracking()
            .Where(w => w.TextId == text.Id && w.Verse!.Book!.CanonicalOrdinal == book
                        && w.Verse.ChapterNumber == chapter && w.Verse.Number == verse && w.Verse.Label == "")
            .OrderBy(w => w.Position)
            .Select(w => w.Surface + w.Trailer)
            .ToList()).TrimEnd();

    /// <summary>
    /// The words both readings hold keep their rows; a repeated word goes with the Strong number its own
    /// lemma gave it; the lost words are written, and a second run writes nothing.
    /// </summary>
    [Fact]
    public async Task EachVerseReadsAsTheEditionAndWhatItSharesKeepsItsRows()
    {
        var text = await Loaded();
        var repeated = At(text, 18, 3, 14, 1);
        repeated.Surface.Should().Be("Νῦν");
        var kings = At(text, 18, 3, 14, 8);
        kings.Surface.Should().Be("μετὰ");
        _db.WordStrongs.Add(new WordStrong
        {
            WordId = repeated.Id, Number = "G3568", Method = LinkMethod.Lexical, Confidence = 0.9,
            Source = SeptuagintStrongLoader.Source, Note = "through the lemma νῦν",
        });
        await _db.SaveChangesAsync();

        var outcome = await _loader.Load(TestResources.SeptuagintFolder);

        outcome.Verses.Should().Be(4);
        outcome.Removed.Should().Be(10);
        outcome.Added.Should().Be(18);
        outcome.Books.Should().Equal(2, 18, 68);
        var edition = SeptuagintTextSource.Read(TestResources.SeptuagintFolder);
        foreach (var edit in BrentonEdits.All)
        {
            Stored(text, edit.Book, edit.Chapter, edit.Verse).Should().Be(BrentonDivisions.Printed(
                edition.Books.Single(b => b.CanonicalOrdinal == edit.Book).Chapters.Single(c => c.Number == edit.Chapter)
                    .Verses.Single(v => v.Number == edit.Verse && v.Label.Length == 0).Words));
        }

        (await _db.Words.AsNoTracking().SingleAsync(w => w.Id == kings.Id)).Position.Should().Be(1);
        (await _db.Words.AnyAsync(w => w.Id == repeated.Id)).Should().BeFalse();
        (await _db.Texts.AsNoTracking().SingleAsync(t => t.Id == text.Id)).RightsNote.Should().Contain(BrentonEdits.Note);

        (await _loader.Load(TestResources.SeptuagintFolder)).Verses.Should().Be(0);
    }

    /// <summary>A Strong number somebody stated on a repeated word is a statement, and the pass stops unread.</summary>
    [Fact]
    public async Task AStatementOnARepeatedWordStopsThePass()
    {
        var text = await Loaded();
        var repeated = At(text, 18, 3, 14, 1);
        _db.WordStrongs.Add(new WordStrong
        {
            WordId = repeated.Id, Number = "G3568", Method = LinkMethod.Manual, Source = "a reader",
        });
        await _db.SaveChangesAsync();

        var editing = () => _loader.Load(TestResources.SeptuagintFolder);

        await editing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*protected*");
        Stored(text, 18, 3, 14).Should().StartWith("Νῦν ἂν κοιμηθεὶς");
    }
}
