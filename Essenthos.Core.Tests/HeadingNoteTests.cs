using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
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
/// A note printed on a chapter's own heading: Biblica's Ukrainian marks six psalm labels (<c>\cl</c>)
/// and the first acrostic letter of Psalm 118 (<c>\qa</c>), each addressed as verse 0 of its psalm.
/// They are about the chapter, so they are kept with it and no verse is made to carry them.
/// </summary>
public class UsfmHeadingNoteTests
{
    private const string Psalms =
        """
        \id PSA
        \h Псалми
        \c 9
        \cl Псалом 9 (9–10)\f + \ft Нумерація псалмів відрізняється.\f*
        \d
        \v 1 Керівнику хору.
        \c 10
        \cl Псалом 10
        \q1
        \v 1 На Господа я покладаюся.
        \c 118
        \cl Псалом 118
        \qa א Алеф\f + \ft Цей псалом написано акровіршем.\f*
        \q1
        \v 1 Блаженні ті, чия дорога невинна,
        \qa ב Бет
        \q1
        \v 9 Як юнак збереже свою дорогу?
        """;

    [Fact]
    public void ANoteOnAChapterLabelIsKeptWithTheChapter() =>
        UsfmReader.Read(Psalms).Chapters.Single(chapter => chapter.Number == 9).Notes.Should().Equal(
            new UsfmHeadingNote("Псалом 9 (9–10)", new UsfmNote(UsfmNoteKind.Footnote, "Нумерація псалмів відрізняється.")));

    [Fact]
    public void ANoteOnAStanzasLetterIsKeptWithTheChapter() =>
        UsfmReader.Read(Psalms).Chapters.Single(chapter => chapter.Number == 118).Notes.Should().Equal(
            new UsfmHeadingNote("א Алеф", new UsfmNote(UsfmNoteKind.Footnote, "Цей псалом написано акровіршем.")));

    [Fact]
    public void NoVerseCarriesItAndAHeadingWithoutANoteGivesNone()
    {
        var book = UsfmReader.Read(Psalms);

        book.Chapters.SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Notes).Should().BeEmpty();
        book.Chapters.Single(chapter => chapter.Number == 10).Notes.Should().BeEmpty();
        book.Chapters.Single(chapter => chapter.Number == 9).Verses.Single().Words.Select(word => word.Surface)
            .Should().Equal("Керівнику", "хору");
        book.TitleNotes.Should().BeEmpty();
    }

    [Fact]
    public void ALabelBeforeTheFirstChapterIsTheBooks() =>
        UsfmReader.Read(
                """
                \id PSA
                \cl Псалом\f + \ft Псалом — пісня.\f*
                \c 1
                \q1
                \v 1 Блаженна людина,
                """)
            .TitleNotes.Should().Equal(new UsfmNote(UsfmNoteKind.Footnote, "Псалом — пісня."));

    [Fact]
    public void TheChaptersDraftCarriesThem() =>
        ChapterNoteDraft.From(UsfmReader.Read(Psalms).Chapters.Single(chapter => chapter.Number == 9)).Should().Equal(
            new ChapterNoteDraft("Псалом 9 (9–10)", VerseNoteKind.Footnote, "Нумерація псалмів відрізняється."));
}

/// <summary>The seven Biblica's Ukrainian prints, read from its Psalms as they are on disk.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class BiblicaHeadingNoteTests
{
    [Fact]
    public void TheSevenStandOnTheirPsalms()
    {
        var psalms = EbibleTextSource.Read(TestResources.EbibleFolder("BiblicaUkrainian2022")).Books
            .Single(book => book.CanonicalOrdinal == 19);

        var noted = psalms.Chapters.Where(chapter => chapter.Notes.Count > 0).ToList();

        noted.Select(chapter => (chapter.Number, chapter.Notes.Single().Heading)).Should().Equal(
            (9, "Псалом 9 (9–10)"), (111, "Псалом 111"), (113, "Псалом 113"), (114, "Псалом 114"),
            (118, "א Алеф"), (144, "Псалом 144"), (146, "Псалом 146"));
        noted.Single(chapter => chapter.Number == 114).Notes.Single().Content
            .Should().Be("114:0 У стандартному єврейському тексті Псалми 114-й і 115-й об’єднані в один.");
    }
}

[Collection(WitnessDatabaseCollection.Name)]
public sealed class HeadingNoteLoaderTests : IDisposable
{
    private const string Joined = "У стандартному єврейському тексті цей Псалом поділений на два.";

    private readonly AppDbContext _db;

    public HeadingNoteLoaderTests(WitnessDatabase database)
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

    private ChapterNoteLoader Loader() => new(_db, NullLogger<ChapterNoteLoader>.Instance);

    [Fact]
    public async Task AnAlreadyLoadedTextGetsItsHeadingNotesOnceAndOnlyOnce()
    {
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(Psalms());

        var first = await Loader().Load(Psalms(Joined));
        var second = await Loader().Load(Psalms(Joined));

        first.Should().Be(new ChapterNoteOutcome("headed", AlreadyLoaded: false, Chapters: 1, Notes: 1));
        second.AlreadyLoaded.Should().BeTrue();
        (await _db.ChapterNotes.Select(note => new { note.Chapter!.Number, note.Position, note.Heading, note.Content })
                .ToListAsync())
            .Should().Equal(new { Number = 113, Position = 1, Heading = "Псалом 113", Content = Joined });
        (await _db.VerseNotes.CountAsync()).Should().Be(0);
        (await _db.BookNotes.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// The edition's Psalm 113 is the Hebrew's 114 and 115: its note stands above the verse it opens
    /// with, which is the first of the canonical 114, and the canonical 115 it runs on into has none.
    /// </summary>
    [Fact]
    public async Task TheyStandAboveTheVerseTheirChapterOpensWith()
    {
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(Psalms());
        await Loader().Load(Psalms(Joined));
        var text = await _db.Texts.SingleAsync();
        foreach (var verse in await _db.Verses.ToListAsync())
        {
            _db.VerseReferences.Add(new VerseReference
            {
                VerseId = verse.Id,
                CanonicalBook = 19,
                CanonicalChapter = verse.Number < 9 ? 114 : 115,
                CanonicalVerse = verse.Number < 9 ? verse.Number : verse.Number - 8,
                IsPrimary = true,
            });
        }

        await _db.SaveChangesAsync();

        var opened = await Texts.ChapterNotesByCanonicalVerse(_db, text.Id, 19, 114, default);
        var runOn = await Texts.ChapterNotesByCanonicalVerse(_db, text.Id, 19, 115, default);
        var own = await Texts.ChapterNotesOfOwnChapter(_db, text.Id, 19, 113, default);
        var otherOwn = await Texts.ChapterNotesOfOwnChapter(_db, text.Id, 19, 112, default);

        var expected = new ChapterNotesResponse(1, "Псалом 113", [new SourceNoteResponse("footnote", Joined)]);
        opened.Keys.Should().Equal(1);
        opened[1].Should().ContainSingle().Which.Should().BeEquivalentTo(expected);
        runOn.Should().BeEmpty();
        own.Keys.Should().Equal(1);
        own[1].Should().ContainSingle().Which.Should().BeEquivalentTo(expected);
        otherOwn.Should().BeEmpty();
    }

    [Fact]
    public async Task AChapterTheTextDoesNotHoldIsRefused()
    {
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(Psalms());
        var elsewhere = Psalms() with
        {
            Books =
            [
                Psalms().Books[0] with
                {
                    Chapters = [new ChapterDraft(150, []) { Notes = [new("Псалом 150", VerseNoteKind.Footnote, "x")] }],
                },
            ],
        };

        var act = () => Loader().Load(elsewhere);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no such chapter*");
        (await _db.ChapterNotes.CountAsync()).Should().Be(0);
    }

    private static TextSource Psalms(params string[] notes) => new(Definition, [
        new BookDraft(19, 1, "Psalms", "psalms", [
            new ChapterDraft(112, [new VerseDraft(1, [new WordDraft("Алилуя", ".")])]),
            new ChapterDraft(113, [
                new VerseDraft(1, [new WordDraft("Коли", " "), new WordDraft("виходив", ".")]),
                new VerseDraft(9, [new WordDraft("Не", " "), new WordDraft("нам", ".")]),
            ])
            {
                Notes = [.. notes.Select(note => new ChapterNoteDraft("Псалом 113", VerseNoteKind.Footnote, note))],
            },
        ], NameNative: "Псалми"),
    ]);

    private static readonly TextDefinition Definition = new(
        Slug: "headed",
        Name: "A headed sample",
        NameNative: null,
        Kind: TextKind.Translation,
        Language: "ukr",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: 2022,
        SourceUrl: "https://example.invalid/headed",
        RightsHolder: null,
        Licence: "CC0-1.0",
        LicenceUrl: "https://creativecommons.org/publicdomain/zero/1.0/",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: null)
    {
        Translators = "Somebody",
        Editors = "Somebody else",
        Edition = "The first",
        EditionYear = 2022,
        About = "What this text is.",
        RightsNote = "What is unsettled about the rights.",
    };
}
