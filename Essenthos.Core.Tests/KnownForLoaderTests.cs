using System.Text.Json;
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

/// <summary>What a person is known for: written once from the file, and served in the reader's language.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class KnownForLoaderTests : IDisposable
{
    private const string Ark = "built the ark and was saved from the Flood with his family";
    private const string ArkUkr = "збудував ковчег і врятувався від потопу зі своєю родиною";
    private const string Covenant = "received God's covenant never again to destroy the earth by a flood";

    private readonly AppDbContext _db;
    private readonly KnownForLoader _loader;
    private readonly Entity _noah;
    private readonly Entity _enoch;

    private static readonly KnownForFile File = new(
        "a test",
        [
            new KnownForRecord(
                "noah",
                [
                    new KnownForClause(Ark, ArkUkr, "baute die Arche", "construyó el arca", ["GEN 6:13-8:19"]),
                    new KnownForClause(Covenant, null, null, null, ["GEN 9:8-17"]),
                ]),
            new KnownForRecord(
                "enoch",
                [new KnownForClause("walked with God, and God took him", "ходив з Богом", null, null, ["GEN 5:22-24", "HEB 11:5"])]),
            new KnownForRecord("nobody", [new KnownForClause("did nothing", null, null, null, ["GEN 1:1"])]),
        ]);

    public KnownForLoaderTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _loader = new KnownForLoader(_db, NullLogger<KnownForLoader>.Instance);
        _noah = Person("noah", "Noah");
        _enoch = Person("enoch", "Enoch");
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
        _db.Dispose();
    }

    private Entity Person(string slug, string name)
    {
        var entity = new Entity
        {
            Kind = EntityKind.Person,
            Slug = slug,
            Name = name,
            SourceId = slug,
            Source = "a test",
        };
        _db.Entities.Add(entity);
        return entity;
    }

    private async Task<List<string>> Fingerprint() =>
    [
        .. await _db.EntityKnownFor.AsNoTracking().OrderBy(k => k.Id)
            .Select(k => $"{k.Id}|{k.EntityId}|{k.Ordinal}|{k.English}|{k.Source}").ToListAsync(),
        .. await _db.EntityKnownForTexts.AsNoTracking().OrderBy(t => t.Id)
            .Select(t => $"{t.Id}|{t.KnownForId}|{t.Language}|{t.Text}").ToListAsync(),
        .. await _db.EntityKnownForReferences.AsNoTracking().OrderBy(r => r.Id)
            .Select(r => $"{r.Id}|{r.KnownForId}|{r.Position}|{r.CanonicalBook}|{r.CanonicalChapter}|{r.CanonicalVerse}|{r.EndChapter}|{r.EndVerse}")
            .ToListAsync(),
    ];

    /// <summary>A person gets their clauses with every rendering and reference, and a second load changes no row.</summary>
    [Fact]
    public async Task APersonGetsTheirClausesOnceAndASecondLoadChangesNothing()
    {
        var first = await _loader.Load(File, CancellationToken.None);
        var written = await Fingerprint();
        _db.ChangeTracker.Clear();
        var second = await _loader.Load(File, CancellationToken.None);

        first.Should().Be(first with { Written = 2, Clauses = 3, Removed = 0, Missing = 1 });
        second.Should().Be(second with { Written = 0, Clauses = 0, Removed = 0, Missing = 1 });
        (await Fingerprint()).Should().Equal(written);

        var noah = await _db.EntityKnownFor.AsNoTracking().Include(k => k.Texts).Include(k => k.References)
            .Where(k => k.EntityId == _noah.Id).OrderBy(k => k.Ordinal).ToListAsync();
        noah.Select(k => k.English).Should().Equal(Ark, Covenant);
        noah[0].Texts.Select(t => (t.Language, t.Text)).Should().BeEquivalentTo(
            [("ukr", ArkUkr), ("deu", "baute die Arche"), ("spa", "construyó el arca")]);
        noah[0].References.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            CanonicalBook = 1, CanonicalChapter = 6, CanonicalVerse = 13, EndChapter = 8, EndVerse = 19,
        });
    }

    /// <summary>
    /// The clause comes in the language asked for, and in English where it has not been rendered into
    /// it; every reference is a run of verses a client can link. Nobody else has any.
    /// </summary>
    [Fact]
    public async Task TheReaderIsAnsweredInTheirLanguageOrInEnglish()
    {
        await _loader.Load(File, CancellationToken.None);

        var ukrainian = await KnownForClauses.Of(_db, _noah.Id, "ukr", CancellationToken.None);
        ukrainian!.Select(k => (k.Text, k.Language)).Should().Equal((ArkUkr, "ukr"), (Covenant, "eng"));
        var range = ukrainian[0].References.Should().ContainSingle().Subject;
        range.First.Should().Be(new VerseRefResponse(1, BookReferences.Name(1), BookReferences.Slug(1), 6, 13));
        range.Last.Should().Be(new VerseRefResponse(1, BookReferences.Name(1), BookReferences.Slug(1), 8, 19));

        var english = await KnownForClauses.Of(_db, _noah.Id, "eng", CancellationToken.None);
        english!.Select(k => (k.Text, k.Language)).Should().Equal((Ark, "eng"), (Covenant, "eng"));

        var enoch = await KnownForClauses.Of(_db, _enoch.Id, "deu", CancellationToken.None);
        enoch!.Single().References.Select(r => (r.First.Slug, r.First.Chapter, r.First.Verse, r.Last.Verse))
            .Should().Equal((BookReferences.Slug(1), 5, 22, 24), (BookReferences.Slug(58), 11, 5, 5));

        var nobody = Person("adam", "Adam");
        await _db.SaveChangesAsync();
        (await KnownForClauses.Of(_db, nobody.Id, "ukr", CancellationToken.None)).Should().BeNull();
    }

    /// <summary>
    /// A changed clause rewrites its person and nobody else, and a person the file no longer names
    /// loses theirs.
    /// </summary>
    [Fact]
    public async Task OnlyWhatTheFileChangedIsRewritten()
    {
        await _loader.Load(File, CancellationToken.None);
        var enochBefore = await _db.EntityKnownFor.AsNoTracking().Where(k => k.EntityId == _enoch.Id)
            .Select(k => k.Id).ToListAsync();
        _db.ChangeTracker.Clear();

        var changed = new KnownForFile(
            "a test",
            [
                new KnownForRecord("noah", [new KnownForClause(Ark, ArkUkr, null, null, ["GEN 6:13-8:19"])]),
                File.Records[1],
            ]);
        var outcome = await _loader.Load(changed, CancellationToken.None);

        outcome.Should().Be(outcome with { Written = 1, Clauses = 1, Removed = 0, Missing = 0 });
        (await _db.EntityKnownFor.AsNoTracking().Where(k => k.EntityId == _enoch.Id).Select(k => k.Id).ToListAsync())
            .Should().Equal(enochBefore);
        (await KnownForClauses.Of(_db, _noah.Id, "deu", CancellationToken.None))!
            .Select(k => k.Text).Should().Equal(Ark);

        _db.ChangeTracker.Clear();
        var withoutEnoch = await _loader.Load(new KnownForFile("a test", [changed.Records[0]]), CancellationToken.None);
        withoutEnoch.Removed.Should().Be(1);
        (await KnownForClauses.Of(_db, _enoch.Id, "eng", CancellationToken.None)).Should().BeNull();
    }

    /// <summary>A write the database refuses leaves every clause that was there, not a person with none.</summary>
    [Fact]
    public async Task AFailedWriteLeavesWhatWasThere()
    {
        await _loader.Load(File, CancellationToken.None);
        var before = await Fingerprint();
        _db.ChangeTracker.Clear();

        // The database refuses one rendering, so the new clauses fail after the old ones were taken away.
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION refuse_known_for() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'refused'; END $$; " +
            "CREATE TRIGGER refuse_known_for BEFORE INSERT ON entity_known_for_text FOR EACH ROW " +
            "WHEN (NEW.text = 'refuse me') EXECUTE FUNCTION refuse_known_for();");
        try
        {
            var refused = new KnownForFile(
                "a test",
                [new KnownForRecord("noah", [new KnownForClause(Ark, "refuse me", null, null, ["GEN 6:13-22"])]), File.Records[1]]);
            var load = () => _loader.Load(refused, CancellationToken.None);

            await load.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await _db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER refuse_known_for ON entity_known_for_text; DROP FUNCTION refuse_known_for();");
        }

        _db.ChangeTracker.Clear();
        (await Fingerprint()).Should().Equal(before);
    }

    /// <summary>The clauses reach the wire on a person's page, and are left off everyone else's.</summary>
    [Fact]
    public async Task ThePageCarriesTheClausesOnTheWire()
    {
        await _loader.Load(File, CancellationToken.None);
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

    /// <summary>A clause with no verse, or a reference that is not a run of verses, stops the load.</summary>
    [Theory]
    [InlineData("GEN 6")]
    [InlineData("GEN 6-9")]
    [InlineData("Genesis six")]
    public async Task AClauseMustCiteVerses(string reference)
    {
        var file = new KnownForFile("a test", [new KnownForRecord("noah", [new KnownForClause(Ark, null, null, null, [reference])])]);

        var load = () => _loader.Load(file, CancellationToken.None);

        await load.Should().ThrowAsync<InvalidDataException>();
        (await _db.EntityKnownFor.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// The file the load reads: every clause in every reader's language, and every reference a run of
    /// verses in the book it names — Jude's prophecy of Enoch is Jude and not Judges.
    /// </summary>
    [Fact]
    public void TheFileSaysEveryClauseInEveryLanguageAndCitesVerses()
    {
        var file = KnownForLoader.Read();

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
