using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Claimed">
/// Places the encyclopedia already held that our own derivation reaches. The row stays and its
/// slug with it; what changes is who says it is here.
/// </param>
/// <param name="Added">Places the derivation reaches that no entity held.</param>
/// <param name="Untouched">
/// Places nothing of ours reaches, which keep exactly the provenance they had. This is the number
/// that says what the design costs: they are still the gazetteer's, and saying so is the honest
/// outcome.
/// </param>
/// <param name="Linked">
/// Records of ours that reach a place OpenBible surveyed, which is the link established by name
/// rather than inherited from where the row came from.
/// </param>
/// <param name="Named">Name rows written, each carrying the Strong number the record is made of.</param>
internal sealed record PlaceRegisterOutcome(
    bool AlreadyLoaded,
    int Entries,
    int Records,
    int Claimed,
    int Added,
    int Untouched,
    int Linked,
    int Named,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the place register is already there"
            : $"{Records} place records of {Entries} lexicon entries read — {Claimed} of them a " +
              $"place the encyclopedia already held, now here because Strong heads the name, and " +
              $"{Added} added — with {Linked} reaching a place OpenBible surveyed and {Named} name " +
              $"rows carrying the Strong number. {Untouched} held places nothing of ours reaches " +
              $"keep the provenance they had, in {Elapsed}";
}

/// <summary>
/// The places, as a record of ours rather than a list somebody lent us.
///
/// The encyclopedia held 1,351 places and 1,233 of them existed because OpenBible surveyed them:
/// a label, a coordinate and no Strong number, so nothing joined them to a word and there was no
/// index to disambiguate. This builds the index the way the peoples were built — out of Strong and
/// the occurrences already loaded — and then meets the gazetteer with it.
///
/// <para>
/// <strong>What the register is.</strong> Every entry of the lexicon that the pass in
/// <c>scripts/places.py</c> considered, with the four witnesses that decided it: the part of speech
/// Strong assigns, the gloss he writes, the name type BHSA marks on the word, and two readings of
/// the entry where the first three left it open. The decision is made there, where it was measured,
/// and carried here as a flag and a sentence — a rule stated in two places is a rule that disagrees
/// with itself.
/// </para>
///
/// <para>
/// <strong>What it does to a row that already exists.</strong> Where our derivation reaches a place
/// the encyclopedia holds, the row stays and its slug stays with it, because every verse, every
/// relationship, every descriptor and the reader's own URLs key on exactly that. What changes is
/// <see cref="Entity.Source"/>: the place is here because these Strong occurrences name it, and the
/// gazetteer's testimony moves to a claim beside ours rather than standing as the reason the record
/// exists. <see cref="Entity.OpenBibleId"/> is untouched — it is how coordinates are reached, and
/// they stay OpenBible's and credited to them.
/// </para>
///
/// <para>
/// <strong>What it does to a row nothing of ours reaches.</strong> Nothing. It keeps exactly the
/// provenance it has, and the count of those is what the design costs: a lexicon-built register
/// does not reach a gazetteer descriptor like <em>Beautiful Gate</em> or a classical site like
/// Carthage, and pretending otherwise by folding the spelling would put a wrong coordinate on a
/// page. An unlinked record is an honest gap; a guessed link is not.
/// </para>
///
/// <para>
/// The match is strict: the name normalised for case, hyphen, accent and ligature, and the
/// gazetteer's feature word — <em>Mount</em> Gilboa, <em>Valley of</em> Eshcol — read off the front
/// as the naming convention it is. Nothing is folded to consonants. That fold reaches 90% instead
/// of 75% and joins <em>Sion</em> to <em>Zoan</em> and <em>Gomorrha</em> to <em>Moreh</em> on the
/// way, which is a wrong coordinate on a page rather than a missing one.
/// </para>
///
/// <para>
/// <strong>The spelling is not the only way to a held place.</strong> Seventeen records met none
/// by it and were written as second pages for a place the corpus already had — <em>Beth-baal-meon</em>
/// beside <em>Beth-meon</em>, <em>Tipsah</em> beside <em>Tiphsah</em>, a doubled vowel or a
/// <em>ts</em> for a <em>z</em> apart. That is worse than an extra page: the number then names two
/// records, which is exactly what <see cref="EntityAnnotationLoader"/> refuses to resolve, so the
/// page that was working loses its annotations too. So where the spelling reaches nothing, the
/// number is asked — <see cref="EntityCandidates.Derived"/>, the corpus's own reading of which
/// place a Hebrew number names, which is the same rule that would afterwards refuse the pair.
/// </para>
///
/// <para>
/// <strong>One name, several records, and not always several places.</strong> A number the register
/// writes onto more than one record is two situations and the corpus must not treat them as one.
/// Zion the settlement and Mount Zion its hill are one place seen twice; Samaria the city and
/// Samaria the country called after it likewise; so are Egypt and the brook, the sea and the river
/// that carry its name, and Edom and Idumea, and Jerusalem and Salem. Jericho at Tell es Sultan and
/// Jericho at Tell el Alayiq are two towns four kilometres apart. The first must resolve, to the
/// place itself, and the second must not resolve at all — and only the gazetteer can tell them
/// apart, so <see cref="Aspects"/> asks it. What that decides is written on the name row as
/// <see cref="EntityName.AspectOfEntityId"/>, which is where every later pass reads it.
/// </para>
///
/// <para>
/// Idempotent on the claim this pass writes, which is the one thing that cannot be true before it
/// has run.
/// </para>
/// </summary>
internal sealed class PlaceRegisterLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<PlaceRegisterLoader> logger)
{
    /// <summary>
    /// What a record made from the lexicon says about itself. The record is ours — Strong wrote a
    /// dictionary of words and not a gazetteer — and the entries it is made of are his.
    /// </summary>
    private const string FromTheLexicon =
        "Essenthos, from the place names Strong's Dictionary heads";

    /// <summary>
    /// What establishes a record, and why it is an inference rather than testimony.
    ///
    /// Nobody states that H1035 is a place; it is concluded from the part of speech, the gloss, the
    /// name type BHSA marks and, where those left it open, two readings of the entry. That is a
    /// conclusion and it carries a number, because this corpus refuses to store an inference that
    /// looks like a dictionary's statement.
    /// </summary>
    private const LinkMethod ByTheEntry = LinkMethod.Lexical;

    /// <summary>
    /// The room the free pass leaves. Two of Strong's own fields agreeing, with BHSA free to
    /// contradict either, is as close to settled as a lexicon gets — and what is short of certainty
    /// is that a dictionary of words is not a survey of places.
    /// </summary>
    private const double Settled = 0.95;

    /// <summary>
    /// The room a record admitted by a reading leaves. Lower on purpose: the free pass could not
    /// settle it, and what carries it is two readings of one sentence.
    /// </summary>
    private const double Read = 0.85;

    private const string SourceIdPrefix = "essenthos:";

    /// <summary>The kind of label a name row made from a lexicon headword is.</summary>
    private const string LexiconName = "name";

    /// <summary>
    /// Entries whose place the corpus already holds under a spelling neither route can meet, by
    /// Strong number, with the held record's slug and the reason they are one place.
    ///
    /// The number cannot reach it either: the gazetteer names the town as the modern translations
    /// print it and the King James prints it another way, so the rendering the number is read
    /// through never spells the gazetteer's name. Asked first, because it is a ruling on this entry
    /// and not a guess the spelling has to confirm.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (string Slug, string Why)> SamePlace =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["H77"] = ("ebez",
                "One town of Issachar, named once, at JOS 19:20. Strong heads the word אֶבֶץ as Ebets "
                + "and the King James prints it Abez; the gazetteer surveys it at the same verse as "
                + "Ebez, the spelling of the modern translations, and records the King James's Abez "
                + "among the names the translations give it."),
        };

    public async Task<PlaceRegisterOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var directory = configuration[PlaceRegisterFiles.ConfigurationKey] is { Length: > 0 } set
            ? set
            : Path.Combine(resources, PlaceRegisterFiles.DefaultFolder);

        if (!Directory.Exists(directory))
        {
            logger.LogWarning(
                "No place register at {Directory}, so the places stay the gazetteer's. Produce it "
                + "with \"python scripts/places.py register\" and \"publish\", or point "
                + "{Key} at a folder that holds it",
                directory,
                PlaceRegisterFiles.ConfigurationKey);
            return new PlaceRegisterOutcome(true, 0, 0, 0, 0, 0, 0, 0, started.Elapsed);
        }

        if (await db.EntityClaims.AnyAsync(c => c.Source == FromTheLexicon, cancellationToken))
        {
            logger.LogInformation("The place register is already there; nothing to do");
            return new PlaceRegisterOutcome(true, 0, 0, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var entries = PlaceRegisterFiles.Read(directory);
        var records = entries.Where(record => record.Kept).ToList();
        if (records.Count == 0)
        {
            logger.LogWarning(
                "The place register at {Directory} holds {Entries} entries and no records. Either "
                + "the reading pass has not run or every entry was refused; check "
                + "\"python scripts/places.py register\"",
                directory,
                entries.Count);
            return new PlaceRegisterOutcome(false, entries.Count, 0, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var held = await db.Entities
            .Where(e => e.Kind == EntityKind.Place)
            .Include(e => e.Names)
            .ToListAsync(cancellationToken);

        var byName = Index(held);
        var byNumber = await Derived(held, cancellationToken);
        var bySlug = held.ToDictionary(place => place.Slug, StringComparer.Ordinal);
        var bearers = await Bearers(cancellationToken);
        var slugs = (await db.Entities.Select(e => e.Slug).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var made = new Dictionary<Entity, List<PlaceRegisterRecord>>();
        var added = new List<Entity>();
        var named = 0;

        foreach (var record in records)
        {
            var hits = Reach(record, byName, byNumber, bySlug);
            if (hits.Count == 0)
            {
                // Indexed as it is created, so that a second entry bearing the same name joins it
                // rather than standing beside it. Judah is one place and two lexicon entries, his
                // and the Greek's, and two pages for it would be this pass's own doing.
                var entity = new Entity
                {
                    Kind = EntityKind.Place,
                    Slug = Unique(Slugs.Of(record.Name), slugs),
                    Name = record.Name,
                    SourceId = SourceIdPrefix + record.Number.ToLowerInvariant(),
                    Source = FromTheLexicon,
                };

                added.Add(entity);
                Index(byName, entity, [record.Name]);

                var met = new Spelling(entity);
                met.Met(false);
                hits = [met];
            }

            var principal = Aspects(record, hits, bearers, byNumber);

            foreach (var hit in hits.Select(spelling => spelling.Place))
            {
                if (!made.TryGetValue(hit, out var already))
                {
                    made[hit] = already = [];
                }

                already.Add(record);

                if (!hit.Names.Any(name => Carries(name, record.Number)))
                {
                    hit.Names.Add(Name(record, principal == hit ? null : principal));
                    named++;
                }
            }
        }

        foreach (var (entity, from) in made)
        {
            if (entity.OpenBibleId is { Length: > 0 } surveyed
                && !string.Equals(entity.Source, FromTheLexicon, StringComparison.Ordinal))
            {
                entity.Claims.Add(new EntityClaim
                {
                    Method = LinkMethod.StatedBySource,
                    Confidence = null,
                    Source = entity.Source,
                    Note = $"surveyed as {surveyed}, which is where this record's coordinates come "
                           + "from and whose they stay",
                });
            }

            entity.Source = FromTheLexicon;
            entity.Claims.Add(Claim(from));
        }

        db.Entities.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);

        var claimed = made.Keys.Except(added).ToList();

        var outcome = new PlaceRegisterOutcome(
            false,
            entries.Count,
            records.Count,
            claimed.Count,
            added.Count,
            held.Count - claimed.Count,
            claimed.Count(e => e.OpenBibleId is { Length: > 0 }),
            named,
            started.Elapsed);

        logger.LogInformation("Named the places: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// One held place under one spelling, and whether the spelling is a label it carries or that
    /// label with the gazetteer's feature word taken off the front.
    ///
    /// The difference is the whole of what tells a place from its own hill. <em>Mount Zion</em>
    /// meets <em>Zion</em> only once <em>Mount</em> is off, and the Nile meets <em>Egypt</em> only
    /// once <em>River of</em> is; Zion and Egypt meet it as they are spelled. That asymmetry is the
    /// gazetteer stating a naming convention, and it is the same statement the index is built on.
    /// </summary>
    private sealed class Spelling(Entity place)
    {
        public Entity Place { get; } = place;

        public bool ByFeature { get; private set; } = true;

        public void Met(bool feature) => ByFeature &= feature;
    }

    /// <summary>
    /// Every held place under every spelling of it a lexicon entry could meet — its own name, the
    /// labels its name rows carry, and each of those with the gazetteer's feature word off the
    /// front.
    /// </summary>
    private static Dictionary<string, List<Spelling>> Index(IEnumerable<Entity> held)
    {
        var index = new Dictionary<string, List<Spelling>>(StringComparer.Ordinal);
        foreach (var place in held)
        {
            Index(index, place, place.Names.Select(name => name.Label).Append(place.Name));
        }

        return index;
    }

    private static void Index(
        Dictionary<string, List<Spelling>> index,
        Entity place,
        IEnumerable<string> labels)
    {
        foreach (var (key, feature) in labels
                     .SelectMany(label => PlaceRegisterFiles.Forms(label)
                         .Select((form, position) => (Form: form, Feature: position > 0)))
                     .Select(form => (Key: PlaceRegisterFiles.Normalise(form.Form), form.Feature))
                     .Where(form => form.Key.Length > 0))
        {
            if (!index.TryGetValue(key, out var at))
            {
                index[key] = at = [];
            }

            var already = at.FirstOrDefault(spelling => spelling.Place == place);
            if (already is null)
            {
                already = new Spelling(place);
                at.Add(already);
            }

            already.Met(feature);
        }
    }

    /// <summary>
    /// The held places one record reaches. Several is not a fault: Strong heads <em>Aroer</em> once
    /// and the gazetteer surveys three of them, and which occurrence is which is the namesake pass.
    /// What the record states here is about the name.
    ///
    /// <para>
    /// The spelling is asked first and the number only where it answers nothing. Where both answer,
    /// the spelling is the finer statement — it is about this entry and this label, where the number
    /// is about every place the corpus reads it onto — and taking the union would put a record on
    /// each of the three Mizpahs merely because it met one of them by name.
    /// </para>
    /// </summary>
    private static List<Spelling> Reach(
        PlaceRegisterRecord record,
        Dictionary<string, List<Spelling>> byName,
        Dictionary<string, List<Entity>> byNumber,
        Dictionary<string, Entity> bySlug)
    {
        var hits = new List<Spelling>();
        if (SamePlace.TryGetValue(record.Number, out var ruled) && bySlug.TryGetValue(ruled.Slug, out var same))
        {
            var met = new Spelling(same);
            met.Met(false);
            hits.Add(met);
            return hits;
        }

        foreach (var name in (record.Names ?? []).Append(record.Name))
        {
            if (!byName.TryGetValue(PlaceRegisterFiles.Normalise(name), out var at))
            {
                continue;
            }

            foreach (var met in at)
            {
                var already = hits.FirstOrDefault(hit => hit.Place == met.Place);
                if (already is null)
                {
                    already = new Spelling(met.Place);
                    hits.Add(already);
                }

                already.Met(met.ByFeature);
            }
        }

        if (hits.Count == 0 && byNumber.TryGetValue(record.Number, out var read))
        {
            foreach (var place in read)
            {
                var met = new Spelling(place);
                met.Met(false);
                hits.Add(met);
            }
        }

        return hits;
    }

    /// <summary>
    /// The record a number's name is that of, where the records bearing it are one place; nothing,
    /// where they are several.
    ///
    /// <para>
    /// <strong>Are they one place?</strong> The gazetteer is asked, and it answers in two ways. It
    /// files a place's hill, its valley, its waters and its river under the place's own name with a
    /// feature word in front — <em>Mount Zion</em>, <em>Valley of Jericho</em>, <em>Brook of
    /// Egypt</em>, <em>River of Egypt</em> — so a record met only once that word is off is that
    /// place seen another way and not a rival to it. Of what is left it states, for each entry, the
    /// site it puts it at: <em>Tell es Sultan</em> against <em>Tell el Alayiq</em>, <em>Jel'ad</em>
    /// against <em>Tell en Nasbeh</em>. <strong>Two sites are two places</strong>, and the number
    /// then resolves to neither.
    /// </para>
    ///
    /// <para>
    /// Silence is not a site — see <see cref="OpenBiblePlaceLoader.Site"/> — and neither is the
    /// catalogue entry echoed back, which is why Samaria the city and Samaria the country are one
    /// place, nor a note that the entry is another name for something, which is why the stone heap
    /// called Mizpah does not make a third Mizpah beside the two the gazetteer does place.
    /// </para>
    ///
    /// <para>
    /// <strong>Which record, then?</strong> Not a choice this pass makes. It is the one the corpus
    /// already reads the number onto — a name row the encyclopedia states, or the King James word
    /// that renders it, which is <see cref="Derived"/>. Where those two name exactly one record,
    /// that record is the name's; where they name several, or none, this pass has nothing better
    /// than a spelling to go on and says nothing.
    /// </para>
    ///
    /// <para>
    /// <strong>A place, and never a person.</strong> Strong heads one entry for the man Jephthah
    /// and the town named after him, and the encyclopedia holds the man. A place is not an aspect of
    /// a person however it came by its name, and the two are told apart by BHSA's marking of the
    /// word or, in the Greek, by which of them the witnesses reach at all — neither of which this
    /// pass may pre-empt. It cost Cos thirteen words, Ephraim eleven and Judah eighty-four when it
    /// did: the Greek resolves each of those by ruling out an Old Testament man the New Testament
    /// never names, and a record deferred to him has nothing left to rule out.
    /// </para>
    ///
    /// <para>
    /// What the gazetteer cannot see, this cannot see either. It places one Bethlehem and not the
    /// other, so it does not say that the Bethlehem of Judah and the Bethlehem of Zebulun are two;
    /// where it places neither of two records, as with the two Selas, it says nothing at all. Those
    /// resolve, and they resolve to the record the corpus was already reading the number onto,
    /// which is the answer that stood before this register existed.
    /// </para>
    /// </summary>
    private static Entity? Aspects(
        PlaceRegisterRecord record,
        IReadOnlyList<Spelling> hits,
        IReadOnlyDictionary<string, List<Entity>> bearers,
        IReadOnlyDictionary<string, List<Entity>> byNumber)
    {
        if (hits.Count == 1 && !bearers.ContainsKey(record.Number))
        {
            return hits[0].Place;
        }

        var held = bearers.TryGetValue(record.Number, out var stated) ? stated : [];
        var named = hits
            .Where(hit => !hit.ByFeature)
            .Select(hit => hit.Place)
            .Concat(held)
            .Distinct()
            .ToList();

        var sites = named
            .Select(OpenBiblePlaceLoader.Site)
            .OfType<string>()
            .Select(PlaceRegisterFiles.Normalise)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count();

        if (sites > 1)
        {
            return null;
        }

        var read = byNumber.TryGetValue(record.Number, out var derived) ? derived : [];
        var candidates = held.Concat(read)
            .Distinct()
            .Intersect(named)
            .Where(candidate => candidate.Kind == EntityKind.Place)
            .Take(2)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// Every record the encyclopedia already names by a Strong number, before this pass writes one.
    ///
    /// Not only the places: the rival to a place the register writes is as often a person, because
    /// Strong heads one entry for the man Jephthah and the town named after him, and the man is
    /// already on a page with the number on it. A rule that looked only at places would make that
    /// number name two records and take the man's twenty-nine words off his page.
    /// </summary>
    private async Task<Dictionary<string, List<Entity>>> Bearers(CancellationToken cancellationToken)
    {
        var rows = await db.EntityNames
            .Where(n => n.HebrewStrongNumber != null || n.GreekStrongNumber != null)
            .Select(n => new
            {
                n.HebrewStrongNumber,
                n.GreekStrongNumber,
                n.EntityId,
            })
            .ToListAsync(cancellationToken);

        var ids = rows.Select(row => row.EntityId).ToHashSet();
        var byId = await db.Entities
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var bearers = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var number in new[] { row.HebrewStrongNumber, row.GreekStrongNumber })
            {
                if (number is not { Length: > 0 } || !byId.TryGetValue(row.EntityId, out var entity))
                {
                    continue;
                }

                if (!bearers.TryGetValue(number, out var at))
                {
                    bearers[number] = at = [];
                }

                if (!at.Contains(entity))
                {
                    at.Add(entity);
                }
            }
        }

        return bearers;
    }

    /// <summary>
    /// Which held place the corpus itself reads each Hebrew number onto, from
    /// <see cref="EntityCandidates.Derived"/> — the geocoding dataset saying a place is named in a
    /// verse, the King James printing that place's name at a word, and BHSA giving that word a
    /// number. It is the same statement the annotation pass is about to be asked, and asking it
    /// here is what stops this pass writing the record that makes it unanswerable.
    ///
    /// <para>
    /// Hebrew only, because the join runs through BHSA's marking of a name and nothing marks a Greek
    /// one. The Greek side has no such duplicate to find: the encyclopedia's Greek numbers reach the
    /// same records the spelling does.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, List<Entity>>> Derived(
        IReadOnlyList<Entity> held,
        CancellationToken cancellationToken)
    {
        var byId = held.ToDictionary(place => place.Id);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(EntityCandidates.Derived, connection);
        command.Parameters.AddWithValue("witness", EntityCandidates.Witness);
        command.Parameters.AddWithValue("rendering", EntityCandidates.Rendering);

        var read = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!byId.TryGetValue(reader.GetInt32(1), out var place))
            {
                continue;
            }

            if (!read.TryGetValue(reader.GetString(0), out var places))
            {
                read[reader.GetString(0)] = places = [];
            }

            places.Add(place);
        }

        return read;
    }

    /// <summary>
    /// One claim per record, naming every entry it was reached by. One row rather than one per
    /// entry because a claim is unique on the entity, the method and the source — and because what
    /// a reader is owed is the whole reason, not one of its halves.
    /// </summary>
    private static EntityClaim Claim(IReadOnlyList<PlaceRegisterRecord> made) =>
        new()
        {
            Method = ByTheEntry,
            Confidence = made.All(record => record.Tier is "read" or "common") ? Read : Settled,
            Source = FromTheLexicon,
            Note = string.Join("; ", made.Select(record =>
                $"{record.Number} \"{record.Definition}\" — {record.Why}")),
        };

    /// <summary>
    /// The name row a record writes, carrying the number it is made of and — for a Greek entry —
    /// the lexicon's own spelling of that name.
    ///
    /// <para>
    /// The spelling is not decoration. <see cref="EntityAnnotationLoader"/> takes a Greek number
    /// only where the encyclopedia's own spelling of the name is one some Greek text writes under
    /// it, which is the gate that refuses Ἰωδά the number of Ἰούδας; with the column null there is
    /// nothing to compare and the number is refused. Eight records were refused for exactly that,
    /// Judaea's 173 occurrences among them. Recording the lemma is a statement of what the entry
    /// says rather than a way past the gate: the record exists because of that entry and no other,
    /// and the gate still asks the texts.
    /// </para>
    ///
    /// <para>
    /// The transliteration is deliberately not recorded with it. Nothing reads it as a spelling of
    /// the name — <see cref="Annotating"/> reads it as one of the forms a translated word may be
    /// moved onto, and Strong's Latin transliteration of a Greek name is a spelling no text of this
    /// corpus prints.
    /// </para>
    /// </summary>
    private static EntityName Name(PlaceRegisterRecord record, Entity? aspectOf) =>
        new()
        {
            Label = record.Name,
            Kind = LexiconName,
            HebrewStrongNumber = record.Number.StartsWith('H') ? record.Number : null,
            GreekStrongNumber = record.Number.StartsWith('G') ? record.Number : null,
            Greek = record.Number.StartsWith('G') ? Blank(record.Lemma) : null,
            AspectOf = aspectOf,
        };

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Carries(EntityName name, string number) =>
        string.Equals(name.HebrewStrongNumber, number, StringComparison.Ordinal)
        || string.Equals(name.GreekStrongNumber, number, StringComparison.Ordinal);

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
