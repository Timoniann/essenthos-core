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

namespace Essenthos.Core.Tests.Witnesses;

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
