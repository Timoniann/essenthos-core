using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record OccupationReview(string Status, string ReviewedBy, DateTimeOffset ReviewedAt);
internal sealed record OccupationSource(string Attribution, string License, string Uri, string Sha256);
internal sealed record OccupationName(string Name, string Masculine, string Feminine, string? Description);
internal sealed record OccupationConcept(string Slug, string Category, Dictionary<string, OccupationName> Labels);
internal sealed record OccupationEvidence(string Kind, string Text, string Reference, string CanonicalReference,
    string Quote, string SourceText, string SourceSha256);
internal sealed record OccupationAssertion(string PersonSlug, string OccupationSlug,
    IReadOnlyList<OccupationEvidence> Witnesses, JsonElement? Origin = null);
internal sealed record OccupationPublication(int SchemaVersion, string PublicationId, OccupationReview Review,
    OccupationSource Source, IReadOnlyList<OccupationConcept> Occupations, IReadOnlyList<OccupationAssertion> Assertions);
internal sealed record OccupationLoadOutcome(int Written, int Removed, int Total);

/// <summary>A fixed, independently reviewed publication, pinned by the caller's expected digest.</summary>
internal sealed class OccupationLoader(AppDbContext db)
{
    internal static readonly string[] Languages = ["eng", "ukr", "deu", "spa"];
    internal static readonly string[] Categories = ["trade", "office", "military_role", "religious_role"];
    internal static readonly JsonSerializerOptions Shape = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static OccupationPublication Read(byte[] bytes, string expectedSha256)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (expectedSha256.Length != 64 || !hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Occupation publication does not match its independently pinned SHA-256.");
        OccupationPublication publication;
        try
        {
            publication = JsonSerializer.Deserialize<OccupationPublication>(bytes, Shape)
                          ?? throw new InvalidDataException("Occupation publication is empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Occupation publication does not match the reviewed publication schema.", error);
        }
        Validate(publication);
        return publication;
    }

    private static bool Filled(string? value) => !string.IsNullOrWhiteSpace(value);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool Slug(string? value) => Filled(value) && value!.Length <= 120
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    internal static void Validate(OccupationPublication file)
    {
        if (file.SchemaVersion != 1 || !Filled(file.PublicationId)
            || file.Review is not { Status: "reviewed" } review || !Filled(review.ReviewedBy)
            || review.ReviewedAt == default || file.Source is not { } source
            || !Filled(source.Attribution) || !Filled(source.License) || !Filled(source.Uri) || !Hash(source.Sha256)
            || file.Occupations is not { Count: > 0 and <= 10000 } || file.Assertions is not { Count: > 0 and <= 50000 })
            throw new InvalidDataException("Occupation publication requires schema 1, named source review, attribution, licence and bounded nonempty inventories.");
        var concepts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in file.Occupations)
        {
            if (concept is null || !Slug(concept.Slug) || !concepts.Add(concept.Slug) || !Categories.Contains(concept.Category)
                || concept.Labels is null || concept.Labels.Count != Languages.Length
                || Languages.Any(l => !concept.Labels.TryGetValue(l, out var label) || label is null
                    || !Filled(label.Name) || !Filled(label.Masculine) || !Filled(label.Feminine)))
                throw new InvalidDataException($"Invalid occupation concept or missing four-language labels: {concept?.Slug}.");
        }
        foreach (var assertion in file.Assertions)
        {
            if (assertion is null || !Slug(assertion.PersonSlug) || !concepts.Contains(assertion.OccupationSlug)
                || assertion.Witnesses is not { Count: > 0 and <= 100 }
                || !assertion.Witnesses.Any(w => w is { Kind: "profession" }))
                throw new InvalidDataException($"Occupation assertion lacks a person, concept or profession witness: {assertion?.PersonSlug}.");
            foreach (var witness in assertion.Witnesses)
            {
                if (witness is null || witness.Kind is not ("profession" or "identity") || !Filled(witness.Text)
                    || Address(witness.Reference) is null || Address(witness.CanonicalReference) is null
                    || !Filled(witness.Quote) || !Filled(witness.SourceText) || !Hash(witness.SourceSha256)
                    || !Digest(witness.SourceText).Equals(witness.SourceSha256, StringComparison.OrdinalIgnoreCase)
                    || !witness.SourceText.Contains(witness.Quote, StringComparison.Ordinal))
                    throw new InvalidDataException($"Occupation witness must retain an exact quoted source and valid source/canonical address: {assertion.PersonSlug}.");
            }
        }
        if (file.Assertions.GroupBy(a => (a.PersonSlug, a.OccupationSlug)).Any(g => g.SelectMany(a => a.Witnesses).Distinct().Count() > 200))
            throw new InvalidDataException("One person's occupation cannot exceed 200 distinct source witnesses.");
    }

    internal static (int Book, int Chapter, int Verse)? Address(string? reference)
    {
        var citation = Citation.Parse(reference);
        if (citation is not { Single: true }) return null;
        var verse = citation.Verses.Single();
        return BookReferences.IsInCanon(verse.Book) && verse.Chapter is > 0 and < 1000 && verse.Verse is >= 0 and < 1000
            ? (verse.Book, verse.Chapter, verse.Verse) : null;
    }

    public async Task<OccupationLoadOutcome> Load(string path, string expectedSha256, CancellationToken cancellationToken = default)
        => await Load(await File.ReadAllBytesAsync(path, cancellationToken), expectedSha256, cancellationToken);

    internal async Task<OccupationLoadOutcome> Load(byte[] bytes, string expectedSha256, CancellationToken cancellationToken = default)
    {
        var file = Read(bytes, expectedSha256);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1380054867)", cancellationToken);
            var held = await db.PersonOccupations.Include(p => p.Witnesses).ToListAsync(cancellationToken);
            var current = held.Where(p => p.PublicationId == file.PublicationId).ToList();
            var assertionCount = file.Assertions.Select(a => (a.PersonSlug, a.OccupationSlug)).Distinct().Count();
            if (current.Count == assertionCount && current.All(p => p.PublicationSha256 == hash)
                && current.Sum(p => p.Witnesses.Count) == file.Assertions.GroupBy(a => (a.PersonSlug, a.OccupationSlug))
                    .Sum(g => g.SelectMany(a => a.Witnesses).Distinct().Count()))
            {
                await transaction.CommitAsync(cancellationToken);
                return new(0, 0, current.Count);
            }
            var personSlugs = file.Assertions.Select(a => a.PersonSlug).Distinct().ToList();
            var people = await db.Entities.Where(e => personSlugs.Contains(e.Slug) && e.Kind == EntityKind.Person)
                .ToDictionaryAsync(e => e.Slug, cancellationToken);
            if (personSlugs.Any(s => !people.ContainsKey(s)))
                throw new InvalidDataException("Occupation publication names an unknown or non-person identity.");
            var concepts = await db.Occupations.Include(o => o.Labels).ToDictionaryAsync(o => o.Slug, cancellationToken);
            foreach (var concept in file.Occupations)
            {
                if (!concepts.TryGetValue(concept.Slug, out var occupation))
                {
                    occupation = new Occupation { Slug = concept.Slug, Category = concept.Category };
                    concepts.Add(concept.Slug, occupation);
                    db.Occupations.Add(occupation);
                }
                occupation.Category = concept.Category;
                foreach (var (language, name) in concept.Labels)
                {
                    var label = occupation.Labels.SingleOrDefault(l => l.Language == language);
                    if (label is null)
                    {
                        label = new OccupationLabel { Language = language, Name = name.Name, Masculine = name.Masculine, Feminine = name.Feminine };
                        occupation.Labels.Add(label);
                    }
                    label.Name = name.Name;
                    label.Masculine = name.Masculine;
                    label.Feminine = name.Feminine;
                    label.Description = name.Description;
                }
            }
            var rows = file.Assertions.GroupBy(a => (a.PersonSlug, a.OccupationSlug)).ToList();
            if (rows.Any(g => held.Any(p => p.PersonId == people[g.Key.PersonSlug].Id
                && p.OccupationId == concepts[g.Key.OccupationSlug].Id && p.PublicationId != file.PublicationId)))
                throw new InvalidDataException("Another publication owns this person's occupation; consolidate the reviewed publication explicitly.");
            db.PersonOccupations.RemoveRange(current);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var group in rows)
            {
                var provenance = JsonSerializer.Serialize(new { file.Source, file.Review, Origins = group.Select(a => a.Origin) }, Shape);
                var row = new PersonOccupation
                {
                    Person = people[group.Key.PersonSlug], Occupation = concepts[group.Key.OccupationSlug],
                    PublicationId = file.PublicationId, PublicationSha256 = hash, Provenance = provenance,
                };
                foreach (var witness in group.SelectMany(a => a.Witnesses).Distinct())
                {
                    var address = Address(witness.CanonicalReference)!.Value;
                    row.Witnesses.Add(new OccupationWitness
                    {
                        Kind = witness.Kind, Text = witness.Text, Reference = witness.Reference,
                        CanonicalBook = address.Book, CanonicalChapter = address.Chapter, CanonicalVerse = address.Verse,
                        Quote = witness.Quote, SourceText = witness.SourceText, SourceSha256 = witness.SourceSha256,
                    });
                }
                db.PersonOccupations.Add(row);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(rows.Count, current.Count, rows.Count);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
