using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The names read from the verses written as our own annotations: on a text no link reaches, on the
/// words nothing names yet, and never over a word another annotation already names.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NameConsensusApplyTests : IDisposable
{
    private static readonly ConsensusBar Bar = new(1, 0.1, 0.05);

    private readonly AppDbContext _db;
    private readonly NameConsensusPass _pass;
    private readonly Text _hebrew;
    private readonly Text _douay;
    private readonly Entity _abraham;
    private readonly Entity _lot;
    private readonly string _resources;

    public NameConsensusApplyTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _pass = new NameConsensusPass(_db, NullLogger<NameConsensusPass>.Instance);
        _resources = Directory.CreateTempSubdirectory("name-consensus").FullName;

        _hebrew = Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo",
            Enumerable.Range(1, 6).Select(verse => (1, verse, new[] { "ויאמר", "אברהם" })).ToArray());
        _douay = Corpus.Add(_db, "DRA", TextKind.Translation, "eng",
            [
                .. Enumerable.Range(1, 6).Select(verse => (1, verse, new[] { "and", "Abraham", "said" })),
                .. Enumerable.Range(7, 6).Select(verse => (1, verse, new[] { "and", "the", "people", "went" })),
            ]);
        _abraham = Record("abraham", "Abraham");
        _lot = Record("lot", "Lot");
        _db.SaveChanges();

        for (var verse = 1; verse <= 6; verse++)
        {
            Names(_db.WordAt(_hebrew, 1, verse, 2), _abraham);
        }

        Names(_db.WordAt(_douay, 1, 6, 2), _lot);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
        Directory.Delete(_resources, recursive: true);
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private Entity Record(string slug, string name)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private void Names(Word word, Entity entity) =>
        _db.WordEntities.Add(new WordEntity { Word = word, Entity = entity, Method = LinkMethod.Manual, Source = "a test" });

    private Task<string> Apply(bool replace = false) =>
        _pass.Run(["DRA"], NameConsensusPass.Precision, null, Bar, apply: true, replace: replace, resources: _resources);

    private Task<List<WordEntity>> Ours() =>
        _db.WordEntities.AsNoTracking()
            .Where(annotation => annotation.Source == NameConsensusPass.Source)
            .Include(annotation => annotation.Word)
            .ToListAsync();

    [Fact]
    public async Task TheNameIsWrittenOnTheWordsNothingNamesYet()
    {
        await Apply();

        var ours = await Ours();
        ours.Should().HaveCount(5).And.OnlyContain(annotation =>
            annotation.EntityId == _abraham.Id && annotation.Word!.Surface == "Abraham" && annotation.Word.TextId == _douay.Id);
        ours.Should().OnlyContain(annotation => annotation.Method == LinkMethod.RuleBased
                                                && annotation.Confidence == NameConsensusPass.Unmeasured);
        (await _db.WordEntityClaims.CountAsync(claim => claim.Source == NameConsensusPass.Source)).Should().Be(5);
    }

    [Fact]
    public async Task AWordAnotherAnnotationNamesIsLeftAndListedForReview()
    {
        await Apply();

        var sixth = _db.WordAt(_douay, 1, 6, 2);
        (await _db.WordEntities.AsNoTracking().Where(annotation => annotation.WordId == sixth.Id).Select(annotation => annotation.EntityId).ToListAsync())
            .Should().Equal(_lot.Id);
        var review = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine([_resources, .. NameConsensusPass.ReviewFile])));
        var entry = review.RootElement.GetProperty("words").EnumerateArray().Single();
        entry.GetProperty("reference").GetString().Should().Be("1:1:6");
        entry.GetProperty("found").GetString().Should().Be("abraham");
        entry.GetProperty("annotated")[0].GetString().Should().Be("lot");
    }

    [Fact]
    public async Task ASecondRunWritesNothingUnlessItReplaces()
    {
        await Apply();
        await Apply();
        (await Ours()).Should().HaveCount(5);
        (await _pass.Written(CancellationToken.None)).Should().BeTrue();

        await Apply(replace: true);
        (await Ours()).Should().HaveCount(5);
    }

    [Fact]
    public async Task TheOriginalsAreNeverWrittenTo()
    {
        await Apply();

        (await Ours()).Should().NotContain(annotation => annotation.Word!.TextId == _hebrew.Id);
    }
}
