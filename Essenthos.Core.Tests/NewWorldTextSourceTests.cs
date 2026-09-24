using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The New World Translation is read from the publisher's EPUB and never loaded. The markup is
/// checked on chapter files written here in its shape, with words of this test's own, because the
/// edition's text may not be copied into a repository; the owner's copy is read whole where it is.
/// </summary>
public class NewWorldTextSourceTests
{
    private const string Psalm =
        """
        <body class="jwac"><p class="w_navigation w_biblebookname"><a href="biblebooknav.xhtml">Psalms</a>  <a href="biblechapternav19.xhtml">3</a> : <a href="bibleversenav19_3.xhtml">1 - 2</a></p>
        <p id="p1" data-pid="1" class="p1 sw"><span id="pos1"></span>A song of David, fleeing from Abʹsa·lom.</p>
        <p id="p2" data-pid="2" class="p2 ss">ל [Lamed]</p>
        <p id="p3" data-pid="3" class="p3 sl"><span id="chapter3"></span><span id="chapter3_verse1"></span><span class="w_ch"><strong>3</strong> </span>How many rise<span id="footnotesource1"></span><a epub:type="noteref" href="#footnote1">*</a> against me,</p>
        <p id="p4" data-pid="4" class="p4 sz">How&#160;many <span id="page9" class="pageNum" data-no="9"></span>stand in the way? (<em>Selah</em>)</p>
        <p id="p5" data-pid="5" class="p5 sl"><span id="chapter3_verse2"></span> <strong><sup>2</sup></strong> But you lift up my head.</p>
        <div class="groupFootnote"><aside epub:type="footnote"><div epub:type="footnote" id="footnote1"><p id="p9" data-pid="9"><a href="x.xhtml#footnotesource1">^ <span class="footnoteref">Ps. 3:1</span></a> Or “stand up.”</p></div></aside></div>
        </body>
        """;

    private const string Gospel =
        """
        <body class="jwac"><p class="w_navigation w_biblebookname"><a href="biblebooknav.xhtml">Matthew</a>  <a href="biblechapternav40.xhtml">17</a> : <a href="bibleversenav40_17.xhtml">20 - 22</a></p>
        <p id="p1" data-pid="1" class="p1 sb"><span id="chapter17_verse20"></span><strong><sup>20</sup></strong> Faith moves the mountain. <span id="chapter17_verse21"></span><strong><sup>21</sup></strong><span id="footnotesource2"></span><a epub:type="noteref" href="#footnote2">*</a> ——</p>
        <p id="p2" data-pid="2" class="p2 sb"><span id="chapter17_verse22"></span><strong><sup>22</sup></strong> They were gathered in Galʹi·lee.</p>
        </body>
        """;

    [Fact]
    public void AChapterFileBecomesTheVersesTheOtherEnglishTextsAreReadInto()
    {
        var book = Read("PSA", new NewWorldTextSource.EpubChapter(19, 3, "Psalms", Psalm));

        book.Name.Should().Be("Psalms");
        var chapter = book.Chapters.Should().ContainSingle().Subject;
        chapter.Number.Should().Be(3);
        chapter.Verses.Select(verse => verse.Number).Should().Equal(1, 2);

        // The superscription heads the first verse as it does in every English text here; the
        // acrostic letter, the footnote and its marker, the page number and the verse numbers do not
        // reach the text, and the names lose their stress marks.
        Text(chapter.Verses[0]).Should().Be(
            "A song of David, fleeing from Absalom. How many rise against me, How many stand in the way? (Selah)");
        Text(chapter.Verses[1]).Should().Be("But you lift up my head.");
    }

    [Fact]
    public void AVerseTheEditionPrintsAsADashIsNoVerse()
    {
        var book = Read("MAT", new NewWorldTextSource.EpubChapter(40, 17, "Matthew", Gospel));

        book.Chapters.Single().Verses.Select(verse => verse.Number).Should().Equal(20, 22);
        Text(book.Chapters.Single().Verses[1]).Should().Be("They were gathered in Galilee.");
    }

    [Fact]
    public void ItMayNotBeRedistributedAndNoLoaderOfTheCorpusListsIt()
    {
        NewWorldTextSource.Definition.Redistribution.Should().Be(Redistribution.Prohibited);
        NewWorldTextSource.Definition.Invoking(definition => definition.Validate())
            .Should().Throw<InvalidOperationException>().WithMessage("*never written to the corpus*");

        TextCorpus.Slugs.Should().NotContain(NewWorldTextSource.Slug);
    }

    [Fact]
    public void AnArchiveWithoutTheBibleIsRefused()
    {
        var folder = Directory.CreateTempSubdirectory("newworld");
        try
        {
            using (var archive = System.IO.Compression.ZipFile.Open(
                       Path.Combine(folder.FullName, NewWorldTextSource.FileName), System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("OEBPS/1.xhtml").Open());
                writer.Write(Psalm);
            }

            var act = () => NewWorldTextSource.Read(folder.FullName);

            act.Should().Throw<InvalidOperationException>().WithMessage("*no chapter of book 1*");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The owner's copy, where it is: the whole Bible in the English numbering, less the verses the
    /// edition does not print. It does not number Mark 16:9-20 or John 7:53-8:11 at all and prints a
    /// dash for sixteen more, where its Greek does not have them.
    /// </summary>
    [Fact]
    public void TheOwnersCopyIsTheWholeBible()
    {
        var folder = TestResources.EbibleFolder(NewWorldTextSource.Folder);
        if (!File.Exists(Path.Combine(folder, NewWorldTextSource.FileName)))
        {
            return;
        }

        var source = NewWorldTextSource.Read(folder);
        var verses = source.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses).ToList();

        source.Books.Select(book => book.CanonicalOrdinal).Should().Equal(Enumerable.Range(1, 66));
        source.Books.Sum(book => book.Chapters.Count).Should().Be(1189);
        verses.Should().HaveCount(23145 + 7957 - 12 - 12 - 16);
        verses.SelectMany(verse => verse.Words)
            .Should().NotContain(word => word.Surface.Contains('ʹ') || word.Surface.Contains('·'));
    }

    private static UsfmBook Read(string code, NewWorldTextSource.EpubChapter chapter) =>
        UsfmReader.Read(NewWorldTextSource.Usfm(code, [chapter]));

    private static string Text(UsfmVerse verse) =>
        string.Concat(verse.Words.Select(word => word.Surface + word.Trailer)).Trim();
}
