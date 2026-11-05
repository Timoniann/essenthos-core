using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Mapping;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A word with two links equally sure is described by the one whose method the merge ranks higher, whichever order the
/// links were written in: the reading is shared by the parallel view and the panes, and the same links must read the same.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ProvenanceTieTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Text _english;
    private readonly Text _greek;

    public ProvenanceTieTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        _english = Corpus.Add(_db, "ENGT", TextKind.Translation, "eng", (1, 1, ["light", "darkness"]));
        _greek = Corpus.Add(_db, "GRKT", TextKind.CriticalEdition, "grc", (1, 1, ["τὸ", "φῶς", "τὸ", "σκότος"]));
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("TRUNCATE text CASCADE");
        _db.Dispose();
    }

    [Theory]
    [InlineData(LinkMethod.Lexical, LinkMethod.StrongNumber)]
    [InlineData(LinkMethod.StrongNumber, LinkMethod.Lexical)]
    public async Task OfTwoEquallySureLinksTheMethodTheMergeRanksHigherDescribesTheWord(LinkMethod first, LinkMethod second)
    {
        var light = _db.WordAt(_english, 1, 1, 1).Id;
        Link(first, 0.95, light, _db.WordAt(_greek, 1, 1, first == LinkMethod.Lexical ? 1 : 2).Id);
        Link(second, 0.95, light, _db.WordAt(_greek, 1, 1, second == LinkMethod.Lexical ? 1 : 2).Id);

        var words = (await ChapterReading.ReadByCanonicalVerse(_db, _english.Id, 1, 1, default))[1].ToDictionary(word => word.Id);

        words[light].MappingProvenance.Should().Be("strong-number:0.95");
    }

    [Fact]
    public async Task ASurerLinkStillDescribesTheWordWhateverItsMethod()
    {
        var darkness = _db.WordAt(_english, 1, 1, 2).Id;
        Link(LinkMethod.StrongNumber, 0.8, darkness, _db.WordAt(_greek, 1, 1, 4).Id);
        Link(LinkMethod.Lexical, 0.9, darkness, _db.WordAt(_greek, 1, 1, 3).Id);

        var words = (await ChapterReading.ReadByCanonicalVerse(_db, _english.Id, 1, 1, default))[1].ToDictionary(word => word.Id);

        words[darkness].MappingProvenance.Should().Be("lexical:0.9");
    }

    /// <summary>The corpus's order of methods is the merge's, each corpus method at the highest rank of the file methods it gathers.</summary>
    [Fact]
    public void TheCorpusRanksItsMethodsAsTheMergeDoes()
    {
        string[] files =
        [
            MappingMethods.Manual, MappingMethods.Stated, MappingMethods.Strong, MappingMethods.Evidentia, MappingMethods.Names,
            MappingMethods.Possessive, MappingMethods.Rule, MappingMethods.ModelReading, MappingMethods.Composed,
            MappingMethods.Aligner, MappingMethods.NameList, MappingMethods.Lexical,
        ];
        var highest = files
            .GroupBy(MappingMethods.DatabaseMethod)
            .ToDictionary(group => group.Key, group => group.Max(file => MappingMethods.Rank(file, 1.0, "safe tier")));

        Enum.GetValues<LinkMethod>().OrderByDescending(LinkMethodPrecedence.Of)
            .Should().Equal(highest.OrderByDescending(pair => pair.Value).Select(pair => pair.Key));
    }

    private void Link(LinkMethod method, double confidence, long source, long target)
    {
        _db.Links.Add(new Link
        {
            FromTextId = _english.Id,
            ToTextId = _greek.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = confidence,
            Provenance = new() { Source = $"a test {method}" },
            Words = [new LinkWord { WordId = source, Side = LinkSide.From }, new LinkWord { WordId = target, Side = LinkSide.To }],
            Claims = [new LinkClaim { Method = method, Confidence = confidence, Provenance = new() { Source = $"a test {method}" } }],
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }
}
