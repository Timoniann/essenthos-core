using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// What a text's page says it holds and what it is joined to. The counts are the page's claims
/// about the corpus, so each is checked against rows written for the purpose.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextFactsTests : IAsyncLifetime
{
    private const string Witness = "FACTS-WITNESS";
    private const string Translation = "FACTS-TRANSLATION";

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;

    public TextFactsTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// The counting runs on connections of its own, so the rows are committed rather than rolled
    /// back, and removed here instead. Deleting the texts takes everything hanging off them.
    /// </summary>
    public async Task DisposeAsync()
    {
        await _db.ReleaseFacts.ExecuteDeleteAsync();
        await _db.Links.Where(l => l.FromText!.Slug.StartsWith("FACTS-")).ExecuteDeleteAsync();
        await _db.Texts.Where(t => t.Slug.StartsWith("FACTS-")).ExecuteDeleteAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task ATextSaysWhatItsWordsCarryAndWhatTheyAreLinkedTo()
    {
        var witness = Corpus.Add(_db, Witness, TextKind.CriticalEdition, "hbo",
            (1, 1, ["בְּרֵאשִׁית", "בָּרָא", "אֱלֹהִים"]),
            (1, 2, ["וְהָאָרֶץ"]));
        var translation = Corpus.Add(_db, Translation, TextKind.Translation, "eng",
            (1, 1, ["In", "the", "beginning"]));
        await _db.SaveChangesAsync();

        var created = _db.WordAt(witness, 1, 1, 2);
        created.Lemma = "ברא";
        created.StrongNumber = "H1254";
        created.Morphology = JsonDocument.Parse("""{"pos":"verb","phono":"bārˈā"}""");
        _db.WordAt(witness, 1, 1, 3).Lemma = "אלהים";
        _db.WordAt(witness, 1, 2, 1).Elided = true;

        var berean = Datasets.All.Single(dataset => dataset.Id == "berean");
        Link(translation, witness, 1, LinkMethod.StatedBySource, $"{berean.Prefix}, a test");
        Link(translation, witness, 2, LinkMethod.Aligner, "an aligner");
        Link(translation, witness, 3, LinkMethod.Aligner, "an aligner");
        await _db.SaveChangesAsync();

        var facts = new TextFacts(Scopes());
        var tally = await facts.Of(witness.Id, CancellationToken.None);

        tally.Counts.Should().Be(new TextCountsResponse(1, 2, 3));
        tally.Features.Lemmas.Should().Be(2);
        tally.Features.StrongNumbers.Should().Be(1);
        tally.Features.Morphology.Should().Be(1);
        tally.Features.Transliteration.Should().Be(1);
        tally.Features.Glosses.Should().Be(0);

        var linked = tally.Links.Should().ContainSingle().Which;
        linked.Text.Should().Be(Translation);
        linked.Links.Should().Be(3);
        linked.Methods.Should().Equal(
            new TextLinkMethodResponse("aligner", 2),
            new TextLinkMethodResponse("stated-by-source", 1));
        linked.StatedBy.Should().Equal(new TextCreditResponse(berean.Name, berean.Author));

        // The same links, read from the other end.
        (await facts.Of(translation.Id, CancellationToken.None)).Links
            .Should().ContainSingle().Which.Text.Should().Be(Witness);
    }

    /// <summary>
    /// The facts the build stores are read back as they were counted, and only an API whose counting code stamped them reads
    /// them: under another stamp the process counts for itself.
    /// </summary>
    [Fact]
    public async Task TheReleaseFactsTheBuildStoresAreReadBackAsCountedAndOnlyUnderTheirOwnStamp()
    {
        var witness = Corpus.Add(_db, Witness, TextKind.CriticalEdition, "hbo", (1, 1, ["בְּרֵאשִׁית", "בָּרָא"]));
        var translation = Corpus.Add(_db, Translation, TextKind.Translation, "eng", (1, 1, ["In", "the"]));
        await _db.SaveChangesAsync();
        Link(translation, witness, 1, LinkMethod.Aligner, "an aligner");
        await _db.SaveChangesAsync();

        var scopes = Scopes();
        var counted = await ReleaseFacts.Count(scopes, CancellationToken.None);
        counted.Select(f => f.Key).Should().Equal(ReleaseFacts.Keys);
        var live = Json(await new TextFacts(scopes).Of(witness.Id, CancellationToken.None));
        var liveDatasets = ReleaseFacts.WriteDatasets(await new DatasetCountsCache(scopes).Get(CancellationToken.None));
        var liveWeights = ReleaseFacts.WriteWeights(await new ContextWeightsCache(scopes, NullLogger<ContextWeightsCache>.Instance).Get(CancellationToken.None));

        var texts = ReleaseFacts.ReadTexts(counted.Single(f => f.Key == ReleaseFacts.TextsKey).Body);
        var doctored = new Dictionary<string, TextTally>(texts)
        {
            [Witness] = texts[Witness] with { Counts = new TextCountsResponse(7, 70, 700) },
        };
        _db.ReleaseFacts.AddRange(counted.Select(f => new ReleaseFact
        {
            Key = f.Key,
            Stamp = ReleaseFacts.Stamp,
            Body = f.Key == ReleaseFacts.TextsKey ? ReleaseFacts.WriteTexts(doctored) : f.Body,
        }));
        await _db.SaveChangesAsync();

        (await new TextFacts(scopes).Of(witness.Id, CancellationToken.None)).Counts.Should().Be(new TextCountsResponse(7, 70, 700));
        Json(await new TextFacts(scopes).Of(translation.Id, CancellationToken.None))
            .Should().Be(Json(texts[Translation]));
        ReleaseFacts.WriteDatasets(await new DatasetCountsCache(scopes).Get(CancellationToken.None)).Should().Be(liveDatasets);
        ReleaseFacts.WriteWeights(await new ContextWeightsCache(scopes, NullLogger<ContextWeightsCache>.Instance).Get(CancellationToken.None))
            .Should().Be(liveWeights);

        await _db.ReleaseFacts.ExecuteUpdateAsync(f => f.SetProperty(x => x.Stamp, "0:another"));
        Json(await new TextFacts(scopes).Of(witness.Id, CancellationToken.None)).Should().Be(live);
    }

    private static string Json(TextTally tally) => ReleaseFacts.WriteTexts(new Dictionary<string, TextTally> { ["t"] = tally });

    /// <summary>
    /// Every text the corpus loads is introduced in every language the interface speaks. Checked
    /// against the loader's own list, so a text added without a summary fails here rather than
    /// reaching a page that has nothing to say about it in Ukrainian.
    /// </summary>
    [Fact]
    public void EveryTextIsSummarisedInEveryInterfaceLanguage()
    {
        foreach (var definition in TextCorpus.Definitions)
        {
            var summary = TextSummaries.For(definition.Slug);
            summary.Should().NotBeNull($"{definition.Slug} has no summary");
            summary!.Keys.Should().BeEquivalentTo(TextSummaries.Languages, definition.Slug);
            summary.Values.Should().OnlyContain(line => line.Length > 0, definition.Slug);
        }
    }

    private void Link(Text from, Text to, int position, LinkMethod method, string source)
    {
        var link = new Link
        {
            FromTextId = from.Id,
            ToTextId = to.Id,
            Relation = LinkRelation.Renders,
            Method = method,
            Confidence = method == LinkMethod.Aligner ? 0.5 : null,
            Provenance = new() { Source = source },
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(from, 1, 1, position), Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, Word = _db.WordAt(to, 1, 1, position), Side = LinkSide.To });
    }

    private IServiceScopeFactory Scopes() =>
        new ServiceCollection()
            .AddScoped(_ => _database.NewContext())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
}
