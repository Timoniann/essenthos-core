using System.Text;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// Strong's dictionary in a reader's language, from the file a translation run publishes to the row
/// the build's <c>tables/strong_entry_translation</c> holds beside the English.
///
/// The fixtures are written here rather than taken from the corpus: the real file is the output of
/// a five-hour model run and is on almost no disk. What matters in every case below is the same
/// thing — the prose may change language, and the addresses inside it may not.
/// </summary>
public sealed class StrongTranslationTests : IDisposable
{
    private const string Model = "claude-sonnet-5";

    private const string Prompt = "lexicon-3";

    private readonly Dictionary<string, StrongEntry> _english = new(StringComparer.Ordinal);

    private readonly string _directory = Directory.CreateTempSubdirectory("essenthos-lexicon").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void TheTranslationStandsBesideTheEnglishAndNamesWhatMadeIt()
    {
        English("H430", definition: "gods in the ordinary sense", derivation: "plural of אֱלוֹהַּ (H433);");
        Published("H430", definition: "боги у звичайному значенні", derivation: "множина від אֱלוֹהַּ (H433);");

        var (rows, refused) = Accepted();

        refused.Total.Should().Be(0);
        var translated = rows.Should().ContainSingle().Which;
        translated.StrongNumber.Should().Be("H430");
        translated.Language.Should().Be("ukr");
        translated.Definition.Should().Be("боги у звичайному значенні");
        translated.Method.Should().Be(LinkMethod.ModelReading);
        translated.Source.Should().Be($"{StrongTranslationLoader.SourcePrefix} {Model}, prompt {Prompt}, run 2026-09-06");
        _english["H430"].Definition.Should().Be("gods in the ordinary sense");
    }

    /// <summary>
    /// The failure the whole exercise is measured on. A dropped <c>H433</c> reads perfectly and
    /// points nowhere, and nothing downstream could tell.
    /// </summary>
    [Fact]
    public void ATranslationThatDroppedAStrongReferenceIsRefused()
    {
        English("H430", derivation: "plural of אֱלוֹהַּ (H433);");
        Published("H430", derivation: "множина від елоах;");

        var (rows, refused) = Accepted();

        rows.Should().BeEmpty();
        refused.LostReference.Should().Be(1);
    }

    [Fact]
    public void ATranslationThatMergedTwoSensesIsRefused()
    {
        English("H1", detailed: "1) father\n1a) of an individual\n1b) of God");
        Published("H1", detailed: "1) батько\n1a) окремої особи, Бога");

        var (rows, refused) = Accepted();

        rows.Should().BeEmpty();
        refused.LostSense.Should().Be(1);
    }

    [Fact]
    public void ANumberTheDictionaryDoesNotHoldIsRefusedAndCounted()
    {
        English("H430", definition: "gods in the ordinary sense");
        Published("H9999", definition: "щось");

        var (rows, refused) = Accepted();

        rows.Should().BeEmpty();
        refused.UnknownNumber.Should().Be(1);
    }

    /// <summary>Each number is translated per language, so a second language stands beside the first.</summary>
    [Fact]
    public void ASecondLanguageStandsBesideTheFirst()
    {
        English("H430", definition: "gods in the ordinary sense");
        Published("H430", definition: "боги у звичайному значенні");
        Published("H430", definition: "Götter im gewöhnlichen Sinne", language: "deu", file: "deu.jsonl");

        var (rows, _) = Accepted();

        rows.Select(r => (r.Language, r.Definition)).Should().BeEquivalentTo(new[]
        {
            ("ukr", "боги у звичайному значенні"),
            ("deu", "Götter im gewöhnlichen Sinne"),
        });
    }

    /// <summary>
    /// A reviewed repair republishes one entry in a later file. Its record takes the earlier one's place, prose and
    /// provenance, and every other entry is as it was.
    /// </summary>
    [Fact]
    public void ALaterFileReplacesTheRecordItRepublishes()
    {
        English("H430", definition: "gods in the ordinary sense");
        English("H6544", definition: "to loosen; by implication, to let alone");
        Published("H430", definition: "боги у звичайному значенні");
        Published("H6544", definition: "відпускати; дозволяти");
        Published("H6544", definition: "відпускати; перешкоджати", file: "zz-repair.jsonl",
            model: "claude-sonnet-5 + gpt-6.1-sol", prompt: "lexicon-3 + strong-archaic-repair-1",
            translatedAt: "2026-10-21T12:00:00+00:00", uncertain: ["let"]);

        var (records, files, replaced) = StrongTranslationFiles.Read(_directory);
        var rows = StrongTranslationLoader.Accepted(records, _english, out var refused).ToDictionary(r => r.StrongNumber);

        (files, replaced, refused.Total).Should().Be((2, 1, 0));
        rows["H6544"].Definition.Should().Be("відпускати; перешкоджати");
        rows["H6544"].Source.Should().Be(
            $"{StrongTranslationLoader.SourcePrefix} claude-sonnet-5 + gpt-6.1-sol, prompt lexicon-3 + strong-archaic-repair-1, run 2026-10-21");
        rows["H6544"].Note.Should().Be("the translator was unsure of: let");
        rows["H430"].Definition.Should().Be("боги у звичайному значенні");
    }

    /// <summary>
    /// The only doubt the run produces per row is the translator's own, and it is kept as it was
    /// given rather than turned into a number nobody measured.
    /// </summary>
    [Fact]
    public void TheTranslatorsOwnDoubtIsKeptAndNoConfidenceIsInvented()
    {
        English("G810", definition: "unsavedness");
        Published("G810", definition: "неспасенність", uncertain: ["unsavedness"]);

        var translated = Accepted().Rows.Should().ContainSingle().Which;

        translated.Confidence.Should().BeNull();
        translated.Note.Should().Contain("unsavedness");
    }

    private (List<StrongEntryTranslation> Rows, TranslationRefusals Refused) Accepted()
    {
        var (records, _, _) = StrongTranslationFiles.Read(_directory);
        var rows = StrongTranslationLoader.Accepted(records, _english, out var refused);
        return (rows, refused);
    }

    private void English(
        string number,
        string? definition = null,
        string? derivation = null,
        string? detailed = null) =>
        _english[number] = new StrongEntry
        {
            StrongNumber = number,
            Definition = definition,
            Derivation = derivation,
            DetailedDefinition = detailed,
        };

    private void Published(
        string number,
        string? definition = null,
        string? derivation = null,
        string? detailed = null,
        string language = "ukr",
        string file = "ukr.jsonl",
        string[]? uncertain = null,
        string model = Model,
        string prompt = Prompt,
        string translatedAt = "2026-09-06T17:41:02+00:00")
    {
        var row = new Dictionary<string, object?>
        {
            ["strong_number"] = number,
            ["language"] = language,
            ["method"] = StrongTranslationFiles.ModelTranslation,
            ["model"] = model,
            ["prompt_version"] = prompt,
            ["translated_at"] = translatedAt,
        };

        Say(row, "definition", definition);
        Say(row, "derivation", derivation);
        Say(row, "detailed_definition", detailed);
        if (uncertain is not null)
        {
            row["uncertain"] = uncertain;
        }

        File.AppendAllText(
            Path.Combine(_directory, file),
            System.Text.Json.JsonSerializer.Serialize(row) + Environment.NewLine,
            Encoding.UTF8);
    }

    private static void Say(Dictionary<string, object?> row, string field, string? value)
    {
        if (value is not null)
        {
            row[field] = value;
        }
    }
}
