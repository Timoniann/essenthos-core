using Essenthos.Core.Alexandrinus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>Codex Sinaiticus and Codex Vaticanus, their New Testaments as INTF transcribed them.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class CodexTests
{
    private const int Matthew = 40;

    private const int Mark = 41;

    private const int Luke = 42;

    private const int John = 43;

    private const int Acts = 44;

    private const int Romans = 45;

    private const int FirstCorinthians = 46;

    private const int Ephesians = 49;

    private const int FirstThessalonians = 52;

    private const int SecondThessalonians = 53;

    private const int FirstTimothy = 54;

    private const int Hebrews = 58;

    private const int James = 59;

    private const int FirstPeter = 60;

    private const int SecondJohn = 63;

    private const int ThirdJohn = 64;

    private const int Jude = 65;

    private const int Revelation = 66;

    private const string NotInTheManuscript = "-";

    private static readonly Lazy<TextSource> Sinaiticus = new(() => CodexTextSource.Sinaiticus.Read(TestResources.Folder(".")));

    private static readonly Lazy<TextSource> Vaticanus = new(() => CodexTextSource.Vaticanus.Read(TestResources.Folder(".")));

    private static BookDraft Book(TextSource source, int ordinal) => source.Books.Single(book => book.CanonicalOrdinal == ordinal);

    private static VerseDraft? Verse(TextSource source, int ordinal, int chapter, int verse) =>
        Book(source, ordinal).Chapters.SingleOrDefault(one => one.Number == chapter)?.Verses.SingleOrDefault(one => one.Number == verse);

    private static string Text(TextSource source, int ordinal, int chapter, int verse) =>
        string.Concat(Verse(source, ordinal, chapter, verse)!.Words.Select(word => word.Surface + word.Trailer)).Trim();

    private static List<(int Book, int Chapter, int Verse)> Addresses(TextSource source) =>
        [.. source.Books.SelectMany(book => book.Chapters.SelectMany(chapter =>
            chapter.Verses.Select(verse => (book.CanonicalOrdinal, chapter.Number, verse.Number))))];

    /// <summary>CNTR's verse keys for the same manuscript, which it transcribed independently.</summary>
    private static HashSet<(int, int, int)> Independent(CodexText codex, string number) =>
        File.ReadLines(Path.Combine(TestResources.Folder(codex.Folder), $"cntr-{number}.txt"))
            .Where(line => line.Length > 8 && char.IsAsciiDigit(line[0]) && line[9..].Trim() != NotInTheManuscript)
            .Select(line => (int.Parse(line[..2]), int.Parse(line[2..5]), int.Parse(line[5..8])))
            .ToHashSet();

    [Fact]
    public void BothAreManuscriptsUnderPlainAttribution()
    {
        foreach (var codex in CodexTextSource.All)
        {
            codex.Definition.Validate();
            codex.Definition.Kind.Should().Be(TextKind.ManuscriptTradition);
            codex.Definition.Redistribution.Should().Be(Redistribution.PermittedWithAttribution);
            codex.Definition.Licence.Should().Be("CC-BY-4.0");
            codex.Definition.PartSources.Should().ContainSingle().Which.Licence.Should().Be("CC-BY-4.0");
            TextCorpus.Slugs.Should().Contain(codex.Slug);
        }

        CodexTextSource.Sinaiticus.Slug.Should().Be("SIN");
        CodexTextSource.Vaticanus.Slug.Should().Be("VAT");
    }

    /// <summary>
    /// Every verse Sinaiticus has is a verse CNTR has, and CNTR has nine more: verses the first hand
    /// passed over and the first corrector wrote in, which are not the first hand's text.
    /// </summary>
    [Fact]
    public void SinaiticusHasTheVersesAnIndependentTranscriptionHasThatTheFirstHandWrote()
    {
        var read = Addresses(Sinaiticus.Value);
        var independent = Independent(CodexTextSource.Sinaiticus, "01");

        read.Should().HaveCount(7900).And.OnlyHaveUniqueItems();
        read.ToHashSet().IsSubsetOf(independent).Should().BeTrue();
        independent.Except(read).Order().Should().Equal(
            (Matthew, 12, 47), (Luke, 17, 35), (John, 19, 20), (John, 21, 25), (Acts, 2, 21),
            (Romans, 11, 30), (FirstCorinthians, 2, 15), (Ephesians, 2, 7), (Hebrews, 4, 9));
        Sinaiticus.Value.Books.Should().HaveCount(27);
    }

    [Fact]
    public void VaticanusHasExactlyTheVersesAnIndependentTranscriptionHas()
    {
        var read = Addresses(Vaticanus.Value);

        read.Should().HaveCount(7093).And.OnlyHaveUniqueItems();
        read.ToHashSet().SetEquals(Independent(CodexTextSource.Vaticanus, "03")).Should().BeTrue();
    }

    /// <summary>
    /// Vaticanus breaks off where its last leaf does, and what followed is absent rather than filled:
    /// the rest of Hebrews, the Pastorals, Philemon and Revelation.
    /// </summary>
    [Fact]
    public void VaticanusEndsWhereItsLastLeafEnds()
    {
        var hebrews = Book(Vaticanus.Value, Hebrews);
        hebrews.Chapters[^1].Number.Should().Be(9);
        hebrews.Chapters[^1].Verses[^1].Number.Should().Be(14);
        Vaticanus.Value.Books.Select(book => book.CanonicalOrdinal)
            .Should().NotContain([FirstTimothy, 55, 56, 57, Revelation]).And.HaveCount(22);
        Verse(Vaticanus.Value, Mark, 16, 8).Should().NotBeNull();
        Verse(Vaticanus.Value, Mark, 16, 9).Should().BeNull();
        Verse(Vaticanus.Value, John, 8, 1).Should().BeNull();
    }

    [Fact]
    public void TheBooksStandInEachCodexsOrder()
    {
        Sinaiticus.Value.Books.Select(book => book.CanonicalOrdinal).Should().Equal(
            40, 41, 42, 43, 45, 46, 47, 48, 49, 50, 51, 52, 53, 58, 54, 55, 56, 57, 44, 59, 60, 61, 62, 63, 64, 65, 66);
        Vaticanus.Value.Books.Select(book => book.CanonicalOrdinal).Should().Equal(
            40, 41, 42, 43, 44, 59, 60, 61, 62, 63, 64, 65, 45, 46, 47, 48, 49, 50, 51, 52, 53, 58);
        foreach (var source in new[] { Sinaiticus.Value, Vaticanus.Value })
        {
            source.Books.Select(book => book.Position).Should().Equal(Enumerable.Range(1, source.Books.Count));
        }
    }

    [Fact]
    public void VersesLabelledInAnotherFormStandInTheirOwnBooks()
    {
        Text(Sinaiticus.Value, ThirdJohn, 1, 1).Should().StartWith("ο πρεσβυτερος γαιω");
        Text(Sinaiticus.Value, Jude, 1, 1).Should().StartWith("ιουδας");
        Verse(Sinaiticus.Value, SecondJohn, 1, 13).Should().NotBeNull();
        Verse(Sinaiticus.Value, FirstThessalonians, 4, 1).Should().NotBeNull();
        Book(Sinaiticus.Value, Hebrews).Chapters.Single(chapter => chapter.Number == 9).Verses.Should().HaveCount(28);

        Text(Vaticanus.Value, Matthew, 17, 1).Should().StartWith("και μεθ ημερας εξ");
        Text(Vaticanus.Value, ThirdJohn, 1, 1).Should().StartWith("ο πρεσβυτερος γαιω");
        // Matthew 16:2b-3, the signs of the times, is not in the first hand.
        Book(Vaticanus.Value, Matthew).Chapters.Single(chapter => chapter.Number == 16).Verses.Should().HaveCount(27);
        Verse(Vaticanus.Value, Matthew, 16, 3).Should().BeNull();
    }

    /// <summary>
    /// A corrected place keeps the first hand in the text and names each corrector in the note, in
    /// the order the transcription gives them.
    /// </summary>
    [Fact]
    public void TheFirstHandIsTheTextAndEachCorrectorIsNamed()
    {
        var verse = Verse(Vaticanus.Value, Matthew, 1, 2)!;
        verse.Words.Select(word => word.Surface).Should().Contain("εγεννησεν").And.NotContain("εγεννησε");
        verse.Notes.Select(note => note.Content).Should().Contain("εγεννησεν] εγεννησε corr.2");

        Verse(Sinaiticus.Value, Romans, 11, 31)!.Notes.Should().NotBeEmpty();
        Sinaiticus.Value.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses)
            .SelectMany(one => one.Notes).Select(note => note.Content)
            .Should().Contain(note => note.Contains("corr.1;") && note.Contains("corr.2"));
    }

    /// <summary>
    /// What the transcription types where a hand wrote nothing — <c>OM</c> in a few leaves of
    /// 1 Thessalonians — is an absence, and the zero-width spaces and typed overlines some leaves
    /// carry are not letters.
    /// </summary>
    [Fact]
    public void PlaceholdersAndMarksAreNotLetters()
    {
        var words = new[] { Sinaiticus.Value, Vaticanus.Value }
            .SelectMany(source => source.Books)
            .SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words)
            .Select(word => word.Surface)
            .ToList();

        words.Should().NotContain("OM");
        words.Should().NotContain(word => word.Contains('​') || word.Contains('̅'));
        Verse(Sinaiticus.Value, FirstThessalonians, 2, 19)!.Notes.Select(note => note.Content)
            .Should().Contain("—] η corr.2");
        Text(Sinaiticus.Value, FirstThessalonians, 2, 15).Should().Contain("κν");
    }

    [Fact]
    public void ACorrectorIsNamedAsTheTranscriptionNamesIt()
    {
        NtvmrTranscription.Corrector("corrector").Should().Be("corr.");
        NtvmrTranscription.Corrector("corrector1").Should().Be("corr.1");
        NtvmrTranscription.Corrector("corrector2a").Should().Be("corr.2a");
        NtvmrTranscription.Corrector("2").Should().Be("corr.2");
        NtvmrTranscription.Corrector("corrector1V").Should().Be("corr.1 vid.");
        NtvmrTranscription.Corrector(null).Should().Be("corr.");
    }

    [Fact]
    public void ALabelNeitherFormNorRepairIsRefused()
    {
        var act = () => NtvmrTranscription.Address("Rom.1.1", NtvmrTranscription.Vaticanus);

        act.Should().Throw<InvalidOperationException>().WithMessage("*CNTR*");
        NtvmrTranscription.Address("XXX.inscriptio", NtvmrTranscription.Sinaiticus).Should().BeNull();
        NtvmrTranscription.Address("XXX.1.1", NtvmrTranscription.Sinaiticus).Should().Be((ThirdJohn, 1, 1));
        NtvmrTranscription.Address("1Thess.2.14", NtvmrTranscription.Sinaiticus).Should().Be((FirstThessalonians, 2, 14));
    }

    [Fact]
    public void JamesFollowsActsInVaticanusAndSecondThessaloniansPrecedesHebrewsInBoth()
    {
        var vaticanus = Vaticanus.Value.Books.Select(book => book.CanonicalOrdinal).ToList();
        vaticanus.IndexOf(James).Should().Be(vaticanus.IndexOf(Acts) + 1);
        foreach (var source in new[] { Sinaiticus.Value, Vaticanus.Value })
        {
            var order = source.Books.Select(book => book.CanonicalOrdinal).ToList();
            order.IndexOf(Hebrews).Should().Be(order.IndexOf(SecondThessalonians) + 1);
        }

        Text(Vaticanus.Value, FirstPeter, 1, 1).Should().StartWith("πετρος αποστολος");
    }
}

/// <summary>Both codices loaded and placed in the frame.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class CodexLoadTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ITestOutputHelper _output;

    public CodexLoadTests(WitnessDatabase database, ITestOutputHelper output)
    {
        _db = database.NewContext();
        _output = output;
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    /// <summary>
    /// Every verse finds its address in the frame, and only where the Greek numbers a verse
    /// otherwise than the English the frame is written in does the address differ from the label.
    /// </summary>
    [Fact]
    public async Task EveryVerseStandsInTheFrame()
    {
        var rules = TvtmsReader.Read(TestResources.Tvtms);
        foreach (var codex in CodexTextSource.All)
        {
            await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(codex.Read(TestResources.Folder(".")));
            var text = await _db.Texts.SingleAsync(one => one.Slug == codex.Slug);
            await new CanonicalFrameLoader(_db, NullLogger<CanonicalFrameLoader>.Instance).Place(text, rules);

            var unplaced = await _db.Verses.CountAsync(verse => verse.TextId == text.Id
                && !_db.VerseReferences.Any(r => r.VerseId == verse.Id && r.IsPrimary));
            var moved = await _db.VerseReferences
                .Where(r => r.IsPrimary && r.Verse!.TextId == text.Id
                            && (r.CanonicalChapter != r.Verse.ChapterNumber || r.CanonicalVerse != r.Verse.Number))
                .Select(r => r.Verse!.Book!.Slug + " " + r.Verse.ChapterNumber + ":" + r.Verse.Number
                             + " -> " + r.CanonicalChapter + ":" + r.CanonicalVerse)
                .ToListAsync();
            _output.WriteLine($"{codex.Slug}: unplaced {unplaced}; placed elsewhere: {string.Join(", ", moved)}");

            unplaced.Should().Be(0);
            moved.Should().BeEquivalentTo(
            [
                "3-john 1:15 -> 1:14", "2-corinthians 13:13 -> 13:14", "philippians 1:16 -> 1:17",
                "philippians 1:17 -> 1:16", .. codex == CodexTextSource.Sinaiticus ? ["revelation 12:18 -> 13:1"] : Array.Empty<string>(),
            ]);
        }
    }
}
