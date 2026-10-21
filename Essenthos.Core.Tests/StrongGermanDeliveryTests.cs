using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongGermanDeliveryTests(WitnessDatabase database, ITestOutputHelper output)
{
    private const int Entries = 14197;

    [Fact]
    [Trait("Category", "Corpus")]
    public async Task TheCompleteGermanDeliveryLoadsWithoutLossAndDoesNotRewriteOnReplay()
    {
        var root = Environment.GetEnvironmentVariable("ESSENTHOS_STRONG_DELIVERY_ROOT");
        root.Should().NotBeNullOrWhiteSpace(
            "this proof requires ESSENTHOS_STRONG_DELIVERY_ROOT with full/source.json and checked/strong-de.jsonl");
        var sourcePath = Path.Combine(root!, "full", "source.json");
        var deliveryPath = Path.Combine(root!, "checked", "strong-de.jsonl");
        File.Exists(sourcePath).Should().BeTrue($"the frozen English source is required at {sourcePath}");
        File.Exists(deliveryPath).Should().BeTrue($"the checked German delivery is required at {deliveryPath}");

        await using var db = database.NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var directory = Directory.CreateTempSubdirectory("essenthos-strong-de-proof").FullName;
        try
        {
            using var source = JsonDocument.Parse(await File.ReadAllTextAsync(sourcePath));
            var english = source.RootElement.EnumerateArray().Select(row => new StrongEntry
            {
                StrongNumber = row.GetProperty("strong_number").GetString()!,
                Lemma = Text(row, "lemma"), Transliteration = Text(row, "transliteration"),
                Morphology = Text(row, "morphology"), SourceLanguage = Text(row, "source_language"),
                Definition = Text(row, "definition"), Derivation = Text(row, "derivation"),
                KjvDefinition = Text(row, "kjv_definition"), DetailedDefinition = Text(row, "detailed_definition"),
            }).ToList();
            english.Should().HaveCount(Entries);
            english.Select(e => e.StrongNumber).Should().OnlyHaveUniqueItems();
            db.StrongEntries.AddRange(english);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var sourceBefore = await SourceFingerprint(db);

            File.Copy(deliveryPath, Path.Combine(directory, "strong-de.jsonl"));
            var (records, files, replaced) = StrongTranslationFiles.Read(directory);
            records.Should().HaveCount(Entries);
            records.Should().OnlyContain(r => r.Language == "deu");
            records.Should().OnlyContain(r => r.Method == StrongTranslationFiles.ModelTranslation
                && !string.IsNullOrWhiteSpace(r.Model) && !string.IsNullOrWhiteSpace(r.PromptVersion)
                && !string.IsNullOrWhiteSpace(r.TranslatedAt));
            files.Should().Be(1);
            replaced.Should().Be(0);
            var expected = records.ToDictionary(r => r.StrongNumber, StringComparer.Ordinal);
            expected.Keys.Should().BeEquivalentTo(english.Select(e => e.StrongNumber));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [StrongTranslationFiles.ConfigurationKey] = directory }).Build();
            var loader = new StrongTranslationLoader(db, configuration, NullLogger<StrongTranslationLoader>.Instance);

            var first = await loader.Load("unused-because-the-translation-directory-is-set");
            var writtenNumbers = await db.StrongEntryTranslations.Select(t => t.StrongNumber).ToListAsync();
            var missing = expected.Keys.Except(writtenNumbers, StringComparer.Ordinal).Take(30);
            first.Refused.Total.Should().Be(0, $"{first.Refused}; missing sample: {string.Join(", ", missing)}");
            first.Written.Should().Be(Entries);
            first.Records.Should().Be(Entries);
            first.Skipped.Should().Be(0);
            first.Languages.Should().Equal("deu");

            var loaded = await db.StrongEntryTranslations.AsNoTracking().OrderBy(t => t.StrongNumber).ToListAsync();
            loaded.Should().HaveCount(Entries);
            foreach (var actual in loaded)
            {
                var record = expected[actual.StrongNumber];
                actual.Language.Should().Be("deu");
                actual.Definition.Should().Be(Prose(record.Definition), actual.StrongNumber);
                actual.Derivation.Should().Be(Prose(record.Derivation), actual.StrongNumber);
                actual.KjvDefinition.Should().Be(Prose(record.KjvDefinition), actual.StrongNumber);
                actual.DetailedDefinition.Should().Be(Prose(record.DetailedDefinition), actual.StrongNumber);
                actual.Method.Should().Be(LinkMethod.ModelReading);
                actual.Confidence.Should().BeNull();
                actual.Source.Should().Be($"{StrongTranslationLoader.SourcePrefix} {record.Model}, "
                    + $"prompt {record.PromptVersion}, run {record.TranslatedAt[..Math.Min(10, record.TranslatedAt.Length)]}");
                actual.Note.Should().Be(record.Uncertain is { Count: > 0 }
                    ? "the translator was unsure of: " + string.Join(", ", record.Uncertain)
                    : null);
            }
            (await SourceFingerprint(db)).Should().Be(sourceBefore);
            var translatedBefore = Fingerprint(loaded);
            var localized = await StrongEndpoints.Translation(db, "G26", "de", default);
            localized.Should().NotBeNull();
            localized!.Definition.Should().Be(expected["G26"].Definition);
            localized.Source.Should().Be(loaded.Single(t => t.StrongNumber == "G26").Source);

            var second = await loader.Load("unused-because-the-translation-directory-is-set");
            second.Written.Should().Be(0);
            second.Refused.Total.Should().Be(0);
            second.AlreadyLoaded.Should().BeTrue();
            second.Skipped.Should().Be(Entries);
            (await SourceFingerprint(db)).Should().Be(sourceBefore);
            var after = await db.StrongEntryTranslations.AsNoTracking().OrderBy(t => t.StrongNumber).ToListAsync();
            Fingerprint(after).Should().Be(translatedBefore);
            output.WriteLine($"German delivery: {Entries} loaded; replay wrote 0; source SHA256 {sourceBefore}; translation SHA256 {translatedBefore}.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.GetString() : null;

    private static string? Prose(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static async Task<string> SourceFingerprint(AppDbContext db) =>
        Fingerprint(await db.StrongEntries.AsNoTracking().OrderBy(e => e.StrongNumber).ToListAsync());

    private static string Fingerprint<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
