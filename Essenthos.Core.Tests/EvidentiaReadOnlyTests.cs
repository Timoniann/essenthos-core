using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// TSK-0510's headline claim is that EVIDENTIA writes nothing. That was established by reading the
/// code and grepping it, which says what is true today and nothing about what the next change may
/// do. This runs a whole chapter measurement against a real database and asks the change tracker.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EvidentiaReadOnlyTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _resources;

    public EvidentiaReadOnlyTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _resources = Directory.CreateTempSubdirectory("evidentia-resources").FullName;
    }

    [Fact]
    public async Task AMeasurementLeavesNothingForTheChangeTrackerToSave()
    {
        var english = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning", "God", "created", "the", "heaven"]));
        var hebrew = Corpus.Add(_db, "HEBT", TextKind.CriticalEdition, "hbo",
            (1, 1, ["בראשית", "ברא", "אלהים", "השמים"]));
        await _db.SaveChangesAsync();

        _db.WordAt(hebrew, 1, 1, 1).StrongNumber = "H7225";
        _db.WordAt(hebrew, 1, 1, 3).StrongNumber = "H430";
        _db.WordAt(hebrew, 1, 1, 3).Morphology = System.Text.Json.JsonDocument.Parse("{\"pos\":\"subs\"}");
        var link = new Link
        {
            FromText = english,
            ToText = hebrew,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "a test's own answer key",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(english, 1, 1, 4), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(hebrew, 1, 1, 3), Side = LinkSide.To });
        await _db.SaveChangesAsync();

        var links = await _db.Links.CountAsync();
        var claims = await _db.LinkClaims.CountAsync();
        var words = await _db.Words.CountAsync();
        _db.ChangeTracker.Clear();

        var measurement = await Loader().MeasureChapter("ENGT", "HEBT", 1, 1,
            new EvidentiaMeasurementOptions(SampleSize: 4));

        measurement.GoldPairs.Should().Be(1);
        measurement.GoldCoveredSourceWords.Should().Be(1, "the answer key reaches only 'God' of the seven words");
        _db.ChangeTracker.HasChanges().Should().BeFalse("a preview is a diagnostic, not a write path");
        _db.ChangeTracker.Entries().Should().BeEmpty("every query on this path is AsNoTracking");
        (await _db.Links.CountAsync()).Should().Be(links);
        (await _db.LinkClaims.CountAsync()).Should().Be(claims);
        (await _db.Words.CountAsync()).Should().Be(words);
    }

    private EvidentiaCorpusPreviewLoader Loader()
    {
        var packs = new LanguagePackRegistry(
            [new EnglishLanguagePack(), new SlavicLanguagePack(), new OriginalLanguagePack()]);
        return new EvidentiaCorpusPreviewLoader(
            _db,
            new EvidentiaPipeline(packs, [new StrongNumberEvidenceSource()]),
            new EvidentiaStrongProposalResolver(),
            new EvidentiaKnownRenderingProposalResolver(),
            new EvidentiaTargetGlossProposalResolver(),
            new EvidentiaDictionaryProposalResolver(),
            new EvidentiaSyntaxReviewGate(),
            new UdpipeAnnotator(Configuration(), new Environment(_resources)),
            new EvidentiaDictionarySenseIndex(_db, packs),
            new EvidentiaKnownRenderingIndex(_db, packs));
    }

    /// <summary>
    /// A resources folder that exists and holds no UDPipe, so the annotator reports the tool as
    /// unavailable instead of the path resolver throwing. The test is about what is written, not
    /// about what the tagger says.
    /// </summary>
    private IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataset:ResourcesPath"] = _resources })
        .Build();

    private void Clear()
    {
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
