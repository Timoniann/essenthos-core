using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

internal static partial class RecordFiles
{
    private static RecordFile[] Reigns =>
    [
        new("records/facts/reigns", ProductKind.Hand,
            "ruler realm position:int period? drawn_under? shared:bool fallback:bool by",
            """
            SELECT e.slug, r.realm, r.position, p.slug, r.drawn_under, r.shared, r.fallback, r.source
            FROM ruler_reign r JOIN entity e ON e.id = r.entity_id LEFT JOIN period p ON p.id = r.period_id ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO ruler_reign (entity_id, realm, position, period_id, drawn_under, shared, fallback, source)
            SELECT e.id, s.realm, s.position, p.id, s.drawn_under, s.shared, s.fallback, s.by
            FROM s JOIN entity e ON e.slug = s.ruler LEFT JOIN period p ON p.slug = s.period
            WHERE s.period IS NULL OR p.id IS NOT NULL ORDER BY s.ruler, s.realm, s.position
            """,
            ["ruler_reign", "reign_statement", "reign_length", "reign_event", "prophet_field", "throne_name", "ruler_verdict", "stated_age"]),
        new("records/facts/reign-statements", ProductKind.Hand,
            "person ruler role kind ref:ref year:int? counted_from? through? end_verse:int? by",
            $"""
            SELECT e.slug, r.slug, x.role, x.kind, {Ref("x")}, x.year, x.counted_from, t.slug, x.end_verse, x.source
            FROM reign_statement x JOIN entity e ON e.id = x.entity_id JOIN entity r ON r.id = x.ruler_entity_id
            LEFT JOIN entity t ON t.id = x.through_entity_id ORDER BY 1, 2, 3, 4, 5, 6, 7, 8
            """,
            """
            INSERT INTO reign_statement (entity_id, ruler_entity_id, role, kind, canonical_book, canonical_chapter, canonical_verse, year,
                                         counted_from, through_entity_id, end_verse, source)
            SELECT e.id, r.id, s.role, s.kind, s.ref[1], s.ref[2], s.ref[3], s.year, s.counted_from, t.id, s.end_verse, s.by
            FROM s JOIN entity e ON e.slug = s.person JOIN entity r ON r.slug = s.ruler LEFT JOIN entity t ON t.slug = s.through
            WHERE s.through IS NULL OR t.id IS NOT NULL ORDER BY s.person, s.ruler, s.role, s.kind, s.ref, s.year, s.counted_from, s.through
            """,
            []),
        new("records/facts/reign-lengths", ProductKind.Hand,
            "ruler ref:ref years:int? months:int? days:int? by",
            $"SELECT e.slug, {Ref("x")}, x.years, x.months, x.days, x.source FROM reign_length x JOIN entity e ON e.id = x.entity_id ORDER BY 1, 2, 3, 4, 5",
            """
            INSERT INTO reign_length (entity_id, canonical_book, canonical_chapter, canonical_verse, years, months, days, source)
            SELECT e.id, s.ref[1], s.ref[2], s.ref[3], s.years, s.months, s.days, s.by FROM s JOIN entity e ON e.slug = s.ruler
            ORDER BY s.ruler, s.ref, s.years, s.months, s.days
            """,
            []),
        new("records/facts/reign-events", ProductKind.Hand,
            "slug kind realm position:int event? ruler year:int? ref:ref end_verse:int? by",
            $"""
            SELECT x.slug, x.kind, x.realm, x.position, v.slug, e.slug, x.year, {Ref("x")}, x.end_verse, x.source
            FROM reign_event x JOIN entity e ON e.id = x.ruler_entity_id LEFT JOIN event v ON v.id = x.timeline_event_id ORDER BY 1
            """,
            """
            INSERT INTO reign_event (slug, kind, realm, position, timeline_event_id, ruler_entity_id, year, canonical_book, canonical_chapter, canonical_verse, end_verse, source)
            SELECT s.slug, s.kind, s.realm, s.position, v.id, e.id, s.year, s.ref[1], s.ref[2], s.ref[3], s.end_verse, s.by
            FROM s JOIN entity e ON e.slug = s.ruler LEFT JOIN event v ON v.slug = s.event WHERE s.event IS NULL OR v.id IS NOT NULL ORDER BY s.slug
            """,
            []),
        new("records/facts/prophet-fields", ProductKind.Hand,
            "prophet realm kind position:int place? ref:ref end_verse:int? by",
            $"""
            SELECT e.slug, x.realm, x.kind, x.position, p.slug, {Ref("x")}, x.end_verse, x.source
            FROM prophet_field x JOIN entity e ON e.id = x.entity_id LEFT JOIN entity p ON p.id = x.place_entity_id ORDER BY 1, 2, 3, 4
            """,
            """
            INSERT INTO prophet_field (entity_id, realm, kind, position, place_entity_id, canonical_book, canonical_chapter, canonical_verse, end_verse, source)
            SELECT e.id, s.realm, s.kind, s.position, p.id, s.ref[1], s.ref[2], s.ref[3], s.end_verse, s.by
            FROM s JOIN entity e ON e.slug = s.prophet LEFT JOIN entity p ON p.slug = s.place WHERE s.place IS NULL OR p.id IS NOT NULL
            ORDER BY s.prophet, s.realm, s.kind, s.position
            """,
            []),
        .. Languages.Select(language => new RecordFile($"records/i18n/{language}/throne-names", ProductKind.Hand,
            "ruler ref:ref name by",
            $"""
            SELECT e.slug, {Ref("x")}, x.name, x.source FROM throne_name x JOIN entity e ON e.id = x.entity_id
            WHERE x.language = '{language}' ORDER BY 1, 2, 3
            """,
            $"""
            INSERT INTO throne_name (entity_id, language, name, canonical_book, canonical_chapter, canonical_verse, source)
            SELECT e.id, '{language}', s.name, s.ref[1], s.ref[2], s.ref[3], s.by FROM s JOIN entity e ON e.slug = s.ruler ORDER BY s.ruler, s.ref, s.name
            """,
            [])),
        new("records/facts/verdicts", ProductKind.Hand,
            "king mark by",
            "SELECT e.slug, x.mark, x.source FROM ruler_verdict x JOIN entity e ON e.id = x.entity_id ORDER BY 1, 2",
            "INSERT INTO ruler_verdict (entity_id, mark, source) SELECT e.id, s.mark, s.by FROM s JOIN entity e ON e.slug = s.king ORDER BY s.king, s.mark",
            []),
        new("records/facts/verdict-witnesses", ProductKind.Hand,
            "king position:int witness mark basis",
            """
            SELECT e.slug, w.position, w.witness, w.mark, w.basis FROM ruler_verdict_witness w JOIN ruler_verdict v ON v.id = w.verdict_id
            JOIN entity e ON e.id = v.entity_id ORDER BY 1, 2
            """,
            """
            INSERT INTO ruler_verdict_witness (verdict_id, position, witness, mark, basis)
            SELECT v.id, s.position, s.witness, s.mark, s.basis FROM s JOIN entity e ON e.slug = s.king JOIN ruler_verdict v ON v.entity_id = e.id
            ORDER BY s.king, s.position
            """,
            []),
        new("records/facts/verdict-passages", ProductKind.Hand,
            "king witness_position:int position:int ref:ref end_verse:int?",
            $"""
            SELECT e.slug, w.position, p.position, {Ref("p")}, p.end_verse FROM ruler_verdict_passage p
            JOIN ruler_verdict_witness w ON w.id = p.witness_id JOIN ruler_verdict v ON v.id = w.verdict_id JOIN entity e ON e.id = v.entity_id
            ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO ruler_verdict_passage (witness_id, position, canonical_book, canonical_chapter, canonical_verse, end_verse)
            SELECT w.id, s.position, s.ref[1], s.ref[2], s.ref[3], s.end_verse
            FROM s JOIN entity e ON e.slug = s.king JOIN ruler_verdict v ON v.entity_id = e.id
            JOIN ruler_verdict_witness w ON w.verdict_id = v.id AND w.position = s.witness_position ORDER BY s.king, s.witness_position, s.position
            """,
            []),
        new("records/facts/stated-ages", ProductKind.Hand,
            "person kind position:int years:int about:bool ref:ref by",
            $"SELECT e.slug, x.kind, x.position, x.years, x.about, {Ref("x")}, x.source FROM stated_age x JOIN entity e ON e.id = x.entity_id ORDER BY 1, 2, 3",
            """
            INSERT INTO stated_age (entity_id, kind, position, years, about, canonical_book, canonical_chapter, canonical_verse, source)
            SELECT e.id, s.kind, s.position, s.years, s.about, s.ref[1], s.ref[2], s.ref[3], s.by FROM s JOIN entity e ON e.slug = s.person
            ORDER BY s.person, s.kind, s.position
            """,
            []),
    ];

    private static RecordFile[] Derived =>
    [
        new("records/facts/images", ProductKind.Machine,
            "slug kind role ordinal:int file digest width:int height:int credit credit_url? licence licence_url? source focus_x? focus_y? bust_x? bust_y? bust_width? bust_height?",
            """
            SELECT e.slug, i.kind, i.role, i.ordinal, i.file, i.digest, i.width, i.height, i.credit, i.credit_url, i.licence, i.licence_url, i.source,
                   i.focus_x::text, i.focus_y::text, i.bust_x::text, i.bust_y::text, i.bust_width::text, i.bust_height::text
            FROM entity_image i JOIN entity e ON e.id = i.entity_id ORDER BY 1, 2, 3, 4
            """,
            """
            INSERT INTO entity_image (entity_id, kind, role, ordinal, file, digest, width, height, credit, credit_url, licence, licence_url, source,
                                      focus_x, focus_y, bust_x, bust_y, bust_width, bust_height)
            SELECT e.id, s.kind, s.role, s.ordinal, s.file, s.digest, s.width, s.height, s.credit, s.credit_url, s.licence, s.licence_url, s.source,
                   s.focus_x::double precision, s.focus_y::double precision, s.bust_x::double precision, s.bust_y::double precision,
                   s.bust_width::double precision, s.bust_height::double precision
            FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug, s.kind, s.role, s.ordinal
            """,
            ["entity_image", "entity_image_caption"],
            About: "Derived from the console's picture files (manifests, choices, faces) and the gazetteer's thumbnails; never edited."),
        .. Languages.Select(language => new RecordFile($"records/i18n/{language}/images", ProductKind.Hand,
            "slug kind role ordinal:int caption",
            language == "eng"
                ? "SELECT e.slug, i.kind, i.role, i.ordinal, i.caption FROM entity_image i JOIN entity e ON e.id = i.entity_id WHERE i.caption IS NOT NULL ORDER BY 1, 2, 3, 4"
                : $"""
                  SELECT e.slug, i.kind, i.role, i.ordinal, c.caption FROM entity_image_caption c JOIN entity_image i ON i.id = c.entity_image_id
                  JOIN entity e ON e.id = i.entity_id WHERE c.language = '{language}' ORDER BY 1, 2, 3, 4
                  """,
            language == "eng"
                ? """
                  UPDATE entity_image i SET caption = s.caption FROM s JOIN entity e ON e.slug = s.slug
                  WHERE i.entity_id = e.id AND i.kind = s.kind AND i.role = s.role AND i.ordinal = s.ordinal
                  """
                : $"""
                  INSERT INTO entity_image_caption (entity_image_id, language, caption)
                  SELECT i.id, '{language}', s.caption FROM s JOIN entity e ON e.slug = s.slug
                  JOIN entity_image i ON i.entity_id = e.id AND i.kind = s.kind AND i.role = s.role AND i.ordinal = s.ordinal
                  ORDER BY s.slug, s.kind, s.role, s.ordinal
                  """,
            [])),

        new("records/facts/wikipedia", ProductKind.Machine,
            "slug language title qid matched_by confidence",
            "SELECT e.slug, w.language, w.title, w.qid, w.matched_by, w.confidence::text FROM entity_wikipedia w JOIN entity e ON e.id = w.entity_id ORDER BY 1, 2",
            """
            INSERT INTO entity_wikipedia (entity_id, language, title, qid, matched_by, confidence)
            SELECT e.id, s.language, s.title, s.qid, s.matched_by, s.confidence::double precision FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug, s.language
            """,
            ["entity_wikipedia"],
            About: "Derived from Wikidata, the gazetteer's identifications and the owner's answers in review/wikipedia-matches.json; never edited."),

        new("records/facts/locations", ProductKind.Machine,
            "slug lon lat kind score:int? modern_id coordinates_source dataset? by",
            """
            SELECT e.slug, l.longitude::text, l.latitude::text, l.kind, l.score, l.modern_id, l.coordinates_source, l.coordinates_dataset, l.source
            FROM place_location l JOIN entity e ON e.id = l.entity_id ORDER BY 1
            """,
            """
            INSERT INTO place_location (entity_id, longitude, latitude, kind, score, modern_id, coordinates_source, coordinates_dataset, source)
            SELECT e.id, s.lon::double precision, s.lat::double precision, s.kind, s.score, s.modern_id, s.coordinates_source, s.dataset, s.by
            FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug
            """,
            ["place_location", "place_candidate"],
            About: "Derived: where a place is, from the gazetteer and the location supplements; never edited."),
        new("records/facts/location-candidates", ProductKind.Machine,
            "slug rank:int score:int? modern_id? site? lon? lat? kind? radius_m:int? site_type? relation? through_openbible? special? withheld? grounds? coordinates_source? dataset? by",
            """
            SELECT e.slug, c.rank, c.score, c.modern_id, c.site, c.longitude::text, c.latitude::text, c.kind, c.radius_meters, c.site_type, c.relation,
                   c.through_open_bible_id, c.special, c.withheld, array_to_string(c.grounds, '|'), c.coordinates_source, c.coordinates_dataset, c.source
            FROM place_candidate c JOIN entity e ON e.id = c.entity_id ORDER BY 1, 2
            """,
            """
            INSERT INTO place_candidate (entity_id, rank, score, modern_id, site, longitude, latitude, kind, radius_meters, site_type, relation,
                                         through_open_bible_id, special, withheld, grounds, coordinates_source, coordinates_dataset, source)
            SELECT e.id, s.rank, s.score, s.modern_id, s.site, s.lon::double precision, s.lat::double precision, s.kind, s.radius_m, s.site_type,
                   s.relation, s.through_openbible, s.special, s.withheld, string_to_array(s.grounds, '|'), s.coordinates_source, s.dataset, s.by
            FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug, s.rank
            """,
            [],
            About: "Derived: the sites proposed for a place, best first; through_openbible is the gazetteer's id of the place a site was reached through."),
    ];

    /// <summary>Every file, in the order the load writes them: records first, then what stands on them.</summary>
    public static RecordFile[] All => Every.Value;

    private static readonly Lazy<RecordFile[]> Every = new(() =>
    [
        .. Records,
        .. PerLanguage(),
        .. Facts,
        .. External,
        .. Reigns,
        .. Derived,
    ]);
}
