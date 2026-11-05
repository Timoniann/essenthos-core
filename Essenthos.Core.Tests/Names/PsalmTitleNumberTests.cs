using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// The titles of Psalms 52 and 60 as a corpus loaded them with every Strong number one word off, and
/// what the names read off those numbers become once the numbers are put right: Doeg's word named
/// David, and the valley of salt named the Salt Sea, each named from its own number again.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PsalmTitleNumberTests : IDisposable
{
    private readonly AppDbContext _db;

    public PsalmTitleNumberTests(WitnessDatabase database)
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

    /// <summary>
    /// Psalm 52's title, two verses of BHSA at one address. Read by position alone, Doeg took the
    /// sixth number the file states, which is David's, and the resolution named David at Doeg's
    /// word. Numbered again, the word carries Doeg's number and is named Doeg.
    /// </summary>
    [Fact]
    public async Task DoegsWordIsNamedDoegOnceItCarriesItsOwnNumber()
    {
        var kjv = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (1, 1, ["Doeg", "Edomite", "what"]));
        var bhsa = Corpus.Add(_db, EntityCandidates.Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["לַ", "הַ", "מְנַצֵּחַ", "מַשְׂכִּיל", "לְ", "דָוִד"]),
            (1, 2, ["בְּ", "בֹוא", "דֹּואֵג", "הָ", "אֲדֹמִי"]),
            (1, 3, ["מַה"]));
        _db.SaveChanges();
        Title(bhsa);
        Name(bhsa, 1, 6, "pers");
        Name(bhsa, 2, 3, "pers");
        Record("david", EntityKind.Person, "David", "H1732");
        Record("doeg", EntityKind.Person, "Doeg", "H1673");
        await _db.SaveChangesAsync();
        var record = new MappingRecord(1, 1, 1,
        [
            new("H9005", "c1", 1, "to"), new("H9009", "c1", 2, "the"), new("H5329", "c1", 3, "prevail"),
            new("H4905", "c1", 4, "maskil"), new("H9005", "c1", 5, "to"), new("H1732", "c1", 6, "David"),
            new("H9003", "c2", 7, "in"), new("H935", "c2", 8, "come"), new("H1673", "c2", 9, "Doeg"),
            new("H9009", "c2", 10, "the"), new("H130", "c2", 11, "Edomite"), new("H4100", "c3", 12, "what"),
        ],
        [Segment(["Doeg"], 9), Segment(["Edomite"], 11), Segment(["what"], 12)]);
        await new OldTestamentLinkLoader(_db, NullLogger<OldTestamentLinkLoader>.Instance).Load([record]);
        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE word w SET strong_number = interleaved.number
            FROM (VALUES (1, 1, 'H9005'), (2, 1, 'H9009'), (1, 2, 'H5329'), (2, 2, 'H4905'), (1, 3, 'H9005'),
                         (2, 3, 'H1732'), (1, 4, 'H9003'), (2, 4, 'H935'), (1, 5, 'H1673'), (2, 5, 'H9009'),
                         (1, 6, 'H130'))
                 AS interleaved(verse, position, number), verse v
            WHERE v.id = w.verse_id AND v.number = interleaved.verse AND w.position = interleaved.position
              AND w.text_id = {0}
            """,
            bhsa.Id);
        await Annotate();
        (await Names(bhsa, 2, 3)).Should().Equal("david");
        (await Names(kjv, 1, 1)).Should().Equal("david");

        await new OldTestamentLinkLoader(_db, NullLogger<OldTestamentLinkLoader>.Instance).Load([record]);
        await Annotate();

        (await Names(bhsa, 2, 3)).Should().Equal("doeg");
        (await Names(kjv, 1, 1)).Should().Equal("doeg");
        (await Names(bhsa, 1, 6)).Should().Equal("david");
    }

    /// <summary>BHSA's title verses both at the frame's verse 0, in the order they are written.</summary>
    private void Title(Text bhsa) =>
        _db.Database.ExecuteSqlRaw(
            """
            UPDATE verse SET sequence = number WHERE text_id = {0};
            UPDATE verse_reference r SET canonical_verse = CASE v.number WHEN 3 THEN 1 ELSE 0 END
            FROM verse v WHERE v.id = r.verse_id AND v.text_id = {0}
            """,
            bhsa.Id);

    private void Name(Text bhsa, int verse, int position, string nameType, string? lemma = null)
    {
        var word = _db.WordAt(bhsa, 1, verse, position);
        word.Lemma = lemma ?? word.Surface;
        word.Morphology = JsonDocument.Parse($$"""{"pos": "nmpr", "nameType": "{{nameType}}"}""");
        _db.SaveChanges();
    }

    private Entity Record(string slug, EntityKind kind, string name, string? number)
    {
        var entity = new Entity { Kind = kind, Slug = slug, Name = name, SourceId = slug, Source = "a test" };
        _db.Entities.Add(entity);
        if (number is not null)
        {
            _db.EntityNames.Add(new EntityName { Entity = entity, Label = name, HebrewStrongNumber = number, Kind = "proper name" });
        }

        return entity;
    }

    private async Task Annotate() =>
        await new EntityAnnotationLoader(_db, NullLogger<EntityAnnotationLoader>.Instance).Load();

    private async Task<string[]> Names(Text text, int verse, int position)
    {
        _db.ChangeTracker.Clear();
        return await _db.WordEntities
            .Where(a => a.Word!.TextId == text.Id && a.Word.Verse!.Number == verse && a.Word.Position == position)
            .Select(a => a.Entity!.Slug)
            .ToArrayAsync();
    }

    private static EnglishSegment Segment(string[] words, int rendersPosition) =>
        new([.. words.Select(word => new EnglishWord(word, false))], new HebrewEntry($"H{rendersPosition}", "c1", rendersPosition, ""));
}
