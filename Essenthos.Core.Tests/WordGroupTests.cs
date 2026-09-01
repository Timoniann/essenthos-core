using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The syntax layer as a schema question rather than a loading one: can two tables hold what eight
/// were proposed for, and does a word reach its groups and a group its words in one read from
/// either side.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WordGroupTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Text _hebrew;

    public WordGroupTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _hebrew = Corpus.Add(_db, "bhsa", TextKind.ManuscriptTradition, "hbo",
            (1, 1, ["בְּ", "רֵאשִׁית", "בָּרָא", "אֱלֹהִים"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// A word sits in roughly seven groups at once — its phrase, its clause, its sentence and their
    /// atoms — and all of them have to be reachable from the word.
    /// </summary>
    [Fact]
    public void AWordReachesEveryGroupItIsIn()
    {
        var sentence = Group(WordGroupKind.Sentence, null, 1, 2, 3, 4);
        var clause = Group(WordGroupKind.Clause, sentence, 1, 2, 3, 4);
        Group(WordGroupKind.Phrase, clause, 1, 2);

        var word = _db.WordAt(_hebrew, 1, 1, 1);
        var groups = _db.WordGroupWords.Where(m => m.WordId == word.Id).Select(m => m.WordGroup!.Kind).ToList();

        groups.Should().BeEquivalentTo(
            [WordGroupKind.Sentence, WordGroupKind.Clause, WordGroupKind.Phrase]);
    }

    [Fact]
    public void AGroupReachesItsWordsInOrder()
    {
        var phrase = Group(WordGroupKind.Phrase, null, 2, 1);

        var words = _db.WordGroupWords
            .Where(m => m.WordGroupId == phrase.Id)
            .OrderBy(m => m.Word!.Position)
            .Select(m => m.Word!.Surface)
            .ToList();

        words.Should().Equal("בְּ", "רֵאשִׁית");
    }

    /// <summary>
    /// The attributes differ per kind — a clause has a domain, a phrase a function — so they are
    /// held as JSON rather than as columns nothing else would use.
    /// </summary>
    [Fact]
    public void AGroupsFeaturesAreQueryable()
    {
        Group(WordGroupKind.Phrase, null, features: """{"function":"Predicate"}""", 3);
        Group(WordGroupKind.Phrase, null, features: """{"function":"Subject"}""", 4);

        var predicates = _db.WordGroups
            .Where(g => g.Features!.RootElement.GetProperty("function").GetString() == "Predicate")
            .ToList();

        predicates.Should().ContainSingle();
    }

    /// <summary>
    /// Nesting is what makes this a layer rather than a list, and a group that loses its parent
    /// takes its children with it — which is what "inside" means.
    /// </summary>
    [Fact]
    public void RemovingAGroupRemovesWhatWasInsideIt()
    {
        var clause = Group(WordGroupKind.Clause, null, 1, 2, 3, 4);
        Group(WordGroupKind.Phrase, clause, 1, 2);

        _db.WordGroups.Remove(clause);
        _db.SaveChanges();

        _db.WordGroups.Count(g => g.TextId == _hebrew.Id).Should().Be(0);
    }

    /// <summary>A word may not be listed in the same group twice; the pair is the key.</summary>
    [Fact]
    public void AWordIsInAGroupOnceOrNotAtAll()
    {
        var phrase = Group(WordGroupKind.Phrase, null, 1);

        var twice = () =>
        {
            _db.WordGroupWords.Add(new WordGroupWord
            {
                WordGroupId = phrase.Id,
                WordId = _db.WordAt(_hebrew, 1, 1, 1).Id,
            });
            _db.SaveChanges();
        };

        twice.Should().Throw<Exception>();
    }

    private WordGroup Group(WordGroupKind kind, WordGroup? parent, params int[] positions) =>
        Group(kind, parent, null, positions);

    private WordGroup Group(WordGroupKind kind, WordGroup? parent, string? features, params int[] positions)
    {
        var group = new WordGroup
        {
            TextId = _hebrew.Id,
            Kind = kind,
            ParentId = parent?.Id,
            Position = 1,
            Features = features is null ? null : JsonDocument.Parse(features),
        };
        _db.WordGroups.Add(group);
        _db.SaveChanges();

        foreach (var position in positions)
        {
            _db.WordGroupWords.Add(new WordGroupWord
            {
                WordGroupId = group.Id,
                WordId = _db.WordAt(_hebrew, 1, 1, position).Id,
            });
        }

        _db.SaveChanges();
        return group;
    }
}
