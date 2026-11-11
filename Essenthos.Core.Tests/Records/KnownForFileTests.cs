using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>What a person is known for: the shipped file, the owner's review list, and the clauses served in the reader's language.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class KnownForFileTests : IDisposable
{
    private const string Ark = "built the ark and was saved from the Flood with his family";
    private const string ArkUkr = "збудував ковчег і врятувався від потопу зі своєю родиною";

    private readonly AppDbContext _db;
    private readonly Entity _noah;

    public KnownForFileTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _noah = new Entity { Kind = EntityKind.Person, Slug = "noah", Name = "Noah", SourceId = "noah", Source = "a test" };
        _db.Entities.Add(_noah);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    /// <summary>The clauses reach the wire on a person's page, and are left off everyone else's.</summary>
    [Fact]
    public async Task ThePageCarriesTheClausesOnTheWire()
    {
        _db.EntityKnownFor.Add(new EntityKnownFor
        {
            EntityId = _noah.Id,
            Ordinal = 0,
            English = Ark,
            Source = "a test",
            Texts = [new EntityKnownForText { Language = "ukr", Text = ArkUkr }],
            References = [new EntityKnownForReference { Position = 0, CanonicalBook = 1, CanonicalChapter = 6, CanonicalVerse = 13, EndChapter = 8, EndVerse = 19 }],
        });
        await _db.SaveChangesAsync();
        var page = new EntityResponse(
            "noah", "person", "Noah", null, "male", null, null, null, null, null, null, "a test", null, 1, 1, 0,
            [], [], [], [], [], [], false)
        {
            KnownFor = await KnownForClauses.Of(_db, _noah.Id, "eng", CancellationToken.None),
        };
        var info = AppJsonSerializerContext.Default.GetTypeInfo(typeof(EntityResponse))!;

        var wire = JsonSerializer.Serialize(page, info);
        var bare = JsonSerializer.Serialize(page with { KnownFor = null }, info);

        wire.Should().Contain($"\"knownFor\":[{{\"text\":\"{Ark}\",\"language\":\"eng\",\"references\":[{{\"first\":{{");
        wire.Should().Contain("\"last\":{\"bookOrdinal\":1,");
        bare.Should().Contain("\"knownFor\":null");
    }

    /// <summary>A review list that is not there approves nothing.</summary>
    [Fact]
    public void AMissingReviewListApprovesNothing() =>
        KnownForReviewList.Approved(Path.Combine(Path.GetTempPath(), $"none-{Guid.NewGuid():N}.json"))
            .Records.Should().BeEmpty();

    /// <summary>
    /// The file the console's known-for answers defer to: every clause in every reader's language, and every reference a run of
    /// verses in the book it names — Jude's prophecy of Enoch is Jude and not Judges.
    /// </summary>
    [Fact]
    public void TheFileSaysEveryClauseInEveryLanguageAndCitesVerses()
    {
        var file = KnownForFile.Read();

        file.Records.Should().HaveCount(34);
        file.Records.Select(r => r.Slug).Should().OnlyHaveUniqueItems();
        file.Records.SelectMany(r => r.Clauses).Should().HaveCount(92).And.AllSatisfy(clause =>
        {
            clause.Languages().Select(one => one.Language).Should().Equal("ukr", "deu", "spa");
            clause.References.Should().NotBeEmpty().And.AllSatisfy(reference =>
                ScriptureSpan.TryParse(reference).Should().Match<ScriptureSpan?>(
                    span => span != null && span.FromVerse != null && span.ToVerse != null, reference));
        });
        var enoch = file.Records.Single(r => r.Slug == "enoch-2").Clauses[1].References.Single();
        ScriptureSpan.Parse(enoch).Book.Should().Be(BookReferences.ResolveOrdinal("jude"));
    }
}
