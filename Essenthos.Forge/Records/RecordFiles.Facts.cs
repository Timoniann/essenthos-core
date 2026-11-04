using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

internal static partial class RecordFiles
{
    /// <summary>The verse rows read off our own words and relationships, which a producer derives (DOC-0226 §3.4).</summary>
    private const string DerivedVerse =
        "(v.source LIKE 'Essenthos, from the words this corpus annotates%' OR v.source LIKE 'Essenthos, the verse a relationship of this project''s own was read from%')";

    /// <summary>The verse rows a dataset states, which are the dataset's references now.</summary>
    private const string DatasetVerse = "(v.source LIKE 'BibleData%' OR v.source LIKE 'OpenBible%')";

    /// <summary>What a row holds until its companion file fills a column a check forbids to be empty.</summary>
    private const string Pending = "pending:records-load";

    private static string Settled(string table, string condition, string what) =>
        $"DO $$ BEGIN IF EXISTS (SELECT 1 FROM {table} WHERE {condition}) THEN RAISE EXCEPTION '{what}'; END IF; END $$";

    private static RecordFile Verses(string product, ProductKind kind, string which, string about) => new(product, kind,
        "slug ref:ref label? names:bool disputed:bool withheld:bool by",
        $"""
        SELECT e.slug, {Ref("v")}, v.label, v.names, v.disputed, v.withheld, v.source
        FROM entity_verse v JOIN entity e ON e.id = v.entity_id WHERE {which} ORDER BY 1, 2, 7, 3, 4, 5, 6
        """,
        """
        INSERT INTO entity_verse (entity_id, canonical_book, canonical_chapter, canonical_verse, label, names, disputed, withheld, source)
        SELECT e.id, s.ref[1], s.ref[2], s.ref[3], s.label, s.names, s.disputed, s.withheld, s.by
        FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug, s.ref, s.by, s.label
        """,
        product.Contains("derived", StringComparison.Ordinal) ? [] : ["entity_verse"],
        After: Settled("entity_verse", DatasetVerse.Replace("v.", ""),
            "a verse row credits a dataset: the verses of a dataset are its references, never the served verses of a record"),
        ExportLoaded: $"""
        SELECT e.slug, {Ref("v")}, v.label, v.names, v.disputed, v.withheld, v.source
        FROM entity_verse v JOIN entity e ON e.id = v.entity_id WHERE {which} ORDER BY 1, 2, 7, 3, 4, 5, 6
        """.Replace(DatasetVerse, "false"),
        About: about);

    private static RecordFile[] Facts =>
    [
        Verses("records/facts/verses", ProductKind.Hand, $"NOT {DerivedVerse} AND NOT {DatasetVerse}",
            "Verses a reading of ours says speak of a record, where no word of ours carries it: verse readings, the passages that command a thing, rulings, terms."),
        Verses("records/derived/verses", ProductKind.Machine, DerivedVerse,
            "Derived: the verses our words and relationships put a record in, and whether a word names it there. Regenerated from names and relationships; never edited."),

        new("records/facts/passages", ProductKind.Hand,
            "slug role ordinal:int book:int chapter:int verse:int? end_chapter:int end_verse:int? by",
            $"""
            SELECT e.slug, p.role, p.ordinal, p.canonical_book, p.canonical_chapter, p.canonical_verse, p.end_chapter, p.end_verse, p.source
            FROM entity_passage p JOIN entity e ON e.id = p.entity_id ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO entity_passage (entity_id, role, ordinal, canonical_book, canonical_chapter, canonical_verse, end_chapter, end_verse, source)
            SELECT e.id, s.role, s.ordinal, s.book, s.chapter, s.verse, s.end_chapter, s.end_verse, s.by
            FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug, s.role, s.ordinal
            """,
            ["entity_passage"]),
        new("records/i18n/eng/passages", ProductKind.Hand,
            "slug role ordinal:int note",
            "SELECT e.slug, p.role, p.ordinal, p.note FROM entity_passage p JOIN entity e ON e.id = p.entity_id WHERE p.note IS NOT NULL ORDER BY 1, 2, 3",
            "UPDATE entity_passage p SET note = s.note FROM s JOIN entity e ON e.slug = s.slug WHERE p.entity_id = e.id AND p.role = s.role AND p.ordinal = s.ordinal",
            []),

        new("records/facts/observance-times", ProductKind.Hand,
            "slug n:int cycle ref:ref month:int? day:int? last_day:int? by",
            $"""
            SELECT e.slug, row_number() OVER (PARTITION BY o.entity_id ORDER BY o.id), o.cycle, {Ref("o")}, o.month, o.day, o.last_day, o.source
            FROM observance_time o JOIN entity e ON e.id = o.entity_id ORDER BY 1, 2
            """,
            """
            INSERT INTO observance_time (entity_id, cycle, canonical_book, canonical_chapter, canonical_verse, month, day, last_day, source)
            SELECT e.id, s.cycle, s.ref[1], s.ref[2], s.ref[3], s.month, s.day, s.last_day, s.by
            FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug, s.n
            """,
            ["observance_time"]),
        new("records/i18n/eng/observance-times", ProductKind.Hand,
            "slug n:int note",
            """
            SELECT * FROM (SELECT e.slug, row_number() OVER (PARTITION BY o.entity_id ORDER BY o.id) n, o.note
                           FROM observance_time o JOIN entity e ON e.id = o.entity_id) x WHERE note IS NOT NULL ORDER BY 1, 2
            """,
            """
            UPDATE observance_time t SET note = s.note
            FROM s, (SELECT o.id, e.slug, row_number() OVER (PARTITION BY o.entity_id ORDER BY o.id) n
                     FROM observance_time o JOIN entity e ON e.id = o.entity_id) k
            WHERE k.id = t.id AND k.slug = s.slug AND k.n = s.n
            """,
            []),

        new("records/facts/known-for", ProductKind.Hand,
            "slug ordinal:int by",
            "SELECT e.slug, k.ordinal, k.source FROM entity_known_for k JOIN entity e ON e.id = k.entity_id ORDER BY 1, 2",
            $"""
            INSERT INTO entity_known_for (entity_id, ordinal, english, source)
            SELECT e.id, s.ordinal, '{Pending}', s.by FROM s JOIN entity e ON e.slug = s.slug ORDER BY s.slug, s.ordinal
            """,
            ["entity_known_for", "entity_known_for_reference", "entity_known_for_text"]),
        new("records/facts/known-for-references", ProductKind.Hand,
            "slug ordinal:int position:int ref:ref end_chapter:int end_verse:int",
            $"""
            SELECT e.slug, k.ordinal, r.position, {Ref("r")}, r.end_chapter, r.end_verse
            FROM entity_known_for_reference r JOIN entity_known_for k ON k.id = r.known_for_id JOIN entity e ON e.id = k.entity_id
            ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO entity_known_for_reference (known_for_id, position, canonical_book, canonical_chapter, canonical_verse, end_chapter, end_verse)
            SELECT k.id, s.position, s.ref[1], s.ref[2], s.ref[3], s.end_chapter, s.end_verse
            FROM s JOIN entity e ON e.slug = s.slug JOIN entity_known_for k ON k.entity_id = e.id AND k.ordinal = s.ordinal
            ORDER BY s.slug, s.ordinal, s.position
            """,
            []),
        .. Languages.Select(language => new RecordFile($"records/i18n/{language}/known-for", ProductKind.Hand,
            "slug ordinal:int text",
            language == "eng"
                ? "SELECT e.slug, k.ordinal, k.english FROM entity_known_for k JOIN entity e ON e.id = k.entity_id ORDER BY 1, 2"
                : $"""
                  SELECT e.slug, k.ordinal, t.text FROM entity_known_for_text t JOIN entity_known_for k ON k.id = t.known_for_id
                  JOIN entity e ON e.id = k.entity_id WHERE t.language = '{language}' ORDER BY 1, 2
                  """,
            language == "eng"
                ? "UPDATE entity_known_for k SET english = s.text FROM s JOIN entity e ON e.slug = s.slug WHERE k.entity_id = e.id AND k.ordinal = s.ordinal"
                : $"""
                  INSERT INTO entity_known_for_text (known_for_id, language, text)
                  SELECT k.id, '{language}', s.text FROM s JOIN entity e ON e.slug = s.slug
                  JOIN entity_known_for k ON k.entity_id = e.id AND k.ordinal = s.ordinal ORDER BY s.slug, s.ordinal
                  """,
            [],
            After: language == "eng" ? Settled("entity_known_for", $"english = '{Pending}'", "a known-for clause has no English text") : null)),

        new("records/facts/titles", ProductKind.Hand,
            "title bearer ref:ref called:json? by",
            $"""
            SELECT t.slug, b.slug, {Ref("x")}, x.called::text, x.source FROM title_bearer x
            JOIN entity t ON t.id = x.title_entity_id JOIN entity b ON b.id = x.bearer_entity_id ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO title_bearer (title_entity_id, bearer_entity_id, canonical_book, canonical_chapter, canonical_verse, called, source)
            SELECT t.id, b.id, s.ref[1], s.ref[2], s.ref[3], s.called, s.by
            FROM s JOIN entity t ON t.slug = s.title JOIN entity b ON b.slug = s.bearer ORDER BY s.title, s.bearer, s.ref
            """,
            ["title_bearer"]),
        new("records/i18n/eng/titles", ProductKind.Hand,
            "title bearer ref:ref note",
            $"""
            SELECT t.slug, b.slug, {Ref("x")}, x.note FROM title_bearer x
            JOIN entity t ON t.id = x.title_entity_id JOIN entity b ON b.id = x.bearer_entity_id WHERE x.note IS NOT NULL ORDER BY 1, 2, 3
            """,
            """
            UPDATE title_bearer x SET note = s.note FROM s JOIN entity t ON t.slug = s.title JOIN entity b ON b.slug = s.bearer
            WHERE x.title_entity_id = t.id AND x.bearer_entity_id = b.id
              AND x.canonical_book = s.ref[1] AND x.canonical_chapter = s.ref[2] AND x.canonical_verse = s.ref[3]
            """,
            []),

        new("records/facts/clauses", ProductKind.Hand,
            "subject ordinal:int relation target ref:ref citation? witness? original? method confidence? by run? note?",
            $"""
            SELECT e.slug, d.ordinal, d.relation, t.slug, {Ref("d")}, d.citation, d.witness, d.original, d.method,
                   d.confidence::text, d.source, d.run, d.note
            FROM entity_descriptor d JOIN entity e ON e.id = d.entity_id JOIN entity t ON t.id = d.target_entity_id ORDER BY 1, 2
            """,
            """
            INSERT INTO entity_descriptor (entity_id, ordinal, relation, target_entity_id, canonical_book, canonical_chapter, canonical_verse,
                                           citation, witness, original, method, confidence, source, run, note)
            SELECT e.id, s.ordinal, s.relation, t.id, s.ref[1], s.ref[2], s.ref[3], s.citation, s.witness, s.original, s.method,
                   s.confidence::double precision, s.by, s.run, s.note
            FROM s JOIN entity e ON e.slug = s.subject JOIN entity t ON t.slug = s.target ORDER BY s.subject, s.ordinal
            """,
            ["entity_descriptor"],
            About: "What the text says a record is, clause by clause: the line, and the source of every relationship."),

        new("records/facts/relationships", ProductKind.Machine,
            "from_slug relation to_slug ref:ref? category citation? method confidence? by notes?",
            """
            SELECT f.slug, r.type, t.slug, CASE WHEN r.canonical_book IS NULL THEN NULL ELSE ARRAY[r.canonical_book, r.canonical_chapter, r.canonical_verse] END,
                   r.category, r.citation, r.method, r.confidence::text, r.source, r.notes
            FROM entity_relationship r JOIN entity f ON f.id = r.from_entity_id JOIN entity t ON t.id = r.to_entity_id
            ORDER BY 1, 2, 3, 4
            """,
            """
            INSERT INTO entity_relationship (from_entity_id, type, to_entity_id, canonical_book, canonical_chapter, canonical_verse,
                                             category, citation, method, confidence, source, notes)
            SELECT f.id, s.relation, t.id, s.ref[1], s.ref[2], s.ref[3], s.category, s.citation, s.method,
                   s.confidence::double precision, s.by, s.notes
            FROM s JOIN entity f ON f.slug = s.from_slug JOIN entity t ON t.slug = s.to_slug ORDER BY s.from_slug, s.relation, s.to_slug, s.ref
            """,
            ["entity_relationship"],
            About: "Derived from records/facts/clauses and the things' relations; edit the clause, never this file."),

        new("records/facts/kin-statements", ProductKind.Hand,
            "slug kind ref:ref ordinal:int? family? sons:int? daughters:int? children:int? grandsons:int? more_sons:bool more_daughters:bool parent? citation? witness? original? method confidence? by note?",
            $"""
            SELECT e.slug, k.kind, {Ref("k")}, k.ordinal, k.family, k.sons, k.daughters, k.children, k.grandsons, k.more_sons,
                   k.more_daughters, p.slug, k.citation, k.witness, k.original, k.method, k.confidence::text, k.source, k.note
            FROM entity_kin_statement k JOIN entity e ON e.id = k.entity_id LEFT JOIN entity p ON p.id = k.parent_entity_id
            ORDER BY 1, 2, 3, 4
            """,
            """
            INSERT INTO entity_kin_statement (entity_id, kind, canonical_book, canonical_chapter, canonical_verse, ordinal, family, sons, daughters,
                children, grandsons, more_sons, more_daughters, parent_entity_id, citation, witness, original, method, confidence, source, note)
            SELECT e.id, s.kind, s.ref[1], s.ref[2], s.ref[3], s.ordinal, s.family, s.sons, s.daughters, s.children, s.grandsons, s.more_sons,
                   s.more_daughters, p.id, s.citation, s.witness, s.original, s.method, s.confidence::double precision, s.by, s.note
            FROM s JOIN entity e ON e.slug = s.slug LEFT JOIN entity p ON p.slug = s.parent
            WHERE s.parent IS NULL OR p.id IS NOT NULL ORDER BY s.slug, s.kind, s.ref, s.ordinal
            """,
            ["entity_kin_statement"]),

        new("records/facts/presence", ProductKind.Hand,
            "person place category refs:refs by",
            """
            SELECT a.slug, b.slug, p.category,
                   (SELECT coalesce(array_agg(x ORDER BY o, k), '{}') FROM unnest(p.addresses) WITH ORDINALITY u(ad, o),
                           LATERAL unnest(ARRAY[ad / 1000000, ad / 1000 % 1000, ad % 1000]) WITH ORDINALITY w(x, k)),
                   p.source
            FROM person_presence p JOIN entity a ON a.id = p.person_id JOIN entity b ON b.id = p.place_id ORDER BY 1, 2, 3
            """,
            $$"""
            INSERT INTO person_presence (person_id, place_id, category, addresses, source, evidence)
            SELECT a.id, b.id, s.category,
                   ARRAY(SELECT s.refs[i] * 1000000 + s.refs[i + 1] * 1000 + s.refs[i + 2]
                         FROM generate_series(1, coalesce(array_length(s.refs, 1), 0), 3) i ORDER BY i),
                   s.by, '["{{Pending}}"]'::jsonb
            FROM s JOIN entity a ON a.slug = s.person JOIN entity b ON b.slug = s.place ORDER BY s.person, s.place, s.category
            """,
            ["person_presence"]),
        new("records/facts/presence-evidence", ProductKind.Hand,
            "person place category evidence:json",
            """
            SELECT a.slug, b.slug, p.category, p.evidence::text
            FROM person_presence p JOIN entity a ON a.id = p.person_id JOIN entity b ON b.id = p.place_id ORDER BY 1, 2, 3
            """,
            """
            UPDATE person_presence p SET evidence = s.evidence FROM s JOIN entity a ON a.slug = s.person JOIN entity b ON b.slug = s.place
            WHERE p.person_id = a.id AND p.place_id = b.id AND p.category = s.category
            """,
            [],
            After: Settled("person_presence", $"evidence = '[\"{Pending}\"]'::jsonb", "a presence has no evidence"),
            About: "What each presence was read from. Evidence: never served."),

        new("records/facts/occupations", ProductKind.Hand,
            "slug category strong_numbers",
            "SELECT slug, category, array_to_string(strong_numbers, ' ') FROM occupation ORDER BY 1",
            """
            INSERT INTO occupation (slug, category, strong_numbers)
            SELECT slug, category, coalesce(string_to_array(nullif(strong_numbers, ''), ' '), '{}') FROM s ORDER BY slug
            """,
            ["occupation", "occupation_label", "person_occupation", "occupation_witness", "occupation_word"]),
        .. Languages.Where(l => l != "rus").Select(language => new RecordFile($"records/i18n/{language}/occupations", ProductKind.Hand,
            "slug name masculine feminine description?",
            $"""
            SELECT o.slug, l.name, l.masculine, l.feminine, l.description FROM occupation_label l JOIN occupation o ON o.id = l.occupation_id
            WHERE l.language = '{language}' ORDER BY 1
            """,
            $"""
            INSERT INTO occupation_label (occupation_id, language, name, masculine, feminine, description)
            SELECT o.id, '{language}', s.name, s.masculine, s.feminine, s.description FROM s JOIN occupation o ON o.slug = s.slug ORDER BY s.slug
            """,
            [])),
        new("records/facts/person-occupations", ProductKind.Hand,
            "person occupation by",
            """
            SELECT e.slug, o.slug, p.source FROM person_occupation p JOIN entity e ON e.id = p.person_id JOIN occupation o ON o.id = p.occupation_id
            ORDER BY 1, 2
            """,
            """
            INSERT INTO person_occupation (person_id, occupation_id, source)
            SELECT e.id, o.id, s.by FROM s JOIN entity e ON e.slug = s.person JOIN occupation o ON o.slug = s.occupation ORDER BY s.person, s.occupation
            """,
            []),
        new("records/facts/occupation-witnesses", ProductKind.Hand,
            "person occupation n:int kind text reference ref:ref quote",
            $"""
            SELECT e.slug, o.slug, row_number() OVER (PARTITION BY w.person_occupation_id ORDER BY w.id), w.kind, w.text, w.reference, {Ref("w")}, w.quote
            FROM occupation_witness w JOIN person_occupation p ON p.id = w.person_occupation_id
            JOIN entity e ON e.id = p.person_id JOIN occupation o ON o.id = p.occupation_id ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO occupation_witness (person_occupation_id, kind, text, reference, canonical_book, canonical_chapter, canonical_verse, quote)
            SELECT p.id, s.kind, s.text, s.reference, s.ref[1], s.ref[2], s.ref[3], s.quote
            FROM s JOIN entity e ON e.slug = s.person JOIN occupation o ON o.slug = s.occupation
            JOIN person_occupation p ON p.person_id = e.id AND p.occupation_id = o.id ORDER BY s.person, s.occupation, s.n
            """,
            [],
            About: "The quotations each occupation stands on, as the named text prints them at its own address (reference)."),
        new("records/derived/occupation-words", ProductKind.Machine,
            "person occupation word_id:int",
            """
            SELECT e.slug, o.slug, w.word_id FROM occupation_word w JOIN person_occupation p ON p.id = w.person_occupation_id
            JOIN entity e ON e.id = p.person_id JOIN occupation o ON o.id = p.occupation_id ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO occupation_word (person_occupation_id, word_id)
            SELECT p.id, s.word_id FROM s JOIN entity e ON e.slug = s.person JOIN occupation o ON o.slug = s.occupation
            JOIN person_occupation p ON p.person_id = e.id AND p.occupation_id = o.id ORDER BY s.person, s.occupation, s.word_id
            """,
            [],
            Requires: "EXISTS (SELECT 1 FROM word)",
            About: "Derived: the words that name each occupation, by word id until word keys land (TSK-1443). Regenerated from the occupations and the mapping."),

        new("records/facts/gentilics", ProductKind.Hand,
            "strong origin_strong kind origin? people? statement by",
            """
            SELECT g.strong_number, g.origin_number, g.kind, o.slug, p.slug, g.statement, g.source FROM strong_gentilic g
            LEFT JOIN entity o ON o.id = g.origin_entity_id LEFT JOIN entity p ON p.id = g.people_entity_id ORDER BY 1, 2, 3
            """,
            """
            INSERT INTO strong_gentilic (strong_number, origin_number, kind, origin_entity_id, people_entity_id, statement, source)
            SELECT s.strong, s.origin_strong, s.kind, o.id, p.id, s.statement, s.by
            FROM s LEFT JOIN entity o ON o.slug = s.origin LEFT JOIN entity p ON p.slug = s.people
            WHERE (s.origin IS NULL OR o.id IS NOT NULL) AND (s.people IS NULL OR p.id IS NOT NULL) ORDER BY s.strong, s.origin_strong, s.kind
            """,
            ["strong_gentilic"]),
    ];
}
