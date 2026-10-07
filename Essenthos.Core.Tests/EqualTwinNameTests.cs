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
/// Swete's Septuagint beside Brenton's Greek: one word printed by two editions, an <c>equals</c> link
/// between them, and a name only one of them carries.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class EqualTwinNameTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _brenton;
    private readonly Text _swete;
    private readonly Entity _moses;
    private readonly Entity _aaron;

    public EqualTwinNameTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _brenton = Corpus.Add(_db, "GRCBRENT", TextKind.Translation, "grc", (1, 1, ["Μωυσῆς", "καὶ", "Ἀαρών"]));
        _swete = Corpus.Add(_db, "SWETE", TextKind.Translation, "grc", (1, 1, ["Μωυσῆς", "καὶ", "Ἀαρών"]));
        _moses = Person("moses");
        _aaron = Person("aaron");
        _db.SaveChanges();
        for (var position = 1; position <= 3; position++)
        {
            Twin(position);
        }

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

    private Entity Person(string slug)
    {
        var person = new Entity { Kind = EntityKind.Person, Slug = slug, Name = slug, SourceId = slug, Source = "a test" };
        _db.Entities.Add(person);
        return person;
    }

    private void Twin(int position)
    {
        var link = new Link
        {
            FromTextId = _swete.Id, ToTextId = _brenton.Id, Relation = LinkRelation.Equals,
            Method = LinkMethod.Lexical, Confidence = 0.95, Provenance = new() { Source = "a test" },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Swete(position), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = Brenton(position), Side = LinkSide.To });
    }

    private Word Brenton(int position) => _db.WordAt(_brenton, 1, 1, position);

    private Word Swete(int position) => _db.WordAt(_swete, 1, 1, position);

    private void Name(Word word, Entity entity, double confidence = 0.9) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = entity.Id, Method = LinkMethod.ModelReading, Confidence = confidence,
            Source = "a reading of the verse",
        });

    private EqualTwinNames Pass() => new(_db, NullLogger<EqualTwinNames>.Instance);

    private async Task<Dictionary<long, WordEntity>> Named(Text text) =>
        await _db.WordEntities.AsNoTracking().Where(a => a.Word!.TextId == text.Id).ToDictionaryAsync(a => a.WordId);

    [Fact]
    public async Task TheSameWordInTheOtherEditionCarriesTheName()
    {
        Name(Brenton(1), _moses);
        await _db.SaveChangesAsync();

        var first = await Pass().Load();
        var second = await Pass().Load();

        var named = await Named(_swete);
        named[Swete(1).Id].EntityId.Should().Be(_moses.Id);
        named[Swete(1).Id].Confidence.Should().BeApproximately(0.9 * 0.95, 1e-9);
        named[Swete(1).Id].Note.Should().StartWith("through grcbrent word");
        (await _db.WordEntityClaims.CountAsync(c => c.Source == EqualTwinNames.Source)).Should().Be(1);
        first.Written.Should().Be(1);
        second.Written.Should().Be(0);
        second.Withdrawn.Should().Be(0);
    }

    [Fact]
    public async Task ATwinAnotherPassNamedKeepsItsAnswer()
    {
        Name(Brenton(1), _moses);
        Name(Swete(1), _aaron);
        await _db.SaveChangesAsync();

        await Pass().Load();

        (await Named(_swete))[Swete(1).Id].EntityId.Should().Be(_aaron.Id);
    }

    [Fact]
    public async Task AWordNamingTwoRecordsGivesNothing()
    {
        Name(Brenton(3), _moses);
        Name(Brenton(3), _aaron);
        await _db.SaveChangesAsync();

        await Pass().Load();

        (await Named(_swete)).Should().BeEmpty();
    }

    /// <summary>A conjunction is never a name, and a faint answer is no answer to give.</summary>
    [Fact]
    public async Task AFunctionWordOrAFaintAnswerGivesNothing()
    {
        Swete(2).NormalisedText = "και";
        Name(Brenton(2), _moses);
        Name(Brenton(3), _aaron, 0.5);
        await _db.SaveChangesAsync();

        await Pass().Load();

        (await Named(_swete)).Should().BeEmpty();
    }

    [Fact]
    public async Task ANameTheTwinNoLongerCarriesIsTakenBack()
    {
        Name(Brenton(1), _moses);
        await _db.SaveChangesAsync();
        await Pass().Load();
        await _db.WordEntities.Where(a => a.WordId == Brenton(1).Id).ExecuteDeleteAsync();

        var outcome = await Pass().Load();

        outcome.Withdrawn.Should().Be(1);
        (await Named(_swete)).Should().BeEmpty();
    }
}
