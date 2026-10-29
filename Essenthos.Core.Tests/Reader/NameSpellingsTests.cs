using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A name other than a record's heading is given as the texts of the reader's language print it,
/// through the Strong number each of its words was named through.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NameSpellingsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Entity _joseph;

    public NameSpellingsTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();

        var greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc", (1, 1, ["Ἰωσῆς", "Ἰωσὴφ", "Ἰωσῆτος"]));
        var ohienko = Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (1, 1, ["Йосипа", "Йосип", "Йосип"]));
        var kulish = Corpus.Add(_db, "UKR1871", TextKind.Translation, "ukr", (1, 1, ["Йосій", "Йосія", "Йосій"]));
        var luther = Corpus.Add(_db, "LUTH1912", TextKind.Translation, "deu", (1, 1, ["Joses", "Joseph", "Joses"]));

        _joseph = new Entity
        {
            Kind = EntityKind.Person, Slug = "joseph-7", Name = "Joseph", SourceId = "Joseph_7", Source = "a test",
        };
        _joseph.Names.Add(new EntityName { Label = "Joseph", Kind = "proper name", GreekStrongNumber = "G2501" });
        _joseph.Names.Add(new EntityName { Label = "Joses", Kind = "proper name", GreekStrongNumber = "G2500" });
        _joseph.Names.Add(new EntityName { Label = "Salome", Kind = "proper name", GreekStrongNumber = "G4539" });
        _db.Entities.Add(_joseph);
        _db.SaveChanges();
        _db.EntityNameForms.Add(new EntityNameForm
        {
            EntityId = _joseph.Id, Language = "ukr", GrammaticalCase = GrammaticalCases.Nominative,
            Form = "Йосип", Method = LinkMethod.ModelReading, Confidence = 1, Source = "a test",
        });

        var g1 = _db.WordAt(greek, 1, 1, 1);
        var g2 = _db.WordAt(greek, 1, 1, 2);
        var g3 = _db.WordAt(greek, 1, 1, 3);
        g1.StrongNumber = "G2500";
        g2.StrongNumber = "G2501";
        g3.StrongNumber = "G2500";

        // The Ohienko Bible prints Joses as it prints Joseph; Kulish as Йосій, in two cases.
        Carried(_db.WordAt(ohienko, 1, 1, 1), g1);
        Carried(_db.WordAt(ohienko, 1, 1, 2), g2);
        Carried(_db.WordAt(ohienko, 1, 1, 3), g3);
        Carried(_db.WordAt(kulish, 1, 1, 1), g1);
        Carried(_db.WordAt(kulish, 1, 1, 2), g3);
        Carried(_db.WordAt(kulish, 1, 1, 3), g1);
        var own = _db.WordAt(luther, 1, 1, 1);
        own.StrongNumber = "G2500";
        Named(own, null);
        Carried(_db.WordAt(luther, 1, 1, 2), g2);
        Carried(_db.WordAt(luther, 1, 1, 3), g3);
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

    private void Carried(Word word, Word from) =>
        Named(word, $"through NESTLE1904 word {from.Id}, linked by aligner");

    private void Named(Word word, string? note) =>
        _db.WordEntities.Add(new WordEntity
        {
            WordId = word.Id, EntityId = _joseph.Id, Method = LinkMethod.Aligner, Confidence = 0.9,
            Source = "a test", Note = note,
        });

    private static readonly BorneName[] Others =
    [
        new("Joses", ["G2500"]),
        new("Salome", ["G4539"]),
    ];

    [Fact]
    public async Task ANameIsSpelledAsTheLanguagesTextsPrintItMost()
    {
        // Joses: Йосипа and Йосип in the Ohienko (two), Йосій twice and Йосія once in Kulish (three).
        var ukrainian = await NameSpellings.Of(_db, _joseph.Id, Others, "ukr", "Йосип", CancellationToken.None);

        ukrainian.Should().BeEquivalentTo(new Dictionary<string, string> { ["Joses"] = "Йосій" });
    }

    [Fact]
    public async Task ANameTheRecordsOwnFormSpellsKeepsThatForm()
    {
        var joseph = await NameSpellings.Of(
            _db, _joseph.Id, [new BorneName("Joseph", ["G2501"])], "ukr", "Йосип", CancellationToken.None);

        joseph.Should().BeEquivalentTo(new Dictionary<string, string> { ["Joseph"] = "Йосип" });
    }

    [Fact]
    public async Task AWordsOwnNumberCountsAndEnglishHasNone()
    {
        var german = await NameSpellings.Of(_db, _joseph.Id, Others, "deu", null, CancellationToken.None);
        var english = await NameSpellings.Of(_db, _joseph.Id, Others, "eng", null, CancellationToken.None);

        german.Should().BeEquivalentTo(new Dictionary<string, string> { ["Joses"] = "Joses" });
        english.Should().BeEmpty();
    }

    [Fact]
    public void TheNumbersOfANameAreReadPastTheGapsTheSourceLeaves() =>
        NameSpellings.Numbers("H1, NONE", null, "G2500").Should().Equal("H1", "G2500");
}
