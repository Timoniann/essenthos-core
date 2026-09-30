using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the reader is told of David, Jacob, Ruth and Jesus, held to what it was told on the day a
/// dataset's relationship rows left the corpus.
///
/// <para>
/// The fixture is every row of this project's own that names one of the four, in the order the
/// table held them, with the entity page's relationships and the family tree's ties as the running
/// API served them then. Relationships have been this project's alone on every page since
/// 2026-09-29, so taking the dataset's rows out of the table must change neither.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class WellKnownRelationshipTests : IDisposable
{
    private static readonly string[] People = ["david", "jacob", "ruth", "jesus"];

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;
    private readonly Fixture _fixture;
    private readonly Dictionary<string, Entity> _entities;

    public WellKnownRelationshipTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
        _fixture = JsonSerializer.Deserialize<Fixture>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "relationships", "well-known.json")),
            Shape)!;

        _entities = _fixture.Kinds.ToDictionary(
            kind => kind.Key,
            kind => new Entity
            {
                Kind = EnumSpelling.ToEntityKind(kind.Value),
                Slug = kind.Key,
                Name = kind.Key,
                SourceId = $"test:{kind.Key}",
                Source = "test",
            });
        _db.Entities.AddRange(_entities.Values);
        _db.SaveChanges();

        // One at a time, so the rows are numbered in the order the corpus held them: a page lists
        // its rows by that number.
        foreach (var row in _fixture.Rows)
        {
            _db.EntityRelationships.Add(new EntityRelationship
            {
                FromEntityId = _entities[row.From].Id,
                ToEntityId = _entities[row.To].Id,
                Type = row.Type,
                Category = RelationshipCategories.Read,
                CanonicalBook = row.At[0],
                CanonicalChapter = row.At[1],
                CanonicalVerse = row.At[2],
                Citation = row.Citation,
                Method = EnumSpelling.ToLinkMethod(row.Method),
                Confidence = row.Confidence,
                Source = row.Source,
            });
            _db.SaveChanges();
        }
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Theory]
    [InlineData("david")]
    [InlineData("jacob")]
    [InlineData("ruth")]
    [InlineData("jesus")]
    public async Task ThePageSaysWhatItSaid(string slug)
    {
        var page = await Relationships.Of(_db, _entities[slug].Id, null, default);

        page.Select(Shown).Should().Equal(_fixture.Pages[slug].Select(Shown));
        page.Should().OnlyContain(row => row.Dataset == Datasets.Own
            && row.Corroboration.All(folded => folded.Dataset == Datasets.Own));
    }

    [Fact]
    public async Task TheFamilyTreeDrawsTheTiesItDrew()
    {
        var family = await FamilyEndpoints.Family(_db, People, null, default);

        family.People.Select(person => person.Slug).Should().Equal(People);
        foreach (var person in family.People)
        {
            person.Ties.Select(tie => $"{tie.Type} {tie.Inward} {tie.Slug} {At(tie.Reference)}")
                .Should().Equal(
                    _fixture.Family[person.Slug].Select(tie => $"{tie.Type} {tie.Inward} {tie.Slug} {At(tie.At)}"),
                    person.Slug);
        }
    }

    /// <summary>
    /// God is nobody's kin on a tree, so Jesus's family is Mary's and Joseph's and no tie of descent
    /// or marriage reaches the divine name.
    /// </summary>
    [Fact]
    public async Task JesusHasNoParentAmongTheDivineNamesOnATree()
    {
        var jesus = (await FamilyEndpoints.Family(_db, ["jesus"], null, default)).People.Single();

        jesus.Ties.Should().Contain(tie => tie.Type == "son-of" && tie.Slug == "mary");
        jesus.Ties.Where(tie => ChapterFamily.Types.Contains(tie.Type))
            .Should().NotContain(tie => FamilyEndpoints.Deities.Contains(tie.Slug));
    }

    private static string Shown(EntityRelationshipResponse row) =>
        $"{row.Type} {row.Inward} {row.Slug} {At(row.Reference)} [{string.Join(' ', (row.Verses ?? []).Select(At))}] "
        + $"{row.Method} {row.Confidence} {row.Source} <"
        + string.Join("; ", row.Corroboration.Select(f => $"{f.Type} {f.Reversed} {At(f.Reference)} {f.Source}")) + ">";

    private static string Shown(PageRow row) =>
        $"{row.Type} {row.Inward} {row.Slug} {At(row.At)} [{string.Join(' ', (row.Verses ?? []).Select(At))}] "
        + $"{row.Method} {row.Confidence} {row.Source} <"
        + string.Join("; ", row.Folded.Select(f => $"{f.Type} {f.Reversed} {At(f.At)} {f.Source}")) + ">";

    private static string At(VerseRefResponse? verse) =>
        verse is null ? "-" : $"{verse.BookOrdinal}.{verse.Chapter}.{verse.Verse}";

    private static string At(int[]? verse) => verse is null ? "-" : string.Join('.', verse);

    private sealed record Fixture(
        Dictionary<string, string> Kinds,
        List<Row> Rows,
        Dictionary<string, List<PageRow>> Pages,
        Dictionary<string, List<Tie>> Family);

    private sealed record Row(
        string From, string Type, string To, int[] At, string? Citation, string Method, double? Confidence, string Source);

    private sealed record PageRow(
        string Type, bool Inward, string Slug, int[]? At, List<int[]>? Verses, string Method, double? Confidence,
        string Source, List<Folded> Folded);

    private sealed record Folded(string Type, bool Reversed, int[]? At, string Source);

    private sealed record Tie(string Type, bool Inward, string Slug, int[]? At);
}
