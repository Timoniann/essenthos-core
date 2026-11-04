using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;
using Essenthos.Core.Products;
using Essenthos.Core.Records;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The owner's known-for answers applied to the files the build reads (DOC-0224 §8.4): an approval writes
/// the list's clauses with his stamp, a rejection or an answer taken back takes them out, a record already as
/// he decided keeps its rows, KnownFor.json is never overridden, and one apply line clears what was applied.
/// </summary>
public sealed class OwnerDecisionsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    private string Log => Path.Combine(_resources, "Essenthos", OwnerChanges.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }

    [Fact]
    public void AnApprovalWritesTheClausesWithTheOwnersStampAndARejectionTakesThemOut()
    {
        var store = Files();
        Line("place/sodom", "rejected", "2026-11-01T10:00:00.000Z");
        Line("place/sodom", "approved", "2026-11-02T10:00:00.000Z");
        Line("place/bethel", "rejected", "2026-11-02T11:00:00.000Z");
        Line("person/noah", "rejected", "2026-11-02T12:00:00.000Z");
        Line("place/gilgal", "approved", "2026-11-02T13:00:00.000Z");

        var outcome = OwnerDecisions.Apply(store, dryRun: false, Now);

        (outcome.Waiting, outcome.Records, outcome.Added, outcome.Removed, outcome.Already, outcome.Recorded)
            .Should().Be((5, 4, 1, 1, 1, true));
        outcome.Held.Should().Equal("noah");
        var by = OwnerDecisions.ApprovedBy("written by a model", "2026-11-02");
        Rows("records/facts/known-for").Should().BeEquivalentTo(new[]
        {
            new[] { "gilgal", "0", "exported" },
            ["noah", "0", "KnownFor.json"],
            ["sodom", "0", by],
            ["sodom", "1", by],
        }, options => options.WithStrictOrdering());
        Rows("records/facts/known-for-references").Should().BeEquivalentTo(new[]
        {
            new[] { "sodom", "0", "0", "1.19:24", "19", "25" },
            ["sodom", "1", "0", "1.13:13", "13", "13"],
            ["sodom", "1", "1", "1.18:20", "18", "21"],
        }, options => options.WithStrictOrdering());
        Rows("records/i18n/eng/known-for").Select(r => r[2]).Should().Equal("gilgal's clause", "noah's clause", "was overthrown", "was wicked");
        Rows("records/i18n/ukr/known-for").Select(r => (r[0], r[2])).Should().Equal(("sodom", "був зруйнований"));
        Rows("records/i18n/deu/known-for").Should().BeEmpty("no clause is rendered into German");
        var lines = File.ReadAllLines(Log);
        var applied = JsonNode.Parse(lines[^1])!;
        (applied["section"]!.GetValue<string>(), applied["action"]!.GetValue<string>(), applied["after"]!.GetValue<string>(), applied["needs"])
            .Should().Be(("apply", "files", "succeeded", (JsonNode?)null));
        OwnerChanges.WaitingOn(Log, OwnerChanges.Files).Should().BeEmpty();

        var again = OwnerDecisions.Apply(new ProductStore(_resources), dryRun: false, Now);
        (again.Waiting, again.Recorded, again.Written.Count).Should().Be((0, false, 0));
        File.ReadAllLines(Log).Should().HaveCount(lines.Length, "nothing waited, so nothing is appended");
    }

    [Fact]
    public void ADryRunWritesNothingAndALineOfAnotherSectionKeepsWaiting()
    {
        var store = Files();
        Line("place/sodom", "approved", "2026-11-02T10:00:00.000Z");
        File.AppendAllText(Log, """{"at":"2026-11-02T10:00:01.000Z","section":"wikipedia","action":"match","target":"person/x","label":null,"before":null,"after":"Q1","note":null,"needs":"files"}""" + "\n");
        var before = File.ReadAllText(store.FullPath("records/facts/known-for", ProductKind.Hand));

        var dry = OwnerDecisions.Apply(store, dryRun: true, Now);
        (dry.Added, dry.Recorded, dry.Written.Count).Should().Be((1, false, 0));
        File.ReadAllText(store.FullPath("records/facts/known-for", ProductKind.Hand)).Should().Be(before);

        var outcome = OwnerDecisions.Apply(new ProductStore(_resources), dryRun: false, Now);
        outcome.Unhandled.Should().ContainSingle().Which.Should().EndWith("wikipedia person/x");
        outcome.Recorded.Should().BeFalse("a line this step cannot apply would be cleared with the rest");
        Rows("records/facts/known-for").Should().Contain(r => r[0] == "sodom");
    }

    [Fact]
    public void AnApprovalOfARecordFoldedIntoAnotherWritesNothingUnderTheOldSlug()
    {
        var store = Files();
        var records = RecordFiles.All.Single(f => f.Product == "records/records");
        Write(store, "records/records", [.. new[] { "bethel", "gilgal", "noah", "sodom-2" }.Select(slug =>
        {
            var row = new string?[records.Parsed.Length];
            (row[0], row[1], row[4]) = (slug, "place", slug);
            return row;
        })]);
        Write(store, "records/folds", ["sodom", "sodom-2", "Sodom", null, "manual", null, "a test", "one place"]);
        Line("place/sodom", "approved", "2026-11-02T10:00:00.000Z");

        var outcome = OwnerDecisions.Apply(store, dryRun: false, Now);

        (outcome.Added, outcome.Recorded).Should().Be((0, true));
        outcome.Unplaced.Should().Equal("sodom: folded into sodom-2, whose own line stands");
        Rows("records/facts/known-for").Should().NotContain(r => r[0] == "sodom", "records-load refuses a row for a slug that is no record");
    }

    [Fact]
    public void WithoutTheBuildsFilesItRefusesAndAppendsNothing()
    {
        Directory.CreateDirectory(Path.Combine(_resources, "Essenthos"));
        Line("place/sodom", "approved", "2026-11-02T10:00:00.000Z");
        var length = File.ReadAllText(Log).Length;

        var apply = () => OwnerDecisions.Apply(new ProductStore(_resources), dryRun: false, Now);

        apply.Should().Throw<FileNotFoundException>().WithMessage("*cut-over export*");
        File.ReadAllText(Log).Length.Should().Be(length);
    }

    private ProductStore Files()
    {
        var store = new ProductStore(_resources);
        Write(store, "records/facts/known-for", ["bethel", "0", "exported"], ["gilgal", "0", "exported"], ["noah", "0", "KnownFor.json"]);
        Write(store, "records/facts/known-for-references");
        Write(store, "records/i18n/eng/known-for", ["bethel", "0", "bethel's clause"], ["gilgal", "0", "gilgal's clause"], ["noah", "0", "noah's clause"]);
        foreach (var language in new[] { "ukr", "rus", "deu", "spa" })
        {
            Write(store, $"records/i18n/{language}/known-for");
        }

        Directory.CreateDirectory(Path.Combine(_resources, "Essenthos", "review"));
        File.WriteAllText(Path.Combine(_resources, "Essenthos", "review", "known-for.json"),
            """
            {
              "source": "written by a model",
              "records": [
                { "slug": "sodom", "clauses": [
                    { "english": "was overthrown", "ukr": "був зруйнований", "references": ["GEN 19:24-25"] },
                    { "english": "was wicked", "references": ["GEN 13:13", "GEN 18:20-21"] } ] },
                { "slug": "bethel", "clauses": [ { "english": "bethel's clause", "references": ["GEN 28:19"] } ] },
                { "slug": "noah", "clauses": [ { "english": "a list's clause", "references": ["GEN 6:9"] } ] }
              ]
            }
            """);
        return store;
    }

    private static void Write(ProductStore store, string product, params string?[][] rows)
    {
        var file = RecordFiles.All.Single(f => f.Product == product);
        using var writer = store.Create(file.Product, file.Kind, file.Schema(), "forge records-export", [],
            header => header.Comments.Add("kept across the edit"));
        writer.WriteAll(rows);
    }

    private List<string?[]> Rows(string product)
    {
        using var reader = new ProductStore(_resources).Open(product);
        reader.Header!.Comments.Should().Contain("kept across the edit");
        return [.. reader.Rows().Select(r => (string?[])r.Clone())];
    }

    private void Line(string target, string? after, string at)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        var line = new JsonObject
        {
            ["at"] = at, ["section"] = "known-for", ["action"] = "verdict", ["target"] = target, ["label"] = null,
            ["before"] = null, ["after"] = after, ["note"] = null, ["needs"] = OwnerChanges.Files,
        };
        File.AppendAllText(Log, line.ToJsonString() + "\n");
    }
}

