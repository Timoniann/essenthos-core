using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A run that computes its books four at a time stores the run one book after another stores: the same
/// run row, the same decisions, under the same ids, in the same order. Both runs start from empty run
/// tables with their sequences reset, so an id that differs is a row written out of turn.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EvidentiaParallelRunTests : IDisposable
{
    private static readonly (string English, string Hebrew, string Strong, string Gloss)[] Vocabulary =
    [
        ("God", "אלהים", "H430", "God"),
        ("created", "ברא", "H1254", "create"),
        ("heaven", "השמים", "H8064", "heaven"),
        ("earth", "הארץ", "H776", "earth"),
        ("light", "אור", "H216", "light"),
        ("saw", "וירא", "H7200", "see"),
        ("waters", "המים", "H4325", "water"),
        ("said", "ויאמר", "H559", "say"),
    ];

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;
    private readonly string _resources;

    public EvidentiaParallelRunTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        _resources = Directory.CreateTempSubdirectory("evidentia-resources").FullName;
        Clear();
    }

    [Fact]
    public async Task FourBooksAtATimeStoreTheRunOneBookAtATimeStores()
    {
        await SixBooks();
        var options = new EvidentiaMeasurementOptions(SecondPass: true, AlignerLinks: true);
        await using var services = Services();
        var books = Enumerable.Range(1, 6).Select(book => new EvidentiaBookScope(book)).ToList();

        var serial = await Runner(services).Run("ENGT", "HEBT", books, options, parallel: 1);
        var serialRows = await Rows();
        await Reset();
        var parallel = await Runner(services).Run("ENGT", "HEBT", books, options, parallel: 4);
        var parallelRows = await Rows();

        serialRows.Decisions.Should().HaveCountGreaterThan(40).And.Equal(parallelRows.Decisions);
        serialRows.Runs.Should().ContainSingle().And.Equal(parallelRows.Runs);
        parallel.Measurements.Select(book => book.CanonicalBook).Should().Equal(1, 2, 3, 4, 5, 6);
        (parallel.Decisions, parallel.Proposals, parallel.Safe, parallel.Absences)
            .Should().Be((serial.Decisions, serial.Proposals, serial.Safe, serial.Absences));
        serialRows.Decisions.Select(row => row.Book).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task MoreBooksAtOnceThanUdpipeMayRunIsRefusedBeforeAnythingIsStored()
    {
        await SixBooks();
        await using var services = Services();

        var run = () => Runner(services).Run("ENGT", "HEBT", [new EvidentiaBookScope(1)], new EvidentiaMeasurementOptions(),
            parallel: EvidentiaRunner.MaxParallel + 1);

        await run.Should().ThrowAsync<ArgumentOutOfRangeException>().WithMessage("*--parallel 1 to 4*");
        (await _db.EvidentiaRuns.CountAsync()).Should().Be(0);
    }

    /// <summary>Six books of unequal length, the first the longest, so the later ones finish before it.</summary>
    private async Task SixBooks()
    {
        string[][] english =
        [
            ["In", "the", "beginning", "God", "created", "the", "heaven", "and", "the", "earth"],
            ["And", "God", "saw", "the", "light"],
            ["And", "God", "said", "Let", "the", "waters", "be"],
        ];
        string[][] hebrew =
        [
            ["בראשית", "ברא", "אלהים", "את", "השמים", "ואת", "הארץ"],
            ["וירא", "אלהים", "את", "אור"],
            ["ויאמר", "אלהים", "יהי", "המים"],
        ];
        string[] names = ["Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua"];
        Text? englishText = null, hebrewText = null;
        for (var book = 1; book <= names.Length; book++)
        {
            var chapters = book == 1 ? 4 : 1 + book % 2;
            var verses = Enumerable.Range(1, chapters)
                .SelectMany(chapter => Enumerable.Range(1, 3).Select(verse => (chapter, verse)))
                .ToList();
            var englishVerses = verses.Select(at => (at.chapter, at.verse, english[(at.verse + book) % 3])).ToArray();
            var hebrewVerses = verses.Select(at => (at.chapter, at.verse, hebrew[(at.verse + book) % 3])).ToArray();
            if (book == 1)
            {
                englishText = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng", englishVerses);
                hebrewText = Corpus.Add(_db, "HEBT", TextKind.CriticalEdition, "hbo", hebrewVerses);
            }
            else
            {
                _db.AddBook(englishText!, book, names[book - 1], englishVerses);
                _db.AddBook(hebrewText!, book, names[book - 1], hebrewVerses);
            }
        }

        await _db.SaveChangesAsync();
        var byHebrew = Vocabulary.ToDictionary(word => word.Hebrew);
        foreach (var word in await _db.Words.Where(word => word.TextId == hebrewText!.Id).ToListAsync())
        {
            if (byHebrew.TryGetValue(word.Surface, out var known))
            {
                word.StrongNumber = known.Strong;
                word.Gloss = known.Gloss;
            }
        }

        // The source states some of its words, so there is something to learn renderings from and an
        // answer key; the aligner guesses others, so the second pass has its pairs to read.
        var words = await _db.Words.Where(word => word.TextId == englishText!.Id || word.TextId == hebrewText!.Id)
            .Select(word => new { word.Id, word.TextId, word.Surface, word.VerseId })
            .ToListAsync();
        var byVerse = words.GroupBy(word => word.VerseId).ToDictionary(group => group.Key, group => group.ToList());
        var verseIds = await _db.VerseReferences
            .Select(reference => new
            {
                reference.VerseId, reference.Verse!.TextId, reference.CanonicalBook, reference.CanonicalChapter, reference.CanonicalVerse,
            })
            .ToListAsync();
        var hebrewVerse = verseIds.Where(verse => verse.TextId == hebrewText!.Id)
            .ToDictionary(verse => (verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse), verse => verse.VerseId);
        var linked = 0;
        var answerKey = new Provenance { Source = "a test's answer key" };
        var aligner = new Provenance { Source = "a test's aligner" };
        foreach (var verse in verseIds.Where(verse => verse.TextId == englishText!.Id))
        {
            var target = byVerse[hebrewVerse[(verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse)]];
            foreach (var (englishWord, hebrewWord, _, _) in Vocabulary)
            {
                var from = byVerse[verse.VerseId].FirstOrDefault(word => word.Surface == englishWord);
                var to = target.FirstOrDefault(word => word.Surface == hebrewWord);
                if (from is null || to is null)
                {
                    continue;
                }

                var stated = linked++ % 3 != 0;
                var link = new Link
                {
                    FromTextId = englishText!.Id,
                    ToTextId = hebrewText!.Id,
                    Relation = LinkRelation.Renders,
                    Method = stated ? LinkMethod.StatedBySource : LinkMethod.Aligner,
                    Confidence = stated ? null : 0.6,
                    Provenance = stated ? answerKey : aligner,
                };
                _db.Links.Add(link);
                _db.LinkWords.Add(new LinkWord { Link = link, WordId = from.Id, Side = LinkSide.From });
                _db.LinkWords.Add(new LinkWord { Link = link, WordId = to.Id, Side = LinkSide.To });
            }
        }

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private sealed record DecisionRow(long Id, int Book, string Row);

    private async Task<(List<DecisionRow> Decisions, List<string> Runs)> Rows()
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        var decisions = new List<DecisionRow>();
        await using (var command = new Npgsql.NpgsqlCommand(
                         "SELECT id, canonical_book, row_to_json(d)::text FROM evidentia_decision d ORDER BY id", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                decisions.Add(new DecisionRow(reader.GetInt64(0), reader.GetInt16(1), reader.GetString(2)));
            }
        }

        var runs = new List<string>();
        await using (var command = new Npgsql.NpgsqlCommand(
                         "SELECT (to_jsonb(r) - 'started_at' - 'finished_at')::text FROM evidentia_run r ORDER BY id", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                runs.Add(reader.GetString(0));
            }
        }

        return (decisions, runs);
    }

    private async Task Reset()
    {
        await _db.Database.ExecuteSqlRawAsync("TRUNCATE evidentia_decision, evidentia_run RESTART IDENTITY CASCADE");
        _db.ChangeTracker.Clear();
    }

    private EvidentiaRunner Runner(ServiceProvider services) =>
        new(_db, Loader(_db), services.GetRequiredService<IServiceScopeFactory>());

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.NewContext());
        services.AddScoped(provider => Loader(provider.GetRequiredService<AppDbContext>()));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private EvidentiaCorpusPreviewLoader Loader(AppDbContext db)
    {
        var packs = new LanguagePackRegistry([new EnglishLanguagePack(), new OriginalLanguagePack()]);
        return new EvidentiaCorpusPreviewLoader(
            db,
            new EvidentiaPipeline(packs, [new StrongNumberEvidenceSource()]),
            new EvidentiaStrongProposalResolver(),
            new EvidentiaKnownRenderingProposalResolver(),
            new EvidentiaTargetGlossProposalResolver(),
            new EvidentiaDictionaryProposalResolver(),
            new EvidentiaSyntaxReviewGate(),
            new UdpipeAnnotator(Configuration(), new Environment(_resources)),
            new EvidentiaDictionarySenseIndex(db, packs),
            new EvidentiaKnownRenderingIndex(db, packs),
            packs,
            new InterlinearLinkLoader(db, NullLogger<InterlinearLinkLoader>.Instance),
            new EvidentiaFileSourceTexts(Configuration(), new Environment(_resources)),
            new EvidentiaContextGlossIndex(Configuration(), new Environment(_resources)));
    }

    /// <summary>A resources folder with no UDPipe in it: what is compared is what is stored, not what the tagger says.</summary>
    private IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataset:ResourcesPath"] = _resources })
        .Build();

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("TRUNCATE evidentia_decision, evidentia_run RESTART IDENTITY CASCADE");
        _db.LinkClaims.ExecuteDelete();
        _db.LinkWords.ExecuteDelete();
        _db.Links.ExecuteDelete();
        _db.Words.ExecuteDelete();
        _db.VerseReferences.ExecuteDelete();
        _db.Verses.ExecuteDelete();
        _db.Chapters.ExecuteDelete();
        _db.Books.ExecuteDelete();
        _db.Texts.ExecuteDelete();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        Directory.Delete(_resources, recursive: true);
    }

    private sealed class Environment(string contentRoot) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Essenthos.Core.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
