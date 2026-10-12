using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A note printed on a book's title. Kulish prints ten, on the titles of Joshua, the books of
/// Samuel, Kings and the first of Chronicles, the Song, Isaiah, Jeremiah and Ezekiel. They are about
/// the book, so they are kept with it and no verse is made to carry them.
/// </summary>
public class UsfmTitleNoteTests
{
    private const string Titled =
        """
        \id 1KI
        \h ПЕРВА КНИГА ЦАРІВ
        \toc1 Перва книга царів
        \mt1 ПЕРВА КНИГА ЦАРІВ.\f + \ft Зветься також: Третя книга царів.\f*
        \c 1
        \p
        \v 1 Царь Давид же постарів ся.
        """;

    [Fact]
    public void ANoteOnTheTitleIsKeptWithTheBook() =>
        UsfmReader.Read(Titled).TitleNotes.Should().Equal(
            new UsfmNote(UsfmNoteKind.Footnote, "Зветься також: Третя книга царів."));

    [Fact]
    public void TheFirstVerseCarriesNoneOfIt()
    {
        var verse = UsfmReader.Read(Titled).Chapters.Single().Verses.Single();

        verse.Notes.Should().BeEmpty();
        verse.Words.Select(word => word.Surface).Should().Equal("Царь", "Давид", "же", "постарів", "ся");
    }

    [Fact]
    public void ATitleWithoutANoteGivesNone() =>
        UsfmReader.Read(Titled.Replace(@"\f + \ft Зветься також: Третя книга царів.\f*", ""))
            .TitleNotes.Should().BeEmpty();

    [Fact]
    public void TheBooksDraftCarriesThem() =>
        BookNoteDraft.From(UsfmReader.Read(Titled)).Should().Equal(
            new BookNoteDraft(VerseNoteKind.Footnote, "Зветься також: Третя книга царів."));
}

[Collection(WitnessDatabaseCollection.Name)]
public sealed class TitleNoteLoaderTests : IDisposable
{
    private readonly AppDbContext _db;

    public TitleNoteLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    private BookNoteLoader Loader() => new(_db, NullLogger<BookNoteLoader>.Instance);

    [Fact]
    public async Task AnAlreadyLoadedTextGetsItsTitleNotesOnceAndOnlyOnce()
    {
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(Kings());
        var noted = Kings(new BookNoteDraft(VerseNoteKind.Footnote, "Зветься також: Третя книга царів."));

        var first = await Loader().Load(noted);
        var second = await Loader().Load(noted);

        first.Should().Be(new BookNoteOutcome("titled", AlreadyLoaded: false, Books: 1, Notes: 1));
        second.AlreadyLoaded.Should().BeTrue();
        (await _db.BookNotes.Select(note => new { note.Book!.CanonicalOrdinal, note.Position, note.Content })
                .ToListAsync())
            .Should().Equal(new { CanonicalOrdinal = 11, Position = 1, Content = "Зветься також: Третя книга царів." });
        (await _db.VerseNotes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheyComeWithTheBooksFirstChapterAndNoOther()
    {
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(Kings());
        await Loader().Load(Kings(
            new BookNoteDraft(VerseNoteKind.Footnote, "Зветься також: Третя книга царів."),
            new BookNoteDraft(VerseNoteKind.CrossReference, "2 Сам. 24")));
        var text = await _db.Texts.SingleAsync();

        var opening = await Texts.BookNotes(_db, [text.Id], 11, 1, default);
        var later = await Texts.BookNotes(_db, [text.Id], 11, 2, default);

        opening.Should().ContainKey(text.Id);
        opening[text.Id].Title.Should().Be("Перва книга царів");
        opening[text.Id].Notes.Should().Equal(
            new SourceNoteResponse("footnote", "Зветься також: Третя книга царів."),
            new SourceNoteResponse("cross-reference", "2 Сам. 24"));
        later.Should().BeEmpty();
    }

    [Fact]
    public async Task ABookTheTextDoesNotHoldIsRefused()
    {
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(Kings());
        var elsewhere = Kings() with
        {
            Books = [Kings().Books[0] with { CanonicalOrdinal = 12, Notes = [new(VerseNoteKind.Footnote, "x")] }],
        };

        var act = () => Loader().Load(elsewhere);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no such book*");
        (await _db.BookNotes.CountAsync()).Should().Be(0);
    }

    private static TextSource Kings(params BookNoteDraft[] notes) => new(Definition, [
        new BookDraft(11, 1, "1 Kings", "1-kings", [
            new ChapterDraft(1, [new VerseDraft(1, [new WordDraft("Царь", " "), new WordDraft("Давид", ".")])]),
            new ChapterDraft(2, [new VerseDraft(1, [new WordDraft("Наближали", ".")])]),
        ], NameNative: "Перва книга царів")
        {
            Notes = notes,
        },
    ]);

    private static readonly TextDefinition Definition = new(
        Slug: "titled",
        Name: "A titled sample",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "ukr",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 1903,
        SourceUrl: "https://example.invalid/titled",
        RightsHolder: null,
        Licence: "CC0-1.0",
        LicenceUrl: "https://creativecommons.org/publicdomain/zero/1.0/",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: null)
    {
        Translators = "Somebody",
        Editors = "Somebody else",
        Edition = "The first",
        EditionYear = 1903,
        About = "What this text is.",
        RightsNote = "What is unsettled about the rights.",
    };
}

/// <summary>
/// The ten on the Kulish Bible itself, written into a text already loaded without them — which is
/// what the corpus holds today. Only each book's first verse is loaded: the notes hang on the books,
/// and the other 31,000 verses would only slow the test down.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class KulishTitleNoteLoadTests : IDisposable
{
    private readonly AppDbContext _db;

    public KulishTitleNoteLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    [Fact]
    public async Task TheTenAreWrittenOnceAndAgainNothing()
    {
        var kulish = KulishTextSource.Read(TestResources.KulishFolder);
        var opening = kulish with
        {
            Books = [
                .. kulish.Books.Select(book => book with
                {
                    Chapters = [new ChapterDraft(1, [book.Chapters[0].Verses[0]])],
                    Notes = [],
                }),
            ],
        };
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(opening);

        var first = await new BookNoteLoader(_db, NullLogger<BookNoteLoader>.Instance).Load(kulish);
        var second = await new BookNoteLoader(_db, NullLogger<BookNoteLoader>.Instance).Load(kulish);

        first.Should().Be(new BookNoteOutcome(KulishTextSource.Slug, AlreadyLoaded: false, Books: 10, Notes: 10));
        second.AlreadyLoaded.Should().BeTrue();
        (await _db.BookNotes.CountAsync()).Should().Be(10);
        (await _db.BookNotes.Select(note => note.Book!.CanonicalOrdinal).OrderBy(ordinal => ordinal).ToListAsync())
            .Should().Equal(6, 9, 10, 11, 12, 13, 22, 23, 24, 26);
        (await _db.VerseNotes.CountAsync()).Should().Be(0);
    }
}
