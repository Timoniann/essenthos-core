using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A composed answer names a verse of the target only where the frame joins it to the source's.
/// The middle text's statement may cross a verse boundary on its own account — the Berean's
/// Philippians 1:16 and 1:17 stand in the critical text's order — and carried onto a text that
/// orders them the other way, the crossing names the wrong verse.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ComposedVerseJoinTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _english;
    private readonly Text _greek;

    public ComposedVerseJoinTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _english = Corpus.Add(_db, "TYN1534", TextKind.Translation, "eng",
            (1, 16, ["The", "one"]), (1, 17, ["The", "other"]), (1, 18, ["What", "then"]));
        _greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc",
            (1, 16, ["οἱ", "δὲ"]), (1, 17, ["οἱ", "μὲν"]), (1, 18, ["τί", "γάρ"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    [Fact]
    public async Task AWordReachesItsOwnVerseAndNotTheNextOne()
    {
        var joins = await Joins();

        joins.Joins(Word(_english, 16), Word(_greek, 16)).Should().BeTrue();
        joins.Joins(Word(_english, 16), Word(_greek, 17)).Should().BeFalse();
    }

    [Fact]
    public async Task AVerseLinkJoinsTwoVersesAtDifferentAddressesEitherWayItIsStored()
    {
        VerseLink(_greek, 18, _english, 17);

        var joins = await Joins();

        joins.Joins(Word(_english, 17), Word(_greek, 18)).Should().BeTrue();
        joins.Joins(Word(_english, 16), Word(_greek, 18)).Should().BeFalse();
    }

    [Fact]
    public async Task ACompositionDropsTheAnswerItsMiddleTextCarriedAcrossVerses()
    {
        var joins = await Joins();
        var middle = 9_000_000L;
        var composed = CompositionPipeline.Compose(
            [(Word(_english, 16), middle, 0.9)],
            new (long Bridge, long To, double Confidence)[] { (middle, Word(_greek, 17), 1.0), (middle, Word(_greek, 16), 0.5) }
                .ToLookup(row => row.Bridge, row => (row.To, row.Confidence)),
            joins);

        composed.Should().ContainSingle().Which.To.Should().Be(Word(_greek, 16));
    }

    private async Task<VerseJoins> Joins()
    {
        await _db.Database.OpenConnectionAsync();
        return await VerseJoins.Load(
            (NpgsqlConnection)_db.Database.GetDbConnection(), _english.Id, _greek.Id, CancellationToken.None);
    }

    private long Word(Text text, int verse) => _db.WordAt(text, 1, verse, 1).Id;

    private void VerseLink(Text from, int fromVerse, Text to, int toVerse)
    {
        _db.VerseLinks.Add(new VerseLink
        {
            FromTextId = from.Id,
            ToTextId = to.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = "a test",
            Verses =
            [
                new VerseLinkVerse { VerseId = _db.VerseAt(from, 1, fromVerse).Id, Side = LinkSide.From },
                new VerseLinkVerse { VerseId = _db.VerseAt(to, 1, toVerse).Id, Side = LinkSide.To },
            ],
        });
        _db.SaveChanges();
    }
}
