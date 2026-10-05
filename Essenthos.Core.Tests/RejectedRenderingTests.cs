using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class RejectedRenderingTests : IDisposable
{
    private readonly AppDbContext db;
    private readonly Dictionary<RuledWord, Word> words;
    private static readonly RuledWord Christ = new("RUSV", "1CO 5:5", 15, "Христа");
    private static readonly RuledWord Satan = new("NESTLE1904", "1CO 5:5", 5, "Σατανᾷ");
    private const string Source = "SIL.Machine, aligned as written and as stems and through KJV";

    public RejectedRenderingTests(WitnessDatabase database)
    {
        db = database.NewContext();
        db.Database.ExecuteSqlRaw("DELETE FROM text; DELETE FROM entity");
        words = db.Place(RejectedRenderings.All.SelectMany(r => new[] { r.From, r.To })
            .Concat(new[] { new RuledWord("RUSV", "ROM 16:20", 5, "сатану"),
                new RuledWord("RUSV", "1CO 5:5", 2, "сатане") }));
    }

    public void Dispose()
    {
        db.Database.ExecuteSqlRaw("DELETE FROM text; DELETE FROM entity");
        db.Dispose();
    }

    [Fact]
    public async Task ARejectedStatisticalChristToSatanPairCannotBeWrittenAgain()
    {
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync();
        var draft = new NewLink(words[Christ].TextId, words[Satan].TextId, LinkRelation.Renders,
            LinkMethod.Aligner, 0.98, Source, null, [words[Christ].Id], [words[Satan].Id]);
        var write = () => LinkWriter.Write(connection, transaction, [draft], CancellationToken.None);
        await write.Should().ThrowAsync<InvalidDataException>().WithMessage("*rejected rendering*");
    }

    [Fact]
    public async Task AdmissionRejectsAllThreePairingsAndTheirReverseButKeepsOtherCorrespondences()
    {
        await db.Database.OpenConnectionAsync();
        var rejected = RejectedRenderings.All.Select(r => Draft(words[r.From], words[r.To])).ToArray();
        var reverse = rejected.Select(d => d with { FromTextId = d.ToTextId, ToTextId = d.FromTextId,
            From = d.To, To = d.From }).ToArray();
        var correct = Draft(words[new("RUSV", "1CO 5:5", 2, "сатане")], words[Satan]);
        var sourced = rejected[0] with { Method = LinkMethod.StatedBySource, Confidence = null, Source = "an independent source" };
        var manual = rejected[0] with { Method = LinkMethod.Manual, Confidence = null, Source = "a person's reading" };
        var phrase = rejected[0] with { From = [.. rejected[0].From, correct.From.Single()] };
        var admitted = await RejectedRenderings.Admitted((NpgsqlConnection)db.Database.GetDbConnection(),
            [.. rejected, .. reverse, correct, sourced, manual, phrase], CancellationToken.None);
        admitted.Should().Equal(correct, sourced, manual, phrase);
    }

    [Fact]
    public async Task ComposingTheStatisticalRoutesOmitsTheRejectedPairsAndKeepsActualSatan()
    {
        await db.Database.OpenConnectionAsync();
        var pipeline = new CompositionPipeline(db,
            new AlignmentPipeline(db, NullLogger<AlignmentPipeline>.Instance), NullLogger<CompositionPipeline>.Instance);
        var russian = words[Christ].Text!;
        var greek = words[Satan].Text!;
        var proposals = RejectedRenderings.All.Where(r => r.To.Text == "NESTLE1904")
            .Select(r => new RoutedLink(words[r.From].Id, words[r.To].Id, 0.98, Route.Written | Route.Composed)).ToList();
        var actual = words[new("RUSV", "1CO 5:5", 2, "сатане")];
        proposals.Add(new(actual.Id, words[Satan].Id, 0.99, Route.Written));
        (await pipeline.Write((NpgsqlConnection)db.Database.GetDbConnection(), russian, greek, ["KJV"], proposals,
            CancellationToken.None)).Should().Be((1, 0));
        var link = await db.Links.Include(l => l.Words).SingleAsync();
        link.Words.Select(w => w.WordId).Should().BeEquivalentTo([actual.Id, words[Satan].Id]);
    }

    [Fact]
    public async Task WarmWithdrawalRemovesOnlyRejectedLinksAndTheirCarriedAnnotationsAndRepeatsWithoutWriting()
    {
        AddWarmLinks();
        AddCarriedAnnotations();
        var kept = await db.Links.Where(l => l.Method == LinkMethod.StrongNumber).Select(l => l.Id).ToArrayAsync();
        var preserved = await db.WordEntities.Where(a => a.Note == "independent seed").Select(a => a.Id).ToArrayAsync();

        (await RejectedRenderings.Withdraw(db)).Should().Be(new RejectedRenderingOutcome(3, 2));
        (await db.Links.Select(l => l.Id).ToArrayAsync()).Should().BeEquivalentTo(kept);
        (await db.WordEntities.Select(a => a.Id).ToArrayAsync()).Should().BeEquivalentTo(preserved);
        (await RejectedRenderings.Withdraw(db)).Should().Be(new RejectedRenderingOutcome(0, 0));
        (await db.Links.Select(l => l.Id).ToArrayAsync()).Should().BeEquivalentTo(kept);
        (await db.WordEntities.Select(a => a.Id).ToArrayAsync()).Should().BeEquivalentTo(preserved);
    }

    [Theory]
    [InlineData(LinkMethod.Manual)]
    [InlineData(LinkMethod.StatedBySource)]
    [InlineData(LinkMethod.RuleBased)]
    public async Task ProtectedLinkClaimsStopTheEntireWithdrawal(LinkMethod method)
    {
        AddWarmLinks();
        AddCarriedAnnotations();
        var link = db.Links.First(l => l.Method == LinkMethod.Aligner);
        link.Claims.Add(new LinkClaim { Method = method, Confidence = method == LinkMethod.RuleBased ? 0.98 : null,
            Provenance = new() { Source = "protected testimony" } });
        await db.SaveChangesAsync();
        var links = await db.Links.CountAsync();
        var annotations = await db.WordEntities.CountAsync();
        var withdraw = () => RejectedRenderings.Withdraw(db);
        await withdraw.Should().ThrowAsync<InvalidDataException>().WithMessage("*Protected evidence*");
        (await db.Links.CountAsync()).Should().Be(links);
        (await db.WordEntities.CountAsync()).Should().Be(annotations);
    }

    [Theory]
    [InlineData(LinkMethod.Manual)]
    [InlineData(LinkMethod.StatedBySource)]
    [InlineData(LinkMethod.RuleBased)]
    public async Task IndependentAnnotationEvidenceStopsTheEntireWithdrawal(LinkMethod method)
    {
        AddWarmLinks();
        AddCarriedAnnotations();
        var annotation = db.WordEntities.First(a => a.Note != "independent seed");
        annotation.Claims.Add(new WordEntityClaim { Method = method,
            Confidence = method == LinkMethod.RuleBased ? 0.98 : null, Source = "protected testimony", Note = "direct reading" });
        await db.SaveChangesAsync();
        var links = await db.Links.CountAsync();
        var annotations = await db.WordEntities.CountAsync();
        var withdraw = () => RejectedRenderings.Withdraw(db);
        await withdraw.Should().ThrowAsync<InvalidDataException>().WithMessage("*Protected evidence*");
        (await db.Links.CountAsync()).Should().Be(links);
        (await db.WordEntities.CountAsync()).Should().Be(annotations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldEditionsWithAMissingOrDifferentRuledWordFailClosed(bool different)
    {
        var word = words[Satan];
        if (different) word.Surface = "another reading";
        else db.Words.Remove(word);
        await db.SaveChangesAsync();
        await db.Database.OpenConnectionAsync();
        var locate = () => RejectedRenderings.Locate((NpgsqlConnection)db.Database.GetDbConnection(), CancellationToken.None);
        await locate.Should().ThrowAsync<InvalidDataException>().WithMessage("*no longer names its ruled words*");
    }

    private static NewLink Draft(Word from, Word to) => new(from.TextId, to.TextId, LinkRelation.Renders,
        LinkMethod.Aligner, 0.98, Source, null, [from.Id], [to.Id]);

    private void AddWarmLinks()
    {
        foreach (var ruling in RejectedRenderings.All)
        {
            AddLink(words[ruling.From], words[ruling.To], LinkMethod.Aligner, Source);
            var position = ruling.From.Reference.StartsWith("ROM", StringComparison.Ordinal) ? 5 : 2;
            AddLink(words[new("RUSV", ruling.From.Reference, position, position == 5 ? "сатану" : "сатане")],
                words[ruling.To], LinkMethod.StrongNumber, "the edition's stated numbering");
        }
        db.SaveChanges();
    }

    private void AddLink(Word from, Word to, LinkMethod method, string source)
    {
        var link = new Link { FromTextId = from.TextId, ToTextId = to.TextId, Relation = LinkRelation.Renders,
            Method = method, Confidence = 0.98, Provenance = new() { Source = source },
            Fingerprint = LinkShape.Of([from.Id], [to.Id]) };
        link.Words.Add(new() { Word = from, Side = LinkSide.From });
        link.Words.Add(new() { Word = to, Side = LinkSide.To });
        link.Claims.Add(new() { Method = method, Confidence = 0.98, Provenance = link.Provenance });
        db.Links.Add(link);
    }

    private void AddCarriedAnnotations()
    {
        var satan = new Entity { Slug = "satan", Name = "Satan", SourceId = "satan", Source = "a test", Kind = EntityKind.Person };
        db.Entities.Add(satan);
        foreach (var ruling in RejectedRenderings.All.DistinctBy(r => r.From))
        {
            var note = $"through {ruling.To.Text} word {words[ruling.To].Id}, linked by aligner";
            db.WordEntities.Add(new() { Word = words[ruling.From], Entity = satan, Method = LinkMethod.StrongNumber,
                Confidence = 0.9702, Source = EntityAnnotationLoader.GreekResolution, Note = note,
                Claims = [new() { Method = LinkMethod.StrongNumber, Confidence = 0.833,
                    Source = EntityAnnotationLoader.GreekResolution, Note = note },
                    new() { Method = LinkMethod.StrongNumber, Confidence = 0.9702,
                    Source = EntityAnnotationLoader.VerseList, Note = note }] });
        }
        foreach (var word in words.Values.Where(w => w.Surface is "Σατανᾶν" or "Σατανᾷ" or "σατανα" or "сатану" or "сатане"))
            db.WordEntities.Add(new() { Word = word, Entity = satan, Method = LinkMethod.StrongNumber,
                Confidence = 0.99, Source = EntityAnnotationLoader.GreekResolution, Note = "independent seed" });
        foreach (var slug in new[] { "jesus", "anointed" })
        {
            var entity = new Entity { Slug = slug, Name = slug, SourceId = slug, Source = "a test",
                Kind = slug == "jesus" ? EntityKind.Person : EntityKind.Title };
            db.Entities.Add(entity);
            db.WordEntities.Add(new() { Word = words[new("RUSV", "ROM 16:20", 14, "Христа")], Entity = entity,
                Method = LinkMethod.RuleBased, Confidence = 0.9702, Source = "the independently resolved title/bearer",
                Note = "independent seed", Claims = [new() { Method = LinkMethod.RuleBased, Confidence = 0.9702,
                    Source = "the independently resolved title/bearer", Note = "independent seed" }] });
        }
        db.SaveChanges();
    }
}
