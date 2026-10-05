using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Verbs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which Greek edition the King James actually reaches most of, asked of the corpus rather than of
/// the literature.
///
/// The Scrivener, Byzantine and Nestle ordering and gap proportion are stable contracts. Tischendorf
/// and Westcott-Hort are measured separately, without assuming which critical edition reaches most.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GreekWitnessReachTests(WitnessDatabase database, ITestOutputHelper output)
    : IAsyncLifetime
{
    /// <summary>
    /// Time enough for a million-word load on a machine also running the rest of the suite. The
    /// default thirty seconds is too short for six texts and five pairs.
    /// </summary>
    private static readonly TimeSpan LongEnoughForAWholeCorpus = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The scratch database is shared by every class in this collection, and several of them build
    /// a <c>KJV</c> or a <c>BHSA</c> inside a transaction they roll back, which assumes the table
    /// is empty. So this empties it either side.
    ///
    /// <c>TRUNCATE</c> rather than <c>DELETE</c>, which is what the small classes use: deleting a
    /// text here cascades through a million and a half words and half a million links, takes longer
    /// than the command timeout, and leaves the corpus behind when it gives up — which fails eleven
    /// other classes on a unique slug and costs each of them thirty seconds finding that out.
    /// </summary>
    public Task InitializeAsync() => Clear();

    public Task DisposeAsync() => Clear();

    private Task Clear() => database.Empty(LongEnoughForAWholeCorpus);

    /// <summary>King James New Testament words reached by at least one of the named witnesses.</summary>
    private const string ReachedSql =
        """
        WITH placed AS (
            SELECT v.id AS verse_id, r.canonical_book AS book
            FROM verse v JOIN verse_reference r ON r.verse_id = v.id AND r.is_primary
        ),
        english AS (
            SELECT w.id, w.strong_number FROM word w
            JOIN text t ON t.id = w.text_id AND t.slug = @english
            JOIN placed p ON p.verse_id = w.verse_id AND p.book BETWEEN 40 AND 66
        ),
        reached AS (
            SELECT DISTINCT lw.word_id FROM link_word lw
            JOIN link l ON l.id = lw.link_id AND l.relation IN ('renders', 'equals')
            JOIN text other ON other.id = l.to_text_id AND other.slug = ANY(@witnesses)
            WHERE lw.side = 'from'
        )
        SELECT count(*), count(*) FILTER (WHERE r.word_id IS NOT NULL),
               count(*) FILTER (WHERE e.strong_number IS NOT NULL AND r.word_id IS NULL)
        FROM english e LEFT JOIN reached r ON r.word_id = e.id
        """;

    private static string Scrivener => TextusReceptusTextSource.Slug(Edition.Scrivener1894);

    [Fact]
    public async Task TheKingJamesReachesTheReceivedTextFirstTheMajorityTextNextAndTheCriticalTextLast()
    {
        await using var db = database.NewContext();
        db.Database.SetCommandTimeout(LongEnoughForAWholeCorpus);

        await LoadTheTexts(db);
        var loads = await LinkTheEnglishToEachGreek(db);
        var apart = await MeasureHowFarTheEditionsStandApart(db);

        var scrivener = await Reached(Scrivener);
        var byzantine = await Reached(ByzantineTextSource.Slug);
        var nestle = await Reached(NestleTextSource.Slug);
        var tischendorf = await Reached(TischendorfTextSource.Slug);
        var westcottHort = await Reached(WestcottHortTextSource.Slug);

        output.WriteLine($"of {scrivener.Words} King James New Testament words:");
        output.WriteLine($"  scrivener 1894  reaches {scrivener.Reached}, leaving {scrivener.Unreached} tagged");
        output.WriteLine($"  byzantine 2018  reaches {byzantine.Reached}, leaving {byzantine.Unreached} tagged");
        output.WriteLine($"  nestle 1904     reaches {nestle.Reached}, leaving {nestle.Unreached} tagged");
        output.WriteLine($"  tischendorf 8   reaches {tischendorf.Reached}, leaving {tischendorf.Unreached} tagged");
        output.WriteLine($"  westcott-hort   reaches {westcottHort.Reached}, leaving {westcottHort.Unreached} tagged");

        var before = await Reached(Scrivener, NestleTextSource.Slug);
        var after = await Reached(Scrivener, NestleTextSource.Slug, ByzantineTextSource.Slug);
        output.WriteLine($"  together, without the byzantine: {before.Reached}, {before.Unreached} tagged");
        output.WriteLine($"  together, with it:               {after.Reached}, {after.Unreached} tagged");
        var five = await Reached([.. DatasetLoader.GreekWitnesses]);
        output.WriteLine($"  together, all five:               {five.Reached}, {five.Unreached} tagged");

        foreach (var reach in new[] { tischendorf, westcottHort })
        {
            reach.Words.Should().Be(scrivener.Words);
            reach.Reached.Should().BeGreaterThan(100_000);
        }
        five.Reached.Should().BeGreaterThanOrEqualTo(after.Reached);
        five.Unreached.Should().BeLessThanOrEqualTo(after.Unreached);

        var both = await Reached(NestleTextSource.Slug, TischendorfTextSource.Slug);
        var other = await Reached(NestleTextSource.Slug, WestcottHortTextSource.Slug);
        var critical = await Reached(NestleTextSource.Slug, TischendorfTextSource.Slug, WestcottHortTextSource.Slug);
        var addedByTischendorf = nestle.Unreached - both.Unreached;
        var addedByWestcottHort = nestle.Unreached - other.Unreached;
        var addedByEither = nestle.Unreached - critical.Unreached;
        var addedByBoth = addedByTischendorf + addedByWestcottHort - addedByEither;
        addedByBoth.Should().BeGreaterThanOrEqualTo(0);
        addedByBoth.Should().BeLessThanOrEqualTo(Math.Min(addedByTischendorf, addedByWestcottHort));
        output.WriteLine($"  {nestle.Unreached} tagged words Nestle misses: both other critical editions reach {addedByBoth}, " +
                         $"Tischendorf only {addedByTischendorf - addedByBoth}, " +
                         $"Westcott-Hort only {addedByWestcottHort - addedByBoth}, neither {critical.Unreached}");

        // The ordering the textual history predicts, measured rather than repeated. Scrivener is
        // the text reconstructed from the English itself, so it should win; the majority text is
        // the tradition that text belongs to; the critical text is furthest away.
        scrivener.Reached.Should().BeGreaterThan(byzantine.Reached);
        byzantine.Reached.Should().BeGreaterThan(nestle.Reached);

        // And by the right proportion. The gap between the Received Text and the tradition behind
        // it is small; the gap to the critical text is several times larger. An ordering alone
        // would still hold if the majority text were nearly as far off as Nestle, and it is not.
        (scrivener.Reached - byzantine.Reached).Should()
            .BeLessThan((byzantine.Reached - nestle.Reached) / 2);

        // A fourth witness cannot take anything away, and the whole question is how much it adds.
        after.Reached.Should().BeGreaterThanOrEqualTo(before.Reached);
        after.Unreached.Should().BeLessThanOrEqualTo(before.Unreached);

        // The same ordering seen from the Greek side, and the reason this edition is worth holding
        // beside two Received Text printings: it stands much closer to them than Nestle does, and
        // is still a different text.
        apart[ByzantineTextSource.Slug].Should().BeGreaterThan(0);
        apart[ByzantineTextSource.Slug].Should().BeLessThan(apart[NestleTextSource.Slug] / 2);

        // A verse the two printings divide differently is still the same verse. Refusing on the
        // word count alone threw away 436 of the 7,957 and 10,605 English words with them, and
        // nothing in the suite noticed, because every measure here counts what was linked.
        foreach (var load in loads)
        {
            load.Refused.Should().BeLessThan(20);
            load.Verses.Should().BeGreaterThan(7_900);

            // And the italics are the one thing in the New Testament that no inference produced:
            // the translators saying they supplied the word. Four thousand of them, and a load
            // that reports none has stopped reading them.
            load.Supplied.Should().BeGreaterThan(4_000);

            // A tag naming several numbers is one English word over a Greek phrase, and reading
            // only its first number left about 2,500 Greek words in each edition named by nobody.
            load.Phrases.Should().BeGreaterThan(2_000);
        }
    }

    private static async Task LoadTheTexts(AppDbContext db)
    {
        var corpus = new CorpusLoader(db, NullLogger<CorpusLoader>.Instance);
        await corpus.Load(Bible4uTextSource.Read(TestResources.Bible4u("KJV"), "KJV"));
        await corpus.Load(TextusReceptusTextSource.Read(
            TestResources.TextusReceptusFolder, Edition.Scrivener1894));
        await corpus.Load(ByzantineTextSource.Read(TestResources.ByzantineFolder));
        await corpus.Load(NestleTextSource.Read(TestResources.Nestle1904));
        await corpus.Load(TischendorfTextSource.Read(TestResources.TischendorfFolder));
        await corpus.Load(WestcottHortTextSource.Read(TestResources.WestcottHortFolder));

        // The matcher reaches past a bare number match through the lexicon's own derivations, so a
        // measurement taken without it would understate every edition by about ten thousand words.
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

    private async Task<List<GreekLinkOutcome>> LinkTheEnglishToEachGreek(AppDbContext db)
    {
        var loads = new List<GreekLinkOutcome>(DatasetLoader.GreekWitnesses.Count);
        var logger = new OutcomeLogger(output);
        using var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(new NewTestamentLinkLoader(db, NullLogger<NewTestamentLinkLoader>.Instance))
            .AddSingleton(new VerseLinkLoader(db, NullLogger<VerseLinkLoader>.Instance))
            .AddSingleton(new StrongRenderingLoader(db, NullLogger<StrongRenderingLoader>.Instance))
            .BuildServiceProvider();
        var run = new ForgeRun(services, logger, TestResources.Folder(""), db.Database.GetConnectionString()!);
        var verb = ForgeVerbs.Find("kjv-greek")!;

        foreach (var greek in DatasetLoader.GreekWitnesses)
        {
            string[] args = ["kjv-greek", greek];
            (await verb.Run(run, args)).Should().Be(0);
            var outcome = logger.Outcome!;
            outcome.AlreadyLoaded.Should().BeFalse();
            output.WriteLine($"{greek}: {outcome}");
            loads.Add(outcome);
            await run.Recount(verb.Changed(args));
            var fingerprint = await Fingerprint();
            (await verb.Run(run, args)).Should().Be(0);
            logger.Outcome!.AlreadyLoaded.Should().BeTrue();
            await run.Recount(verb.Changed(args));
            (await Fingerprint()).Should().Be(fingerprint);
            output.WriteLine($"{greek}: second verb run and recount preserved the table fingerprints");
        }

        return loads;
    }

    private async Task<string> Fingerprint()
    {
        await using var connection = database.NewConnection();
        await connection.OpenAsync();
        var fingerprints = new List<string>();
        foreach (var table in new[] { "link", "link_word", "link_claim", "verse_link", "verse_link_verse",
                     "word", "word_strong", "strong_rendering", "strong_reach", "strong_reach_method" })
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = (int)LongEnoughForAWholeCorpus.TotalSeconds;
            command.CommandText = $"SELECT count(*)::text || ':' || coalesce(sum(hashtext(x::text)::bigint), 0)::text FROM {table} x";
            fingerprints.Add($"{table}:{await command.ExecuteScalarAsync()}");
        }
        return string.Join(';', fingerprints);
    }

    private sealed class OutcomeLogger(ITestOutputHelper output) : ILogger
    {
        public GreekLinkOutcome? Outcome { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var value in values)
                {
                    if (value.Value is GreekLinkOutcome outcome) Outcome = outcome;
                }
            }
            output.WriteLine(formatter(state, exception));
        }
    }

    /// <summary>
    /// How many words each edition does not share with Scrivener — a different word in the same
    /// place, or a word one has and the other has not. It is the number this measurement exists to
    /// produce and the one nobody here had.
    /// </summary>
    private async Task<Dictionary<string, int>> MeasureHowFarTheEditionsStandApart(AppDbContext db)
    {
        var apart = new Dictionary<string, int>(2);

        foreach (var witness in new[] { NestleTextSource.Slug, ByzantineTextSource.Slug })
        {
            var loader = new GreekWitnessLinkLoader(db, NullLogger<GreekWitnessLinkLoader>.Instance);
            var outcome = await loader.Load(witness, Scrivener);
            apart[witness] = outcome.Differing + outcome.Missing + outcome.Added;
            output.WriteLine($"{witness} against {Scrivener}: {outcome}");
        }

        return apart;
    }

    private async Task<(long Words, long Reached, long Unreached)> Reached(params string[] witnesses)
    {
        await using var connection = database.NewConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = ReachedSql;
        command.CommandTimeout = (int)LongEnoughForAWholeCorpus.TotalSeconds;
        command.Parameters.AddWithValue("english", Bible4uTextSource.KingJames);
        command.Parameters.AddWithValue("witnesses", witnesses);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }
}
