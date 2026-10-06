using System.Text.Json;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public sealed class FixedTitleWitnessTests
{
    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<object[]> Witnesses =>
        new[] { "NESTLE1904", "RP2018", "TR1550", "TR1894", "TISCH", "WH1881" }.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(Witnesses))]
    public void HeldTitleFormsNameTheirBearerAndLeaveDescriptionsAlone(string text)
    {
        var held = Read(Path.Combine(AppContext.BaseDirectory, "Resources", "titles", "greek-witnesses.json"))
            .Where(w => w.Text == text).ToList();
        held.Should().HaveCount(17);
        foreach (var word in held)
            (FixedTitles.Of(word.Word())?.Slug).Should().Be(word.Expected,
                $"{text} {word.Book}:{word.Chapter}:{word.Verse} position {word.Position} prints {word.Surface}");
    }

    [TitleMeasurementFact]
    [Trait("Category", "Corpus")]
    public void MeasureTheFrozenHeldTitleCandidates()
    {
        var input = Environment.GetEnvironmentVariable("ESSENTHOS_TITLE_SNAPSHOT")
            ?? throw new InvalidOperationException("A frozen held title snapshot is required.");
        var output = Environment.GetEnvironmentVariable("ESSENTHOS_TITLE_MEASUREMENT")
            ?? throw new InvalidOperationException("A scratch measurement output is required.");
        var held = Read(input);
        held.Select(w => w.Text).Distinct().Should().BeEquivalentTo(Witnesses.Select(w => (string)w[0]));
        var rows = held.Select(w => (Word: w, Rule: FixedTitles.Of(w.Word())))
            .Where(x => x.Rule is not null).ToList();
        var measurement = rows.GroupBy(x => (x.Word.Text, x.Word.Number)).OrderBy(g => g.Key.Text).ThenBy(g => g.Key.Number)
            .Select(g => new
            {
                g.Key.Text, g.Key.Number, Words = g.Count(),
                Verses = g.Select(x => (x.Word.Book, x.Word.Chapter, x.Word.Verse)).Distinct().Count(),
                Listed = g.Where(x => x.Word.Listed).Select(x => (x.Word.Book, x.Word.Chapter, x.Word.Verse)).Distinct().Count(),
                Unnamed = g.Count(x => x.Word.Named.Count == 0),
                DifferentlyNamed = g.Where(x => x.Word.Named.Any(s => s != x.Rule!.Slug)).Select(x => x.Word).ToList(),
            }).ToList();
        File.WriteAllText(output, JsonSerializer.Serialize(measurement, new JsonSerializerOptions { WriteIndented = true }));
        rows.Should().NotBeEmpty();
    }

    private static IReadOnlyList<HeldWord> Read(string path) =>
        JsonSerializer.Deserialize<List<HeldWord>>(File.ReadAllText(path), Shape)
        ?? throw new InvalidOperationException("The held title snapshot was empty.");

    private sealed record HeldWord(string Text, int Book, int Chapter, int Verse, int Position, string Surface,
        string Number, JsonElement? Morphology, JsonElement? Previous, IReadOnlyList<string> Following, int Nth,
        bool Listed, IReadOnlyList<string> Named, string? Expected)
    {
        public FixedTitleWord Word() => new(0, Text, Book, Number, Form(Morphology),
            Previous is null ? null : Form(Previous), Following, Chapter, Verse, Nth);

        private static FixedTitleMorphology Form(JsonElement? element)
        {
            string? Value(string key) => element is { ValueKind: JsonValueKind.Object } json
                && json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return new(Value("pos"), Value("case"), Value("number"), Value("state"), Value("form") ?? Value("robinson"));
        }
    }
}

/// <summary>
/// The measurement reads a frozen snapshot and writes a scratch file, so it runs only when both are named;
/// a plain run of the corpus tests skips it rather than failing on what it was never given.
/// </summary>
public sealed class TitleMeasurementFactAttribute : FactAttribute
{
    public TitleMeasurementFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ESSENTHOS_TITLE_SNAPSHOT") is null
            || Environment.GetEnvironmentVariable("ESSENTHOS_TITLE_MEASUREMENT") is null)
            Skip = "Set ESSENTHOS_TITLE_SNAPSHOT and ESSENTHOS_TITLE_MEASUREMENT to measure the held title candidates.";
    }
}
