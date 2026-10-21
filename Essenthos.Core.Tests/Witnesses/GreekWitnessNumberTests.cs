using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Migrations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>Joda of Luke 3:26, whom the critical editions tag with the number of the land of Judah.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class GreekWitnessNumberReadingTests
{
    private const int Luke = 42;

    private static TextSource Luke3(params (int Verse, string Surface, string Number)[] words) =>
        new(NestleTextSource.Definition,
        [
            new BookDraft(Luke, 1, "Luke", "luk",
            [
                new ChapterDraft(3,
                [
                    .. words.Select(w => new VerseDraft(w.Verse, [new WordDraft(w.Surface, " ", StrongNumber: w.Number)])),
                ]),
            ]),
        ]);

    private static string? Number(TextSource source, int verse) =>
        source.Books[0].Chapters[0].Verses.Single(v => v.Number == verse).Words[0].StrongNumber;

    [Theory]
    [InlineData("Ἰωδὰ")]
    [InlineData("ιωδα")]
    public void JodaIsReadUnderTheNumberOfTheMan(string printed) =>
        Number(GreekWitnessNumbers.Apply(Luke3((26, printed, "G2448"))), 26).Should().Be("G2455");

    /// <summary>
    /// The three editions as they are read: Joda under the man's number, and the land of Luke 1:39
    /// still under its own.
    /// </summary>
    [Fact]
    public void EveryCriticalEditionReadsJodaAsTheMan()
    {
        foreach (var edition in new[]
                 {
                     NestleTextSource.Read(TestResources.Nestle1904),
                     TischendorfTextSource.Read(TestResources.TischendorfFolder),
                     WestcottHortTextSource.Read(TestResources.WestcottHortFolder),
                 })
        {
            var luke = edition.Books.Single(book => book.CanonicalOrdinal == Luke);
            Numbers(luke, 3, 26).Should().Contain("G2455", edition.Definition.Slug).And.NotContain("G2448");
            Numbers(luke, 1, 39).Should().Contain("G2448", edition.Definition.Slug);
        }
    }

    private static IEnumerable<string?> Numbers(BookDraft book, int chapter, int verse) =>
        book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse).Words
            .Select(word => word.StrongNumber);

    /// <summary>The region keeps its number wherever it is the region, and so does any other word.</summary>
    [Fact]
    public void EveryOtherWordKeepsTheNumberItCarries()
    {
        var read = GreekWitnessNumbers.Apply(Luke3((30, "Ἰούδα", "G2455"), (26, "Ἰωσὴχ", "G2501")));

        Number(read, 30).Should().Be("G2455");
        Number(read, 26).Should().Be("G2501");
    }
}
