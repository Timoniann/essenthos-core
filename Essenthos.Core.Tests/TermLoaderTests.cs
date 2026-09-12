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
/// The words the text uses of God — elohim, el, eloah — as entries of their own.
///
/// <para>
/// Asked of Postgres because what is under test is the statement that reads every word BHSA numbers
/// with an entry, and a verse holding the word twice has to come out as one reference.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TermLoaderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TermLoader _loader;

    public TermLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new TermLoader(_db, NullLogger<TermLoader>.Instance);

        var hebrew = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["אלהים", "אלהים"]),
            (1, 2, ["אל"]),
            (1, 3, ["ארץ"]));
        _db.SaveChanges();

        _db.WordAt(hebrew, 1, 1, 1).StrongNumber = "H430";
        _db.WordAt(hebrew, 1, 1, 2).StrongNumber = "H430";
        _db.WordAt(hebrew, 1, 2, 1).StrongNumber = "H410";
        _db.WordAt(hebrew, 1, 3, 1).StrongNumber = "H776";

        _db.StrongEntries.AddRange(
            new StrongEntry { StrongNumber = "H430", Lemma = "אֱלֹהִים", Transliteration = "ʼĕlôhîym", Definition = "gods" },
            new StrongEntry { StrongNumber = "H410", Lemma = "אֵל", Transliteration = "ʼêl", Definition = "strength" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    /// <summary>
    /// A word Strong holds becomes an entry of the term kind, named and numbered from his entry, and a
    /// verse that holds the word twice is one reference. A word he does not hold is not invented.
    /// </summary>
    [Fact]
    public async Task AWordForGodIsAnEntryOfItsOwnWithTheVersesItStandsIn()
    {
        var outcome = await _loader.Load();

        outcome.Written.Should().Be(2, "Strong holds H430 and H410 here and not H433");
        outcome.Referenced.Should().Be(2);

        var elohim = await _db.Entities.Include(e => e.Names).SingleAsync(e => e.Slug == "elohim");
        elohim.Kind.Should().Be(EntityKind.Term);
        elohim.Distinguisher.Should().Contain("not always");
        var name = elohim.Names.Should().ContainSingle().Which;
        name.HebrewStrongNumber.Should().Be("H430");
        name.Kind.Should().Be(TermLoader.NameKind);
        name.Hebrew.Should().Be("אֱלֹהִים");

        (await _db.EntityVerses.CountAsync(v => v.EntityId == elohim.Id)).Should().Be(1);
    }

    /// <summary>The startup pipeline runs on every boot, and a second boot writes nothing (RUL-0005).</summary>
    [Fact]
    public async Task ASecondBootWritesNothing()
    {
        (await _loader.Load()).AlreadyLoaded.Should().BeFalse();

        var again = await _loader.Load();

        again.AlreadyLoaded.Should().BeTrue();
        (await _db.Entities.CountAsync(e => e.Kind == EntityKind.Term)).Should().Be(2);
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Database.ExecuteSqlRaw("DELETE FROM strong_entry WHERE strong_number IN ('H430', 'H410', 'H433')");
    }
}
