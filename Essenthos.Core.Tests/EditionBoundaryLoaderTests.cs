using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Swete;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

[Trait(TestCategory.Name, TestCategory.Corpus)]
public class EditionBoundaryLoaderTests : IClassFixture<WitnessDatabase>, IDisposable
{
    private readonly AppDbContext db;
    public EditionBoundaryLoaderTests(WitnessDatabase fixture) => db = fixture.NewContext();
    public void Dispose() => db.Dispose();

    private async Task Reset() => await db.Database.ExecuteSqlRawAsync("TRUNCATE text RESTART IDENTITY CASCADE");

    [Fact]
    public async Task AHeadedWarmEditionKeepsItsRealWordsAndASecondPassWritesNothing()
    {
        await Reset();
        await SeedKjv();
        var heading = await db.Words.OrderBy(w => w.Position).FirstAsync(w => w.Verse!.Book!.CanonicalOrdinal == 67);
        var other = Corpus.Add(db, "OTHER", TextKind.CriticalEdition, "grc", (1, 1, ["λόγος"]));
        await db.SaveChangesAsync();
        var target = await db.Words.SingleAsync(w => w.TextId == other.Id);
        await db.Database.OpenConnectionAsync();
        await LinkWriter.Write((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), null,
            [new NewLink(heading.TextId, target.TextId, LinkRelation.Renders, LinkMethod.Aligner, .5,
                "a model heading guess", null, [heading.Id], [target.Id])], CancellationToken.None);
        var before = await db.Words.AsNoTracking().OrderBy(w => w.Id).ToListAsync();
        var loader = new EditionBoundaryRepairLoader(db);
        var outcome = await loader.Load(TestResources.Folder(string.Empty));
        outcome.RemovedWords.Should().Be(56);
        outcome.Texts.Should().Equal(Sources.KingJamesSlug);
        outcome.Pairs.Should().Contain(new BoundaryPair(Sources.KingJamesSlug, "OTHER"));
        (await db.Links.CountAsync()).Should().Be(0);
        var kept = await db.Words.AsNoTracking().OrderBy(w => w.Id).ToListAsync();
        kept.Select(w => w.Id).Should().BeSubsetOf(before.Select(w => w.Id));
        kept.Count.Should().Be(before.Count - 56);
        var baseline = kept.Select(w => (w.Id, w.VerseId, w.Position, w.Surface, w.Trailer)).ToArray();
        (await loader.Load(TestResources.Folder(string.Empty))).RemovedWords.Should().Be(0);
        (await db.Words.AsNoTracking().OrderBy(w => w.Id).ToListAsync())
            .Select(w => (w.Id, w.VerseId, w.Position, w.Surface, w.Trailer)).Should().Equal(baseline);
        foreach (var verse in await db.Verses.AsNoTracking().ToListAsync())
            (await db.Words.Where(w => w.VerseId == verse.Id).OrderBy(w => w.Position).Select(w => w.Position).ToListAsync())
                .Should().Equal(Enumerable.Range(1, kept.Count(w => w.VerseId == verse.Id)));
    }

    [Fact]
    public async Task AProtectedHeadingStopsTheWholeTransactionBeforeEarlierRepairsCanCommit()
    {
        await Reset();
        await SeedKjv();
        var heading = await db.Words.OrderBy(w => w.Position).FirstAsync(w => w.Verse!.Book!.CanonicalOrdinal == 72);
        var other = Corpus.Add(db, "OTHER", TextKind.CriticalEdition, "grc", (1, 1, ["λόγος"]));
        await db.SaveChangesAsync();
        var target = await db.Words.SingleAsync(w => w.TextId == other.Id);
        await db.Database.OpenConnectionAsync();
        await LinkWriter.Write((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), null,
            [new NewLink(heading.TextId, target.TextId, LinkRelation.Renders, LinkMethod.Manual, null,
                "owner reviewed testimony", null, [heading.Id], [target.Id])], CancellationToken.None);
        var before = await db.Words.AsNoTracking().OrderBy(w => w.Id).Select(w => new { w.Id, w.Position, w.Surface }).ToListAsync();
        var correcting = () => new EditionBoundaryRepairLoader(db).Load(TestResources.Folder(string.Empty));
        await correcting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*protected*evidence*");
        (await db.Words.AsNoTracking().OrderBy(w => w.Id).Select(w => new { w.Id, w.Position, w.Surface }).ToListAsync())
            .Should().BeEquivalentTo(before, options => options.WithStrictOrdering());
        (await db.LinkClaims.SingleAsync()).Method.Should().Be(LinkMethod.Manual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwetesSplitPreservesEveryWordAndManualClaimAndRejectsAnUnknownCombinedReading(bool historical)
    {
        await Reset();
        var source = SweteTextSource.Read(TestResources.SweteFolder, chapterMarkers: false);
        var book = source.Books.Single(b => b.CanonicalOrdinal == 10);
        book.CanonicalOrdinal.Should().Be(10);
        var chapter = book.Chapters.Single(c => c.Number == 19);
        var first = chapter.Verses.Single(v => v.Number == 42);
        var last = chapter.Verses.Single(v => v.Number == 43);
        var raw = SweteReader.Read(SweteRestorations.Apply("12.Regnorum_II",
            File.ReadLines(Path.Combine(TestResources.SweteFolder, "12.Regnorum_II.txt")),
            [.. SweteCorrections.All.Where(c => c.Book == "12.Regnorum_II" && c.Chapter == 19 && c.Verse == 42)]),
            keepChapterMarkers: true);
        raw.Number.Should().Be(12);
        var rawWords = raw.Chapters.Single(c => c.Number == 19).Verses.Single(v => v.Number == 42).Words;
        var warmWords = historical ? rawWords.Select(w => new WordDraft(w.Surface, w.Trailer)).ToArray()
            : first.Words.Concat(last.Words).ToArray();
        var warm = new TextSource(source.Definition, [book with { Chapters = [
            book.Chapters.Single(c => c.Number == 18), chapter with
            { Verses = [.. chapter.Verses.Where(v => v.Number < 42), first with { Words = warmWords }] }] }]);
        await new CorpusLoader(db, NullLogger<CorpusLoader>.Instance).Load(warm);
        var before = await db.Words.AsNoTracking().Where(w => w.Verse!.ChapterNumber == 19 && w.Verse.Number == 42)
            .OrderBy(w => w.Position).ToListAsync();
        var moved = before[first.Words.Count];
        var other = Corpus.Add(db, "OTHER", TextKind.CriticalEdition, "grc", (1, 1, ["λόγος"]));
        await db.SaveChangesAsync();
        var target = await db.Words.SingleAsync(w => w.TextId == other.Id);
        await db.Database.OpenConnectionAsync();
        await LinkWriter.Write((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), null,
            [new NewLink(moved.TextId, target.TextId, LinkRelation.Renders, LinkMethod.Manual, null,
                "owner reviewed testimony", null, [moved.Id], [target.Id])], CancellationToken.None);
        await db.Words.Where(w => w.Id == moved.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Surface, "unknown"));
        var correcting = () => new EditionBoundaryRepairLoader(db).Load(TestResources.Folder(string.Empty));
        await correcting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*known combined*");
        (await db.Verses.CountAsync(v => v.TextId == moved.TextId)).Should().Be(75);
        await db.Words.Where(w => w.Id == moved.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Surface, moved.Surface));
        var outcome = await new EditionBoundaryRepairLoader(db).Load(TestResources.Folder(string.Empty));
        outcome.MovedWords.Should().Be(last.Words.Count);
        outcome.RemovedWords.Should().Be(1);
        var actual = await db.Words.AsNoTracking().Where(w => w.TextId == moved.TextId
                && w.Verse!.ChapterNumber == 19 && w.Verse.Number >= 42)
            .OrderBy(w => w.Verse!.Number).ThenBy(w => w.Position).ToListAsync();
        actual.Select(w => w.Id).Should().Equal(before.SkipLast(1).Select(w => w.Id));
        actual.Select(w => w.Surface + w.Trailer).Should().Equal(before.SkipLast(1).Select(w => w.Surface + w.Trailer));
        (await db.LinkClaims.SingleAsync()).Method.Should().Be(LinkMethod.Manual);
        (await db.LinkWords.SingleAsync(w => w.WordId == moved.Id)).WordId.Should().Be(moved.Id);
        (await new EditionBoundaryRepairLoader(db).Load(TestResources.Folder(string.Empty))).MovedWords.Should().Be(0);
        var next = await db.Verses.SingleAsync(v => v.TextId == moved.TextId && v.Number == 43);
        (await db.Words.AsNoTracking().SingleAsync(w => w.Id == moved.Id)).VerseId.Should().Be(next.Id);
        var text = await db.Texts.SingleAsync(t => t.Id == moved.TextId);
        var placer = new CanonicalFrameLoader(db, NullLogger<CanonicalFrameLoader>.Instance);
        var rules = TvtmsReader.Read(TestResources.Tvtms);
        await placer.Place(text, rules);
        (await db.VerseReferences.AsNoTracking().SingleAsync(r => r.VerseId == next.Id && r.IsPrimary))
            .CanonicalVerse.Should().Be(43);
        (await placer.Place(text, rules)).AlreadyPlaced.Should().BeTrue();
        db.AddBook(other, 10, "Second Samuel", (19, 43, ["καὶ"]));
        var manualVerse = new VerseLink
        {
            FromTextId = text.Id, ToTextId = other.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.Manual, Source = "owner reviewed verse testimony",
            Verses = [new VerseLinkVerse { VerseId = next.Id, Side = LinkSide.From },
                new VerseLinkVerse { VerseId = target.VerseId, Side = LinkSide.To }],
        };
        db.VerseLinks.Add(manualVerse);
        await db.SaveChangesAsync();
        var verseLinks = new VerseLinkLoader(db, NullLogger<VerseLinkLoader>.Instance);
        await verseLinks.Refresh(new HashSet<string> { SweteTextSource.Slug });
        var joined = await db.VerseLinkVerses.Where(m => m.VerseId == next.Id).Select(m => m.VerseLinkId).ToListAsync();
        joined.Should().Contain(manualVerse.Id);
        joined.Should().HaveCount(2);
        (await db.VerseLinks.AsNoTracking().SingleAsync(l => l.Id == manualVerse.Id)).Source
            .Should().Be("owner reviewed verse testimony");
        (await db.LinkClaims.SingleAsync()).Method.Should().Be(LinkMethod.Manual);
    }

    [Fact]
    public async Task AProtectedSweteMarkerRollsBackTheSplitAndKeepsEveryClaim()
    {
        await Reset();
        var source = SweteTextSource.Read(TestResources.SweteFolder, chapterMarkers: false);
        var book = source.Books.Single(b => b.CanonicalOrdinal == 10);
        var chapter = book.Chapters.Single(c => c.Number == 19);
        var first = chapter.Verses.Single(v => v.Number == 42);
        var last = chapter.Verses.Single(v => v.Number == 43);
        var warm = new TextSource(source.Definition, [book with { Chapters = [chapter with
            { Verses = [first with { Words = [.. first.Words, .. last.Words] }] }] }]);
        await new CorpusLoader(db, NullLogger<CorpusLoader>.Instance).Load(warm);
        var marker = await db.Words.OrderByDescending(w => w.Position).FirstAsync();
        marker.Surface.Should().Be("XX");
        var other = Corpus.Add(db, "OTHER", TextKind.CriticalEdition, "grc", (1, 1, ["λόγος"]));
        await db.SaveChangesAsync();
        var target = await db.Words.SingleAsync(w => w.TextId == other.Id);
        await db.Database.OpenConnectionAsync();
        await LinkWriter.Write((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), null,
            [new NewLink(marker.TextId, target.TextId, LinkRelation.Renders, LinkMethod.Manual, null,
                "owner reviewed testimony", null, [marker.Id], [target.Id])], CancellationToken.None);
        var baseline = await db.Words.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.VerseId, w.Position, w.Surface, w.Trailer }).ToListAsync();
        var correcting = () => new EditionBoundaryRepairLoader(db).Load(TestResources.Folder(string.Empty));
        await correcting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*protected*evidence*");
        (await db.Words.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.VerseId, w.Position, w.Surface, w.Trailer }).ToListAsync())
            .Should().BeEquivalentTo(baseline, options => options.WithStrictOrdering());
        (await db.Verses.AnyAsync(v => v.TextId == marker.TextId && v.Number == 43)).Should().BeFalse();
        (await db.LinkClaims.SingleAsync()).Method.Should().Be(LinkMethod.Manual);
    }

    [Theory]
    [InlineData("derived")]
    [InlineData("manual")]
    [InlineData("source")]
    [InlineData("other-source")]
    [InlineData("rendering")]
    [InlineData("accepted-claim")]
    public async Task ASweteMarkerDiscardsOnlyItsOwnDerivedAbsence(string evidence)
    {
        await Reset();
        var source = SweteTextSource.Read(TestResources.SweteFolder, chapterMarkers: false);
        var book = source.Books.Single(b => b.CanonicalOrdinal == 10);
        var chapter = book.Chapters.Single(c => c.Number == 19);
        var first = chapter.Verses.Single(v => v.Number == 42);
        var last = chapter.Verses.Single(v => v.Number == 43);
        await new CorpusLoader(db, NullLogger<CorpusLoader>.Instance).Load(new TextSource(source.Definition,
            [book with { Chapters = [chapter with { Verses = [first, last] }] }]));
        var marker = await db.Words.SingleAsync(w => w.Surface == "XX");
        var other = Corpus.Add(db, "GRCBRENT", TextKind.CriticalEdition, "grc", (1, 1, ["λόγος"]));
        await db.SaveChangesAsync();
        var target = await db.Words.SingleAsync(w => w.TextId == other.Id);
        await db.Database.OpenConnectionAsync();
        var connection = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
        var method = evidence switch
        {
            "manual" => LinkMethod.Manual,
            "source" => LinkMethod.StatedBySource,
            _ => LinkMethod.Lexical,
        };
        var provenance = evidence == "other-source" ? "another lexical reader" : SeptuagintLinkLoader.Source;
        var relation = evidence == "rendering" ? LinkRelation.Renders : LinkRelation.Expands;
        await LinkWriter.Write(connection, null,
            [new NewLink(marker.TextId, target.TextId, relation, method,
                method is LinkMethod.Manual or LinkMethod.StatedBySource ? null : .85, provenance, null,
                [marker.Id], evidence == "rendering" ? [target.Id] : [])], CancellationToken.None);
        if (evidence == "accepted-claim")
            await LinkWriter.Write(connection, null,
                [new NewLink(marker.TextId, target.TextId, LinkRelation.Expands, LinkMethod.RuleBased, .85,
                    "accepted reading", null, [marker.Id], [])], CancellationToken.None);
        var before = await db.Words.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.VerseId, w.Position, w.Surface, w.Trailer }).ToListAsync();
        var claims = await db.LinkClaims.AsNoTracking().OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.LinkId, c.Method, c.ProvenanceId }).ToListAsync();
        var loader = new EditionBoundaryRepairLoader(db);
        if (evidence is "derived" or "rendering")
        {
            (await loader.Load(TestResources.Folder(string.Empty))).RemovedWords.Should().Be(1);
            (await db.Words.AnyAsync(w => w.Id == marker.Id)).Should().BeFalse();
            (await db.Links.CountAsync()).Should().Be(0);
            (await db.Words.AsNoTracking().OrderBy(w => w.Id)
                .Select(w => new { w.Id, w.VerseId, w.Position, w.Surface, w.Trailer }).ToListAsync())
                .Should().BeEquivalentTo(before.Where(w => w.Id != marker.Id), o => o.WithStrictOrdering());
            (await loader.Load(TestResources.Folder(string.Empty))).RemovedWords.Should().Be(0);
        }
        else
        {
            var correcting = () => loader.Load(TestResources.Folder(string.Empty));
            await correcting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*protected*evidence*");
            (await db.Words.AsNoTracking().OrderBy(w => w.Id)
                .Select(w => new { w.Id, w.VerseId, w.Position, w.Surface, w.Trailer }).ToListAsync())
                .Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
            (await db.LinkClaims.AsNoTracking().OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.LinkId, c.Method, c.ProvenanceId }).ToListAsync())
                .Should().BeEquivalentTo(claims, o => o.WithStrictOrdering());
        }
    }

    [Theory]
    [InlineData("word_strong")]
    [InlineData("word_parsing")]
    [InlineData("shared-aligner-side")]
    public async Task AChapterNumberAnythingElseStandsOnStopsThePassAndChangesNothing(string evidence)
    {
        await Reset();
        var marked = SweteTextSource.Read(TestResources.SweteFolder, chapterMarkers: false);
        var book = marked.Books.Single(b => b.CanonicalOrdinal == 10);
        await new CorpusLoader(db, NullLogger<CorpusLoader>.Instance).Load(new TextSource(marked.Definition,
            [book with { Chapters = [.. book.Chapters.Where(c => c.Number is 11 or 12)] }]));
        var marker = await db.Words.SingleAsync(w => w.Verse!.ChapterNumber == 11 && w.Verse.Number == 27 && w.Surface == "XII");
        var neighbour = await db.Words.SingleAsync(w => w.VerseId == marker.VerseId && w.Position == marker.Position - 1);
        switch (evidence)
        {
            case "word_strong":
                db.WordStrongs.Add(new WordStrong { WordId = marker.Id, Number = "G1427", Method = LinkMethod.StatedBySource, Source = "a test" });
                await db.SaveChangesAsync();
                break;
            case "word_parsing":
                db.WordParsings.Add(new WordParsing
                {
                    WordId = marker.Id, Morphology = System.Text.Json.JsonDocument.Parse("{}"),
                    Method = LinkMethod.StatedBySource, Source = "a test",
                });
                await db.SaveChangesAsync();
                break;
            default:
                var other = Corpus.Add(db, "OTHER", TextKind.CriticalEdition, "grc", (1, 1, ["λόγος"]));
                await db.SaveChangesAsync();
                var target = await db.Words.SingleAsync(w => w.TextId == other.Id);
                await db.Database.OpenConnectionAsync();
                await LinkWriter.Write((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), null,
                    [new NewLink(marker.TextId, target.TextId, LinkRelation.Renders, LinkMethod.Aligner, .5,
                        "a model guess over two words", null, [neighbour.Id, marker.Id], [target.Id])], CancellationToken.None);
                break;
        }

        var before = await db.Words.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.VerseId, w.Position, w.Surface, w.Trailer }).ToListAsync();
        var correcting = () => new EditionBoundaryRepairLoader(db).Load(TestResources.Folder(string.Empty));
        await correcting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*protected*");
        (await db.Words.AsNoTracking().OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.VerseId, w.Position, w.Surface, w.Trailer }).ToListAsync())
            .Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task ChapterNumbersGoWithTheirOwnMatcherLinksAndTheWordsAroundThemKeepTheirRows()
    {
        await Reset();
        var marked = SweteTextSource.Read(TestResources.SweteFolder, chapterMarkers: false);
        var read = SweteTextSource.Read(TestResources.SweteFolder);
        var book = marked.Books.Single(b => b.CanonicalOrdinal == 10);
        await new CorpusLoader(db, NullLogger<CorpusLoader>.Instance).Load(new TextSource(marked.Definition,
            [book with { Chapters = [.. book.Chapters.Where(c => c.Number is >= 11 and <= 17)] }]));
        var marker = await db.Words.SingleAsync(w => w.Verse!.ChapterNumber == 11 && w.Verse.Number == 27 && w.Surface == "XII");
        var other = Corpus.Add(db, "GEEZ81", TextKind.Translation, "gez", (1, 1, ["ቃል"]));
        await db.SaveChangesAsync();
        var target = await db.Words.SingleAsync(w => w.TextId == other.Id);
        await db.Database.OpenConnectionAsync();
        await LinkWriter.Write((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), null,
            [new NewLink(target.TextId, marker.TextId, LinkRelation.Renders, LinkMethod.Aligner, .5,
                "a model guess", null, [target.Id], [marker.Id])], CancellationToken.None);
        var before = await db.Words.AsNoTracking().Where(w => w.TextId == marker.TextId)
            .Select(w => new { w.Id, w.Surface, w.Trailer }).ToListAsync();

        var loader = new EditionBoundaryRepairLoader(db);
        var outcome = await loader.Load(TestResources.Folder(string.Empty));

        outcome.RemovedWords.Should().Be(3);
        outcome.Texts.Should().Equal(SweteTextSource.Slug);
        (await db.Links.CountAsync()).Should().Be(0);
        var after = await db.Words.AsNoTracking().Where(w => w.TextId == marker.TextId)
            .Select(w => new { w.Id, w.Surface, w.Trailer }).ToListAsync();
        after.Should().BeEquivalentTo(before.Where(w => w.Surface is not ("XII" or "XVI" or "XVII")));
        foreach (var chapter in read.Books.Single(b => b.CanonicalOrdinal == 10).Chapters.Where(c => c.Number is >= 11 and <= 17))
        foreach (var verse in chapter.Verses)
            (await db.Words.AsNoTracking().Where(w => w.TextId == marker.TextId && w.Verse!.ChapterNumber == chapter.Number
                    && w.Verse.Number == verse.Number).OrderBy(w => w.Position).Select(w => w.Surface + w.Trailer).ToListAsync())
                .Should().Equal(verse.Words.Select(w => w.Surface + w.Trailer), $"2 Samuel {chapter.Number}:{verse.Number}");
        var again = await loader.Load(TestResources.Folder(string.Empty));
        again.ToString().Should().Contain("nothing to do");
        (await db.Words.AsNoTracking().Where(w => w.TextId == marker.TextId)
            .Select(w => new { w.Id, w.Surface, w.Trailer }).ToListAsync()).Should().BeEquivalentTo(after);
    }

    private async Task SeedKjv()
    {
        var folder = TestResources.Folder(DeuterocanonTextSource.KingJamesFolder);
        var books = new List<BookDraft>();
        foreach (var (code, ordinal, chapter, verse) in new[] { ("BAR", 67, 6, 1), ("SIR", 72, 1, 1), ("BEL", 78, 1, 1) })
        {
            var file = Directory.GetFiles(folder, "*.usfm").Single(path =>
                File.ReadAllText(path).StartsWith($"\\id {code}", StringComparison.Ordinal));
            var read = UsfmReader.Read(File.ReadAllText(file));
            read.Book.Should().Be(code);
            var words = read.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse).Words;
            books.Add(new BookDraft(ordinal, books.Count + 1, BookReferences.Name(ordinal), BookReferences.Slug(ordinal),
                [new ChapterDraft(chapter, [new VerseDraft(verse, [.. words.Select(w => new WordDraft(w.Surface, w.Trailer))])])]));
        }
        await new CorpusLoader(db, NullLogger<CorpusLoader>.Instance)
            .Load(new TextSource(Bible4uTextSource.Definitions[Sources.KingJamesSlug], books));
    }
}
