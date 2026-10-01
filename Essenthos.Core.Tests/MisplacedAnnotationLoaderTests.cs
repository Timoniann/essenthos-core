using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>A record taken off the words of a verse that do not mean it, in every text, and left elsewhere.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class MisplacedAnnotationLoaderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _greek;
    private readonly Text _english;
    private readonly Entity _hermes;

    /// <summary>
    /// Verse 1 greets Hermes; verse 2 is where the god is named. Both are annotated to the man in the
    /// Greek and in the English.
    /// </summary>
    public MisplacedAnnotationLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _greek = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc", (4, 1, ["Ἑρμῆν"]), (4, 2, ["Ἑρμῆν"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (4, 1, ["Hermes"]), (4, 2, ["Mercurius"]));
        _hermes = new Entity { Kind = EntityKind.Person, Slug = "hermes", Name = "Hermes", SourceId = "hermes", Source = "a test" };
        _db.Entities.Add(_hermes);
        _db.SaveChanges();
        foreach (var text in new[] { _greek, _english })
        {
            foreach (var verse in new[] { 1, 2 })
            {
                _db.WordEntities.Add(new WordEntity
                {
                    WordId = _db.WordAt(text, 4, verse, 1).Id, EntityId = _hermes.Id,
                    Method = LinkMethod.StrongNumber, Confidence = 0.85, Source = "a resolution",
                });
            }
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }

    private MisplacedAnnotationLoader Loader() => new(_db, NullLogger<MisplacedAnnotationLoader>.Instance);

    /// <summary>
    /// What a pass is about to write is held out where the list takes the record off, so the next
    /// step has nothing to take back; the shipped list names Hermes in Acts 14:12.
    /// </summary>
    [Fact]
    public async Task APassWritesNothingTheListWouldTakeOffAgain()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM word_entity");
        var acts = _db.AddBook(_greek, 44, "Acts", (14, 12, ["Ἑρμῆν"]), (14, 13, ["Ἑρμῆν"]));
        _db.SaveChanges();
        var named = _db.WordAt(acts, 14, 12, 1).Id;
        var elsewhere = _db.WordAt(acts, 14, 13, 1).Id;

        await _db.Database.OpenConnectionAsync();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var connection = (Npgsql.NpgsqlConnection)_db.Database.GetDbConnection();
        await Annotating.Run(connection, transaction, Annotating.Workspace, default);
        await Annotating.Seed(connection, [(named, _hermes.Id, 0.85, false, "Mercurius"), (elsewhere, _hermes.Id, 0.85, false, "Hermes")], default);
        await Annotating.Run(connection, transaction, MisplacedAnnotationLoader.Withhold, default,
            await MisplacedAnnotationLoader.Withheld(_db, default));

        await using var pending = new Npgsql.NpgsqlCommand("SELECT array_agg(word_id) FROM pending_annotation", connection);
        ((long[])(await pending.ExecuteScalarAsync())!).Should().Equal(elsewhere);
    }

    [Fact]
    public async Task TheRecordLeavesEveryWordOfTheVerseAndKeepsTheOthers()
    {
        var misplaced = new[] { new MisplacedAnnotation("hermes", "GEN 4:2", "the god"), new MisplacedAnnotation("nobody", "GEN 4:1", "") };

        var first = await Loader().Load(misplaced, default);
        var second = await Loader().Load(misplaced, default);

        first.Removed.Should().Be(2);
        first.Missing.Should().Be(1);
        second.Removed.Should().Be(0);
        _db.ChangeTracker.Clear();
        (await _db.WordEntities.Select(a => a.Word!.Verse!.Number).ToListAsync()).Should().Equal(1, 1);
    }

    [Fact]
    public void TheShippedListTakesHermesOffActs14()
    {
        var file = MisplacedAnnotationLoader.Read();

        file.DecidedBy.Should().Contain("owner's instruction");
        file.Annotations.Should().ContainSingle(a => a.Record == "hermes" && a.Reference == "ACT 14:12");
        file.Annotations.Should().OnlyContain(a => Citation.Parse(a.Reference) != null && a.Why.Length > 0);
    }
}
