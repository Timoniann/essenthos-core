using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What each text calls a book, served from the corpus.
///
/// A client matching what a reader typed — <em>Йов</em> for Job — cannot do it from the English
/// name, and the only other way to have the forms is a copy inside the client, which stops being
/// true the moment a text is loaded or a name corrected.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class BookNameFormTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public BookNameFormTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task EveryTextsOwnNameForABookIsServedUnderItsLanguage()
    {
        Name(Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (1, 1, ["На", "початку"])), "Буття");
        Name(Corpus.Add(_db, "UKR1871", TextKind.Translation, "ukr", (1, 1, ["У", "починї"])),
            "Перва книга Мойсея");
        Name(Corpus.Add(_db, "RUSV", TextKind.Translation, "rus", (1, 1, ["В", "начале"])), "Бытие");
        await _db.SaveChangesAsync();

        var forms = await ReadEndpoints.NameForms(_db, default);

        forms[1].Should().BeEquivalentTo(
            new[]
            {
                new BookNameFormResponse("rus", "Бытие"),
                new BookNameFormResponse("ukr", "Буття"),
                new BookNameFormResponse("ukr", "Перва книга Мойсея"),
            },
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// Two texts printing the same word say nothing twice, and a text printing the frame's own
    /// name — BHSA's <em>Genesis</em> — adds nothing to what the listing already states.
    /// </summary>
    [Fact]
    public async Task ANameAlreadyStatedIsNotStatedAgain()
    {
        Name(Corpus.Add(_db, "LUTH1912", TextKind.Translation, "deu", (1, 1, ["Am", "Anfang"])),
            "Das 1. Buch Mose");
        Name(Corpus.Add(_db, "ELB1905", TextKind.Translation, "deu", (1, 1, ["Im", "Anfang"])),
            "Das 1. Buch Mose");
        Name(Corpus.Add(_db, "BHSA", TextKind.ManuscriptTradition, "hbo", (1, 1, ["בְּרֵאשִׁית"])), "Genesis");
        await _db.SaveChangesAsync();

        var forms = await ReadEndpoints.NameForms(_db, default);

        forms[1].Should().ContainSingle().Which.Should().Be(new BookNameFormResponse("deu", "Das 1. Buch Mose"));
    }

    private void Name(Database.Entities.Text text, string native)
    {
        foreach (var book in _db.Books.Local.Where(b => b.Text == text))
        {
            book.NameNative = native;
        }
    }
}
