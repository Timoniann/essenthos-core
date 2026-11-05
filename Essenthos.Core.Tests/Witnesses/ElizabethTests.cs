using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Sword;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>The Church Slavonic Elizabeth Bible, read from CrossWire's module in the Synodal versification.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class ElizabethTests
{
    private const int Psalms = 19;
    private const int Daniel = 27;
    private const int Matthew = 40;
    private const int SecondEsdras = 69;
    private const int ThirdMaccabees = 80;

    internal static string Folder => TestResources.Folder(Path.Combine("ElizabethBible", "CSlElizabeth"));

    private static readonly Lazy<TextSource> Source = new(() => SwordTextSource.Read(Folder));

    private static BookDraft Book(int ordinal) => Source.Value.Books.Single(book => book.CanonicalOrdinal == ordinal);

    private static string Text(int ordinal, int chapter, int verse) =>
        string.Concat(Book(ordinal).Chapters.Single(one => one.Number == chapter).Verses.Single(one => one.Number == verse)
            .Words.Select(word => word.Surface + word.Trailer)).Trim();

    [Fact]
    public void ItIsAPublicDomainSlavonicTranslation()
    {
        var definition = Source.Value.Definition;
        definition.Validate();
        definition.Slug.Should().Be("CSLELIZABETH");
        definition.Kind.Should().Be(TextKind.Translation);
        definition.Language.Should().Be("chu");
        definition.Redistribution.Should().Be(Redistribution.PublicDomain);
        definition.About.Should().Contain("modernised").And.Contain("3 Maccabees");
        TextCorpus.Slugs.Should().Contain("CSLELIZABETH");
    }

    /// <summary>
    /// SWORD's index names no verse, so the Synodal layout is what puts every verse at its address:
    /// read with it, the module fills its index exactly and begins every book where the book begins.
    /// </summary>
    [Fact]
    public void TheSynodalLayoutReadsEveryBookAtItsOwnAddress()
    {
        Text(1, 1, 1).Should().Be("В начале сотвори Бог небо и землю.");
        Text(Matthew, 1, 1).Should().StartWith("Книга родства Иисуса Христа");
        Text(Psalms, 151, 1).Should().StartWith("Мал бех в братии моей");
        Book(Daniel).Chapters.Should().HaveCount(14);
        Source.Value.Books.SelectMany(book => book.Chapters).Sum(chapter => chapter.Verses.Count).Should().Be(36000);
    }

    /// <summary>
    /// The books beyond the Hebrew canon the module holds are read, and the two the printed Bible has
    /// and the electronic text does not are absent rather than empty.
    /// </summary>
    [Fact]
    public void TheDeuterocanonIsReadAndWhatTheFileLacksIsAbsent()
    {
        Source.Value.Books.Select(book => book.CanonicalOrdinal).Where(book => book > 66)
            .Should().Equal(67, 68, 70, 71, 72, 73, 74, 75, 76, 79);
        Source.Value.Books.Select(book => book.CanonicalOrdinal).Should().NotContain([SecondEsdras, ThirdMaccabees]);
        Source.Value.Books.Should().HaveCount(76);
    }

    [Fact]
    public void ASynodalModuleIsLaidOutInItsOwnBookOrder()
    {
        var (oldTestament, newTestament) = SwordModule.Layout("Synodal");

        oldTestament.Select(book => book.Book).Take(17).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 79, 15, 16);
        newTestament.Select(book => book.Book).Should().Equal(
            [40, 41, 42, 43, 44, 59, 60, 61, 62, 63, 64, 65, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 66]);
        oldTestament.Single(book => book.Book == Psalms).Verses.Should().HaveCount(151);
    }
}
