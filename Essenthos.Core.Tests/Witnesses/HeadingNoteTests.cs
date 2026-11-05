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

namespace Essenthos.Core.Tests.Witnesses;

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
