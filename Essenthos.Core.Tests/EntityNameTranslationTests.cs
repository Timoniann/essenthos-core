using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The titles and glosses of a names block in a reader's language: loading them, and asking for them.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EntityNameTranslationTests : IDisposable
{
    private const string King = "King of Israel";
    private const string Son = "son [of] Jesse";
    private const string Box = "box, ark (GEN 6:14)";

    private readonly AppDbContext _db;
    private readonly EntityNameTranslationLoader _loader;

    private static readonly EntityNameTranslationRecord[] Pass =
    [
        new(King, "цар Ізраїля", "König von Israel", null, null, "a model", "2026-10-14"),
        new(Son, "син [Єссея]", null, null, null, "a model", "2026-10-14"),
        new(Box, "скриня, ковчег (GEN 6:14)", null, null, null, "a model", "2026-10-14"),
    ];

    public EntityNameTranslationTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity_name_translation");
        _loader = new EntityNameTranslationLoader(_db, NullLogger<EntityNameTranslationLoader>.Instance);
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity_name_translation");
        _db.Dispose();
    }

    [Fact]
    public async Task EachRenderingIsWrittenOnceAndServedInItsLanguage()
    {
        var first = await _loader.Load(Pass, 1, default);
        var second = await _loader.Load(Pass, 1, default);

        first.Written.Should().Be(4);
        second.Written.Should().Be(0);
        second.Removed.Should().Be(0);

        (await EntityNameTranslations.Of(_db, [King, Son, "beloved one", null], "ukr", default))
            .Should().Equal(new Dictionary<string, string> { [King] = "цар Ізраїля", [Son] = "син [Єссея]" });
        (await EntityNameTranslations.Of(_db, [King, Son], "deu", default))
            .Should().Equal(new Dictionary<string, string> { [King] = "König von Israel" });
        (await EntityNameTranslations.Of(_db, [King], null, default)).Should().BeEmpty();
        (await _db.EntityNameTranslations.SingleAsync(t => t.Language == "deu")).Source
            .Should().Be("Essenthos, rendered from the English by a model on 2026-10-14");
    }

    [Fact]
    public async Task ALaterPassWinsAndARenderingNoPassGivesIsRemoved()
    {
        await _loader.Load(Pass, 1, default);

        var outcome = await _loader.Load(
            [new EntityNameTranslationRecord(King, "цар Ізраїлю", null, null, null, "a later model", "2026-10-15")],
            1,
            default);

        outcome.Written.Should().Be(1);
        outcome.Removed.Should().Be(3);
        (await EntityNameTranslations.Of(_db, [King, Son], "ukr", default))
            .Should().Equal(new Dictionary<string, string> { [King] = "цар Ізраїлю" });
    }

    /// <summary>The page turns a cited verse into a link; a rendering that lost it is refused.</summary>
    [Fact]
    public async Task ARenderingThatLosesAVerseIsRefused()
    {
        var outcome = await _loader.Load(
            [new EntityNameTranslationRecord(Box, "скриня, ковчег", null, null, null, "a model", "2026-10-14")],
            1,
            default);

        outcome.Refused.Should().Be(1);
        (await _db.EntityNameTranslations.CountAsync()).Should().Be(0);
    }
}
