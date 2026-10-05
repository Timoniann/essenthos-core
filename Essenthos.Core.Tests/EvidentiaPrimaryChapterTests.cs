using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class EvidentiaPrimaryChapterTests : IDisposable
{
    private readonly AppDbContext db;
    private readonly IDbContextTransaction transaction;
    private readonly ITestOutputHelper output;
    private readonly string resources = Directory.CreateTempSubdirectory("evidentia-primary-chapter").FullName;

    public EvidentiaPrimaryChapterTests(WitnessDatabase database, ITestOutputHelper output)
    {
        db = database.NewContext();
        transaction = db.Database.BeginTransaction();
        this.output = output;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrozenJobBoundaryCountsTheSpanningSourceOnce(bool sourceStrong)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Resources", "evidentia", "job-boundary.json");
        var fixture = JsonSerializer.Deserialize<JobFixture>(await File.ReadAllTextAsync(fixturePath))!;
        foreach (var textVerses in fixture.Verses.GroupBy(verse => verse.Slug))
        {
            var slug = textVerses.Key;
            var text = new Text
            {
                Slug = slug, Name = slug,
                Kind = slug == "BHSA" ? TextKind.CriticalEdition : TextKind.Translation,
                Language = slug == "BHSA" ? "hbo" : "spa",
            };
            db.Texts.Add(text);
            db.AddBook(text, 18, "Job",
                [.. textVerses.Select(verse => (verse.Chapter, verse.Number, verse.Words.Select(word => word.Surface).ToArray()))]);
            await db.SaveChangesAsync();
            foreach (var verse in textVerses)
            {
                var row = db.VerseAt(text, verse.Chapter, verse.Number);
                await db.VerseReferences.Where(reference => reference.VerseId == row.Id).ExecuteDeleteAsync();
                db.VerseReferences.AddRange(verse.References.Select(reference => new VerseReference
                {
                    Verse = row, CanonicalBook = reference.Book, CanonicalChapter = reference.Chapter,
                    CanonicalVerse = reference.Verse, IsPrimary = reference.IsPrimary,
                }));
                foreach (var word in verse.Words)
                {
                    var stored = db.WordAt(text, verse.Chapter, verse.Number, word.Position);
                    stored.Trailer = word.Trailer;
                    stored.Lemma = word.Lemma;
                    stored.StrongNumber = word.StrongNumber;
                    stored.Gloss = word.Gloss;
                    stored.Morphology = word.Morphology is { } morphology ? JsonDocument.Parse(morphology.GetRawText()) : null;
                }
            }
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var books = await db.Books.AsNoTracking().Select(book => new { book.Name, book.CanonicalOrdinal }).ToListAsync();
        books.Should().HaveCount(2).And.OnlyContain(book => book.Name == "Job" && book.CanonicalOrdinal == 18);
        var loader = Loader();
        var measured = await loader.MeasureBook("RV1909", "BHSA", 18,
            new EvidentiaMeasurementOptions(AllowSourceStrongEvidence: sourceStrong,
                AllowKnownRenderingEvidence: false, RecordWords: true), firstChapter: 39, lastChapter: 40);
        var reverse = await loader.Preview("BHSA", "RV1909", 18, 40, 1, allowKnownRenderingEvidence: false);

        output.WriteLine(JsonSerializer.Serialize(new
        {
            fixture.CapturedAt,
            Books = books,
            SourceStrongEvidence = sourceStrong,
            KnownRenderingEvidence = false,
            measured.SourceWords,
            UniqueSourceWords = measured.Words.Select(word => word.SourceWordId).Distinct().Count(),
            measured.TargetWords,
            measured.GoldPairs,
            measured.GoldCoveredSourceWords,
            Chapters = measured.Chapters.Select(chapter => new
            {
                chapter.CanonicalChapter, chapter.SourceWords, chapter.TargetWords,
                chapter.ContentSourceWords, chapter.CoveredSourceWords, chapter.GoldPairs,
                chapter.SourceStrongWords, chapter.TargetStrongWords, chapter.SourceAnnotationStatus,
            }),
            ReverseTargetWords = reverse.TargetWordCount,
        }));

        measured.Chapters.Select(chapter => chapter.SourceWords).Should().Equal(111, 10);
        measured.SourceWords.Should().Be(121);
        measured.Words.Select(word => word.SourceWordId).Should().OnlyHaveUniqueItems();
        measured.Chapters.Select(chapter => chapter.TargetWords).Should().Equal(9, 7);
        measured.Chapters.Select(chapter => chapter.SourceStrongWords).Should().Equal(0, 0);
        measured.Chapters.Select(chapter => chapter.TargetStrongWords).Should().Equal(9, 7);
        measured.GoldPairs.Should().Be(0, "this frozen word/reference fixture contains no answer key");
        reverse.SourceWordCount.Should().Be(7);
        reverse.TargetWordCount.Should().Be(121, "the spanning verse remains target context in chapter 40");
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.Words.CountAsync()).Should().Be(137);
        (await db.Links.CountAsync()).Should().Be(0);
        (await db.LinkClaims.CountAsync()).Should().Be(0);
        (await db.EvidentiaRuns.CountAsync()).Should().Be(0);
        (await db.EvidentiaDecisions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SourceAndGoldDenominatorsUseOnlyThePrimaryChapter()
    {
        var source = Corpus.Add(db, "SOURCE", TextKind.Translation, "eng", (1, 1, ["God"]), (2, 2, ["earth"]));
        var target = Corpus.Add(db, "TARGET", TextKind.CriticalEdition, "hbo", (1, 1, ["אלהים"]), (2, 1, ["אלהים"]), (2, 2, ["ארץ"]));
        await db.SaveChangesAsync();
        Alias(source, 1, 1, 2, 1);
        var god = db.WordAt(source, 1, 1, 1);
        var earth = db.WordAt(source, 2, 2, 1);
        Gold(source, target, god, db.WordAt(target, 1, 1, 1));
        Gold(source, target, god, db.WordAt(target, 2, 1, 1));
        Gold(source, target, earth, db.WordAt(target, 2, 2, 1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var measured = await Loader().MeasureBook("SOURCE", "TARGET", 1,
            new EvidentiaMeasurementOptions(AllowKnownRenderingEvidence: false, RecordWords: true));

        measured.Chapters.Select(chapter => chapter.SourceWords).Should().Equal(1, 1);
        measured.Chapters.Select(chapter => chapter.GoldPairs).Should().Equal(1, 1);
        measured.GoldCoveredSourceWords.Should().Be(2);
        measured.Words.Select(word => word.SourceWordId).Should().OnlyHaveUniqueItems();
        (await db.Links.CountAsync()).Should().Be(3, "admission does not alter the stored key");
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task AChapterWithOnlyASecondarySourcePlacementHasAnEmptyMeasurement()
    {
        var source = Corpus.Add(db, "SOURCE", TextKind.Translation, "eng", (1, 1, ["God"]));
        Corpus.Add(db, "TARGET", TextKind.CriticalEdition, "hbo", (1, 1, ["אלהים"]), (2, 1, ["אלהים"]));
        await db.SaveChangesAsync();
        Alias(source, 1, 1, 2, 1);
        await db.SaveChangesAsync();

        var measured = await Loader().MeasureChapter("SOURCE", "TARGET", 1, 2,
            new EvidentiaMeasurementOptions(AllowKnownRenderingEvidence: false, RecordWords: true));

        measured.SourceWords.Should().Be(0);
        measured.Verses.Should().Be(0);
        measured.Words.Should().BeEmpty();
        measured.TargetWords.Should().Be(1);
        var missing = () => Loader().Preview("SOURCE", "TARGET", 1, 3, null, allowKnownRenderingEvidence: false);
        await missing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no words at canonical address 1:3*");
    }

    [Fact]
    public async Task ASameChapterAlternateAddressRemainsAvailableToASourceVersePreview()
    {
        var source = Corpus.Add(db, "SOURCE", TextKind.Translation, "eng", (1, 1, ["God"]));
        Corpus.Add(db, "TARGET", TextKind.CriticalEdition, "hbo", (1, 1, ["אלהים"]), (1, 2, ["אלהים"]));
        await db.SaveChangesAsync();
        Alias(source, 1, 1, 1, 2);
        await db.SaveChangesAsync();

        var loader = Loader();
        var atAlias = await loader.Preview("SOURCE", "TARGET", 1, 1, 2, allowKnownRenderingEvidence: false);
        var chapter = await loader.MeasureChapter("SOURCE", "TARGET", 1, 1,
            new EvidentiaMeasurementOptions(AllowKnownRenderingEvidence: false));

        atAlias.SourceWordCount.Should().Be(1);
        chapter.SourceWords.Should().Be(1);
    }

    [Fact]
    public async Task ATargetSpanningTwoChaptersStillSuppliesCandidatesAtItsAlternateAddress()
    {
        var source = Corpus.Add(db, "SOURCE", TextKind.Translation, "eng", (2, 1, ["God"]));
        var target = Corpus.Add(db, "TARGET", TextKind.CriticalEdition, "hbo", (1, 1, ["אלהים"]));
        await db.SaveChangesAsync();
        Alias(target, 1, 1, 2, 1);
        db.WordAt(source, 2, 1, 1).StrongNumber = "H430";
        db.WordAt(target, 1, 1, 1).StrongNumber = "H430";
        await db.SaveChangesAsync();
        var targetId = db.WordAt(target, 1, 1, 1).Id;

        var preview = await Loader().Preview("SOURCE", "TARGET", 1, 2, 1, allowKnownRenderingEvidence: false);

        preview.TargetWordCount.Should().Be(1);
        preview.Result.Candidates.Should().Contain(candidate => candidate.Target.Token.Id == targetId
            && candidate.Target.Token.Address.Chapter == 2 && candidate.Target.Token.Address.Verse == 1);
    }

    private void Alias(Text text, int printedChapter, int printedVerse, int chapter, int verse) =>
        db.VerseReferences.Add(new VerseReference
        {
            Verse = db.VerseAt(text, printedChapter, printedVerse), CanonicalBook = 1,
            CanonicalChapter = chapter, CanonicalVerse = verse, IsPrimary = false,
        });

    private void Gold(Text source, Text target, Word from, Word to)
    {
        var link = new Link
        {
            FromText = source, ToText = target, Relation = LinkRelation.Renders, Method = LinkMethod.StatedBySource,
            Provenance = new() { Source = "a test's own answer key" },
        };
        db.Links.Add(link);
        db.LinkWords.Add(new LinkWord { Link = link, Word = from, Side = LinkSide.From });
        db.LinkWords.Add(new LinkWord { Link = link, Word = to, Side = LinkSide.To });
    }

    private EvidentiaCorpusPreviewLoader Loader()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataset:ResourcesPath"] = resources }).Build();
        var environment = new TestEnvironment(resources);
        var packs = new LanguagePackRegistry([new EnglishLanguagePack(), new SpanishLanguagePack(), new OriginalLanguagePack()]);
        return new EvidentiaCorpusPreviewLoader(db,
            new EvidentiaPipeline(packs, [new StrongNumberEvidenceSource()]),
            new EvidentiaStrongProposalResolver(), new EvidentiaKnownRenderingProposalResolver(),
            new EvidentiaTargetGlossProposalResolver(), new EvidentiaDictionaryProposalResolver(), new EvidentiaSyntaxReviewGate(),
            new UdpipeAnnotator(configuration, environment), new EvidentiaDictionarySenseIndex(db, packs),
            new EvidentiaKnownRenderingIndex(db, packs), packs,
            new InterlinearLinkLoader(db, NullLogger<InterlinearLinkLoader>.Instance),
            new EvidentiaFileSourceTexts(configuration, environment), new EvidentiaContextGlossIndex(configuration, environment));
    }

    public void Dispose()
    {
        transaction.Dispose();
        db.Dispose();
        Directory.Delete(resources, recursive: true);
    }

    private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Essenthos.Core.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Testing";
    }

    private sealed record JobFixture(DateTimeOffset CapturedAt, List<FixtureVerse> Verses);
    private sealed record FixtureVerse(string Slug, int Chapter, int Number, List<FixtureReference> References, List<FixtureWord> Words);
    private sealed record FixtureReference(int Book, int Chapter, int Verse, bool IsPrimary);
    private sealed record FixtureWord(int Position, string Surface, string Trailer, string? Lemma, string? StrongNumber,
        string? Gloss, JsonElement? Morphology);
}
