using Essenthos.Core.ClearBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A translation written out of Clear Bible's token files: punctuation on the word it follows, a space
/// only where the file says one follows, and a psalm's title at the head of its first verse.
/// </summary>
public sealed class ClearBibleTextSourceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"essenthos-tokens-{Guid.NewGuid():N}");

    public ClearBibleTextSourceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Tokens(params string[] rows)
    {
        var path = Path.Combine(_folder, $"{Guid.NewGuid():N}.tsv");
        File.WriteAllLines(path,
            ["id\tsource_verse\ttext\tskip_space_after\texclude\tid_range_end\tsource_verse_range_end", .. rows]);
        return path;
    }

    private static string Written(IEnumerable<Loading.WordDraft> words) =>
        string.Concat(words.Select(word => word.Surface + word.Trailer));

    [Fact]
    public void AVerseReadsAsTheEditionPrintsIt()
    {
        var path = Tokens(
            "01001001001\t01001001\tA\t\t\t\t",
            "01001001002\t01001001\tfarko-farko\ty\t\t\t",
            "01001001003\t01001001\t,\t\ty\t\t",
            "01001001004\t01001001\tAllah\t\t\t\t",
            "01001001005\t01001001\tya\t\t\t\t",
            "01001001006\t01001001\thalicci\ty\t\t\t",
            "01001001007\t01001001\t.\t\ty\t\t");

        var verse = ClearBibleTextSource.Verses(path).Should().ContainSingle().Which;

        verse.Address.Should().Be((1, 1, 1));
        verse.Words.Select(word => word.Surface).Should().Equal("A", "farko-farko", "Allah", "ya", "halicci");
        Written(verse.Words).Should().Be("A farko-farko, Allah ya halicci.");
    }

    [Fact]
    public void PunctuationOpeningAVerseOpensItsFirstWord()
    {
        var path = Tokens(
            "40001001001\t40001001\t“\ty\ty\t\t",
            "40001001002\t40001001\tKai\ty\t\t\t",
            "40001001003\t40001001\t”\t\ty\t\t");

        ClearBibleTextSource.Verses(path).Single().Words.Should().ContainSingle()
            .Which.Should().Be(new Loading.WordDraft("“Kai", "”"));
    }

    [Fact]
    public void APsalmsTitleStandsAtTheHeadOfItsFirstVerse()
    {
        var path = Tokens(
            "19003000001\t19003000\tZabura\t\t\t\t",
            "19003000002\t19003000\tta\t\t\t\t",
            "19003000003\t19003000\tDawuda\ty\t\t\t",
            "19003000004\t19003000\t.\t\ty\t\t",
            "19003001001\t19003001\tYa\t\t\t\t",
            "19003001002\t19003001\tUbangiji\ty\t\t\t",
            "19003001003\t19003001\t!\t\ty\t\t",
            "19003002001\t19003002\tDa\ty\t\t\t");

        var source = ClearBibleTextSource.Read(ClearBibleTextSource.Definition, [path]);

        var verses = source.Books.Single().Chapters.Single().Verses;
        verses.Select(verse => verse.Number).Should().Equal(1, 2);
        Written(verses[0].Words).Should().Be("Zabura ta Dawuda. Ya Ubangiji!");
        verses[0].MarksASuperscription.Should().BeTrue();
        verses[1].MarksASuperscription.Should().BeFalse();
    }
}
