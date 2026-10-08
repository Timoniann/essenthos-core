using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record PlacesOutcome(
    bool AlreadyLoaded,
    int Places,
    int Joined,
    int Added,
    int References,
    int Unaddressed,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the second places source is already loaded"
            : $"{Places} places with {References} references — {Joined} of them joined onto a place " +
              $"the other dataset already had and {Added} new — and {Unaddressed} citations naming a " +
              $"verse the canonical frame does not hold, in {Elapsed}";
}

/// <summary>
/// OpenBible.info's Bible Geocoding: the place layer's second source, and the one that makes it a
/// layer at all.
///
/// BibleData's places are marked in progress by their own author and read that way: 118 places,
/// 492 verses, and every one of them in Genesis or Exodus, so Jerusalem's page said the text names
/// it once. This states 1,342 places and 8,742 place-verse references across 61 books, with
/// Jerusalem at 955.
///
/// **It is not a replacement.** 111 of BibleData's 118 places already carry OpenBible's identifier,
/// 109 of which OpenBible still has, and those join onto the entity that exists rather than
/// standing beside it as a second Jerusalem —
/// but the references themselves each carry the name of the dataset that stated them, so a count
/// is never a blend of two sources presented as one claim.
///
/// **CC BY 4.0**, which is why it and not the alternative. Theographic states 7,310 place-verse
/// references and is CC BY-SA 4.0; share-alike at that scale reaches everything built on top of
/// it, and it is that clause, not the non-commercial one, that bars a dataset here — this project
/// is not commercial, so NonCommercial does not. Measured against the King James text the corpus
/// already serves, of the 7,600 references OpenBible says the King James itself carries a name
/// for, 99.1% have that name in the verse; the 64 that do not were read one by one and every one
/// is a name too short for the check, a spelling the source records under a different heading, or
/// a psalm superscription the corpus's own King James text drops.
///
/// Only <c>ancient.jsonl</c> is read here. Where the places are is a separate step,
/// <see cref="OpenBibleLocationLoader"/>, because the coordinates are partly OpenStreetMap's and
/// carry ODbL, and only the points credited to someone else are held — see the LICENCE.md
/// kept beside the data.
/// </summary>
internal sealed partial class OpenBiblePlaceLoader(AppDbContext db, ILogger<OpenBiblePlaceLoader> logger)
{
    private const string Source =
        "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    private const string FileName = "ancient.jsonl";

    /// <summary>
    /// The source disambiguates same-named places by a trailing index — <em>Aroer 2</em>. That is
    /// an ordinal in its own catalogue and not part of the name, so it comes off; what tells the
    /// places apart on a page is the identification beside it.
    /// </summary>
    [GeneratedRegex(@"\s+\d+$")]
    private static partial Regex TrailingIndex();

    /// <summary>
    /// How the source says one entry is only another's other name: <em>another name for Mizpah
    /// 1</em>, <em>another name for the Arnon</em>. It names its target by the catalogue index this
    /// corpus does not keep, so what the phrase can be read for is that the entry claims no site of
    /// its own — not which entry it points at.
    /// </summary>
    private const string OtherName = "another name for";

    /// <summary>
    /// The id the source writes inside the phrase: <c>another name for &lt;ancient id="a6d57ed"&gt;Ramah
    /// 1&lt;/ancient&gt;</c>. It is the catalogue id <see cref="Entity.OpenBibleId"/> holds, so the target
    /// resolves exactly.
    /// </summary>
    [GeneratedRegex(@"^another name for\s+(?:the\s+)?<ancient id=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AnotherNameFor();

    /// <summary>
    /// Where the source puts a place, in its own words — <em>Tell es Sultan</em>, <em>Khirbet
    /// Rabud</em>, <em>between Dedan and Kedar</em> — or nothing where it says nothing.
    ///
    /// It is the one statement the gazetteer makes that tells two places of one name apart, and it
    /// is what <see cref="PlaceRegisterLoader"/> reads to decide whether a Strong number borne by
    /// several records is borne by several <em>places</em>. Two entries put at two sites are two
    /// places: Jericho at Tell es Sultan and Jericho at Tell el Alayiq are four kilometres apart.
    ///
    /// <para>
    /// Two things it looks like and is not. An entry the source only files under its own name and
    /// index — <em>Samaria</em>, <em>Samaria 2</em>, <em>Judea 1</em> — has not been placed
    /// anywhere; the identification is the catalogue entry echoed back. And an entry that says it
    /// is another name for something claims no site of its own either. Reading either as a site
    /// would make a city and the country called after it two places, which they are not.
    /// </para>
    ///
    /// <para>
    /// A record that is not a place has none at all, whatever its distinguisher holds. On a person
    /// that column is a life and not a location — <em>son of Nahor (GEN 11:24)</em> — and reading it
    /// as a site made the town named after Terah a second Terah and took 73 rows off his page.
    /// </para>
    /// </summary>
    internal static string? Site(Entity place)
    {
        if (place.Kind != EntityKind.Place)
        {
            return null;
        }

        var identification = place.ModernEquivalent ?? place.Distinguisher;
        if (string.IsNullOrWhiteSpace(identification))
        {
            return null;
        }

        if (place.AnotherNameForEntityId is not null
            || identification.StartsWith(OtherName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var stated = TrailingIndex().Replace(identification, string.Empty);
        return string.Equals(
            PlaceRegisterFiles.Normalise(stated),
            PlaceRegisterFiles.Normalise(place.Name),
            StringComparison.Ordinal)
            ? null
            : identification;
    }

    /// <summary>
    /// Descriptions carry inline markup naming other entries — <c>along the &lt;modern
    /// id="m664b51"&gt;Wadi el Esh&lt;/modern&gt;</c>. The corpus has no page for those ids yet, so
    /// the tags come out and the words stay.
    /// </summary>
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Markup();

    public async Task<PlacesOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var file = Path.Combine(folder, FileName);
        if (!File.Exists(file))
        {
            logger.LogWarning(
                "No second places source: {File} is not there. Run scripts/fetch-openbible.ps1. The " +
                "place layer stands on BibleData alone, which reaches Genesis and Exodus only.",
                file);
            return new PlacesOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var places = Read(file);

        if (await db.EntityVerses.AnyAsync(v => v.Source == Source, cancellationToken))
        {
            await NameTheAliases(places, cancellationToken);
            await Spell(places, cancellationToken);
            return new PlacesOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var byOpenBibleId = await db.Entities
            .Where(e => e.Kind == EntityKind.Place && e.OpenBibleId != null)
            .ToDictionaryAsync(e => e.OpenBibleId!, cancellationToken);
        var slugs = (await db.Entities.Select(e => e.Slug).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var shared = places
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        var added = new List<Entity>();
        var entities = new Dictionary<string, Entity>(places.Count, StringComparer.Ordinal);

        foreach (var place in places)
        {
            if (byOpenBibleId.TryGetValue(place.Id, out var existing))
            {
                entities[place.Id] = existing;
                continue;
            }

            var entity = new Entity
            {
                Kind = EntityKind.Place,
                Slug = Unique(Slugs.Of(place.Name), slugs),
                Name = place.Name,
                Distinguisher = shared.Contains(place.Name)
                    ? place.Identification ?? (place.AnotherNameFor is null ? null : OtherName)
                    : null,
                PlaceKind = place.Kind,
                ModernEquivalent = place.Identification == place.Name ? null : place.Identification,
                OpenBibleId = place.Id,
                SourceId = $"openbible:{place.Id}",
                Source = Source,
            };

            entities[place.Id] = entity;
            added.Add(entity);
        }

        db.Entities.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);

        var references = new List<EntityVerse>(9_000);
        var unaddressed = 0;
        var cited = new HashSet<(int, int, int, int)>();

        foreach (var place in places)
        {
            var entity = entities[place.Id];
            foreach (var citation in place.Verses)
            {
                if (Reference(citation) is not { } reference)
                {
                    unaddressed++;
                    continue;
                }

                if (!cited.Add((entity.Id, reference.Book, reference.Chapter, reference.Verse)))
                {
                    continue;
                }

                references.Add(new EntityVerse
                {
                    EntityId = entity.Id,
                    CanonicalBook = reference.Book,
                    CanonicalChapter = reference.Chapter,
                    CanonicalVerse = reference.Verse,
                    Disputed = false,
                    Source = Source,
                });
            }
        }

        db.EntityVerses.AddRange(references);
        await db.SaveChangesAsync(cancellationToken);
        await NameTheAliases(places, cancellationToken);
        await Spell(places, cancellationToken);

        if (unaddressed > 0)
        {
            logger.LogWarning(
                "{Rows} of the second places source's citations name a book the canonical frame does " +
                "not hold and were dropped.",
                unaddressed);
        }

        var outcome = new PlacesOutcome(
            false,
            places.Count,
            places.Count - added.Count,
            added.Count,
            references.Count,
            unaddressed,
            started.Elapsed);

        logger.LogInformation("Loaded the second places source: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Each place the gazetteer says is only another name for another, tied to the place it names by
    /// the id the source writes, and its line freed of the phrase: <em>Ramah 4</em> is another name
    /// for <em>Ramah 1</em>, and what its modern equivalent then says is where the gazetteer puts it,
    /// <em>Al Ram</em>, if it puts it anywhere; where it does not, the line under a shared name names
    /// the place instead of the catalogue index. A record whose target this corpus does not hold keeps the
    /// phrase, because the phrase is then the only way left to see that it claims no site. Only what
    /// differs is written, so a second load changes nothing.
    /// </summary>
    private async Task<int> NameTheAliases(List<Place> places, CancellationToken cancellationToken)
    {
        var aliases = places.Where(p => p.AnotherNameFor is not null).ToDictionary(p => p.Id, StringComparer.Ordinal);
        if (aliases.Count == 0)
        {
            return 0;
        }

        var wanted = aliases.Keys.Concat(aliases.Values.Select(a => a.AnotherNameFor!)).Distinct().ToList();
        var held = await db.Entities
            .Where(e => e.Kind == EntityKind.Place && e.OpenBibleId != null && wanted.Contains(e.OpenBibleId))
            .ToDictionaryAsync(e => e.OpenBibleId!, StringComparer.Ordinal, cancellationToken);

        var changed = 0;
        foreach (var (id, alias) in aliases)
        {
            if (!held.TryGetValue(id, out var entity)
                || !held.TryGetValue(alias.AnotherNameFor!, out var target)
                || target.Id == entity.Id)
            {
                continue;
            }

            var before = (entity.AnotherNameForEntityId, entity.ModernEquivalent, entity.Distinguisher);
            entity.AnotherNameForEntityId = target.Id;
            if (entity.ModernEquivalent?.StartsWith(OtherName, StringComparison.OrdinalIgnoreCase) == true)
            {
                entity.ModernEquivalent = alias.Identification == alias.Name ? null : alias.Identification;
            }

            if (entity.Distinguisher?.StartsWith(OtherName, StringComparison.OrdinalIgnoreCase) == true)
            {
                entity.Distinguisher = alias.Identification ?? Resolved(target);
            }

            if (before != (entity.AnotherNameForEntityId, entity.ModernEquivalent, entity.Distinguisher))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Aliases} places were tied to the place the gazetteer says they are another name for", changed);
        }

        return changed;
    }

    /// <summary>
    /// What tells an alias from its namesakes when the gazetteer gives it no site: the phrase with the
    /// place it names written out, <em>another name for Aroer (Khirbet Arair)</em>, where the
    /// source's own phrase named it by a catalogue index.
    /// </summary>
    private static string Resolved(Entity target) =>
        target.ModernEquivalent is { Length: > 0 } site && !site.StartsWith(OtherName, StringComparison.OrdinalIgnoreCase)
            ? $"{OtherName} {target.Name} ({site})"
            : $"{OtherName} {target.Name}";

    /// <summary>What a name row this source adds is: a spelling some English translation prints.</summary>
    internal const string SpellingKind = "spelling";

    /// <summary>
    /// Every spelling of each place an English translation prints, as a name of the place credited
    /// to this source: the King James writes <em>Tyrus</em>, <em>Zidon</em> and <em>Charchemish</em>,
    /// and a reader searching for the word on the page has to find the place. A spelling the place
    /// already answers to is not written again, and neither is a gentilic — <em>Tyrians</em> is the
    /// people of Tyre, not a name of the city — nor a word that is the name of another record: where
    /// a translation prints <em>Judah</em> or <em>Zion</em> for Jerusalem it is naming something
    /// else in its place, and a search for Judah has to find Judah. Written on every load, and only
    /// what is missing.
    /// </summary>
    private async Task<int> Spell(List<Place> places, CancellationToken cancellationToken)
    {
        var byOpenBibleId = await db.Entities
            .Where(e => e.Kind == EntityKind.Place && e.OpenBibleId != null)
            .Select(e => new { e.Id, e.Name, OpenBibleId = e.OpenBibleId! })
            .ToDictionaryAsync(e => e.OpenBibleId, cancellationToken);
        var held = (await db.EntityNames
                .Where(n => n.Entity!.Kind == EntityKind.Place)
                .Select(n => new { n.EntityId, n.Label })
                .ToListAsync(cancellationToken))
            .Select(n => (n.EntityId, n.Label.ToLowerInvariant()))
            .ToHashSet();
        var named = (await db.Entities.Select(e => new { e.Id, e.Name }).ToListAsync(cancellationToken))
            .Select(e => (e.Id, Name: e.Name.ToLowerInvariant()))
            .Concat((await db.EntityNames.Where(n => n.Kind != SpellingKind).Select(n => new { n.EntityId, n.Label })
                    .ToListAsync(cancellationToken))
                .Select(n => (Id: n.EntityId, Name: n.Label.ToLowerInvariant())))
            .ToLookup(e => e.Name, e => e.Id);

        var written = new List<EntityName>();
        foreach (var place in places)
        {
            if (!byOpenBibleId.TryGetValue(place.Id, out var entity))
            {
                continue;
            }

            foreach (var spelling in Spellings(place.Spellings, entity.Name))
            {
                var lower = spelling.ToLowerInvariant();
                if (named[lower].Any(id => id != entity.Id))
                {
                    continue;
                }

                if (held.Add((entity.Id, lower)))
                {
                    written.Add(new EntityName
                    {
                        EntityId = entity.Id, Label = spelling, Kind = SpellingKind, Source = Source,
                    });
                }
            }
        }

        if (written.Count > 0)
        {
            db.EntityNames.AddRange(written);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Spellings} spellings the translations print were added to the places", written.Count);
        }

        return written.Count;
    }

    /// <summary>The spellings worth a name row: not the place's own name, and not a gentilic.</summary>
    internal static IEnumerable<string> Spellings(IEnumerable<string> printed, string name) =>
        printed
            .Select(s => s.Trim())
            .Where(s => s.Length > 0 && !string.Equals(s, name, StringComparison.OrdinalIgnoreCase))
            .Where(s => !Gentilic().IsMatch(s));

    [GeneratedRegex(@"(ites|ians|eans|ines|ite|ian)$")]
    private static partial Regex Gentilic();

    /// <param name="Verses">
    /// Where the place is named, as the source addresses it. Its own frame is the ESV's, and every
    /// one of its citations lands on a verse this corpus already holds; the two it knows the King
    /// James numbers differently it says so about itself, and those are taken at the King James
    /// address because that is the numbering the canonical frame keeps.
    /// </param>
    internal sealed record Place(
        string Id,
        string Name,
        string? Identification,
        string? Kind,
        IReadOnlyList<string> Verses)
    {
        /// <summary>The catalogue id of the place this one is another name for, where the first line of the source says so.</summary>
        public string? AnotherNameFor { get; init; }

        /// <summary>What the English translations print for the place, from <c>translation_name_counts</c>.</summary>
        public IReadOnlyList<string> Spellings { get; init; } = [];
    }

    internal static List<Place> Read(string file)
    {
        var places = new List<Place>(1_400);

        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var id = Text(root, "id");
            var friendly = Text(root, "friendly_id");
            if (id is null || friendly is null)
            {
                continue;
            }

            places.Add(new Place(
                id,
                TrailingIndex().Replace(friendly, string.Empty),
                Identification(root),
                Kinds(root),
                [.. Citations(root)])
            {
                AnotherNameFor = Alias(root),
                Spellings = root.TryGetProperty("translation_name_counts", out var counts)
                            && counts.ValueKind == JsonValueKind.Object
                    ? [.. counts.EnumerateObject().Select(c => c.Name)]
                    : [],
            });
        }

        return places;
    }

    /// <summary>
    /// Where the scholarship puts the place, in the source's own words: <em>Khirbet Ayun Musa</em>,
    /// <em>between Dedan and Kedar</em>. The first line the dataset orders is the one taken, except
    /// that a line saying the entry is <em>another name for</em> another is not a place at all: that
    /// is <see cref="Alias"/>, and the list is read on to the first line that names a site. An entry
    /// that says nothing else has none.
    /// </summary>
    private static string? Identification(JsonElement root)
    {
        return Lines(root).FirstOrDefault(line => !line.StartsWith(OtherName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The catalogue id of the place the first line says this one is another name for.</summary>
    private static string? Alias(JsonElement root)
    {
        if (!root.TryGetProperty("identifications", out var identifications))
        {
            return null;
        }

        foreach (var identification in identifications.EnumerateArray())
        {
            if (Text(identification, "description") is { } description)
            {
                return AnotherNameFor().Match(description) is { Success: true } match ? match.Groups[1].Value : null;
            }
        }

        return null;
    }

    private static IEnumerable<string> Lines(JsonElement root)
    {
        if (!root.TryGetProperty("identifications", out var identifications))
        {
            yield break;
        }

        foreach (var identification in identifications.EnumerateArray())
        {
            if (Text(identification, "description") is { } description
                && Markup().Replace(description, string.Empty).Trim() is { Length: > 0 } stripped)
            {
                yield return stripped;
            }
        }
    }

    private static string? Kinds(JsonElement root)
    {
        if (!root.TryGetProperty("types", out var types) || types.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var kinds = string.Join(", ", types.EnumerateArray()
            .Select(type => type.GetString())
            .Where(type => !string.IsNullOrWhiteSpace(type)));

        return kinds.Length > 0 ? kinds : null;
    }

    /// <summary>
    /// The source states each citation in its own frame and, where a translation puts the words in
    /// a different verse, states that too. Two rows in the whole file carry a King James
    /// alternative and it is taken, because the canonical frame numbers verses as the King James
    /// does.
    /// </summary>
    private static IEnumerable<string> Citations(JsonElement root)
    {
        if (!root.TryGetProperty("verses", out var verses) || verses.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var verse in verses.EnumerateArray())
        {
            var citation = Text(verse, "osis");
            if (verse.TryGetProperty("alternate_verses", out var alternates)
                && alternates.ValueKind == JsonValueKind.Object
                && Text(alternates, "kjv") is { } instead)
            {
                citation = instead;
            }

            if (citation is not null)
            {
                yield return citation;
            }
        }
    }

    internal static (int Book, int Chapter, int Verse)? Reference(string citation)
    {
        var parts = citation.Split('.');
        if (parts.Length != 3
            || BibleBookAbbreviation.GetAbbreviation(parts[0]) is not { } book
            || !int.TryParse(parts[1], out var chapter)
            || !int.TryParse(parts[2], out var verse))
        {
            return null;
        }

        return (book.Ordinal, chapter, verse);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static string Unique(string slug, HashSet<string> taken)
    {
        var candidate = slug;
        var suffix = 2;
        while (!taken.Add(candidate))
        {
            candidate = $"{slug}-{suffix++}";
        }

        return candidate;
    }
}
