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
/// A person carried onto a pronoun stays where the verse settles who the pronoun is and goes where it
/// does not — the owner's ruling of 2026-10-07 on the Berean's <em>him</em> for Moses.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PronounReferentTests : IDisposable
{
    private readonly AppDbContext _db;

    public PronounReferentTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
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

    private Entity Person(string slug, string name, string? sex)
    {
        var person = new Entity
        {
            Kind = EntityKind.Person, Slug = slug, Name = name, Sex = sex, SourceId = slug, Source = "a test",
        };
        _db.Entities.Add(person);
        return person;
    }

    private async Task Name(int chapter, int verse, int position, Entity who, double? confidence = 0.9)
    {
        var word = await _db.Words.SingleAsync(w => w.Verse!.ChapterNumber == chapter
                                                    && w.Verse.Number == verse && w.Position == position);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id,
            EntityId = who.Id,
            Method = confidence is null ? LinkMethod.Manual : LinkMethod.Aligner,
            Confidence = confidence,
            Source = "a test",
            Note = confidence is null ? null : "through BHSA word 1, linked by aligner",
        });
    }

    [Fact]
    public async Task APronounKeepsThePersonOnlyWhereTheVerseSettlesWhoItIs()
    {
        Corpus.Add(_db, "BSB", TextKind.Translation, "eng",
            (5, 1, ["Moses", "spoke", "to", "the", "people"]),
            (5, 2, ["and", "he", "went", "up"]),
            (5, 3, ["Moses", "and", "Aaron", "came", "and", "he", "spoke"]),
            (5, 4, ["Moses", "said", "she", "will", "go"]),
            (5, 5, ["Moses", "heard", "the", "LORD", "say", "that", "He", "will", "go"]),
            (5, 6, ["Ruth", "showed", "her", "mother-in-law"]),
            (5, 7, ["Moses", "said", "to", "thee"]),
            (5, 8, ["Moses", "and", "Aaron", "and", "him"]));
        var moses = Person("moses", "Moses", "male");
        var aaron = Person("aaron", "Aaron", "male");
        var yhvh = Person("yhvh", "YHVH", "male");
        var ruth = Person("ruth", "Ruth", "female");
        var naomi = Person("naomi", "Naomi", "female");
        await _db.SaveChangesAsync();

        await Name(5, 1, 1, moses);
        await Name(5, 2, 2, moses);                 // he, Moses named the verse before: kept
        await Name(5, 3, 1, moses);
        await Name(5, 3, 3, aaron);
        await Name(5, 3, 6, moses);                 // he, with Aaron beside Moses: taken back
        await Name(5, 4, 1, moses);
        await Name(5, 4, 3, moses);                 // she for a man: taken back
        await Name(5, 5, 1, moses);
        await Name(5, 5, 4, yhvh);
        await Name(5, 5, 7, yhvh);                  // He, capitalised mid-sentence for YHVH: kept
        await Name(5, 6, 1, ruth);
        await Name(5, 6, 4, naomi);
        await Name(5, 6, 3, naomi);                 // her mother-in-law: the her is Ruth's, taken back
        await Name(5, 7, 1, moses);
        await Name(5, 7, 4, moses);                 // thee, which says no one: taken back
        await Name(5, 8, 1, moses);
        await Name(5, 8, 3, aaron);
        await Name(5, 8, 5, moses, confidence: null); // a ruling: never taken back
        await _db.SaveChangesAsync();

        var pass = new PronounReferents(_db, NullLogger<PronounReferents>.Instance);
        var outcome = await pass.Withdraw();

        outcome.Withdrawn.Should().Be(4);
        outcome.Kept.Should().Be(2);
        var pronouns = await _db.WordEntities
            .Where(a => new[] { "he", "He", "she", "her", "thee", "him" }.Contains(a.Word!.Surface))
            .Select(a => a.Word!.Verse!.Number + " " + a.Word.Surface + " " + a.Entity!.Slug)
            .ToListAsync();
        pronouns.Should().BeEquivalentTo("2 he moses", "5 He yhvh", "8 him moses");

        (await pass.Withdraw()).Withdrawn.Should().Be(0, "the rule reads only words it does not touch");
    }

    [Fact]
    public async Task APlaceOrAPeopleCarriedOntoAPronounIsTakenBackAndAGreekPronounIsJudgedLikeTheRest()
    {
        Corpus.Add(_db, "GRCBRENT", TextKind.Translation, "grc",
            (5, 1, ["Μωυσῆς", "εἶπεν", "αὐτόν"]),
            (5, 2, ["Μωυσῆς", "καὶ", "Ἀαρὼν", "αὐτὸν"]),
            (5, 3, ["Αἴγυπτος", "ἦν", "αὐτῆς"]),
            (5, 4, ["Ἰσραὴλ", "εἶπεν", "αὐτῶν"]),
            (5, 5, ["Μωυσῆς", "γυνή"]));
        var moses = Person("moses", "Moses", "male");
        var aaron = Person("aaron", "Aaron", "male");
        var egypt = new Entity { Kind = EntityKind.Place, Slug = "egypt", Name = "Egypt", SourceId = "egypt", Source = "a test" };
        var israel = new Entity { Kind = EntityKind.People, Slug = "israelites", Name = "Israelites", SourceId = "israelites", Source = "a test" };
        _db.Entities.AddRange(egypt, israel);
        await _db.SaveChangesAsync();

        await Name(5, 1, 1, moses);
        await Name(5, 1, 3, moses);                 // αὐτόν, Moses named in the verse and nobody else male: kept
        await Name(5, 2, 1, moses);
        await Name(5, 2, 3, aaron);
        await Name(5, 2, 4, moses);                 // αὐτὸν with a second man in the verse: taken back
        await Name(5, 3, 1, egypt);
        await Name(5, 3, 3, egypt);                 // a place is never a pronoun's person: taken back
        await Name(5, 4, 1, israel);
        await Name(5, 4, 3, israel);                // nor a people: taken back
        await Name(5, 5, 1, moses);
        await Name(5, 5, 2, moses);                 // γυνή is a common noun, not a pronoun: left
        await _db.SaveChangesAsync();

        var outcome = await new PronounReferents(_db, NullLogger<PronounReferents>.Instance).Withdraw();

        outcome.Withdrawn.Should().Be(3);
        outcome.Kept.Should().Be(1);
        var left = await _db.WordEntities
            .Select(a => a.Word!.Verse!.Number + " " + a.Word.Surface + " " + a.Entity!.Slug)
            .ToListAsync();
        left.Should().BeEquivalentTo("1 Μωυσῆς moses", "1 αὐτόν moses", "2 Μωυσῆς moses", "2 Ἀαρὼν aaron", "3 Αἴγυπτος egypt",
            "4 Ἰσραὴλ israelites", "5 Μωυσῆς moses", "5 γυνή moses");
    }

    /// <summary>
    /// A carry that writes a pronoun's person only for the pass after it to take it back burns an id on
    /// every load for nothing. The rows it is about to write are judged first, and the ones the verse does
    /// not settle are left out of what it writes.
    /// </summary>
    [Fact]
    public async Task APersonTheVerseDoesNotSettleIsLeftOutOfWhatACarryIsAboutToWrite()
    {
        Corpus.Add(_db, "BSB", TextKind.Translation, "eng",
            (5, 1, ["Moses", "spoke", "to", "the", "people"]),
            (5, 2, ["and", "he", "went", "up"]),
            (5, 3, ["Moses", "and", "Aaron", "came", "and", "he", "spoke"]),
            (5, 4, ["Moses", "said", "she", "will", "go"]),
            (5, 5, ["Moses", "heard", "the", "LORD", "say", "that", "He", "will", "go"]),
            (5, 6, ["Ruth", "showed", "her", "mother-in-law"]),
            (5, 7, ["Moses", "said", "to", "thee"]));
        var moses = Person("moses", "Moses", "male");
        var aaron = Person("aaron", "Aaron", "male");
        var yhvh = Person("yhvh", "YHVH", "male");
        var ruth = Person("ruth", "Ruth", "female");
        var naomi = Person("naomi", "Naomi", "female");
        await _db.SaveChangesAsync();

        // The names on words that are not pronouns are in the corpus already; the pronouns' are expected.
        await Name(5, 1, 1, moses);
        await Name(5, 3, 1, moses);
        await Name(5, 3, 3, aaron);
        await Name(5, 4, 1, moses);
        await Name(5, 5, 1, moses);
        await Name(5, 5, 4, yhvh);
        await Name(5, 6, 1, ruth);
        await Name(5, 6, 4, naomi);
        await Name(5, 7, 1, moses);
        await _db.SaveChangesAsync();

        await _db.Database.OpenConnectionAsync();
        var connection = (Npgsql.NpgsqlConnection)_db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var create = new Npgsql.NpgsqlCommand(
                         "CREATE TEMP TABLE expected (word_id bigint, entity_id integer, confidence double precision) ON COMMIT DROP",
                         connection, transaction))
        {
            await create.ExecuteNonQueryAsync();
        }

        async Task Expect(int verse, int position, Entity who, double? confidence = 0.9)
        {
            var word = await _db.Words.SingleAsync(w => w.Verse!.ChapterNumber == 5 && w.Verse.Number == verse && w.Position == position);
            await using var insert = new Npgsql.NpgsqlCommand(
                "INSERT INTO expected VALUES (@word, @entity, @confidence)", connection, transaction);
            insert.Parameters.AddWithValue("word", word.Id);
            insert.Parameters.AddWithValue("entity", who.Id);
            insert.Parameters.AddWithValue("confidence", (object?)confidence ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        await Expect(2, 2, moses);                 // he, Moses named the verse before: written
        await Expect(3, 6, moses);                 // he, with Aaron beside Moses: left out
        await Expect(4, 3, moses);                 // she for a man: left out
        await Expect(5, 7, yhvh);                  // He, capitalised mid-sentence for YHVH: written
        await Expect(6, 3, naomi);                 // her mother-in-law: left out
        await Expect(7, 4, moses);                 // thee, which says no one: left out
        await Expect(3, 4, aaron, confidence: null); // an answer nobody estimated is never left out

        var pass = new PronounReferents(_db, NullLogger<PronounReferents>.Instance);
        (await pass.LeaveOut(connection, transaction, CancellationToken.None)).Should().Be(4);

        var left = new List<string>();
        await using (var read = new Npgsql.NpgsqlCommand(
                         "SELECT w.verse_id, w.position, e.slug FROM expected x JOIN word w ON w.id = x.word_id JOIN entity e ON e.id = x.entity_id ORDER BY 1, 2",
                         connection, transaction))
        await using (var reader = await read.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                left.Add($"{reader.GetInt32(1)} {reader.GetString(2)}");
            }
        }

        left.Should().BeEquivalentTo("2 moses", "7 yhvh", "4 aaron");
        (await pass.LeaveOut(connection, transaction, CancellationToken.None)).Should().Be(0, "what is left is what is written");
        await transaction.RollbackAsync();
        (await _db.WordEntities.CountAsync(a => new[] { "he", "He", "she", "her", "thee" }.Contains(a.Word!.Surface)))
            .Should().Be(0, "nothing was written");
    }

    [Theory]
    [InlineData("eng", "Him", true)]
    [InlineData("ukr", "його", true)]
    [InlineData("deu", "ihm", true)]
    [InlineData("eng", "Moses", false)]
    [InlineData("grc", "αὐτόν", true)]
    [InlineData("grc", "αὐτὸν", true)]
    [InlineData("grc", "ἐγὼ", true)]
    [InlineData("grc", "ἑαυτοῦ", true)]
    [InlineData("grc", "τοῦτο", true)]
    [InlineData("grc", "Μωυσῆς", false)]
    [InlineData("grc", "γυνή", false)]
    public void APronounIsKnownForWhatItIs(string language, string word, bool pronoun) =>
        Pronouns.Is(language, word.ToLowerInvariant()).Should().Be(pronoun);
}
