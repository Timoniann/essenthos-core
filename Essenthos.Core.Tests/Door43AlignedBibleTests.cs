using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Door43;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The older translationCore exports, which name many original words by spelling alone: a milestone
/// with an empty Strong number is a span where the source is one of those, and is what it always was
/// everywhere else.
/// </summary>
public sealed class Door43UnnumberedSpanTests
{
    private const string Verse =
        """
        \c 1
        \v 1 \zaln-s | x-strong="" x-lemma="" x-morph="" x-occurrence="1" x-occurrences="1" x-content="Βίβλος"\*\w kitab|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
        \zaln-s | x-strong="G10780" x-lemma="γένεσις" x-morph="Gr,N,,,,,GFS," x-occurrence="1" x-occurrences="1" x-content="γενέσεως"\*\w nasab|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
        """;

    [Fact]
    public void AMilestoneNamingItsWordBySpellingAloneIsASpanWhereTheSourceIsUnnumbered() =>
        Usfm3AlignmentReader.Read(Verse, unnumbered: true).Single().Spans.Select(span => span.Content)
            .Should().Equal("Βίβλος", "γενέσεως");

    [Fact]
    public void ElsewhereOnlyANumberedMilestoneIsASpan() =>
        Usfm3AlignmentReader.Read(Verse).Single().Spans.Select(span => span.Content).Should().Equal("γενέσεως");

    [Fact]
    public void EveryAlignedBibleIsKnownToTheInterlinearLoader()
    {
        foreach (var bible in Door43AlignedBible.All)
        {
            InterlinearLinkLoader.Interlinear(bible.Slug).Should().Be((bible.Folder, bible.Source));
        }
    }
}

/// <summary>
/// A Door43 alignment arriving for a text another source has already stated links for: it is a second
/// statement and is written, adding its claim where the two say the same thing.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class Door43SecondStatementTests : IDisposable
{
    private const string Clear = "Clear Bible test set";

    private const string Door43 = "Door43 test alignment";

    private readonly AppDbContext _db;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"essenthos-door43-{Guid.NewGuid():N}");

    public Door43SecondStatementTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");

        var bengali = Corpus.Add(_db, Door43TextSource.IrvBengali, TextKind.Translation, "ben", (1, 1, ["kitab", "nasab"]));
        var greek = Corpus.Add(_db, "NESTLE1904", TextKind.CriticalEdition, "grc", (1, 1, ["Βίβλος", "γενέσεως"]));
        Corpus.Add(_db, "BHSA", TextKind.CriticalEdition, "hbo", (1, 1, ["בראשית"]));
        _db.SaveChanges();
        _db.In(bengali, 40);
        _db.In(greek, 40);
        foreach (var word in _db.Words.Include(word => word.Text).ToList())
        {
            word.NormalisedText = WordFolding.Fold(word.Surface, word.Text!.Language);
        }

        _db.Links.Add(new Database.Entities.Link
        {
            FromTextId = bengali.Id,
            ToTextId = greek.Id,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.StatedBySource,
            Source = Clear,
            Words =
            [
                new Database.Entities.LinkWord { WordId = _db.WordAt(bengali, 1, 1, 1).Id, Side = LinkSide.From },
                new Database.Entities.LinkWord { WordId = _db.WordAt(greek, 1, 1, 1).Id, Side = LinkSide.To },
            ],
            Claims = [new Database.Entities.LinkClaim { Method = LinkMethod.StatedBySource, Source = Clear }],
        });
        _db.SaveChanges();

        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "41-MAT.usfm"),
            """
            \id MAT
            \c 1
            \p
            \v 1 \zaln-s | x-strong="" x-lemma="" x-morph="" x-occurrence="1" x-occurrences="1" x-content="Βίβλος"\*\w kitab|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            \zaln-s | x-strong="G10780" x-lemma="γένεσις" x-morph="Gr,N,,,,,GFS," x-occurrence="1" x-occurrences="1" x-content="γενέσεως"\*\w nasab|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """);
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task ItIsWrittenBesideTheFirstAndAgreesWhereTheySayTheSame()
    {
        var loader = new InterlinearLinkLoader(_db, NullLogger<InterlinearLinkLoader>.Instance);

        var outcome = await loader.Load(_folder, Door43TextSource.IrvBengali, Door43);

        outcome.Written.Should().Be(new InterlinearReconciliation(0, 1, 1, 0, 0, 0, 0));
        var links = await _db.Links.Include(link => link.Claims).ToListAsync();
        links.Should().HaveCount(2);
        links.Should().ContainSingle(link => link.Source == Clear)
            .Which.Claims.Select(claim => claim.Source).Should().BeEquivalentTo([Clear, Door43]);
        links.Should().ContainSingle(link => link.Source == Door43);

        (await loader.Load(_folder, Door43TextSource.IrvBengali, Door43)).Written.Should().BeNull();
    }
}
