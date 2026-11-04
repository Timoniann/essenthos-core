using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

/// <summary>
/// The hand files no table holds: lists the Forge carries embedded until these files replace them.
/// The export writes them from the lists; the load checks them against the records it has just
/// loaded and writes them to no table, because the site reads none of them yet.
/// </summary>
internal static partial class RecordFiles
{
    private static RecordFile[] Lists =>
    [
        new("records/withdrawn", ProductKind.Hand,
            "slug dataset external_id why owner_ruled? own_record? by",
            "", "", [],
            About: Embedded("WithdrawnRecords.json").GetProperty("policy").GetString(),
            Listed: Withdrawn,
            Check: """
            SELECT count(*) FROM s
            WHERE NOT EXISTS (SELECT 1 FROM entity e WHERE e.slug = s.slug)
              AND NOT EXISTS (SELECT 1 FROM merged_record m WHERE m.slug = s.slug)
              AND s.dataset IN ('bibledata', 'openbible', 'essenthos')
            """,
            After: "CREATE TEMP TABLE withdrawn_slug ON COMMIT DROP AS SELECT slug FROM s"),

        new("records/held-apart", ProductKind.Hand,
            "one other why by",
            "", "", [],
            About: SenseReadingFiles.HeldApart().Policy,
            Listed: () => SenseReadingFiles.HeldApart().Pairs
                .Select(p => new string?[] { p.One, p.Other, p.Why, p.DecidedBy })
                .OrderBy(r => r[0], StringComparer.Ordinal).ThenBy(r => r[1], StringComparer.Ordinal),
            Check: "SELECT count(*) FROM s JOIN entity a ON a.slug = s.one JOIN entity b ON b.slug = s.other WHERE a.id <> b.id"),

        new("records/facts/withheld-clauses", ProductKind.Hand,
            "subject relation target ref:ref why by",
            "", "", [],
            About: OwnRelationshipLoader.WithheldClauses().Policy,
            Listed: WithheldClauses,
            Check: $"""
            SELECT count(*) FROM s JOIN entity a ON a.slug = s.subject JOIN entity b ON b.slug = s.target
            WHERE s.relation IN ({string.Join(", ", DescriptorRelations.All.Order(StringComparer.Ordinal).Select(r => $"'{r}'"))})
            """),

        new("records/folds-declined", ProductKind.Hand,
            "strong one other why by",
            "", "", [],
            About: "Pairs of records the audit of same-name records held might be one, and that do not hold up against their "
                   + "verses, or rest on a question the text leaves open: each stays a record of its own.",
            Listed: FoldsDeclined,
            Check: $"SELECT count(*) FROM s WHERE s.one <> s.other AND {Addressed("s.one")} AND {Addressed("s.other")}"),

        new("records/facts/location-supplements", ProductKind.Hand,
            "modern_id dataset site lon lat region_of? credit by",
            "", "", [],
            About: "Coordinates read from a named source other than the gazetteer, for sites whose gazetteer point cannot be "
                   + "held, and regions the gazetteer does not place; region_of names the record a region is the point of.",
            Listed: LocationSupplements,
            Check: """
            SELECT count(*) FROM s
            WHERE s.lon::double precision BETWEEN -180 AND 180 AND s.lat::double precision BETWEEN -90 AND 90 AND s.credit <> ''
              AND (s.region_of IS NULL OR EXISTS (SELECT 1 FROM entity e WHERE e.slug = s.region_of AND e.kind = 'place'))
            """),

        new("records/vocabulary", ProductKind.Hand,
            "domain term",
            "", "", [],
            About: "The closed vocabularies the facts are written in, one term per row: what a relation, a kind or a mark may be. "
                   + "Their words in a reader's language are the interface's until the API reads them from here.",
            Listed: Vocabulary,
            Check: "SELECT count(DISTINCT (domain, term)) FROM s"),
    ];

    /// <summary>
    /// Whether a slug is an address the corpus answers: a record, a record's old address after a fold,
    /// or a record withdrawn since a list named it.
    /// </summary>
    private static string Addressed(string slug) =>
        $"(EXISTS (SELECT 1 FROM entity e WHERE e.slug = {slug}) OR EXISTS (SELECT 1 FROM merged_record m WHERE m.slug = {slug}) " +
        $"OR EXISTS (SELECT 1 FROM withdrawn_slug w WHERE w.slug = {slug}))";

    private static IEnumerable<string?[]> Withdrawn()
    {
        var root = Embedded("WithdrawnRecords.json");
        var by = root.GetProperty("decidedBy").GetString();
        return WithdrawnRecordLoader.Read()
            .Select(r =>
            {
                var (dataset, id) = SourceIdOf(r.SourceId);
                return new string?[] { r.Slug, dataset, id, r.Why, r.OwnerRuled, r.OwnRecord, by };
            })
            .OrderBy(r => r[0], StringComparer.Ordinal);
    }

    /// <summary>
    /// A withdrawn record's old source id as a dataset and its id there: BibleData's keep their table
    /// (<c>person:Arad_1</c>), as in records/external-ids; <c>openbible:</c> and our own <c>essenthos:</c> lose their prefix.
    /// </summary>
    internal static (string Dataset, string Id) SourceIdOf(string sourceId)
    {
        var colon = sourceId.IndexOf(':');
        var prefix = colon < 0 ? "" : sourceId[..colon];
        return prefix is "openbible" or "essenthos" ? (prefix, sourceId[(colon + 1)..]) : ("bibledata", sourceId);
    }

    /// <summary>The old source id a withdrawn row stands for, the inverse of <see cref="SourceIdOf"/>.</summary>
    internal static string SourceIdFrom(string dataset, string id) => dataset == "bibledata" ? id : $"{dataset}:{id}";

    private static IEnumerable<string?[]> WithheldClauses()
    {
        var list = OwnRelationshipLoader.WithheldClauses();
        return list.Clauses
            .Select(c => new string?[] { c.Entity, c.Relation, c.Target, CanonicalVerse(c.Reference!), c.Why, list.DecidedBy })
            .OrderBy(r => r[0], StringComparer.Ordinal).ThenBy(r => r[1], StringComparer.Ordinal)
            .ThenBy(r => r[2], StringComparer.Ordinal).ThenBy(r => VerseKey.Parse(r[3]));
    }

    /// <summary>A citation of one verse in book codes (<c>1SA 9:1</c>) as its canonical key (<c>9.9:1</c>).</summary>
    internal static string CanonicalVerse(string citation)
    {
        var span = ScriptureSpan.Parse(citation);
        if (!span.IsVerse || span.ToChapter != span.FromChapter || span.ToVerse != span.FromVerse || span.Nth is not null)
        {
            throw new FormatException($"'{citation}' is not one verse; a key in a list names one.");
        }

        return new VerseKey(span.Book, span.FromChapter, span.FromVerse!.Value).ToString();
    }

    private static IEnumerable<string?[]> FoldsDeclined()
    {
        var root = Embedded("DuplicateRecords.json");
        var by = root.GetProperty("source").GetString();
        return root.GetProperty("declined").EnumerateArray()
            .Select(d =>
            {
                var pair = d.GetProperty("pair");
                return new string?[] { d.GetProperty("strongNumber").GetString(), pair[0].GetString(), pair[1].GetString(), d.GetProperty("why").GetString(), by };
            })
            .OrderBy(r => r[1], StringComparer.Ordinal).ThenBy(r => r[2], StringComparer.Ordinal).ThenBy(r => r[0], StringComparer.Ordinal);
    }

    private static IEnumerable<string?[]> LocationSupplements()
    {
        var by = Embedded("LocationSupplements.json").GetProperty("readBy").GetString();
        var supplement = LocationSupplement.Read();
        return supplement.Points
            .Select(p => new string?[] { p.ModernId, p.Dataset, p.Site, Number(p.Lon), Number(p.Lat), null, p.Credit, by })
            .Concat(supplement.Regions.Select(r => new string?[] { r.ModernId, r.Dataset, r.Site, Number(r.Lon), Number(r.Lat), r.Slug, r.Credit, by }))
            .OrderBy(r => r[0], StringComparer.Ordinal);
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The closed vocabularies, from the code tables that hold them today.</summary>
    internal static IEnumerable<string?[]> Vocabulary()
    {
        (string Domain, IEnumerable<string> Terms)[] domains =
        [
            ("relation", DescriptorRelations.All),
            ("thing-relation", ThingRelations.All),
            ("kin-statement-kind", KinStatementKinds.All),
            ("passage-role", PassageRoles.All),
            ("observance-cycle", ObservanceCycles.All),
            ("presence-category", PersonPresenceCategories),
            ("occupation-category", OccupationLoader.Categories),
            ("gentilic-kind", [Strong.GentilicKinds.Patronymic, Strong.GentilicKinds.Patrial, Strong.GentilicKinds.Either]),
            ("grammatical-case", GrammaticalCases.All),
            ("ruler-realm", RulerRealms.All),
            ("reign-role", ReignRoles.All),
            ("reign-statement-kind", ReignStatementKinds.All),
            ("reign-count", ReignCounts.All),
            ("reign-event-kind", ReignEventKinds.All),
            ("prophet-realm", ProphetRealms.All),
            ("prophet-field-kind", ProphetFieldKinds.All),
            ("ruler-mark", RulerMarks.All),
            ("verdict-basis", VerdictBases.All),
            ("verdict-witness", VerdictWitnesses.All),
            ("stated-age-kind", StatedAgeKinds.All),
        ];
        return domains
            .SelectMany(d => d.Terms.Distinct(StringComparer.Ordinal).Select(term => new string?[] { d.Domain, term }))
            .OrderBy(r => r[0], StringComparer.Ordinal).ThenBy(r => r[1], StringComparer.Ordinal);
    }

    /// <summary>The categories <see cref="PersonPresenceLoader"/> admits.</summary>
    private static readonly string[] PersonPresenceCategories = ["present", "buried"];

    private static JsonElement Embedded(string file)
    {
        var resource = $"Essenthos.Core.Loading.Encyclopedia.{file}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"{resource} is not embedded in the Forge assembly.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
