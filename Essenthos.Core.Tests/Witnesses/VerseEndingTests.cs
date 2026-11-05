using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// Writing the end of a verse onto a text whose file cut it short: Ohienko's Genesis 22:19 in
/// miniature, and then the seven verses of the real file against the real transcription.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class VerseEndingTests : IDisposable
{
    private readonly AppDbContext _db;

    public VerseEndingTests(WitnessDatabase database)
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
    public void TheTranscriptionIsWrittenInThePunctuationTheLoadedTextUses()
    {
        LostVerseEndings.InTheLoadedPunctuation(
                "І сказав Господь до сатани́: „Звідки ти йдеш?“ А сатана́ відповів Господе́ві й сказав: " +
                "„Я мандрував по землі та й перейшов її“.")
            .Should().Be(
                "І сказав Господь до сатани: Звідки ти йдеш? А сатана відповів Господеві й сказав: " +
                "Я мандрував по землі та й перейшов її.");

        LostVerseEndings.InTheLoadedPunctuation("Отож, Господь Бог допоможе Мені, — хто ж отой ?")
            .Should().Be("Отож, Господь Бог допоможе Мені, хто ж отой?");
    }

    [Fact]
    public void ALatinLetterInTheTranscriptionIsRefused()
    {
        var reading = () => LostVerseEndings.InTheLoadedPunctuation("I сказав Господь");

        reading.Should().Throw<InvalidOperationException>().WithMessage("*Latin letter*");
    }

    private List<Word> Words(int chapter, int verse) =>
    [
        .. _db.Words.AsNoTracking()
            .Where(w => w.Verse!.ChapterNumber == chapter && w.Verse.Number == verse)
            .OrderBy(w => w.Position),
    ];

    private string Verse(int chapter, int verse) => Verse(1, chapter, verse);

    private string Verse(int book, int chapter, int verse) => string.Concat(_db.Words
        .Where(w => w.Verse!.Book!.CanonicalOrdinal == book && w.Verse.ChapterNumber == chapter
                    && w.Verse.Number == verse)
        .OrderBy(w => w.Position)
        .Select(w => w.Surface + w.Trailer)).TrimEnd();
}
