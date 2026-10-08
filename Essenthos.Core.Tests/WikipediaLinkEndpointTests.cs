using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A page links to the article of the reader's own language and to no other: a record whose item has
/// no article in that language has no link, rather than one that reads as theirs and is not.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WikipediaLinkEndpointTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Entity _moses;

    public WikipediaLinkEndpointTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _moses = new Entity { Kind = EntityKind.Person, Slug = "moses", Name = "Moses", SourceId = "test:moses", Source = "test" };
        _db.Entities.Add(_moses);
        _db.SaveChanges();
        _db.EntityWikipedia.AddRange(
            Row("en", "Moses"),
            Row("uk", "Мойсей"),
            Row("de", "Mose (Bibel)"));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    private EntityWikipedia Row(string language, string title) => new()
    {
        EntityId = _moses.Id, Language = language, Title = title, Qid = "Q9077", MatchedBy = EntityWikipedia.Evidence.Kin, Confidence = 0.9,
    };

    [Theory]
    [InlineData("eng", "en", "Moses")]
    [InlineData("ukr", "uk", "Мойсей")]
    [InlineData("uk", "uk", "Мойсей")]
    [InlineData("deu", "de", "Mose (Bibel)")]
    public async Task AReaderIsSentToTheArticleOfTheirOwnLanguage(string asked, string wikipedia, string title)
    {
        var link = await WikipediaLinks.Of(_db, _moses.Id, asked, default);

        link.Should().NotBeNull();
        link!.Language.Should().Be(wikipedia);
        link.Title.Should().Be(title);
        link.Url.Should().StartWith($"https://{wikipedia}.wikipedia.org/wiki/");
    }

    [Theory]
    [InlineData("spa")]
    [InlineData("rus")]
    [InlineData("fra")]
    [InlineData(null)]
    [InlineData("")]
    public async Task AReaderWhoseLanguageHasNoArticleIsGivenNoLinkRatherThanAnotherLanguagesOne(string? asked)
    {
        (await WikipediaLinks.Of(_db, _moses.Id, asked, default)).Should().BeNull();
    }

    [Fact]
    public async Task ARecordWithNoLinkGivesNone()
    {
        var other = new Entity { Kind = EntityKind.Person, Slug = "aaron", Name = "Aaron", SourceId = "test:aaron", Source = "test" };
        _db.Entities.Add(other);
        await _db.SaveChangesAsync();

        (await WikipediaLinks.Of(_db, other.Id, "eng", default)).Should().BeNull();
    }

    [Fact]
    public void AnAddressHasTheTitleWithUnderscoresAndEscapedForAPath()
    {
        WikipediaLinks.Address("en", "Tidal (king)").Should().Be("https://en.wikipedia.org/wiki/Tidal_%28king%29");
        WikipediaLinks.Address("de", "Mose (Bibel)").Should().Be("https://de.wikipedia.org/wiki/Mose_%28Bibel%29");
        WikipediaLinks.Address("uk", "Мойсей").Should().Be("https://uk.wikipedia.org/wiki/%D0%9C%D0%BE%D0%B9%D1%81%D0%B5%D0%B9");
        WikipediaLinks.Address("en", "AC/DC").Should().Be("https://en.wikipedia.org/wiki/AC%2FDC");
    }

    [Fact]
    public async Task ARecordHoldsOneArticlePerLanguage()
    {
        _db.EntityWikipedia.Add(Row("en", "Moses (again)"));

        var act = () => _db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TheTableRefusesALanguageNobodyReads()
    {
        _db.EntityWikipedia.Add(new EntityWikipedia
        {
            EntityId = _moses.Id, Language = "fr", Title = "Moïse", Qid = "Q9077", MatchedBy = "kin", Confidence = 0.9,
        });

        var thrown = await FluentActions.Awaiting(() => _db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException!.Message.Should().Contain("ck_entity_wikipedia_language");
    }

    [Fact]
    public async Task TheTableRefusesATieNothingEstablished()
    {
        _db.EntityWikipedia.Add(new EntityWikipedia
        {
            EntityId = _moses.Id, Language = "es", Title = "Moisés", Qid = "Q9077", MatchedBy = "guess", Confidence = 0.5,
        });

        var thrown = await FluentActions.Awaiting(() => _db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException!.Message.Should().Contain("ck_entity_wikipedia_matched_by");
    }
}
