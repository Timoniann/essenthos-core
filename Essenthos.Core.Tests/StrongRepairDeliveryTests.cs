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

/// <summary>
/// A repaired lexicon file published over a corpus that already holds the earlier one: the
/// published German, Ukrainian and Spanish load first, then the staged repaired files replace the
/// German and Ukrainian ones. Only the entries the repair names may change, in place, and a second
/// load writes nothing.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class StrongRepairDeliveryTests(WitnessDatabase database, ITestOutputHelper output)
{
    private const int Entries = 14197;

    private static readonly string[] Published = ["strong-de.jsonl", "strong-es.jsonl", "strong-uk.jsonl"];

    private static readonly string[] Repaired = ["strong-de.jsonl", "strong-uk.jsonl"];

    [Fact]
    [Trait("Category", "Corpus")]
    public async Task TheRepairedFilesRewriteOnlyTheRepairedEntriesAndReplayWritesNothing()
    {
        var run = Environment.GetEnvironmentVariable("ESSENTHOS_STRONG_REPAIR_RUN");
        var lexicon = Environment.GetEnvironmentVariable("ESSENTHOS_STRONG_PUBLISHED");
        var sourcePath = Environment.GetEnvironmentVariable("ESSENTHOS_STRONG_SOURCE");
        run.Should().NotBeNullOrWhiteSpace("ESSENTHOS_STRONG_REPAIR_RUN names the run with staged/ and staged/report.json");
        lexicon.Should().NotBeNullOrWhiteSpace("ESSENTHOS_STRONG_PUBLISHED names the published lexicon folder");
        sourcePath.Should().NotBeNullOrWhiteSpace("ESSENTHOS_STRONG_SOURCE names the frozen English source.json");

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run!, "staged", "report.json")));
        var languages = report.RootElement.GetProperty("languages");
        var named = new HashSet<(string, string)>();
        foreach (var (code, language) in new[] { ("de", "deu"), ("uk", "ukr") })
        {
            var entry = languages.GetProperty(code);
            Sha(Path.Combine(lexicon!, $"strong-{code}.jsonl")).Should().Be(
                entry.GetProperty("published_sha256").GetString(), "the repair was staged from the file now published");
            Sha(Path.Combine(run!, "staged", $"strong-{code}.jsonl")).Should().Be(entry.GetProperty("sha256").GetString());
            foreach (var number in entry.GetProperty("numbers").EnumerateArray())
            {
                named.Add((number.GetString()!, language));
            }
        }

        await using var db = database.NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var directory = Directory.CreateTempSubdirectory("essenthos-strong-repair-proof").FullName;
        try
        {
            using var source = JsonDocument.Parse(await File.ReadAllTextAsync(sourcePath!));
            db.StrongEntries.AddRange(source.RootElement.EnumerateArray().Select(row => new StrongEntry
            {
                StrongNumber = row.GetProperty("strong_number").GetString()!,
                Lemma = Text(row, "lemma"), Transliteration = Text(row, "transliteration"),
                Morphology = Text(row, "morphology"), SourceLanguage = Text(row, "source_language"),
                Definition = Text(row, "definition"), Derivation = Text(row, "derivation"),
                KjvDefinition = Text(row, "kjv_definition"), DetailedDefinition = Text(row, "detailed_definition"),
            }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var english = await EnglishFingerprint(db);

            foreach (var name in Published)
            {
                File.Copy(Path.Combine(lexicon!, name), Path.Combine(directory, name));
            }

            // The corpus as it stands: every published row, written as the loader writes it. A cold
            // load would refuse some Ukrainian rows that lost a reference, but the live rows hold them.
            db.StrongEntryTranslations.AddRange(StrongTranslationFiles.Read(directory).Records.Select(Row));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var loader = Loader(db, directory);
            var settled = await loader.Load("unused-because-the-translation-directory-is-set");
            settled.AlreadyLoaded.Should().BeTrue("the published files are what the corpus already holds");
            settled.Written.Should().Be(0);
            settled.Updated.Should().Be(0);
            settled.Refused.Total.Should().Be(0);
            var before = await Rows(db);
            before.Should().HaveCount(3 * Entries);

            foreach (var name in Repaired)
            {
                File.Copy(Path.Combine(run!, "staged", name), Path.Combine(directory, name), overwrite: true);
            }

            var repair = await loader.Load("unused-because-the-translation-directory-is-set");
            repair.Refused.Total.Should().Be(0, repair.Refused.ToString());
            repair.Written.Should().Be(0);
            repair.Updated.Should().Be(named.Count);
            repair.Skipped.Should().Be(3 * Entries - named.Count);

            var after = await Rows(db);
            after.Keys.ToHashSet().SetEquals(before.Keys).Should().BeTrue("no row is added or removed");
            var expected = StrongTranslationFiles.Read(directory).Records
                .ToDictionary(r => (r.StrongNumber, r.Language));
            var changed = after.Keys.Where(k => Json(after[k]) != Json(before[k])).ToHashSet();
            changed.Except(named).Should().BeEmpty("only the entries the repair names change");
            named.Except(changed).Should().BeEmpty("every entry the repair names changes");
            foreach (var key in named)
            {
                var row = after[key];
                var record = expected[key];
                row.Id.Should().Be(before[key].Id, "a repaired row is rewritten in place");
                row.Definition.Should().Be(Prose(record.Definition), key.ToString());
                row.Derivation.Should().Be(Prose(record.Derivation), key.ToString());
                row.KjvDefinition.Should().Be(Prose(record.KjvDefinition), key.ToString());
                row.DetailedDefinition.Should().Be(Prose(record.DetailedDefinition), key.ToString());
                row.Source.Should().Be($"{StrongTranslationLoader.SourcePrefix} {record.Model}, "
                    + $"prompt {record.PromptVersion}, run {record.TranslatedAt[..Math.Min(10, record.TranslatedAt.Length)]}");
                row.Note.Should().Be(record.Uncertain is { Count: > 0 }
                    ? "the translator was unsure of: " + string.Join(", ", record.Uncertain)
                    : null);
            }

            Rowprint(after.Where(p => p.Key.Language == "spa")).Should().Be(
                Rowprint(before.Where(p => p.Key.Language == "spa")), "Spanish is not part of the repair");
            (await EnglishFingerprint(db)).Should().Be(english, "the English is never written");

            var sample = named.OrderBy(k => k.Item2).ThenBy(k => k.Item1, StringComparer.Ordinal).First();
            var served = await StrongEndpoints.Translation(db, sample.Item1, sample.Item2, default);
            served!.KjvDefinition.Should().Be(after[sample].KjvDefinition);
            served.Definition.Should().Be(after[sample].Definition);

            var replay = await loader.Load("unused-because-the-translation-directory-is-set");
            replay.Written.Should().Be(0);
            replay.Updated.Should().Be(0);
            replay.AlreadyLoaded.Should().BeTrue();
            Rowprint(await Rows(db)).Should().Be(Rowprint(after));

            output.WriteLine($"Repair delivery: {named.Count} rows rewritten in place "
                + $"({named.Count(k => k.Item2 == "deu")} deu, {named.Count(k => k.Item2 == "ukr")} ukr), "
                + $"{3 * Entries - named.Count} left alone, Spanish and English unchanged, replay wrote 0; "
                + $"translations SHA256 {Rowprint(after)}.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static StrongEntryTranslation Row(StrongTranslationRecord record) => new()
    {
        StrongNumber = record.StrongNumber,
        Language = record.Language,
        Definition = Prose(record.Definition),
        Derivation = Prose(record.Derivation),
        KjvDefinition = Prose(record.KjvDefinition),
        DetailedDefinition = Prose(record.DetailedDefinition),
        Method = LinkMethod.ModelReading,
        Source = $"{StrongTranslationLoader.SourcePrefix} {record.Model}, prompt {record.PromptVersion}, "
            + $"run {record.TranslatedAt[..Math.Min(10, record.TranslatedAt.Length)]}",
        Note = record.Uncertain is { Count: > 0 } ? "the translator was unsure of: " + string.Join(", ", record.Uncertain) : null,
    };

    private static StrongTranslationLoader Loader(AppDbContext db, string directory) =>
        new(db, new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { [StrongTranslationFiles.ConfigurationKey] = directory }).Build(),
            NullLogger<StrongTranslationLoader>.Instance);

    private static async Task<Dictionary<(string Number, string Language), StrongEntryTranslation>> Rows(AppDbContext db) =>
        await db.StrongEntryTranslations.AsNoTracking().ToDictionaryAsync(t => (t.StrongNumber, t.Language));

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.GetString() : null;

    private static string? Prose(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string Json(StrongEntryTranslation row) => JsonSerializer.Serialize(row);

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static async Task<string> EnglishFingerprint(AppDbContext db) =>
        Fingerprint(await db.StrongEntries.AsNoTracking().OrderBy(e => e.StrongNumber).ToListAsync());

    private static string Rowprint(IEnumerable<KeyValuePair<(string Number, string Language), StrongEntryTranslation>> rows) =>
        Fingerprint(rows.OrderBy(p => p.Key.Language, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Number, StringComparer.Ordinal).Select(p => p.Value).ToList());

    private static string Fingerprint<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
