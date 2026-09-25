using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Verification;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which two editions outvoted which one, asked of the corpus.
///
/// Nestle collated no manuscripts. He took Tischendorf's eighth edition, Westcott and Hort, and
/// Weiss, and printed whatever two of the three agreed on — so Nestle 1904 is not a witness that
/// these two stand against, it is a function of them, and the corpus held the output and neither
/// input until now. Loading them makes the decomposition a query: for every word of Nestle, does
/// Tischendorf write it, does Westcott and Hort, do both, or does neither.
///
/// The last of those four is the one worth publishing. Neither voter agreeing means Weiss decided,
/// and no free machine-readable Weiss was found — so what the corpus can show is two thirds of a
/// vote, and it should say so rather than presenting an apparatus that looks complete.
///
/// It is asserted as a shape rather than as four numbers. The counts move whenever the pairing
/// improves and that is not a regression; what must not move is that both editions agree with
/// Nestle far more often than either does alone, that each of them wins somewhere, and that the
/// unexplained remainder is small and not zero.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class NestleVoteTests(WitnessDatabase database, ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly TimeSpan LongEnoughForAWholeCorpus = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The scratch database is shared by every class in this collection and the small ones assume
    /// an empty text table, so this empties it either side — the same reason and the same TRUNCATE
    /// as <see cref="GreekWitnessReachTests"/>.
    /// </summary>
    public Task InitializeAsync() => Clear();

    public Task DisposeAsync() => Clear();

    private Task Clear() => database.Empty(LongEnoughForAWholeCorpus);

    [Fact]
    public async Task NestleAgreesWithBothVotersFarMoreOftenThanWithEitherAlone()
    {
        await using var db = database.NewContext();
        db.Database.SetCommandTimeout(LongEnoughForAWholeCorpus);

        await Load(db);
        var links = await LinkTheVoters(db);

        var measures = await new CorpusCheck(db, NullLogger<CorpusCheck>.Instance).Measure();
        var vote = measures.Vote.Should().ContainSingle().Which;

        foreach (var reach in measures.Reach.OrderBy(r => r.Witness).ThenBy(r => r.From))
        {
            output.WriteLine($"  {reach.From} reaches {reach.Reached} of {reach.Lexical} words of "
                             + $"{reach.Witness} ({reach.Share:P2})");
        }

        output.WriteLine($"of {vote.Words} Nestle 1904 words:");
        output.WriteLine($"  both editions write the same word     {vote.Both}");
        output.WriteLine($"  Tischendorf only                      {vote.FirstOnly}");
        output.WriteLine($"  Westcott and Hort only                {vote.SecondOnly}");
        output.WriteLine($"  neither, so Weiss decided             {vote.Neither}");

        vote.Text.Should().Be(NestleTextSource.Slug);
        vote.Words.Should().BeGreaterThan(130_000);

        // The three counts sum to the whole, which is what makes this a decomposition rather than
        // four unrelated measurements.
        (vote.Both + vote.FirstOnly + vote.SecondOnly + vote.Neither).Should().Be(vote.Words);

        // Nestle's text is a majority of these two and one more, so the overwhelming case is that
        // both of them agree with him. If that stopped being true, the editions would have been
        // laid against the wrong text.
        vote.Both.Should().BeGreaterThan(vote.Words * 8 / 10);

        // And each voter wins somewhere on its own. This is the thing no other pair of texts in the
        // corpus can show, and a zero on either side would mean one of the two had stopped being
        // read as a distinct edition.
        vote.FirstOnly.Should().BeGreaterThan(0);
        vote.SecondOnly.Should().BeGreaterThan(0);

        // The remainder Weiss decided: real, and small. Large would mean the pairing is failing
        // rather than that Nestle disagreed with both his sources thousands of times.
        vote.Neither.Should().BeGreaterThan(0).And.BeLessThan(vote.Words / 5);

        // The pairing rested on an equivalence for exactly the words Tischendorf numbers its own
        // way. Without it those are not variants Nestle chose, they are 2,729 the corpus invented.
        links[(TischendorfTextSource.Slug, NestleTextSource.Slug)].Lemmatised.Should().BeGreaterThan(2_500);
        links[(WestcottHortTextSource.Slug, NestleTextSource.Slug)].Lemmatised.Should().Be(0);

        // And the corpus these two texts were added to is still sound. The integrity checks are the
        // only measure with a right answer, and each of them is a shape no correct load produces —
        // a lemma that joins to nothing, a link naming a word of the wrong text, an absence whose
        // relation contradicts the side its words are on.
        foreach (var check in measures.Integrity)
        {
            output.WriteLine($"  {check.Found,7} {check.Breaks}");
        }

        measures.Integrity.Should().OnlyContain(check => check.Found == 0);
    }

    private static async Task Load(AppDbContext db)
    {
        var corpus = new CorpusLoader(db, NullLogger<CorpusLoader>.Instance);
        await corpus.Load(NestleTextSource.Read(TestResources.Nestle1904));
        await corpus.Load(TischendorfTextSource.Read(TestResources.TischendorfFolder));
        await corpus.Load(WestcottHortTextSource.Read(TestResources.WestcottHortFolder));
        await corpus.Load(TextusReceptusTextSource.Read(
            TestResources.TextusReceptusFolder, Edition.Scrivener1894));

        // The numbers resolve through the lexicon, and one integrity check asks whether every
        // number in the corpus does. Without it that check passes by having nothing to check.
        await new StrongLexiconLoader(db, NullLogger<StrongLexiconLoader>.Instance).Load(
            TestResources.Path("Strong", "StrongHebrew.xml"),
            TestResources.Path("Strong", "StrongGreek.xml"));

        var rules = TvtmsReader.Read(TestResources.Tvtms);
        var frame = new CanonicalFrameLoader(db, NullLogger<CanonicalFrameLoader>.Instance);
        foreach (var text in await db.Texts.ToListAsync())
        {
            await frame.Place(text, rules);
        }
    }

    /// <summary>
    /// Each voter against Nestle, which is the pair the decomposition is read off, and against
    /// Scrivener, which is how they join the Greek panes already here — a word carries the witness
    /// ids it reaches, and Scrivener is what every other Greek edition in the corpus is linked to.
    /// </summary>
    private async Task<Dictionary<(string, string), GreekWitnessOutcome>> LinkTheVoters(AppDbContext db)
    {
        var outcomes = new Dictionary<(string, string), GreekWitnessOutcome>(4);
        var scrivener = TextusReceptusTextSource.Slug(Edition.Scrivener1894);

        foreach (var against in new[] { NestleTextSource.Slug, scrivener })
        {
            foreach (var voter in new[] { TischendorfTextSource.Slug, WestcottHortTextSource.Slug })
            {
                var loader = new GreekWitnessLinkLoader(db, NullLogger<GreekWitnessLinkLoader>.Instance);
                outcomes[(voter, against)] = await loader.Load(voter, against);
                output.WriteLine($"{voter} against {against}: {outcomes[(voter, against)]}");
            }
        }

        return outcomes;
    }
}
