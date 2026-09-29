using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The word two readings of the passage found standing for a person, written on the original: what is
/// written and carried, and what is never touched.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PassageReadingLoaderTests : IDisposable
{
    private const string Pronoun = "G846";

    private readonly AppDbContext _db;
    private readonly Text _greek;
    private readonly Text _english;
    private readonly Entity _jesus;
    private readonly Entity _peter;

    /// <summary>
    /// Verse 1 εἶπεν αὐτῷ, said unto him; verse 2 the same, the pronoun already named as Peter by the owner;
    /// verse 3 the same. The King James renders verse 1.
    /// </summary>
    public PassageReadingLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        _greek = Corpus.Add(_db, NestleTextSource.Slug, TextKind.CriticalEdition, "grc",
            (4, 1, ["εἶπεν", "αὐτῷ"]), (4, 2, ["εἶπεν", "αὐτῷ"]), (4, 3, ["εἶπεν", "αὐτῷ"]));
        _english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (4, 1, ["said", "unto", "him"]));
        _db.SaveChanges();
        foreach (var verse in new[] { 1, 2, 3 })
        {
            Greek(verse, 2).StrongNumber = Pronoun;
        }

        _jesus = Add("jesus", "Jesus");
        _peter = Add("peter", "Peter");
        _db.SaveChanges();

        _db.WordEntities.Add(new WordEntity
        {
            WordId = Greek(2, 2).Id, EntityId = _peter.Id, Method = LinkMethod.Manual, Source = "the owner",
        });
        var link = new Link
        {
            FromTextId = _english.Id, ToTextId = _greek.Id, Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource, Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(_english, 4, 1, 3), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Greek(1, 2), Side = LinkSide.To });
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

    private Word Greek(int verse, int position) => _db.WordAt(_greek, 4, verse, position);

    private Entity Add(string slug, string name)
    {
        var entity = new Entity { Kind = EntityKind.Person, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        return entity;
    }

    private static PassageReadingRecord Line(int verse, string strong = Pronoun, bool write = true) =>
        new($"GEN 4:{verse}", "jesus", NestleTextSource.Slug, 2, "αὐτῷ", strong, "him", "pronoun", "pronoun", 0.9,
            "'said unto him' is Jesus",
            new PassageReadingRun("claude-sonnet-5-5", "medium", "references-2", "2026-09-29"),
            new PassageReadingCheck(true, "Jesus is the one addressed", "claude-sonnet-5-5", "references-check-2",
                "2026-09-29"),
            write);

    private PassageReadingLoader Loader() =>
        new(_db, new ConfigurationBuilder().Build(), NullLogger<PassageReadingLoader>.Instance);

    private async Task<Dictionary<long, WordEntity>> Ours()
    {
        _db.ChangeTracker.Clear();
        var source = PassageReadingLoader.SourceOf(Line(1));
        return await _db.WordEntities.Where(a => a.Source == source).ToDictionaryAsync(a => a.WordId);
    }

    /// <summary>The pronoun is written as a model's reading at its confidence, and the English word rendering it says so too.</summary>
    [Fact]
    public async Task TheWordReadIsWrittenAndCarried()
    {
        var outcome = await Loader().Load([Line(1)]);

        var ours = await Ours();
        var row = ours[Greek(1, 2).Id];
        row.EntityId.Should().Be(_jesus.Id);
        row.Method.Should().Be(LinkMethod.ModelReading);
        row.Confidence.Should().Be(0.9);
        row.Source.Should().Contain("claude-sonnet-5-5").And.Contain("2026-09-29");
        (await _db.WordEntityClaims.CountAsync(c => c.WordEntityId == row.Id)).Should().Be(1);
        ours.Should().ContainKey(_db.WordAt(_english, 4, 1, 3).Id);
        outcome.Written.Should().Be(2);
    }

    /// <summary>A word somebody already named, a word that is no longer the one read, and a line not marked are all left.</summary>
    [Fact]
    public async Task WhatIsNamedMovedOrUnmarkedIsLeft()
    {
        var outcome = await Loader().Load([Line(2), Line(3, strong: "G3004"), Line(1, write: false)]);

        (await Ours()).Should().BeEmpty();
        (await _db.WordEntities.SingleAsync(a => a.WordId == Greek(2, 2).Id)).EntityId.Should().Be(_peter.Id);
        outcome.Named.Should().Be(1);
        outcome.Moved.Should().Be(1);
        outcome.Marked.Should().Be(2);
    }

    /// <summary>A second run writes nothing.</summary>
    [Fact]
    public async Task ItRunsOnce()
    {
        await Loader().Load([Line(1)]);

        (await Loader().Load([Line(1)])).AlreadyLoaded.Should().BeTrue();
    }

    [Theory]
    [InlineData("2KI 25:21", 12, 25, 21)]
    [InlineData("1JN 4:14", 62, 4, 14)]
    [InlineData("EZK 37:3", 26, 37, 3)]
    public void AReferenceIsACanonicalAddress(string reference, int book, int chapter, int verse) =>
        PassageReadingLoader.Place(reference).Should().Be(new CanonicalReference(book, chapter, verse));
}
