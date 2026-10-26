using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record PresenceRecord(string Person, string Place, string Category, int[] Addresses, JsonElement Evidence);
internal sealed record PresenceFile(int Version, string Source, IReadOnlyList<PresenceRecord> Records, IReadOnlyList<ThingRecord> Sites);
internal sealed record PresenceOutcome(int Written, int Removed, int Total)
{
    public override string ToString() => $"person presence: {Written} written, {Removed} removed, {Total} supported pairs";
}

/// <summary>Only admitted, cited presence findings enter this table; rejected and unresolved readings stay in their run.</summary>
internal sealed class PersonPresenceLoader(AppDbContext db, ThingLoader sites)
{
    public const string FileName = "person-presence.json";
    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };
    private static readonly ThingSet SiteSet = new(
        "the places and structures named in the presence readings",
        "Essenthos, model and agent readings of named places and structures, 2026-10-26",
        "Essenthos, reviewed presence sites", "Essenthos, passages naming presence sites", []);

    public async Task<PresenceOutcome> Load(string resources, CancellationToken cancellationToken)
    {
        var path = Path.Combine(resources, "Essenthos", FileName);
        // An installation without this optional dataset does not erase an already loaded one.
        if (!File.Exists(path))
        {
            return new PresenceOutcome(0, 0, await db.PersonPresences.CountAsync(cancellationToken));
        }
        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<PresenceFile>(stream, Shape, cancellationToken)
                   ?? throw new InvalidDataException($"{path} is empty.");
        return await Load(file, cancellationToken);
    }

    internal async Task<PresenceOutcome> Load(PresenceFile file, CancellationToken cancellationToken = default)
    {
        if (file.Version != 1 || string.IsNullOrWhiteSpace(file.Source) || file.Records.Count == 0)
            throw new InvalidDataException("Presence requires version 1, attributed source and a nonempty admitted inventory.");
        var keys = new HashSet<(string, string, string)>();
        foreach (var row in file.Records)
        {
            if (!keys.Add((row.Person, row.Place, row.Category)) || row.Category is not ("present" or "buried")
                || row.Addresses.Length == 0 || row.Addresses.Any(a => a / 1_000_000 is < 1 or > 93 || a % 1_000_000 / 1_000 < 1)
                || row.Evidence.ValueKind != JsonValueKind.Array || row.Evidence.GetArrayLength() == 0)
                throw new InvalidDataException($"Invalid or repeated presence: {row.Person}/{row.Place}/{row.Category}.");
        }
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            if (file.Sites.Count > 0)
            {
                var outcome = await sites.Load(SiteSet, file.Sites, [], cancellationToken);
                if (outcome.Missing != 0 || outcome.Refused != 0)
                    throw new InvalidDataException("Presence sites were not all resolved; no presence data was replaced.");
            }
            var slugs = file.Records.SelectMany(r => new[] { r.Person, r.Place }).Distinct().ToList();
            var entities = await db.Entities.Where(e => slugs.Contains(e.Slug)).ToDictionaryAsync(e => e.Slug, cancellationToken);
            var aliases = await db.MergedRecords.Where(e => slugs.Contains(e.Slug)).Include(e => e.Entity).ToListAsync(cancellationToken);
            foreach (var alias in aliases)
                if (alias.Entity is { } entity) entities.TryAdd(alias.Slug, entity);
            var wanted = new List<PersonPresence>();
            foreach (var row in file.Records)
            {
                if (!entities.TryGetValue(row.Person, out var person) || person.Kind != EntityKind.Person
                    || !entities.TryGetValue(row.Place, out var place) || place.Kind != EntityKind.Place)
                    throw new InvalidDataException($"Unknown or wrong-kind presence identity: {row.Person}/{row.Place}.");
                wanted.Add(new PersonPresence
                {
                    PersonId = person.Id, PlaceId = place.Id, Category = row.Category,
                    Addresses = row.Addresses.Distinct().Order().ToArray(), Source = file.Source,
                    Evidence = row.Evidence.GetRawText(),
                });
            }
            wanted = wanted.GroupBy(r => (r.PersonId, r.PlaceId, r.Category)).Select(group => new PersonPresence
            {
                PersonId = group.Key.PersonId, PlaceId = group.Key.PlaceId, Category = group.Key.Category,
                Addresses = group.SelectMany(r => r.Addresses).Distinct().Order().ToArray(), Source = file.Source,
                Evidence = JsonSerializer.Serialize(group.SelectMany(r => JsonSerializer.Deserialize<JsonElement>(r.Evidence).EnumerateArray())),
            }).ToList();
            var held = await db.PersonPresences.ToListAsync(cancellationToken);
            var old = held.ToDictionary(r => (r.PersonId, r.PlaceId, r.Category));
            var stale = new List<PersonPresence>();
            var fresh = new List<PersonPresence>();
            foreach (var row in wanted)
            {
                if (old.Remove((row.PersonId, row.PlaceId, row.Category), out var was))
                {
                    if (was.Source == row.Source && was.Addresses.SequenceEqual(row.Addresses)
                        && JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(was.Evidence), JsonSerializer.Deserialize<JsonElement>(row.Evidence)))
                        continue;
                    stale.Add(was);
                }
                fresh.Add(row);
            }
            stale.AddRange(old.Values);
            db.PersonPresences.RemoveRange(stale);
            await db.SaveChangesAsync(cancellationToken);
            db.PersonPresences.AddRange(fresh);
            await db.SaveChangesAsync(cancellationToken);
            if (own is not null) await own.CommitAsync(cancellationToken);
            return new PresenceOutcome(fresh.Count, stale.Count, wanted.Count);
        }
        catch
        {
            if (own is not null) await own.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (own is not null) await own.DisposeAsync();
        }
    }
}
