using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The lines this corpus writes under its own records, and their renderings into the languages a
/// reader can ask for. The file is held to the records: a line rewritten in its record and not
/// rendered again is found here rather than on a page that quietly falls back to English.
/// </summary>
public partial class DistinguisherTests
{
    private static readonly DistinguisherFile File = DistinguisherLoader.Read();

    private static readonly string[] Languages = ["ukr", "deu", "spa"];

    /// <summary>Every record whose English line this corpus wrote, by slug.</summary>
    private static Dictionary<string, string> OurOwnLines()
    {
        var lines = ThingFiles.Read(ThingSet.MadeAndKept).Records
            .Concat(ThingFiles.Read(ThingSet.Narratives).Records)
            .ToDictionary(r => r.Slug, r => r.Distinguisher, StringComparer.Ordinal);

        foreach (var (resource, list) in (ReadOnlySpan<(string, string)>)[("TitleRecords.json", "titles"), ("Peoples.json", "tribes")])
        {
            using var stream = typeof(ThingFiles).Assembly.GetManifestResourceStream(
                $"Essenthos.Core.Loading.Encyclopedia.{resource}")!;
            using var json = JsonDocument.Parse(stream);
            foreach (var record in json.RootElement.GetProperty(list).EnumerateArray())
            {
                lines[record.GetProperty("slug").GetString()!] = record.GetProperty("distinguisher").GetString()!;
            }
        }

        foreach (var term in TermLoader.Terms)
        {
            lines[term.Slug] = term.Distinguisher;
        }

        // A record a dataset listed and a ruling re-headed is ours, line and all.
        foreach (var ruling in SenseReadingFiles.AddressedRulings().Rulings
                     .Where(r => r.Existing is not null && r.Says?.Name is not null && r.Says.Distinguisher is not null))
        {
            lines[ruling.Existing!] = ruling.Says!.Distinguisher!;
        }

        // A record a ruling created is ours from the start.
        foreach (var created in SenseReadingFiles.AllRulings().SelectMany(file => file.Rulings)
                     .Select(r => r.Create)
                     .Where(r => r?.Distinguisher is not null))
        {
            lines[created!.Slug] = created.Distinguisher!;
        }

        return lines;
    }

    [Fact]
    public void EveryLineWeWriteIsRenderedFromWhatItSaysNow()
    {
        var rendered = File.Records.ToDictionary(r => r.Slug, r => r.English, StringComparer.Ordinal);

        rendered.Should().BeEquivalentTo(
            OurOwnLines(),
            "a record whose English line changed keeps no translation until this file is rendered again");
    }

    [Fact]
    public void EveryLineIsRenderedIntoEveryLanguage()
    {
        foreach (var record in File.Records)
        {
            record.Languages().Select(one => one.Language).Should().Equal(Languages, record.Slug);
        }
    }

    [Fact]
    public void ARenderingKeepsEveryVerseItsLineCites()
    {
        foreach (var record in File.Records)
        {
            var cited = Reference().Matches(record.English).Select(m => m.Value).ToList();
            foreach (var (language, text) in record.Languages())
            {
                Reference().Matches(text).Select(m => m.Value).Should()
                    .BeEquivalentTo(cited, $"{record.Slug} in {language} cites what the English cites");
            }
        }
    }

    [Fact]
    public void TheUkrainianWritesProekt()
    {
        File.Records.Select(r => r.Ukr).Should().NotContain(text => text!.Contains("проєкт"));
    }

    /// <summary>
    /// The lines this project wrote for the records a dataset supplied: every one in every language,
    /// citing what its English cites, each record once and in neither file twice, and credited to this
    /// project.
    /// </summary>
    [Fact]
    public void OurOwnLinesForTheDatasetsRecordsAreWholeInEveryLanguage()
    {
        var own = DistinguisherLoader.ReadOwnLines();

        own.SetsTheLine.Should().BeTrue();
        own.Source.Should().StartWith("Essenthos, ");
        own.Records.Select(r => r.Slug).Should().OnlyHaveUniqueItems()
            .And.NotIntersectWith(File.Records.Select(r => r.Slug));
        foreach (var record in own.Records)
        {
            record.English.Should().NotBeNullOrWhiteSpace(record.Slug);
            record.Languages().Select(one => one.Language).Should().Equal(Languages, record.Slug);
            var cited = Reference().Matches(record.English).Select(m => m.Value).ToList();
            cited.Should().NotBeEmpty($"{record.Slug}'s line cites the verse it was written from");
            foreach (var (language, text) in record.Languages())
            {
                Reference().Matches(text).Select(m => m.Value).Should()
                    .BeEquivalentTo(cited, $"{record.Slug} in {language} cites what the English cites");
            }
        }

        own.Records.Select(r => r.Ukr).Should().NotContain(text => text!.Contains("проєкт"));
    }

    [GeneratedRegex(@"\b[1-3]?[A-Z]{2,3} \d+(?::\d+(?:-\d+)?)?")]
    private static partial Regex Reference();
}
