using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// What a composition writes into the pair's <c>composed</c> method file, read from the pair's final and nothing else: an
/// agreement with a statement is a row of the stated link's shape, which the merge folds into that link as a claim; a word
/// another method answers for gets no guess beside it; a pair the held editions' wording rules out is never written.
/// </summary>
public sealed class CompositionRowsTests : IDisposable
{
    private const string Door43 = "a Door43 interlinear";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-compose-test", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;

    public CompositionRowsTests()
    {
        _store = new ProductStore(_root);
        ProductCorpus.Text(_store, "UBIO", "ukr", [("1.1:1", ["Павло", "апостол", "Христа"])]);
        ProductCorpus.Text(_store, "NESTLE1904", "grc", [("1.1:1", ["Παῦλος", "ἀπόστολος", "Χριστοῦ"])]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void AnAgreementWithAStatementIsOneRowOfTheStatedLinksShape()
    {
        Final("UBIO-NESTLE1904", Settled(1, "1.1:1.1", "1.1:1.1", "stated", null, Door43));

        var (rows, fresh, corroborated) = Composer().Rows("UBIO", "NESTLE1904", ["KJV"], [new RoutedLink(0, 0, 0.95, Route.Written | Route.Composed)], null);

        (fresh, corroborated).Should().Be((0, 1));
        rows.Should().ContainSingle().Which.Should().Match<MappingRow>(row => row.From == "1.1:1.1" && row.To == "1.1:1.1"
            && row.Method == MappingMethods.Composed && row.Confidence == 0.95 && row.Source == Routes.Describe(Route.Written | Route.Composed, "KJV"));
    }

    [Fact]
    public void AWordAnotherMethodAnswersForGetsNoGuessAndAnUnansweredOneDoes()
    {
        Final("UBIO-NESTLE1904", Settled(1, "1.1:1.1", "1.1:1.1", "stated", null, Door43));

        var (rows, fresh, corroborated) = Composer().Rows("UBIO", "NESTLE1904", ["KJV"],
            [new RoutedLink(0, 1, 0.7, Route.Written), new RoutedLink(1, 1, 0.6, Route.Reduced)], null);

        (fresh, corroborated).Should().Be((1, 0));
        rows.Should().ContainSingle().Which.Should().Match<MappingRow>(row => row.From == "1.1:1.2" && row.To == "1.1:1.2"
            && row.Evidence == null);
    }

    [Fact]
    public void ARuleBasedLinkKeepsItsWordAnsweredAndIsNoAnswerKeyToMeasureAgainst()
    {
        Final("UBIO-NESTLE1904",
            Settled(1, "1.1:1.1", "1.1:1.1", "evidentia", 0.8, "EVIDENTIA run 1 (test), global-review-known-rendering"),
            Settled(2, "1.1:1.2", "1.1:1.2", "stated", null, Door43));
        var composer = Composer();

        var key = composer.Key("UBIO", "NESTLE1904");
        var (_, fresh, corroborated) = composer.Rows("UBIO", "NESTLE1904", ["KJV"], [new RoutedLink(0, 2, 0.7, Route.Written)], null);

        key.Keys.Should().BeEquivalentTo([1L], "a rule's answer measured against would score one inference by another");
        (fresh, corroborated).Should().Be((0, 0), "the rule already answers the word, and a guess is not written beside it");
    }

    [Fact]
    public void ARejectedStatisticalPairIsNotWrittenAndTheActualSatanIs()
    {
        var russian = Enumerable.Range(1, 15).Select(n => $"w{n}").ToArray();
        russian[1] = "сатане";
        russian[14] = "Христа";
        ProductCorpus.Text(_store, "RUSV", "rus", [("46.5:5", russian)]);
        ProductCorpus.Text(_store, "NESTLE1904", "grc", [("46.5:5", ["παραδοῦναι", "τὸν", "τοιοῦτον", "τῷ", "Σατανᾷ"])]);
        var rulings = Path.Combine(_store.ResourcesPath, "Essenthos", "mapping", "RUSV-NESTLE1904", "rulings.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(rulings)!);
        File.WriteAllLines(rulings,
        [
            "# producer   by hand",
            "action	from	to	relation	source	reason",
            "refuse	46.5:5.15	46.5:5.5	renders	SIL.Machine	the Synodal adds Christ after Jesus; Satan is named at position 2",
        ]);

        var (rows, fresh, _) = Composer().Rows("RUSV", "NESTLE1904", ["KJV"],
            [new RoutedLink(14, 4, 0.98, Route.Written | Route.Composed), new RoutedLink(1, 4, 0.99, Route.Written)], null);

        fresh.Should().Be(1);
        rows.Should().ContainSingle().Which.From.Should().Be("46.5:5.2");
    }

    [Fact]
    public void AStatementTheLastCompositionAlsoClaimsIsStillAStatement()
    {
        Final("UBIO-NESTLE1904",
            Settled(1, "1.1:1.1", "1.1:1.1", "composed", 0.95, "SIL.Machine, aligned as written and through KJV"),
            Claim(1, "1.1:1.1", "1.1:1.1", "strong", 0.5, "a Strong numbering"),
            Settled(2, "1.1:1.2", "1.1:1.2", "composed", 0.9, "SIL.Machine, aligned as written"));
        var composer = Composer();

        var key = composer.Key("UBIO", "NESTLE1904");
        var (rows, fresh, corroborated) = composer.Rows("UBIO", "NESTLE1904", ["KJV"],
            [new RoutedLink(0, 0, 0.95, Route.Written | Route.Composed), new RoutedLink(1, 1, 0.9, Route.Written)], null);

        key.Keys.Should().BeEquivalentTo([0L], "the composed claim the merge settled on the Strong pairing does not hide it");
        (fresh, corroborated).Should().Be((1, 1), "the word only a composition linked is guessed again, and the Strong pairing is agreed with");
        rows.Should().HaveCount(2);
    }

    [Fact]
    public void ARowTheMergeWithdrewForAComposedClaimIsReadAsALink()
    {
        Final("UBIO-NESTLE1904", Settled(1, "1.1:1.2", "1.1:1.1", "composed", 0.9, "SIL.Machine, aligned as written"));
        using (var writer = _store.Create(MappingProducts.Of("UBIO-NESTLE1904", MappingProducts.Statuses), ProductKind.Machine, MappingRow.Schema, "test", []))
        {
            writer.WriteAll([new MappingRow("1.1:1.1", "1.1:1.1", "renders", "strong", 0.5, "a Strong numbering", null, null,
                MappingStatus.WithdrawnBy(MappingMethods.Composed))]);
        }

        var (_, fresh, corroborated) = Composer().Rows("UBIO", "NESTLE1904", ["KJV"], [new RoutedLink(0, 0, 0.8, Route.Written)], null);

        (fresh, corroborated).Should().Be((0, 1), "without the composition the Strong pairing would stand, so the guess agrees with it");
    }

    private CompositionPipeline Composer() =>
        new(new ProductTexts(_store), ProductCorpus.Aligner(_store), NullLogger.Instance);

    private static FinalRow Settled(long id, string from, string to, string method, double? confidence, string source) =>
        new(id, from, to, "renders", null, true, method, confidence, source);

    private static FinalRow Claim(long id, string from, string to, string method, double? confidence, string source) =>
        new(id, from, to, "renders", null, false, method, confidence, source);

    private void Final(string pair, params FinalRow[] rows)
    {
        using var writer = _store.Create(MappingProducts.Of(pair, MappingProducts.Final), ProductKind.Machine, FinalRow.Schema, "test", []);
        writer.WriteAll(rows);
    }
}
