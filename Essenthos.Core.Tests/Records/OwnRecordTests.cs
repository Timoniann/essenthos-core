using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The records this corpus writes for itself, exercised against the rulings it actually ships
/// rather than against a fixture standing in for them.
///
/// The words are written at the addresses the ruling files name, so what is under test is the file a
/// reader would be shown from — a ruling whose address is malformed, or whose referent slug was
/// renamed away, fails here rather than quietly annotating nothing on a live database.
///
/// <para>
/// The tests are classes by subject, each with a database of its own, so that they run side by side:
/// a load of the rulings takes seconds, and one class holding all of them took half an hour. A test
/// that changes the world before it loads gets a world of its own; the tests that only read what one
/// load wrote share it in <see cref="OwnRecordLoadedTests"/>.
/// </para>
/// </summary>
[LongRunning]
public abstract class OwnRecordTests : IDisposable
{
    private readonly bool _owned;
    private OwnRecordWorld? _world;

    /// <summary>A world of the test's own, made afresh, for a test that changes it.</summary>
    private protected OwnRecordTests(WitnessDatabase database)
    {
        _world = OwnRecordWorld.Make(database);
        _owned = true;
    }

    /// <summary>A world the class shares, which it sets in <see cref="World"/> before each test.</summary>
    private protected OwnRecordTests()
    {
    }

    private protected OwnRecordWorld World
    {
        get => _world ?? throw new InvalidOperationException("The test's world is set before the test runs.");
        set => _world = value;
    }

    private protected AppDbContext Db => World.Db;

    private protected IReadOnlyList<OwnRecordRuling> Rulings => World.Rulings;

    private protected Dictionary<RuledWord, long> Words => World.Words;

    private protected Text English => World.English;

    public void Dispose()
    {
        if (_owned)
        {
            _world?.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private protected Task<OwnRecordOutcome> Load() => World.Load();

    private protected OwnRecordRuling Ruled(string slug, string reference) =>
        Rulings.First(r => (r.Create?.Slug ?? r.Existing) == slug && r.Reference == reference);

    private protected void Tie(long from, Word to)
    {
        var hebrew = Db.Words.Single(w => w.Id == from);
        var link = new Link
        {
            FromTextId = hebrew.TextId, ToTextId = to.TextId, Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner, Confidence = 0.9, Provenance = new() { Source = "a test" },
        };
        Db.Links.Add(link);
        Db.LinkWords.Add(new LinkWord { Link = link, Word = hebrew, Side = LinkSide.From });
        Db.LinkWords.Add(new LinkWord { Link = link, Word = to, Side = LinkSide.To });
    }
}
