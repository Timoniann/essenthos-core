using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Glaux;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

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
