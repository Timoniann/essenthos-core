using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

internal static partial class RecordFiles
{
    private static string NullableRef(string alias, string prefix = "canonical_") =>
        $"CASE WHEN {alias}.{prefix}book IS NULL THEN NULL ELSE ARRAY[{alias}.{prefix}book, {alias}.{prefix}chapter, {alias}.{prefix}verse] END";

    /// <summary>The id a dataset gives the record a verse row hangs on: its own, else that of the one record folded into it.</summary>
    private static string DatasetId(string dataset) => dataset == "bibledata"
        ? """
          coalesce(CASE WHEN e.source_id ~ '^(person|place):' THEN e.source_id END,
                   (SELECT min(m.record_source_id) FROM merged_record m WHERE m.entity_id = e.id AND m.record_source_id ~ '^(person|place):'))
          """
        : "coalesce(e.open_bible_id, (SELECT min(substr(m.record_source_id, 11)) FROM merged_record m WHERE m.entity_id = e.id AND m.record_source_id LIKE 'openbible:%'))";

    private static RecordFile DatasetRecords(string dataset, string credit) => new($"external/{dataset}/records", ProductKind.Hand,
        "external_id kind name attributes:json",
        $"""
        SELECT {(dataset == "bibledata" ? "e.source_id" : "e.open_bible_id")}, e.kind, e.name,
               jsonb_strip_nulls(jsonb_build_object('distinguisher', e.distinguisher, 'notes', CASE WHEN {OwnNotes} THEN NULL ELSE e.notes END, 'sex', e.sex, 'tribe', e.tribe,
                   'modern_equivalent', e.modern_equivalent, 'place_kind', e.place_kind, 'record', e.slug))::text
        FROM entity e WHERE e.source LIKE '{credit}%' ORDER BY 1
        """,
        $"""
        INSERT INTO external_record (dataset, external_id, kind, name, attributes)
        SELECT '{dataset}', external_id, kind, name, attributes FROM s ORDER BY external_id
        """,
        dataset == "bibledata" ? ["external_record", "external_reference"] : [],
        ExportLoaded: $"SELECT external_id, kind, name, attributes::text FROM external_record WHERE dataset = '{dataset}' ORDER BY 1",
        About: "What the dataset states of the records it supplied, as the corpus held it at the export. Never a record of ours; reached through records/external-ids.");

    private static RecordFile DatasetReferences(string dataset, string credit) => new($"external/{dataset}/references", ProductKind.Hand,
        "external_id? record ref:ref label? names:bool disputed:bool withheld:bool",
        $"""
        SELECT {DatasetId(dataset)}, e.slug, {Ref("v")}, v.label, v.names, v.disputed, v.withheld
        FROM entity_verse v JOIN entity e ON e.id = v.entity_id WHERE v.source LIKE '{credit}%' ORDER BY 2, 3, 1, 4, 5, 6, 7
        """,
        $"""
        INSERT INTO external_reference (dataset, external_id, entity_id, canonical_book, canonical_chapter, canonical_verse, label, names, disputed, withheld)
        SELECT '{dataset}', s.external_id, e.id, s.ref[1], s.ref[2], s.ref[3], s.label, s.names, s.disputed, s.withheld
        FROM s JOIN entity e ON e.slug = s.record ORDER BY s.record, s.ref, s.external_id, s.label
        """,
        [],
        ExportLoaded: $"""
        SELECT x.external_id, e.slug, {Ref("x")}, x.label, x.names, x.disputed, x.withheld
        FROM external_reference x JOIN entity e ON e.id = x.entity_id WHERE x.dataset = '{dataset}' ORDER BY 2, 3, 1, 4, 5, 6, 7
        """,
        About: "The verses the dataset lists for a record, and the record of ours the row was filed under (record), which the corpus moved some rows to.");

    private static RecordFile Events(string product, string where) => new(product, ProductKind.Hand,
        "slug name description? kind? record? year_from_creation:int? bce_year:int? age_at_event:int? calculation? ref:ref? location? location_ref:ref? uri? notes? realm region? region_at_the_time? name_source sequence_in_year:int? by",
        $"""
        SELECT x.slug, x.name, x.description, x.kind, e.slug, x.year_from_creation, x.bce_year, x.age_at_event, x.calculation, {NullableRef("x")},
               x.location, {NullableRef("x", "location_")}, x.uri, x.notes, x.realm, x.region, x.region_at_the_time, x.name_source,
               x.sequence_in_year, x.source
        FROM event x LEFT JOIN entity e ON e.id = x.entity_id WHERE {where} ORDER BY 1
        """,
        """
        INSERT INTO event (slug, name, description, kind, entity_id, year_from_creation, bce_year, age_at_event, calculation,
                           canonical_book, canonical_chapter, canonical_verse, location, location_book, location_chapter, location_verse,
                           uri, notes, realm, region, region_at_the_time, name_source, sequence_in_year, source)
        SELECT s.slug, s.name, s.description, s.kind, e.id, s.year_from_creation, s.bce_year, s.age_at_event, s.calculation,
               s.ref[1], s.ref[2], s.ref[3], s.location, s.location_ref[1], s.location_ref[2], s.location_ref[3],
               s.uri, s.notes, s.realm, s.region, s.region_at_the_time, s.name_source, s.sequence_in_year, s.by
        FROM s LEFT JOIN entity e ON e.slug = s.record WHERE s.record IS NULL OR e.id IS NOT NULL ORDER BY s.slug
        """,
        product == "external/bibledata/events" ? ["event", "event_date", "chronology", "period"] : [],
        About: "Events as the dataset gives them; record names the record of ours an event is about.");

    private static RecordFile Periods(string product, string where) => new(product, ProductKind.Hand,
        "slug name kind? level:int parent? record? start_event? end_event? start_year:int? end_year:int? notes? realm region? uri? by",
        $"""
        SELECT x.slug, x.name, x.kind, x.level, p.slug, e.slug, a.slug, b.slug, x.start_year, x.end_year, x.notes, x.realm, x.region, x.uri, x.source
        FROM period x LEFT JOIN period p ON p.id = x.parent_id LEFT JOIN entity e ON e.id = x.entity_id
        LEFT JOIN event a ON a.id = x.start_event_id LEFT JOIN event b ON b.id = x.end_event_id WHERE {where} ORDER BY 1
        """,
        """
        INSERT INTO period (slug, name, kind, level, entity_id, start_event_id, end_event_id, start_year, end_year, notes, realm, region, uri, source)
        SELECT s.slug, s.name, s.kind, s.level, e.id, a.id, b.id, s.start_year, s.end_year, s.notes, s.realm, s.region, s.uri, s.by
        FROM s LEFT JOIN entity e ON e.slug = s.record LEFT JOIN event a ON a.slug = s.start_event LEFT JOIN event b ON b.slug = s.end_event
        WHERE (s.record IS NULL OR e.id IS NOT NULL) AND (s.start_event IS NULL OR a.id IS NOT NULL) AND (s.end_event IS NULL OR b.id IS NOT NULL)
        ORDER BY s.slug
        """,
        [],
        After: """
        CREATE TEMP TABLE IF NOT EXISTS period_parent (slug text, parent text) ON COMMIT DROP;
        INSERT INTO period_parent SELECT slug, parent FROM s WHERE parent IS NOT NULL;
        UPDATE period p SET parent_id = q.id FROM period_parent x JOIN period q ON q.slug = x.parent WHERE p.slug = x.slug;
        """);

    private static RecordFile Chronologies(string product, string where) => new(product, ProductKind.Hand,
        "slug name authority? basis? last_year_bce:int is_default:bool position:int by?",
        $"SELECT slug, name, authority, basis, last_year_before_the_common_era, is_default, position, source FROM chronology WHERE {where} ORDER BY 1",
        """
        INSERT INTO chronology (slug, name, authority, basis, last_year_before_the_common_era, is_default, position, source)
        SELECT slug, name, authority, basis, last_year_bce, is_default, position, by FROM s ORDER BY slug
        """,
        []);

    private static RecordFile EventDates(string product, string chronologies) => new(product, ProductKind.Hand,
        "event chronology year:int? earliest_year:int? latest_year:int? stated_year:int? calculation? citation? notes? calculation_parts:json?",
        $"""
        SELECT x.slug, c.slug, d.year, d.earliest_year, d.latest_year, d.stated_year, d.calculation, d.citation, d.notes, d.calculation_parts::text
        FROM event_date d JOIN event x ON x.id = d.event_id JOIN chronology c ON c.id = d.chronology_id
        WHERE c.slug IN ({chronologies}) ORDER BY 1, 2
        """,
        """
        INSERT INTO event_date (event_id, chronology_id, year, earliest_year, latest_year, stated_year, calculation, citation, notes, calculation_parts)
        SELECT x.id, c.id, s.year, s.earliest_year, s.latest_year, s.stated_year, s.calculation, s.citation, s.notes, s.calculation_parts
        FROM s JOIN event x ON x.slug = s.event JOIN chronology c ON c.slug = s.chronology ORDER BY s.event, s.chronology
        """,
        [],
        About: "When each event falls by this reckoning.");

    private static RecordFile CrossReferences(string set, string product, ProductKind kind) => new(product, kind,
        "from_ref:ref to_ref:ref to_end:ref? rank:int votes:int? note? passage:int? matched_in? matched? by",
        $"""
        SELECT ARRAY[book, chapter, verse], ARRAY[to_book, to_chapter, to_verse],
               CASE WHEN to_end_book IS NULL THEN NULL ELSE ARRAY[to_end_book, to_end_chapter, to_end_verse] END,
               rank, votes, note, passage, matched_in, matched, source
        FROM cross_reference WHERE set = '{set}' ORDER BY 1, 2, 3
        """,
        $"""
        INSERT INTO cross_reference (set, book, chapter, verse, to_book, to_chapter, to_verse, to_end_book, to_end_chapter, to_end_verse,
                                     rank, votes, note, passage, matched_in, matched, source)
        SELECT '{set}', from_ref[1], from_ref[2], from_ref[3], to_ref[1], to_ref[2], to_ref[3], to_end[1], to_end[2], to_end[3],
               rank, votes, note, passage, matched_in, matched, by FROM s ORDER BY s.from_ref, s.to_ref, s.to_end
        """,
        set == "openbible" ? ["cross_reference"] : []);

    private static RecordFile[] External =>
    [
        DatasetRecords("bibledata", "BibleData"),
        DatasetRecords("openbible", "OpenBible"),
        DatasetReferences("bibledata", "BibleData"),
        DatasetReferences("openbible", "OpenBible"),

        Chronologies("external/bibledata/chronologies", "slug = 'bibledata'"),
        Chronologies("external/ussher/chronologies", "slug = 'ussher'"),
        Chronologies("external/shulman/chronologies", "slug = 'shulman'"),
        Chronologies("records/chronology/chronologies", "slug IN ('septuagint', 'septuagint-alexandrinus')"),
        Events("external/bibledata/events", "x.source LIKE 'BibleData%'"),
        Events("external/ussher/events", "x.source LIKE 'Ussher''s%'"),
        Events("external/wikidata/events", "x.source LIKE 'Wikidata%'"),
        Events("records/chronology/events", "x.source LIKE 'Essenthos%'"),
        Periods("external/bibledata/periods", "x.source LIKE 'BibleData%'"),
        Periods("external/wikidata/periods", "x.source LIKE 'Wikidata%'"),
        EventDates("external/bibledata/event-dates", "'bibledata'"),
        EventDates("external/ussher/event-dates", "'ussher'"),
        EventDates("external/shulman/event-dates", "'shulman'"),
        EventDates("records/chronology/event-dates", "'septuagint', 'septuagint-alexandrinus'"),

        new("external/periodo/authorities", ProductKind.Hand,
            "periodo_id attribution citation title? creators? year_published:int? locator? uri?",
            "SELECT periodo_id, attribution, citation, title, creators, year_published, locator, uri FROM period_authority ORDER BY 1",
            """
            INSERT INTO period_authority (periodo_id, attribution, citation, title, creators, year_published, locator, uri)
            SELECT periodo_id, attribution, citation, title, creators, year_published, locator, uri FROM s ORDER BY periodo_id
            """,
            ["period_authority", "period_definition"]),
        new("external/periodo/definitions", ProductKind.Hand,
            "periodo_id authority label language_tag? labels:json? region coverage:json? coverage_description? start_label? start_earliest:int start_latest:int stop_label? stop_earliest:int stop_latest:int broader? note? editorial_note? by",
            """
            SELECT d.periodo_id, a.periodo_id, d.label, d.language_tag, d.labels::text, d.region, d.coverage::text, d.coverage_description,
                   d.start_label, d.start_earliest, d.start_latest, d.stop_label, d.stop_earliest, d.stop_latest, d.broader, d.note,
                   d.editorial_note, d.source
            FROM period_definition d JOIN period_authority a ON a.id = d.authority_id ORDER BY 1
            """,
            """
            INSERT INTO period_definition (periodo_id, authority_id, label, language_tag, labels, region, coverage, coverage_description,
                start_label, start_earliest, start_latest, stop_label, stop_earliest, stop_latest, broader, note, editorial_note, source)
            SELECT s.periodo_id, a.id, s.label, s.language_tag, s.labels, s.region, s.coverage, s.coverage_description, s.start_label,
                   s.start_earliest, s.start_latest, s.stop_label, s.stop_earliest, s.stop_latest, s.broader, s.note, s.editorial_note, s.by
            FROM s JOIN period_authority a ON a.periodo_id = s.authority ORDER BY s.periodo_id
            """,
            []),

        new("external/maimonides/commandments", ProductKind.Hand,
            "kind number:int mishneh_torah_number:int title note? by",
            "SELECT kind, number, mishneh_torah_number, title, note, source FROM commandment ORDER BY 1, 2",
            """
            INSERT INTO commandment (kind, number, mishneh_torah_number, title, note, source)
            SELECT kind, number, mishneh_torah_number, title, note, by FROM s ORDER BY kind, number
            """,
            ["commandment", "commandment_reference"]),
        new("external/maimonides/commandment-references", ProductKind.Hand,
            "kind number:int n:int book:int chapter:int first_verse:int last_verse:int",
            """
            SELECT c.kind, c.number, row_number() OVER (PARTITION BY r.commandment_id ORDER BY r.id), r.canonical_book, r.canonical_chapter, r.first_verse, r.last_verse
            FROM commandment_reference r JOIN commandment c ON c.id = r.commandment_id ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO commandment_reference (commandment_id, canonical_book, canonical_chapter, first_verse, last_verse)
            SELECT c.id, s.book, s.chapter, s.first_verse, s.last_verse FROM s JOIN commandment c ON c.kind = s.kind AND c.number = s.number
            ORDER BY s.kind, s.number, s.n
            """,
            []),

        new("external/bibledata/topics", ProductKind.Hand,
            "slug name by",
            "SELECT slug, name, source FROM topic ORDER BY 1",
            "INSERT INTO topic (slug, name, source) SELECT slug, name, by FROM s ORDER BY slug",
            ["topic", "topic_reference"]),
        new("external/bibledata/topic-references", ProductKind.Hand,
            "slug n:int book:int chapter:int first_verse:int? last_verse:int? heading?",
            """
            SELECT t.slug, row_number() OVER (PARTITION BY r.topic_id ORDER BY r.id), r.canonical_book, r.canonical_chapter, r.first_verse, r.last_verse, r.heading
            FROM topic_reference r JOIN topic t ON t.id = r.topic_id ORDER BY 1, 2
            """,
            """
            INSERT INTO topic_reference (topic_id, canonical_book, canonical_chapter, first_verse, last_verse, heading)
            SELECT t.id, s.book, s.chapter, s.first_verse, s.last_verse, s.heading FROM s JOIN topic t ON t.slug = s.slug ORDER BY s.slug, s.n
            """,
            []),

        CrossReferences("openbible", "external/openbible/cross-references", ProductKind.Hand),
        CrossReferences("tsk", "external/tsk/cross-references", ProductKind.Hand),
        CrossReferences("parallels", "records/derived/parallels", ProductKind.Machine),
    ];
}
