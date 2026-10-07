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

namespace Essenthos.Core.Tests;

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

/// <summary>
/// The Elizabeth Bible placed in the frame, measured against the Synodal: the two are Slavonic and
/// Russian versions of one Bible, so a verse placed at the right address shares words with the
/// Synodal's verse there, and a misplaced run shares almost none.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ElizabethPlacementTests : IDisposable
{
    private const int Psalms = 19;
    private const int FirstKings = 11;
    private const int Malachi = 39;

    private readonly AppDbContext _db;
    private readonly ITestOutputHelper _output;

    public ElizabethPlacementTests(WitnessDatabase database, ITestOutputHelper output)
    {
        _db = database.NewContext();
        _output = output;
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    /// <summary>The words of a verse cut to their first four letters, which Slavonic and Russian share far more than their endings.</summary>
    private static HashSet<string> Stems(IEnumerable<string> words) =>
    [
        .. words.Select(word => new string([.. word.ToLowerInvariant().Replace('ё', 'е').Where(char.IsLetter)]))
            .Where(word => word.Length >= 3).Select(word => word.Length > 4 ? word[..4] : word),
    ];

    [Fact]
    public async Task EveryVerseIsPlacedAndFewStandAgainstUnrelatedWords()
    {
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(SwordTextSource.Read(ElizabethTests.Folder));
        var text = await _db.Texts.SingleAsync();
        await new CanonicalFrameLoader(_db, NullLogger<CanonicalFrameLoader>.Instance).Place(text, TvtmsReader.Read(TestResources.Tvtms));

        var unplaced = await _db.Verses.CountAsync(v => v.TextId == text.Id && !_db.VerseReferences.Any(r => r.VerseId == v.Id && r.IsPrimary));
        var placed = await _db.VerseReferences.Where(r => r.IsPrimary && r.Verse!.TextId == text.Id && r.CanonicalBook <= 66)
            .Select(r => new { r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse, r.VerseId })
            .ToListAsync();
        var words = (await _db.Words.Where(w => w.TextId == text.Id).Select(w => new { w.VerseId, w.Surface }).ToListAsync())
            .GroupBy(w => w.VerseId).ToDictionary(g => g.Key, g => Stems(g.Select(w => w.Surface)));
        var synodal = Bible4uTextSource.Read(TestResources.Bible4u("RUSV"), "RUSV").Books
            .SelectMany(b => b.Chapters.SelectMany(c => c.Verses.Select(v => (Key: (b.CanonicalOrdinal, c.Number, v.Number), Stems: Stems(v.Words.Select(w => w.Surface))))))
            .ToDictionary(v => v.Key, v => v.Stems);

        var weak = new List<int>();
        foreach (var verse in placed)
        {
            if (synodal.TryGetValue((verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse), out var other)
                && words.TryGetValue(verse.VerseId, out var mine) && mine.Count >= 3 && other.Count >= 3
                && (double)mine.Intersect(other).Count() / Math.Min(mine.Count, other.Count) < 0.2)
            {
                weak.Add(verse.CanonicalBook);
            }
        }

        _output.WriteLine($"{placed.Count} placed, {unplaced} unplaced, {weak.Count} verses sharing under a fifth of their stems with the Synodal's; "
                          + string.Join(", ", weak.GroupBy(book => book).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key}: {g.Count()}")));

        unplaced.Should().Be(0);
        // Job and Proverbs are most of what is left, and they are the Synodal translating the Hebrew
        // where the Slavonic translates the Greek, not misplaced verses.
        weak.Count.Should().BeLessThanOrEqualTo(3_423);
        weak.Count(book => book == Psalms).Should().BeLessThan(240);
        weak.Count(book => book == FirstKings).Should().BeLessThanOrEqualTo(60);
        weak.Count(book => book == Malachi).Should().BeLessThanOrEqualTo(6);
    }
}
