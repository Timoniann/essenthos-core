using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// Correcting what bible4u's King James and Synodal print wrong: read from the files and the
/// witnesses on this disk, made by the reader on a cold load and in place on a warm one.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextRepairTests : IDisposable
{
    private readonly AppDbContext _db;

    public TextRepairTests(WitnessDatabase database)
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

    private static bool KingJamesOnDisk =>
        Directory.Exists(TestResources.KingJames2006Folder) && File.Exists(TestResources.Bible4u("KJV"));

    [Fact]
    public void TheKingJamesTakesThe1769sCapitalsReadingsAndSubscriptions()
    {
        if (!KingJamesOnDisk)
        {
            return;
        }

        var repairs = KingJamesRepairs();
        var printed = repairs.Verses.ToDictionary(r => (r.Book, r.Chapter, r.Verse), r => r.Printed);

        printed[(46, 9, 1)].Should().StartWith("Am I not an apostle? am I not free?");
        printed[(2, 38, 22)].Should().Contain("Bezaleel the son of Uri");
        printed[(24, 30, 11)].Should().Contain("saith the LORD, to save thee").And.Contain("yet will I not make");
        printed[(1, 2, 4)].Should().Contain("the LORD God made the earth");
        printed[(19, 110, 1)].Should().StartWith("The LORD said unto my Lord,");
        printed[(45, 16, 27)].Should().EndWith(
            "Amen. Written to the Romans from Corinthus, and sent by Phebe servant of the church at Cenchrea.");
        printed[(41, 12, 7)].Should().EndWith("shall be ours.");

        // Only the capitals change in a verse whose reading is right: the words stay the file's.
        var genesis = repairs.Verses.Single(r => r is { Book: 1, Chapter: 2, Verse: 4 });
        genesis.After.Select(t => t.Word.ToLowerInvariant()).Should().Equal(genesis.Before.Select(t => t.Word.ToLowerInvariant()));

        repairs.Verses.Count(r => r.After.Count > r.Before.Count && r.Book >= 45 && r.Book <= 58 &&
                                  r.Printed.Contains(" written", StringComparison.OrdinalIgnoreCase)).Should().Be(14);
        repairs.Verses.Count(r => !r.After.Select(t => t.Word.ToLowerInvariant())
                .SequenceEqual(r.Before.Select(t => t.Word.ToLowerInvariant())))
            .Should().Be(27 - 1 + 14 + 95,
                "every reading but the stray quotation mark changes the words, and so does every subscription and "
                + "each of the 95 verses printing the possessive of the divine name apart");
        repairs.Verses.SelectMany(r => r.After).Count(t => t.Word is "LORD" or "LORD's").Should().BeGreaterThan(6_400);
        repairs.Verses.SelectMany(r => r.Before).Should().NotContain(t => t.Word == "LORD");
    }

    [Fact]
    public void EveryCorrectionOfTheSynodalNamesATokenItsFilePrints()
    {
        if (!File.Exists(TestResources.Bible4u("RUSV")))
        {
            return;
        }

        var repairs = SynodalRepairs();
        var printed = repairs.Verses.ToDictionary(r => (r.Book, r.Chapter, r.Verse), r => r.Printed);

        SynodalCorrections.All.Should().HaveCount(209);
        repairs.Verses.Should().HaveCount(130);
        printed[(11, 7, 14)].Should().Contain("Отец его Тирянин был медник").And.Contain("и производил у него");
        printed[(1, 10, 13)].Should().Contain("От Мицраима произошли Лудим");
        printed[(6, 4, 10)].Should().Contain("сказать народу – так, как завещал");
        printed[(11, 7, 19)].Should().Contain("[наподобие лилии]");
        printed[(38, 3, 8)].Should().EndWith("ОТРАСЛЬ.");

        // Words run together come apart and dashes written as a letter become punctuation again, so
        // the verses gain 162 words between them; the 14 whose count stands are letters and names.
        repairs.Verses.Sum(r => r.After.Count - r.Before.Count).Should().Be(162);
        repairs.Verses.Where(r => r.After.Count == r.Before.Count).Should().HaveCount(14);
    }

    private static TextRepairs KingJamesRepairs() =>
        Bible4uTextSource.Repairs(TestResources.Bible4u("KJV"), "KJV", TestResources.Folder("."));

    private static TextRepairs SynodalRepairs() =>
        Bible4uTextSource.Repairs(TestResources.Bible4u("RUSV"), "RUSV", TestResources.Folder("."));

    private Link Link(Word rendering, Word witness)
    {
        var link = new Link
        {
            FromTextId = rendering.TextId,
            ToTextId = witness.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.5,
            Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, WordId = rendering.Id, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, WordId = witness.Id, Side = LinkSide.To });
        _db.SaveChanges();
        return link;
    }

    private List<Word> Words(int book, int chapter, int verse) =>
    [
        .. _db.Words.AsNoTracking()
            .Where(w => w.Verse!.Book!.CanonicalOrdinal == book && w.Verse.ChapterNumber == chapter
                        && w.Verse.Number == verse && w.Text!.Kind == TextKind.Translation)
            .OrderBy(w => w.Position),
    ];

    private string Verse(int book, int chapter, int verse) =>
        string.Concat(Words(book, chapter, verse).Select(w => w.Surface + w.Trailer)).TrimEnd();
}
