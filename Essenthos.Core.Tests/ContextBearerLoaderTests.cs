using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A name two records bear, printed where nothing settled which, in a book that names one of them:
/// what is written, what is left, and what goes to the review list instead.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ContextBearerLoaderTests : IDisposable
{
    private const string Jonathan = "H3083";

    private const string ShortJonathan = "H3129";

    private const string Micah = "H4318";

    private const string BibleData =
        "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    private readonly AppDbContext _db;
    private readonly string _resources;
    private readonly Text _hebrew;
    private readonly Text _english;
    private readonly Entity _saulsSon;
    private readonly Entity _abiatharsSon;

    /// <summary>
    /// Chapter 20: Saul's son named at verses 1 and 2; verse 3 the name nothing settled; verse 4 a
    /// name a reading answered with nobody the encyclopedia holds; verse 5 a name the dataset files
    /// under the other Jonathan. Chapter 21: two Micahs the book names both of, and the name again.
    /// </summary>
    public ContextBearerLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _resources = Path.Combine(Path.GetTempPath(), $"context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_resources, SenseReadingFiles.DefaultFolder, "run-1"));

        _hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (20, 1, ["יהונתן"]), (20, 2, ["יונתן"]), (20, 3, ["יהונתן"]), (20, 4, ["יהונתן"]),
            (20, 5, ["יהונתן"]), (21, 1, ["מיכה"]), (21, 2, ["מיכה"]), (21, 3, ["מיכה"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (20, 3, ["Jonathan"]));
        _db.SaveChanges();

        foreach (var (chapter, verse, number) in new[]
                 {
                     (20, 1, Jonathan), (20, 2, ShortJonathan), (20, 3, Jonathan), (20, 4, Jonathan),
                     (20, 5, Jonathan), (21, 1, Micah), (21, 2, Micah), (21, 3, Micah),
                 })
        {
            var word = Hebrew(chapter, verse);
            word.StrongNumber = number;
            word.Morphology = JsonDocument.Parse("""{"pos": "nmpr", "nameType": "pers"}""");
        }

        _saulsSon = Add("jonathan-2", "Jonathan", ShortJonathan);
        _abiatharsSon = Add("jonathan-3", "Jonathan", Jonathan);
        var micah = Add("micah-2", "Micah", Micah);
        var micahs = Add("micah-3", "Micah", Micah);
        _db.SaveChanges();

        Read(20, 1, _saulsSon);
        Read(20, 2, _saulsSon);
        Read(21, 1, micah);
        Read(21, 2, micahs);
        _db.EntityVerses.Add(new EntityVerse
        {
            Entity = _abiatharsSon, CanonicalBook = 1, CanonicalChapter = 20, CanonicalVerse = 5, Source = BibleData,
        });
        _db.SaveChanges();

        File.WriteAllText(Path.Combine(_resources, SenseReadingFiles.DefaultFolder, "run-1", SenseReadingFiles.AnswersFileName),
            $$"""{"word_id": {{Hebrew(20, 4).Id}}, "strong_number": "{{Jonathan}}", "referent": "unlisted", "names": null, "confidence": "high", "reason": "a test", "prompt_version": "sense-1", "model": "a test", "run": "2026-09-29"}""" + "\n");
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private Entity Add(string slug, string name, string number)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test",
            Names = [new EntityName { Label = name, HebrewStrongNumber = number, Kind = "proper name" }],
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Read(int chapter, int verse, Entity entity) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = Hebrew(chapter, verse).Id, EntityId = entity.Id, Method = LinkMethod.ModelReading,
            Confidence = 0.9, Source = "a reading",
        });

    private Word Hebrew(int chapter, int verse) => _db.WordAt(_hebrew, chapter, verse, 1);

    private ContextBearerLoader Loader() =>
        new(_db, new ConfigurationBuilder().Build(), NullLogger<ContextBearerLoader>.Instance);

    private async Task<Dictionary<long, string>> Ours() =>
        await _db.WordEntities.Where(a => a.Source == ContextBearerLoader.Source)
            .ToDictionaryAsync(a => a.WordId, a => a.Entity!.Slug);

    /// <summary>Saul's son under the other number of his name, named by the two verses before it.</summary>
    [Fact]
    public async Task TheNameIsTheOneBearerTheBookNames()
    {
        var outcome = await Loader().Load(_resources);

        (await Ours()).Should().ContainKey(Hebrew(20, 3).Id).WhoseValue.Should().Be("jonathan-2");
        var row = await _db.WordEntities.SingleAsync(a => a.WordId == Hebrew(20, 3).Id);
        row.Method.Should().Be(LinkMethod.RuleBased);
        row.Confidence.Should().Be(0.99);
        (await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == row.Id)).Should().Be(1);
        outcome.Written.Should().Be(1);
    }

    /// <summary>A reading that answered the word, even with nobody the encyclopedia holds, keeps it.</summary>
    [Fact]
    public async Task AWordAReadingAnsweredIsLeft()
    {
        await Loader().Load(_resources);

        (await Ours()).Should().NotContainKey(Hebrew(20, 4).Id);
    }

    /// <summary>Where the dataset's list files the verse under the other bearer, nothing is written and the word is listed.</summary>
    [Fact]
    public async Task WhereTheDatasetFilesAnotherBearerTheWordGoesToReview()
    {
        var outcome = await Loader().Load(_resources);

        (await Ours()).Should().NotContainKey(Hebrew(20, 5).Id);
        outcome.Contradicted.Should().Be(1);
        var review = await File.ReadAllTextAsync(Path.Combine([_resources, .. ContextBearerLoader.ReviewFile]));
        review.Should().Contain("\"reference\": \"1:20:5\"").And.Contain("jonathan-3");
    }

    /// <summary>A book that names both Micahs has not said which the third is.</summary>
    [Fact]
    public async Task ABookNamingBothBearersLeavesTheWord()
    {
        await Loader().Load(_resources);

        (await Ours()).Should().NotContainKey(Hebrew(21, 3).Id);
    }

    /// <summary>
    /// The answer crosses the link to the translation, except onto a word that already names somebody
    /// else, which keeps its answer and is listed.
    /// </summary>
    [Fact]
    public async Task TheAnswerCrossesTheLinkButNeverOverAnotherAnnotation()
    {
        var rendering = _db.WordAt(_english, 20, 3, 1);
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _hebrew.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StrongNumber, Confidence = 0.9, Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = rendering, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Hebrew(20, 3), Side = LinkSide.To });
        await _db.SaveChangesAsync();

        (await Loader().Load(_resources)).Written.Should().Be(2);
        (await Ours()).Should().ContainKey(rendering.Id).WhoseValue.Should().Be("jonathan-2");

        _db.WordEntities.RemoveRange(_db.WordEntities.Where(a => a.Source == ContextBearerLoader.Source));
        _db.WordEntities.Add(new WordEntity
        {
            WordId = rendering.Id, EntityId = _abiatharsSon.Id, Method = LinkMethod.Manual, Source = "the owner",
        });
        await _db.SaveChangesAsync();

        var outcome = await Loader().Load(_resources);

        (await Ours()).Should().NotContainKey(rendering.Id);
        (await _db.WordEntities.SingleAsync(a => a.WordId == rendering.Id)).EntityId.Should().Be(_abiatharsSon.Id);
        outcome.Contested.Should().Be(1);
    }

    /// <summary>The held-out measure asks every settled word as if it were not, and a second run writes nothing.</summary>
    [Fact]
    public async Task TheRuleIsMeasuredOnTheSettledWordsAndRunsOnce()
    {
        var first = await Loader().Load(_resources);
        var second = await Loader().Load(_resources);

        first.Measured.Tested.Should().Be(4);
        first.Measured.Answered.Should().Be(0);
        second.AlreadyLoaded.Should().BeTrue();
    }
}
