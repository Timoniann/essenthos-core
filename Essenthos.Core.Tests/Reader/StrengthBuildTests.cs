using Essenthos.Core.Build;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Products;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The verse strength counted at build: every pair and chapter as the per-request reading answered it (links counted once in
/// either direction, stated links counted and not averaged, a chapter joined only by a reading), copied rather than counted
/// again where the product is current, and held against the links by verify.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrengthBuildTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public StrengthBuildTests(WitnessDatabase database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly string[] Texts = ["GRCBRENT", "BHSA", "GEEZ81"];

    [Fact]
    public async Task EveryPairAndChapterIsWhatTheLinksGiveAndACurrentProductIsCopied()
    {
        await using (var db = _database.NewContext())
        {
            var greek = Corpus.Add(db, "GRCBRENT", TextKind.Translation, "eng",
                (1, 1, ["a", "spreading", "trunk"]), (1, 2, ["and"]), (3, 1, ["λόγος"]), (4, 1, ["λόγος"]));
            var hebrew = Corpus.Add(db, "BHSA", TextKind.ManuscriptTradition, "hbo", (1, 1, ["a", "doe", "loosed"]), (1, 2, ["and"]));
            var geez = Corpus.Add(db, "GEEZ81", TextKind.Translation, "gez", (3, 1, ["ቃል"]), (4, 1, ["ቃል"]));
            db.SaveChanges();

            Link(db, greek, 1, 1, hebrew, 1, 1, 0.2);
            Link(db, hebrew, 1, 2, greek, 1, 2, 0.6);
            Link(db, greek, 1, 3, hebrew, 1, 3, null);
            var both = Link(db, greek, 2, 1, hebrew, 2, 1, 0.4);
            db.LinkWords.Add(new LinkWord { LinkId = both.Id, WordId = db.WordAt(greek, 1, 1, 1).Id, Side = LinkSide.From });
            db.LinkWords.Add(new LinkWord { LinkId = both.Id, WordId = db.WordAt(greek, 1, 1, 2).Id, Side = LinkSide.From });
            Join(db, geez, greek, 3, LinkMethod.ModelReading);
            Join(db, geez, greek, 4, LinkMethod.ModelReading);
            Join(db, geez, greek, 4, LinkMethod.StatedBySource);
            db.SaveChanges();
        }

        var store = new ProductStore(_root);
        var first = await Run(store);
        first.Should().OnlyContain(o => o.Made);
        first.Single(o => o.Text == "GRCBRENT").Rows.Should().Be(2, "chapter 1 against BHSA and chapter 3 against GEEZ81 by reading alone");

        await using (var db = _database.NewContext())
        {
            var ids = await db.Texts.Where(t => Texts.Contains(t.Slug)).ToDictionaryAsync(t => t.Slug, t => t.Id);
            foreach (var text in Texts)
            {
                foreach (var reference in Texts.Where(r => r != text))
                {
                    foreach (var chapter in new[] { 1, 2, 3, 4 })
                    {
                        var live = await ParallelEndpoints.Strengths(db, ids[text], ids[reference], 1, chapter, default);
                        var reading = await ParallelEndpoints.JoinedOnlyByReading(db, ids[text], ids[reference], 1, chapter, default);
                        var stored = await db.VerseStrengths.SingleOrDefaultAsync(s =>
                            s.TextId == ids[text] && s.ReferenceId == ids[reference] && s.CanonicalBook == 1 && s.CanonicalChapter == chapter);
                        var verses = stored is null ? [] : VerseStrengths.Unpack(stored.Verses);
                        verses.Keys.Should().BeEquivalentTo(live.Keys, $"{text} against {reference} in chapter {chapter}");
                        foreach (var (verse, strength) in live)
                        {
                            verses[verse].Links.Should().Be(strength.Links);
                            verses[verse].Stated.Should().Be(strength.Stated);
                            if (strength.Confidence is { } confidence)
                            {
                                verses[verse].Confidence.Should().BeApproximately(confidence, 1e-12);
                            }
                            else
                            {
                                verses[verse].Confidence.Should().BeNull();
                            }
                        }

                        (stored?.ByReading ?? false).Should().Be(reading, $"{text} against {reference} in chapter {chapter} by reading");
                    }
                }
            }

            var greekOne = VerseStrengths.Unpack((await db.VerseStrengths.SingleAsync(s => s.TextId == ids["GRCBRENT"] && s.CanonicalChapter == 1)).Verses);
            greekOne[1].Should().Be(new VerseStrengthCount(4, 1, (0.2 + 0.6 + 0.4) / 3), "the link naming two words of verse 1 and one of verse 2 is counted once in each");
        }

        (await Broken()).Should().Be(0);

        (await Run(store)).Should().OnlyContain(o => !o.Made, "nothing the strength reads has moved");

        await using (var connection = _database.NewConnection())
        {
            await connection.OpenAsync();
            await using var delete = new NpgsqlCommand("DELETE FROM verse_strength WHERE text_id = (SELECT id FROM text WHERE slug = 'BHSA')", connection);
            await delete.ExecuteNonQueryAsync();
        }

        var copied = await Run(store);
        copied.Where(o => o.Made).Select(o => o.Text).Should().Equal(["BHSA"], "the table no longer holds what BHSA's product lists");
        (await Broken()).Should().Be(0);

        await using (var connection = _database.NewConnection())
        {
            await connection.OpenAsync();
            await using var change = new NpgsqlCommand("UPDATE link SET confidence = 0.9 WHERE confidence = 0.2", connection);
            await change.ExecuteNonQueryAsync();
        }

        (await Broken()).Should().BeGreaterThan(0, "a confidence the links no longer give is found");
    }

    private async Task<long> Broken()
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(CorpusCheck.StrengthSql(1), connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<IReadOnlyList<StrengthOutcome>> Run(ProductStore store)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.NewContext());
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var connection = _database.NewConnection();
        return await new StrengthBuild(connection.ConnectionString, store, provider, NullLogger.Instance).Run(Texts, false, 2, default);
    }

    private static Link Link(AppDbContext db, Text from, int fromVerse, int fromPosition, Text to, int toVerse, int toPosition, double? confidence)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = to.Id,
            Relation = LinkRelation.Renders,
            Method = confidence is null ? LinkMethod.StatedBySource : LinkMethod.Aligner,
            Provenance = new() { Source = "a test" },
            Confidence = confidence,
        };
        db.Links.Add(link);
        db.SaveChanges();
        db.LinkClaims.Add(new LinkClaim { LinkId = link.Id, Method = link.Method, Confidence = link.Confidence, Provenance = link.Provenance });
        db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = db.WordAt(from, 1, fromVerse, fromPosition).Id, Side = LinkSide.From });
        db.LinkWords.Add(new LinkWord { LinkId = link.Id, WordId = db.WordAt(to, 1, toVerse, toPosition).Id, Side = LinkSide.To });
        db.SaveChanges();
        return link;
    }

    private static void Join(AppDbContext db, Text from, Text to, int chapter, LinkMethod method)
    {
        var link = new VerseLink
        {
            FromTextId = from.Id, ToTextId = to.Id, Relation = LinkRelation.Renders, Method = method,
            Confidence = method == LinkMethod.StatedBySource ? null : 0.9, Source = "a test",
        };
        db.VerseLinks.Add(link);
        db.VerseLinkVerses.Add(new VerseLinkVerse { VerseLink = link, VerseId = db.VerseAt(from, chapter, 1).Id, Side = LinkSide.From });
        db.VerseLinkVerses.Add(new VerseLinkVerse { VerseLink = link, VerseId = db.VerseAt(to, chapter, 1).Id, Side = LinkSide.To });
    }
}
