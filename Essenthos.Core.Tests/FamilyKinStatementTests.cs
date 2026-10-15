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

/// <summary>What the verses say of a person's children, loaded from the shipped file and said in each language.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class FamilyKinStatementTests : IDisposable
{
    private readonly AppDbContext _db;

    private readonly KinStatementLoader _loader;

    private const string Decided = "an agent reading the original on the project owner's instruction, decided 2026-10-15";

    public FamilyKinStatementTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new KinStatementLoader(_db, NullLogger<KinStatementLoader>.Instance);

        foreach (var (slug, chapter, verse) in new[] { ("gideon", 8, 30), ("jesse", 2, 13), ("david", 2, 15), ("nobody", 1, 1) })
        {
            var person = new Entity
            {
                Kind = EntityKind.Person, Slug = slug, Name = slug, Sex = "male", SourceId = slug, Source = "a test",
            };
            person.Verses.Add(new EntityVerse
            {
                CanonicalBook = slug == "gideon" ? 7 : 13, CanonicalChapter = chapter, CanonicalVerse = verse, Source = "a test",
            });
            _db.Entities.Add(person);
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private static readonly KinStatementRecord[] Statements =
    [
        new("gideon", KinStatementKinds.Count, "JDG 8:30", Decided, Sons: 70, Witness: "BHSA", Original: "שִׁבְעִ֣ים בָּנִ֔ים"),
        new("david", KinStatementKinds.Order, "1CH 2:13-15", Decided, Ordinal: 7, Parent: "jesse"),
    ];

    [Fact]
    public async Task EachStatementIsWrittenOnceAndASecondRunWritesNothing()
    {
        var first = await _loader.Load(Statements, default);
        var second = await _loader.Load(Statements, default);

        first.Written.Should().Be(2);
        second.Written.Should().Be(0);
        second.Removed.Should().Be(0);
        var gideon = await _db.EntityKinStatements.SingleAsync(s => s.Kind == KinStatementKinds.Count);
        (gideon.Sons, gideon.CanonicalBook, gideon.CanonicalChapter, gideon.CanonicalVerse, gideon.Method, gideon.Confidence)
            .Should().Be((70, 7, 8, 30, LinkMethod.Manual, (double?)null));
        gideon.Source.Should().Be("read from Scripture by " + Decided);
        var david = await _db.EntityKinStatements.SingleAsync(s => s.Kind == KinStatementKinds.Order);
        (david.Ordinal, david.Citation, david.CanonicalVerse).Should().Be((7, "1CH 2:13-15", 13));

        var third = await _loader.Load([Statements[0]], default);
        third.Removed.Should().Be(1);
        (await _db.EntityKinStatements.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AStatementOfNobodyTheCorpusHoldsOrOfAVerseThatDoesNotNameThemIsRefused()
    {
        var outcome = await _loader.Load(
        [
            new("noone", KinStatementKinds.Count, "JDG 8:30", Decided, Sons: 3),
            new("nobody", KinStatementKinds.Count, "JDG 8:30", Decided, Sons: 3),
            new("gideon", KinStatementKinds.Count, "JDG 8:30", Decided),
            new("gideon", KinStatementKinds.More, "JDG 8:30", Decided),
            new("david", KinStatementKinds.Order, "1CH 2:15", Decided, Ordinal: 7, Parent: "noone"),
        ], default);

        outcome.Refused.Should().Be(5);
        outcome.Written.Should().Be(0);
    }

    [Fact]
    public void EveryShippedStatementIsReadInAnOriginalAndSaysSomething()
    {
        var records = KinStatementLoader.Read(TestResources.Shipped(KinStatementLoader.Folder));

        records.Should().NotBeEmpty();
        records.Should().OnlyContain(r => KinStatementKinds.All.Contains(r.Kind)
                                          && Citation.Parse(r.Reference) != null
                                          && (r.Witness == "BHSA" || r.Witness == "NESTLE1904")
                                          && !string.IsNullOrWhiteSpace(r.Original)
                                          && !string.IsNullOrWhiteSpace(r.DecidedBy));
        records.Where(r => r.Kind == KinStatementKinds.Count)
            .Should().OnlyContain(r => r.Sons > 0 || r.Daughters > 0 || r.Children > 0 || r.Grandsons > 0);
        records.Where(r => r.Kind == KinStatementKinds.More).Should().OnlyContain(r => r.MoreSons || r.MoreDaughters);
        records.Where(r => r.Kind == KinStatementKinds.Order).Should().OnlyContain(r => r.Parent != null && r.Ordinal >= 1);
        records.Where(r => r.Entity == "job").Select(r => r.Family).Distinct().Should().HaveCount(2);
        records.Should().Contain(r => r.Entity == "david" && r.Ordinal == 7 && r.Parent == "jesse");
    }

    [Theory]
    [InlineData("eng", "seven named sons and a daughter")]
    [InlineData("ukr", "семи названих синів та дочки")]
    [InlineData("deu", "von sieben genannten Söhnen und einer Tochter")]
    [InlineData("spa", "siete hijos nombrados y una hija")]
    public void ACountOfTheNamedSaysSo(string language, string said) =>
        DescriptorPhrasings.Counted(language, [(Kin.Son, 7), (Kin.Daughter, 1)], named: true).Should().Be(said);

    [Theory]
    [InlineData("eng", 7, false, "seventh son of ")]
    [InlineData("eng", 1, false, "firstborn son of ")]
    [InlineData("ukr", 7, false, "сьомий син ")]
    [InlineData("ukr", 1, false, "первісток ")]
    [InlineData("ukr", 1, true, "первістка ")]
    [InlineData("ukr", 2, true, "друга дочка ")]
    [InlineData("deu", 3, false, "dritter Sohn ")]
    [InlineData("spa", 3, false, "tercer hijo de ")]
    [InlineData("spa", 1, true, "primogénita de ")]
    public void APlaceAmongTheChildrenIsSaidInEachLanguage(string language, int ordinal, bool daughter, string before)
    {
        var plain = DescriptorPhrasings.For(language)![daughter ? DescriptorRelations.DaughterOf : DescriptorRelations.SonOf];

        DescriptorPhrasings.Ordinal(language, plain, daughter, ordinal)!.Before.Should().Be(before);
        DescriptorPhrasings.Ordinal(language, plain, daughter, 13).Should().BeNull();
    }

    [Theory]
    [InlineData(1, false, ", з них названий ")]
    [InlineData(1, true, ", з них названа ")]
    [InlineData(3, false, ", з них названі ")]
    public void TheNamedAmongACountAgreeWithTheirNumber(int named, bool daughters, string said) =>
        DescriptorPhrasings.AmongThem("ukr", named, daughters).Should().Be(said);

    [Theory]
    [InlineData("eng", " and other sons and daughters", ", and of other sons")]
    [InlineData("ukr", " та інших синів і дочок", ", а також інших синів")]
    [InlineData("deu", " und weiterer Söhne und Töchter", " sowie weiteren Söhnen")]
    [InlineData("spa", " y otros hijos e hijas", ", y de otros hijos")]
    public void MoreChildrenAreSaidAfterNamesAndAfterACount(string language, string afterNames, string afterCount)
    {
        DescriptorPhrasings.Others(language, sons: true, daughters: true, afterCount: false).Should().Be(afterNames);
        DescriptorPhrasings.Others(language, sons: true, daughters: false, afterCount: true).Should().Be(afterCount);
    }

    private void Clear()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Database.ExecuteSqlRaw("DELETE FROM entity");
    }
}
