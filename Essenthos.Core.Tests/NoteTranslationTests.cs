using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The notes of our own records in a reader's language: kept while the record still says the English
/// they render, and out of sight — the English shown instead — once it says something else.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NoteTranslationTests : IDisposable
{
    private const string Lampstand =
        "Made of one piece of beaten gold, with six branches and seven lamps (Exodus 25:31-40).";

    private readonly AppDbContext _db;

    public NoteTranslationTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Entities.Add(new Entity
        {
            Kind = EntityKind.Object,
            Slug = "lampstand",
            Name = "Lampstand",
            Notes = Lampstand,
            SourceId = "lampstand",
            Source = "a test",
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private Task<NoteTranslationOutcome> Load(params NoteTranslationRecord[] records) =>
        new NoteTranslationLoader(_db, NullLogger<NoteTranslationLoader>.Instance).Load(records, default);

    private static NoteTranslationRecord Rendered(string english, string? sha = null) =>
        new("ObjectRecords.json", "lampstand", sha ?? EnglishNotes.Digest(english), english,
            "Зроблений з одного куска кутого золота, з шістьма відгалуженнями й сімома лампадами (Вихід 25:31-40).",
            "Aus einem Stück getriebenen Goldes, mit sechs Armen und sieben Lampen (2. Mose 25,31-40).",
            null, "gpt-6.1-sol", "high", "2026-10-07T12:00:00Z");

    private async Task<string?> Shown(string language)
    {
        var lampstand = await _db.Entities.AsNoTracking().SingleAsync(e => e.Slug == "lampstand");
        return await NoteTranslations.Of(_db, lampstand.Id, lampstand.Notes, language, default);
    }

    [Fact]
    public async Task NotesRenderedFromWhatTheRecordSaysAreShownInTheReadersLanguage()
    {
        var outcome = await Load(Rendered(Lampstand));

        outcome.Written.Should().Be(2, "Ukrainian and German were rendered; Spanish was not");
        (await Shown("ukr")).Should().StartWith("Зроблений");
        (await Shown("deu")).Should().StartWith("Aus einem Stück");
        (await Shown("spa")).Should().BeNull("nothing was rendered into Spanish, so the English is shown");
        (await Shown("eng")).Should().BeNull("an English reader reads the notes themselves");
        (await _db.EntityNoteTranslations.Select(t => t.Source).FirstAsync())
            .Should().Contain("gpt-6.1-sol");

        var again = await Load(Rendered(Lampstand));
        again.Written.Should().Be(0);
        again.Removed.Should().Be(0);
    }

    /// <summary>
    /// The owner rewrites a record's notes in his console. Until the next load the stored translation
    /// is out of sight; at the load it is removed, and an entry rendered from the old English is not
    /// stored again.
    /// </summary>
    [Fact]
    public async Task NotesRewrittenSinceTheyWereRenderedShowTheEnglish()
    {
        await Load(Rendered(Lampstand));
        await _db.Entities.Where(e => e.Slug == "lampstand")
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Notes, Lampstand + " It stood on the south side."));

        (await Shown("ukr")).Should().BeNull("the translation renders what the record no longer says");

        var outcome = await Load(Rendered(Lampstand));
        outcome.Stale.Should().Be(1);
        outcome.Removed.Should().Be(2);
        (await _db.EntityNoteTranslations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnEntryWhoseDigestIsNotItsEnglishIsRefused()
    {
        var outcome = await Load(Rendered(Lampstand, sha: EnglishNotes.Digest("something else")));

        outcome.Unsound.Should().Be(1);
        (await _db.EntityNoteTranslations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnEntryForARecordNobodyHoldsIsCountedAndSkipped()
    {
        var outcome = await Load(Rendered(Lampstand) with { Slug = "golden-calf" });

        outcome.Missing.Should().Be(1);
        outcome.Written.Should().Be(0);
    }

    /// <summary>The digest is SHA-256 of the UTF-8 English in lower-case hex, as the rendering pass writes it.</summary>
    [Fact]
    public void TheDigestIsTheSha256OfTheEnglish() =>
        EnglishNotes.Digest("abc").Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

    [Fact]
    public void TheShippedFileReads() =>
        NoteTranslationLoader.Read().Should().NotBeNull();
}
